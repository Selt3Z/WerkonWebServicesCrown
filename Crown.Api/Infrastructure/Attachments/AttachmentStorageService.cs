using Microsoft.Extensions.Options;

namespace Crown.Infrastructure.Attachments;

public sealed class AttachmentStorageService(IOptions<AttachmentStorageOptions> options)
{
    private readonly AttachmentStorageOptions _options = options.Value;

    public long MaxFileBytes => _options.MaxFileBytes;

    public string RootPath => _options.RootPath;

    public string HostPath =>
        string.IsNullOrWhiteSpace(_options.HostPath) ? _options.RootPath : _options.HostPath.Trim();

    public string? LegacyHostPath =>
        string.IsNullOrWhiteSpace(_options.LegacyHostPath) ? null : _options.LegacyHostPath.Trim();

    public bool HasDistinctLegacyRoot
    {
        get
        {
            var legacy = GetLegacyRootOrNull();
            if (legacy is null)
            {
                return false;
            }

            return !string.Equals(
                Path.GetFullPath(_options.RootPath),
                Path.GetFullPath(legacy),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Абсолютный путь под корнем записи (без проверки существования).</summary>
    public string GetAbsolutePath(string relativePath) =>
        CombineUnderRoot(_options.RootPath, relativePath);

    /// <summary>
    /// Ищет файл: сначала основной диск, потом legacy (если смонтирован).
    /// Нужен при смене ATTACHMENTS_HOST_PATH без полной миграции.
    /// </summary>
    public string? ResolveExistingPath(string relativePath)
    {
        var primary = CombineUnderRoot(_options.RootPath, relativePath);
        if (File.Exists(primary))
        {
            return primary;
        }

        var legacyRoot = GetLegacyRootOrNull();
        if (legacyRoot is null)
        {
            return null;
        }

        var legacy = CombineUnderRoot(legacyRoot, relativePath);
        return File.Exists(legacy) ? legacy : null;
    }

    public async Task<(string RelativePath, long Size)> SaveAsync(
        long requestId,
        string originalFileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.RootPath);
        var safeName = SanitizeFileName(originalFileName);
        var relative = Path.Combine(requestId.ToString(), $"{Guid.NewGuid():N}_{safeName}");
        var absolute = GetAbsolutePath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);

        await using var fs = File.Create(absolute);
        await content.CopyToAsync(fs, cancellationToken);
        var size = fs.Length;
        return (relative.Replace('\\', '/'), size);
    }

    public void DeleteIfExists(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        // Удаляем везде, где лежит копия (основной + legacy).
        foreach (var root in EnumerateRoots())
        {
            var absolute = CombineUnderRoot(root, relativePath);
            if (File.Exists(absolute))
            {
                File.Delete(absolute);
            }
        }
    }

    private IEnumerable<string> EnumerateRoots()
    {
        yield return _options.RootPath;
        var legacy = GetLegacyRootOrNull();
        if (legacy is not null
            && !string.Equals(
                Path.GetFullPath(_options.RootPath),
                Path.GetFullPath(legacy),
                StringComparison.OrdinalIgnoreCase))
        {
            yield return legacy;
        }
    }

    private string? GetLegacyRootOrNull()
    {
        if (string.IsNullOrWhiteSpace(_options.LegacyRootPath))
        {
            return null;
        }

        var legacy = _options.LegacyRootPath.Trim();
        return Directory.Exists(legacy) ? legacy : null;
    }

    private static string CombineUnderRoot(string root, string relativePath)
    {
        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(rootFull, relativePath));
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Некорректный путь вложения.");
        }

        return full;
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "file.bin" : name;
    }
}
