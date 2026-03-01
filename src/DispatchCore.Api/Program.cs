using System.Text.Json;
using DispatchCore.Contracts;
using DispatchCore.Core.Interfaces;
using DispatchCore.Core.Models;
using DispatchCore.Storage;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .Enrich.FromLogContext()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var connectionString = builder.Configuration.GetConnectionString("Postgres")!;
var redisConnection = builder.Configuration.GetConnectionString("Redis")!;

// Services
builder.Services.AddSingleton<IJobRepository>(new PostgresJobRepository(connectionString));
builder.Services.AddSingleton<MigrationRunner>(sp =>
    new MigrationRunner(connectionString,
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "migrations"),
        sp.GetRequiredService<ILogger<MigrationRunner>>()));

// Health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres")
    .AddRedis(redisConnection, name: "redis");

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());

var app = builder.Build();

// Run migrations on startup
using (var scope = app.Services.CreateScope())
{
    var runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
    await runner.RunAsync();
}

app.UseSerilogRequestLogging();

// Health checks
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description
            })
        };
        await context.Response.WriteAsJsonAsync(result);
    }
});

// POST /jobs
app.MapPost("/jobs", async (CreateJobRequest request, IJobRepository repo) =>
{
    // Idempotency check
    if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
    {
        var existing = await repo.FindByIdempotencyKeyAsync(request.TenantId, request.IdempotencyKey);
        if (existing is not null)
        {
            return Results.Ok(MapToResponse(existing));
        }
    }

    var job = new Job
    {
        JobId = Guid.NewGuid(),
        TenantId = request.TenantId,
        Type = request.Type,
        Payload = request.Payload?.GetRawText() ?? "{}",
        Status = request.RunAt.HasValue && request.RunAt > DateTimeOffset.UtcNow
            ? JobStatus.Scheduled
            : JobStatus.Pending,
        RunAt = request.RunAt ?? DateTimeOffset.UtcNow,
        MaxAttempts = request.MaxAttempts,
        PartitionKey = request.PartitionKey,
        IdempotencyKey = request.IdempotencyKey,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    var created = await repo.CreateAsync(job);
    return Results.Created($"/jobs/{created.JobId}", MapToResponse(created));
});

// GET /jobs/{id}
app.MapGet("/jobs/{id:guid}", async (Guid id, IJobRepository repo) =>
{
    var job = await repo.GetByIdAsync(id);
    return job is null ? Results.NotFound() : Results.Ok(MapToResponse(job));
});

// GET /tenants/{tenantId}/jobs
app.MapGet("/tenants/{tenantId}/jobs", async (string tenantId, IJobRepository repo, int? limit, int? offset) =>
{
    var jobs = await repo.GetByTenantAsync(tenantId, limit ?? 50, offset ?? 0);
    return Results.Ok(jobs.Select(MapToResponse));
});

// POST /jobs/{id}/cancel
app.MapPost("/jobs/{id:guid}/cancel", async (Guid id, IJobRepository repo) =>
{
    var job = await repo.GetByIdAsync(id);
    if (job is null) return Results.NotFound();

    if (job.Status is JobStatus.Running or JobStatus.Succeeded or JobStatus.DeadLetter)
        return Results.BadRequest(new { error = $"Cannot cancel job in {job.Status} status" });

    job.Status = JobStatus.Failed;
    job.LastError = "Cancelled by user";
    job.LockedBy = null;
    job.LockUntil = null;
    await repo.UpdateAsync(job);
    return Results.Ok(MapToResponse(job));
});

// POST /admin/requeue-deadletter/{id}
app.MapPost("/admin/requeue-deadletter/{id:guid}", async (Guid id, IJobRepository repo) =>
{
    var job = await repo.GetByIdAsync(id);
    if (job is null) return Results.NotFound();
    if (job.Status != JobStatus.DeadLetter)
        return Results.BadRequest(new { error = "Job is not in dead letter status" });

    job.Status = JobStatus.Pending;
    job.Attempts = 0;
    job.LastError = null;
    job.RunAt = DateTimeOffset.UtcNow;
    job.LockedBy = null;
    job.LockUntil = null;
    await repo.UpdateAsync(job);
    return Results.Ok(MapToResponse(job));
});

// GET /admin/metrics
app.MapGet("/admin/metrics", async (IJobRepository repo) =>
{
    var metrics = await repo.GetMetricsAsync();
    return Results.Ok(metrics);
});

app.Run();

static JobResponse MapToResponse(Job job) => new()
{
    JobId = job.JobId,
    TenantId = job.TenantId,
    Type = job.Type,
    Payload = string.IsNullOrEmpty(job.Payload) ? null : JsonDocument.Parse(job.Payload).RootElement,
    Status = job.Status,
    RunAt = job.RunAt,
    Attempts = job.Attempts,
    MaxAttempts = job.MaxAttempts,
    LastError = job.LastError,
    CreatedAt = job.CreatedAt,
    UpdatedAt = job.UpdatedAt,
    LockedBy = job.LockedBy,
    PartitionKey = job.PartitionKey,
    IdempotencyKey = job.IdempotencyKey
};
