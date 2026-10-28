using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Configuration;
using WerkonWebServicesRatchet.Contracts.Common;
using WerkonWebServicesRatchet.Infrastructure.Backups;

namespace WerkonWebServicesRatchet.Tests;

public sealed class BackupUploadValidationTests
{
    [Fact]
    public async Task SaveUploaded_RejectsNonGzip()
    {
        var (service, root) = CreateService();
        await using var _ = new TempDir(root);

        await using var stream = new MemoryStream("not a gzip"u8.ToArray());
        var ex = await Assert.ThrowsAsync<BackupFileException>(() =>
            service.SaveUploadedAsync(stream, "evil.sql.gz"));

        Assert.Equal(ApiErrorCodes.BackupInvalidFile, ex.ErrorCode);
    }

    [Fact]
    public async Task SaveUploaded_RejectsBareGzExtension()
    {
        var (service, root) = CreateService();
        await using var _ = new TempDir(root);

        await using var stream = new MemoryStream(CreateGzip("SET x = 1;\nCREATE TABLE t(id int);\n"u8.ToArray()));
        var ex = await Assert.ThrowsAsync<BackupFileException>(() =>
            service.SaveUploadedAsync(stream, "dump.gz"));

        Assert.Equal(ApiErrorCodes.BackupInvalidFile, ex.ErrorCode);
    }

    [Fact]
    public async Task SaveUploaded_AcceptsPgDumpLikeSqlGz_WithForcedName()
    {
        var (service, root) = CreateService();
        await using var _ = new TempDir(root);

        var payload = Encoding.UTF8.GetBytes(
            "--\n-- PostgreSQL database dump\n--\nSET statement_timeout = 0;\nCREATE TABLE public.demo(id int);\n");
        await using var stream = new MemoryStream(CreateGzip(payload));
        var saved = await service.SaveUploadedAsync(stream, "client-name.sql.gz");

        Assert.StartsWith("daily/ratchet-upload-", saved.RelativePath);
        Assert.EndsWith(".sql.gz", saved.RelativePath);
        Assert.True(File.Exists(Path.Combine(root, saved.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    private static (BackupCatalogService Service, string Root) CreateService()
    {
        var root = Path.Combine(Path.GetTempPath(), "ratchet-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Backup:BackupsDirectory"] = root
            })
            .Build();
        return (new BackupCatalogService(config), root);
    }

    private static byte[] CreateGzip(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gz.Write(raw);
        }

        return ms.ToArray();
    }

    private sealed class TempDir(string path) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // ignore
            }

            return ValueTask.CompletedTask;
        }
    }
}
