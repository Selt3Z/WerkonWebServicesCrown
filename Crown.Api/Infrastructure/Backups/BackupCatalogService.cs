using System.IO.Compression;
using System.Text;
using Crown.Contracts.Common;
using Crown.Contracts.System;

namespace Crown.Infrastructure.Backups;

public sealed class BackupCatalogService
{
    public const long MinCompressedBytes = 20;
    public const long MinDecompressedBytes = 50;
    public const long MaxCompressedBytes = 512L * 1024 * 1024;
    public const long MaxDecompressedBytes = 2L * 1024 * 1024 * 1024;

    private readonly string _backupsRoot;

    public BackupCatalogService(IConfiguration configuration)
    {
        _backupsRoot = configuration["Backup:BackupsDirectory"]
            ?? Path.GetDirectoryName(configuration["Backup:StatusFilePath"] ?? string.Empty)
            ?? "backups";
    }

    public string BackupsRoot => Path.GetFullPath(_backupsRoot);

    public IReadOnlyList<BackupFileInfoResponse> ListAvailable()
    {
        if (!Directory.Exists(BackupsRoot))
        {
            return [];
        }

        var files = new List<BackupFileInfoResponse>();
        Collect(files, Path.Combine(BackupsRoot, "daily"), "daily");
        Collect(files, Path.Combine(BackupsRoot, "weekly"), "weekly");

        return files
            .OrderByDescending(x => x.CreatedUtc)
            .ToList();
    }

    public bool TryResolveSafePath(string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(BackupsRoot, normalized));
        if (!candidate.StartsWith(BackupsRoot, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(candidate)
            || !candidate.EndsWith(".sql.gz", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    /// <summary>
    /// Сохраняет upload: только .sql.gz, magic gzip, peek pg_dump/SQL,
    /// cap распаковки, имя всегда crown-upload-… (без overwrite scheduler).
    /// </summary>
    public async Task<BackupFileInfoResponse> SaveUploadedAsync(
        Stream content,
        string? originalFileName,
        CancellationToken cancellationToken = default)
    {
        var extensionOk = !string.IsNullOrWhiteSpace(originalFileName)
            && originalFileName.EndsWith(".sql.gz", StringComparison.OrdinalIgnoreCase);
        if (!extensionOk)
        {
            throw new BackupFileException(
                ApiErrorCodes.BackupInvalidFile,
                "Only .sql.gz backup files are accepted.");
        }

        Directory.CreateDirectory(Path.Combine(BackupsRoot, "daily"));

        var tempPath = Path.Combine(
            BackupsRoot,
            "daily",
            $".upload-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(fs, cancellationToken);
            }

            var compressed = new FileInfo(tempPath).Length;
            if (compressed < MinCompressedBytes)
            {
                throw new BackupFileException(
                    ApiErrorCodes.BackupInvalidFile,
                    "Backup file is too small to be a valid dump.");
            }

            if (compressed > MaxCompressedBytes)
            {
                throw new BackupFileException(
                    ApiErrorCodes.BackupFileTooLarge,
                    "Backup file exceeds the maximum allowed size.");
            }

            await ValidateGzipDumpAsync(tempPath, cancellationToken);

            // Имя всегда своё — клиентское имя не используем (overwrite / traversal).
            var fileName = $"crown-upload-{DateTime.UtcNow:yyyyMMddTHHmmssZ}-{Random.Shared.Next(1000, 9999)}.sql.gz";
            var relative = $"daily/{fileName}";
            var fullPath = Path.GetFullPath(Path.Combine(BackupsRoot, relative));
            if (!fullPath.StartsWith(BackupsRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid upload path.");
            }

            File.Move(tempPath, fullPath, overwrite: false);
            tempPath = string.Empty;

            var info = new FileInfo(fullPath);
            return new BackupFileInfoResponse
            {
                RelativePath = relative,
                FileName = info.Name,
                Folder = "daily",
                SizeBytes = info.Length,
                CreatedUtc = info.LastWriteTimeUtc.ToString("O")
            };
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>Перед restore: gzip + SQL/pg_dump + bomb-cap.</summary>
    public Task ValidateDumpFileAsync(string fullPath, CancellationToken cancellationToken = default) =>
        ValidateGzipDumpAsync(fullPath, cancellationToken);

    private static async Task ValidateGzipDumpAsync(string path, CancellationToken cancellationToken)
    {
        await using var read = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var magic = new byte[2];
        var magicRead = await read.ReadAsync(magic.AsMemory(0, 2), cancellationToken);
        if (magicRead < 2 || magic[0] != 0x1F || magic[1] != 0x8B)
        {
            throw new BackupFileException(
                ApiErrorCodes.BackupInvalidFile,
                "Backup is not a valid gzip archive.");
        }

        read.Position = 0;
        await using var gz = new GZipStream(read, CompressionMode.Decompress);

        var peek = new byte[8192];
        var peekLen = 0;
        var total = 0L;
        var buf = new byte[64 * 1024];

        int n;
        while ((n = await gz.ReadAsync(buf.AsMemory(0, buf.Length), cancellationToken)) > 0)
        {
            if (peekLen < peek.Length)
            {
                var copy = Math.Min(n, peek.Length - peekLen);
                Buffer.BlockCopy(buf, 0, peek, peekLen, copy);
                peekLen += copy;
            }

            total += n;
            if (total > MaxDecompressedBytes)
            {
                throw new BackupFileException(
                    ApiErrorCodes.BackupFileTooLarge,
                    "Decompressed backup exceeds the maximum allowed size.");
            }
        }

        if (total < MinDecompressedBytes)
        {
            throw new BackupFileException(
                ApiErrorCodes.BackupInvalidFile,
                "Decompressed backup is empty or too small.");
        }

        if (!LooksLikePgDumpOrSql(peek.AsSpan(0, peekLen)))
        {
            throw new BackupFileException(
                ApiErrorCodes.BackupInvalidFile,
                "Backup content does not look like a PostgreSQL dump.");
        }
    }

    private static bool LooksLikePgDumpOrSql(ReadOnlySpan<byte> peek)
    {
        if (peek.IsEmpty)
        {
            return false;
        }

        // UTF-8 / ASCII текстовый dump.
        var text = Encoding.UTF8.GetString(peek);
        if (text.Contains('\0'))
        {
            return false;
        }

        // Типичный plain pg_dump (не custom-format). Одного "--" мало — слишком слабо.
        if (text.Contains("PostgreSQL database dump", StringComparison.OrdinalIgnoreCase)
            || text.Contains("pg_catalog.set_config", StringComparison.OrdinalIgnoreCase)
            || text.Contains("SELECT pg_catalog", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var hasSet = text.Contains("SET ", StringComparison.Ordinal);
        var hasDdl = text.Contains("CREATE ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ALTER ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("COPY ", StringComparison.OrdinalIgnoreCase);
        return hasSet && hasDdl;
    }

    private static void Collect(List<BackupFileInfoResponse> files, string directory, string folder)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.sql.gz"))
        {
            var info = new FileInfo(path);
            if (info.Length < MinCompressedBytes)
            {
                continue;
            }

            files.Add(new BackupFileInfoResponse
            {
                RelativePath = $"{folder}/{info.Name}",
                FileName = info.Name,
                Folder = folder,
                SizeBytes = info.Length,
                CreatedUtc = info.LastWriteTimeUtc.ToString("O")
            });
        }
    }
}
