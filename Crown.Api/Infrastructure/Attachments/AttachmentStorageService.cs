using Microsoft.Extensions.Options;

namespace Crown.Infrastructure.Attachments;

public sealed class AttachmentStorageService(IOptions<AttachmentStorageOptions> options)
{
    private readonly AttachmentStorageOptions _options = options.Value;

    public long MaxFileBytes => _options.MaxFileBytes;

    public string GetAbsolutePath(string relativePath)
    {
        var root = Path.GetFullPath(_options.RootPath);
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Некорректный путь вложения.");
        }

        return full;
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

        var absolute = GetAbsolutePath(relativePath);
        if (File.Exists(absolute))
        {
            File.Delete(absolute);
        }
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
