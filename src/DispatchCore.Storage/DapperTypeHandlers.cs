using System.Data;
using Dapper;
using DispatchCore.Contracts;

namespace DispatchCore.Storage;

public class JobStatusTypeHandler : SqlMapper.TypeHandler<JobStatus>
{
    public override void SetValue(IDbDataParameter parameter, JobStatus value)
    {
        parameter.Value = value.ToString();
        parameter.DbType = DbType.String;
    }

    public override JobStatus Parse(object value)
    {
        return Enum.Parse<JobStatus>(value.ToString()!, ignoreCase: true);
    }
}

public static class DapperConfig
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        SqlMapper.AddTypeHandler(new JobStatusTypeHandler());

        // Map snake_case Postgres columns to PascalCase C# properties
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        _initialized = true;
    }
}
