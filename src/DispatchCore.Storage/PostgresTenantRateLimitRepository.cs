using Dapper;
using DispatchCore.Core.Interfaces;
using DispatchCore.Core.Models;
using Npgsql;

namespace DispatchCore.Storage;

public sealed class PostgresTenantRateLimitRepository : ITenantRateLimitRepository
{
    private readonly string _connectionString;
    private const int DefaultMaxPerMinute = 10;

    public PostgresTenantRateLimitRepository(string connectionString)
    {
        _connectionString = connectionString;
        DapperConfig.Initialize();
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    public async Task<TenantRateLimitConfig?> GetAsync(string tenantId, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM tenant_rate_limits WHERE tenant_id = @TenantId";
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<TenantRateLimitConfig>(sql, new { TenantId = tenantId });
    }

    public async Task<int> GetMaxPerMinuteAsync(string tenantId, CancellationToken ct = default)
    {
        var config = await GetAsync(tenantId, ct);
        return config?.MaxPerMinute ?? DefaultMaxPerMinute;
    }
}
