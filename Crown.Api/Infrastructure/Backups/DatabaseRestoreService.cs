using System.Diagnostics;
using System.IO.Compression;
using Npgsql;

namespace Crown.Infrastructure.Backups;

public sealed class DatabaseRestoreService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseRestoreService> _logger;
    private readonly BackupCatalogService _backupCatalog;

    public DatabaseRestoreService(
        IConfiguration configuration,
        ILogger<DatabaseRestoreService> logger,
        BackupCatalogService backupCatalog)
    {
        _configuration = configuration;
        _logger = logger;
        _backupCatalog = backupCatalog;
    }

    public async Task RestoreAsync(string dumpFilePath, CancellationToken cancellationToken)
    {
        // Без bash: gunzip в процессе + stdin в psql (нет shell injection по path).
        await _backupCatalog.ValidateDumpFileAsync(dumpFilePath, cancellationToken);

        var connectionString = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string is not configured.");

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var database = builder.Database
            ?? throw new InvalidOperationException("Database name is missing from connection string.");
        var host = builder.Host ?? "postgres";
        var port = builder.Port;
        var user = builder.Username ?? "postgres";
        var password = builder.Password ?? string.Empty;

        await RecreateDatabaseAsync(builder, database, cancellationToken);

        var psi = new ProcessStartInfo
        {
            FileName = "psql",
            ArgumentList =
            {
                "-v", "ON_ERROR_STOP=1",
                "-h", host,
                "-p", port.ToString(),
                "-U", user,
                "-d", database
            },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["PGPASSWORD"] = password;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start psql restore process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await using (var file = new FileStream(dumpFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var gz = new GZipStream(file, CompressionMode.Decompress))
            {
                var buf = new byte[64 * 1024];
                var total = 0L;
                int n;
                while ((n = await gz.ReadAsync(buf.AsMemory(0, buf.Length), cancellationToken)) > 0)
                {
                    total += n;
                    if (total > BackupCatalogService.MaxDecompressedBytes)
                    {
                        try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                        throw new BackupFileException(
                            Contracts.Common.ApiErrorCodes.BackupFileTooLarge,
                            "Decompressed backup exceeds the maximum allowed size.");
                    }

                    await process.StandardInput.BaseStream.WriteAsync(buf.AsMemory(0, n), cancellationToken);
                }
            }

            await process.StandardInput.DisposeAsync();
        }
        catch (BackupFileException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            _logger.LogError(ex, "Failed streaming dump into psql from {DumpFile}.", dumpFilePath);
            throw new InvalidOperationException("Restore failed while streaming dump into database.");
        }

        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;
        var stdout = await stdoutTask;

        if (process.ExitCode != 0)
        {
            _logger.LogError(
                "Database restore failed. Exit={ExitCode}. Stderr={Stderr}. Stdout={Stdout}",
                process.ExitCode, Truncate(stderr), Truncate(stdout));
            // Клиенту — без сырого stderr (пути, роли, SQL).
            throw new InvalidOperationException("Database restore failed. See server logs for details.");
        }

        _logger.LogInformation("Database restore completed from {DumpFile}.", Path.GetFileName(dumpFilePath));
    }

    private static string Truncate(string? value, int max = 2000) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= max ? value : value[..max] + "…";

    private static async Task RecreateDatabaseAsync(
        NpgsqlConnectionStringBuilder source,
        string database,
        CancellationToken cancellationToken)
    {
        var admin = new NpgsqlConnectionStringBuilder(source.ConnectionString)
        {
            Database = "postgres"
        };

        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var terminate = connection.CreateCommand())
        {
            terminate.CommandText =
                """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @db AND pid <> pg_backend_pid();
                """;
            terminate.Parameters.AddWithValue("db", database);
            await terminate.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var drop = connection.CreateCommand())
        {
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{EscapeIdentifier(database)}\" WITH (FORCE);";
            try
            {
                await drop.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (PostgresException)
            {
                drop.CommandText = $"DROP DATABASE IF EXISTS \"{EscapeIdentifier(database)}\";";
                await drop.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{EscapeIdentifier(database)}\";";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string EscapeIdentifier(string value) => value.Replace("\"", "\"\"", StringComparison.Ordinal);
}
