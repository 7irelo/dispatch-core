using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DispatchCore.Storage;

public sealed class MigrationRunner
{
    private readonly string _connectionString;
    private readonly string _migrationsPath;
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(string connectionString, string migrationsPath, ILogger<MigrationRunner> logger)
    {
        _connectionString = connectionString;
        _migrationsPath = migrationsPath;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Ensure migration_history table exists (bootstrap)
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS migration_history (
                script_name TEXT PRIMARY KEY,
                applied_at  TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """);

        var applied = (await conn.QueryAsync<string>(
            "SELECT script_name FROM migration_history ORDER BY script_name"))
            .ToHashSet();

        var scripts = Directory.GetFiles(_migrationsPath, "*.sql")
            .OrderBy(f => Path.GetFileName(f))
            .ToList();

        foreach (var script in scripts)
        {
            var name = Path.GetFileName(script);
            if (applied.Contains(name))
            {
                _logger.LogDebug("Migration {Script} already applied, skipping", name);
                continue;
            }

            _logger.LogInformation("Applying migration {Script}", name);
            var sql = await File.ReadAllTextAsync(script, ct);
            await conn.ExecuteAsync(sql);
            await conn.ExecuteAsync(
                "INSERT INTO migration_history (script_name) VALUES (@Name) ON CONFLICT DO NOTHING",
                new { Name = name });
            _logger.LogInformation("Migration {Script} applied successfully", name);
        }
    }
}
