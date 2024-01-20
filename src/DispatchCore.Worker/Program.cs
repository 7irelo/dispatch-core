using DispatchCore.Core.Interfaces;
using DispatchCore.Executor;
using DispatchCore.Executor.Handlers;
using DispatchCore.Locking;
using DispatchCore.RateLimit;
using DispatchCore.Storage;
using DispatchCore.Worker;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using StackExchange.Redis;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .Enrich.FromLogContext()
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog();

var connectionString = builder.Configuration.GetConnectionString("Postgres")!;
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")!;

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnectionString));

// Repositories
builder.Services.AddSingleton<IJobRepository>(new PostgresJobRepository(connectionString));
builder.Services.AddSingleton<ITenantRateLimitRepository>(new PostgresTenantRateLimitRepository(connectionString));

// Locking
builder.Services.AddSingleton<IDistributedLockProvider, RedisDistributedLockProvider>();

// Rate limiting
builder.Services.AddSingleton<IRateLimiter, RedisTokenBucketRateLimiter>();

// Handler registry
builder.Services.AddSingleton(sp =>
{
    var registry = new JobHandlerRegistry();
    registry.Register(new EmailSendHandler(sp.GetRequiredService<ILogger<EmailSendHandler>>()));
    registry.Register(new ReportGenerateHandler(sp.GetRequiredService<ILogger<ReportGenerateHandler>>()));
    return registry;
});

// Job channel + executor
builder.Services.AddSingleton(new JobChannel(capacity: 100));
builder.Services.AddSingleton<JobExecutor>();

// Migration runner
builder.Services.AddSingleton(sp =>
    new MigrationRunner(connectionString,
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "migrations"),
        sp.GetRequiredService<ILogger<MigrationRunner>>()));

// Worker config
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));

// Background services
builder.Services.AddHostedService<MigrationHostedService>();
builder.Services.AddHostedService<JobPollerService>();
builder.Services.AddHostedService<JobConsumerService>();
builder.Services.AddHostedService<LockReaperService>();

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddConsoleExporter())
    .WithMetrics(metrics => metrics.AddConsoleExporter());

var host = builder.Build();
host.Run();
