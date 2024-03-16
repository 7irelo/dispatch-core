# Dispatch Core

A production-grade distributed job processing system built with .NET 8. Designed as an alternative to Hangfire with first-class support for multi-tenancy, rate limiting, and horizontal scaling.

[![Build & Test](https://github.com/7irelo/dispatch-core/actions/workflows/build.yml/badge.svg)](https://github.com/7irelo/dispatch-core/actions/workflows/build.yml)

## Features

- **Atomic job claiming** — `SELECT ... FOR UPDATE SKIP LOCKED` prevents double-processing
- **Multi-tenant** — every job is scoped to a `TenantId` with per-tenant rate limiting
- **Idempotent submissions** — duplicate `IdempotencyKey` per tenant returns the original job
- **Exponential backoff** — retries with jitter, automatic dead-lettering after max attempts
- **Redis token bucket** — configurable rate limits per tenant (default 10 jobs/min)
- **Distributed locking** — Redis locks with Lua-based compare-and-delete release
- **Partition sharding** — workers can target specific `PartitionKey` values for workload isolation
- **Channel-based scheduler** — bounded `Channel<T>` with backpressure and configurable concurrency
- **Crash recovery** — lock reaper resets expired locks so stalled jobs get re-processed
- **Blazor dashboard** — real-time metrics, job search, cancel/requeue actions
- **Observability** — Serilog structured logging, OpenTelemetry tracing + metrics, health checks

## Architecture

```
┌──────────────┐     ┌──────────────────┐     ┌───────────────────┐
│   API        │     │   Worker         │     │   Dashboard       │
│   (REST)     │     │   (BackgroundSvc)│     │   (Blazor Server) │
└──────┬───────┘     └────────┬─────────┘     └─────────┬─────────┘
       │                      │                         │
       ├──────────────────────┼─────────────────────────┤
       │                      │                         │
  ┌────▼────┐  ┌──────────────▼──────────────┐    ┌─────▼─────┐
  │Contracts│  │  Core (models, interfaces,  │    │  Storage   │
  │ (DTOs)  │  │  retry policy, scheduling)  │    │  (Dapper)  │
  └─────────┘  └──────────────┬──────────────┘    └─────┬─────┘
                              │                         │
               ┌──────────────┼──────────────┐          │
               │              │              │          │
          ┌────▼───┐   ┌─────▼─────┐  ┌─────▼─────┐   │
          │Locking │   │ RateLimit │  │ Executor  │   │
          │(Redis) │   │ (Redis)   │  │ (Channel) │   │
          └────────┘   └───────────┘  └───────────┘   │
                                                       │
                              ┌─────────────────────────┘
                              │
                    ┌─────────▼─────────┐
                    │  PostgreSQL + Redis │
                    └───────────────────┘
```

| Project | Description |
|---------|-------------|
| `DispatchCore.Api` | .NET 8 Minimal API — job submission, queries, admin |
| `DispatchCore.Worker` | Worker Service — polls, executes, reaps stale locks |
| `DispatchCore.Dashboard` | Blazor Server — admin UI with metrics and job management |
| `DispatchCore.Contracts` | Shared DTOs — `CreateJobRequest`, `JobResponse`, `MetricsResponse` |
| `DispatchCore.Core` | Domain — `Job` model, interfaces, `RetryPolicy` |
| `DispatchCore.Storage` | Postgres persistence — Dapper repos, migration runner |
| `DispatchCore.Locking` | Redis distributed locking with Lua release scripts |
| `DispatchCore.RateLimit` | Redis token bucket rate limiter |
| `DispatchCore.Executor` | Bounded channel scheduler, handler registry, execution pipeline |

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://www.docker.com/) (for Postgres and Redis)

### 1. Start Infrastructure

```bash
docker-compose up -d
```

This starts:
- **PostgreSQL 16** on port `5432` (db: `dispatch_core`, user: `dispatch`, pass: `dispatch_secret`)
- **Redis 7** on port `6379`

### 2. Run the API

```bash
dotnet run --project src/DispatchCore.Api
```

Migrations run automatically on startup. The API is available at `http://localhost:5000`.

### 3. Run the Worker

```bash
dotnet run --project src/DispatchCore.Worker
```

The worker starts polling for due jobs immediately.

### 4. Run the Dashboard (optional)

```bash
dotnet run --project src/DispatchCore.Dashboard
```

Navigate to `http://localhost:5002` and click "Login as Admin".

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/jobs` | Submit a new job |
| `GET` | `/jobs/{id}` | Get job by ID |
| `GET` | `/tenants/{tenantId}/jobs` | List jobs for a tenant (`?limit=50&offset=0`) |
| `POST` | `/jobs/{id}/cancel` | Cancel a pending/scheduled job |
| `POST` | `/admin/requeue-deadletter/{id}` | Requeue a dead-lettered job |
| `GET` | `/admin/metrics` | Aggregate job status counts |
| `GET` | `/health` | Health check (Postgres + Redis) |

### Submit a Job

```bash
curl -X POST http://localhost:5000/jobs \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "acme-corp",
    "type": "email.send",
    "payload": { "to": "user@example.com", "subject": "Hello" },
    "maxAttempts": 5,
    "idempotencyKey": "welcome-email-user-42"
  }'
```

Submitting the same `idempotencyKey` for the same `tenantId` returns the original job instead of creating a duplicate.

### Schedule a Job

```bash
curl -X POST http://localhost:5000/jobs \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "acme-corp",
    "type": "report.generate",
    "payload": { "reportId": "monthly-sales" },
    "runAt": "2024-12-01T09:00:00Z"
  }'
```

## Job Lifecycle

```
  ┌─────────┐    RunAt <= now    ┌─────────┐
  │ Pending ├───────────────────►│ Running │
  └────┬────┘                    └────┬────┘
       │                              │
  RunAt > now                    ┌────┴────┐
       │                         │         │
  ┌────▼─────┐            success│         │failure
  │Scheduled ├──RunAt<=now──►    │         │
  └──────────┘                   │         │
                           ┌─────▼──┐  ┌───▼────────┐
                           │Succeeded│  │ attempts < │
                           └────────┘  │ max?       │
                                       └──┬────┬────┘
                                     yes  │    │ no
                                ┌─────────▼┐ ┌─▼──────────┐
                                │  Pending  │ │ DeadLetter │
                                │(retry w/  │ └────────────┘
                                │ backoff)  │
                                └──────────┘
```

Jobs can also be **cancelled** (moves to `Failed` with "Cancelled by user") or **requeued** from dead letter (resets to `Pending` with attempts zeroed).

## Job Model

| Column | Type | Description |
|--------|------|-------------|
| `job_id` | `UUID` | Primary key |
| `tenant_id` | `TEXT` | Tenant identifier |
| `type` | `TEXT` | Handler type (e.g. `email.send`) |
| `payload` | `JSONB` | Arbitrary JSON payload |
| `status` | `ENUM` | Pending, Scheduled, Running, Succeeded, Failed, DeadLetter |
| `run_at` | `TIMESTAMPTZ` | When the job becomes eligible for processing |
| `attempts` | `INT` | Current attempt count |
| `max_attempts` | `INT` | Max retries before dead-lettering (default 3) |
| `last_error` | `TEXT` | Error message from last failure |
| `locked_by` | `TEXT` | Worker ID holding the lock |
| `lock_until` | `TIMESTAMPTZ` | Lock expiry (reaper resets if past) |
| `partition_key` | `TEXT` | Optional partition for worker sharding |
| `idempotency_key` | `TEXT` | Unique per tenant for deduplication |

## Writing Job Handlers

Implement `IJobHandler` and register it in the worker's handler registry:

```csharp
public sealed class InvoiceHandler : IJobHandler
{
    public string JobType => "invoice.generate";

    public async Task HandleAsync(Job job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<InvoicePayload>(job.Payload);
        // your logic here
    }
}
```

Register in `Program.cs`:

```csharp
registry.Register(new InvoiceHandler(logger));
```

Built-in sample handlers: `email.send`, `report.generate`.

## Worker Configuration

Configure via `appsettings.json` or environment variables:

```json
{
  "Worker": {
    "PollIntervalMs": 1000,
    "BatchSize": 10,
    "Concurrency": 5,
    "ReaperIntervalMs": 30000
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `PollIntervalMs` | `1000` | Milliseconds between polling cycles |
| `BatchSize` | `10` | Max jobs claimed per poll |
| `Concurrency` | `5` | Max parallel job executions |
| `ReaperIntervalMs` | `30000` | Milliseconds between lock reaper sweeps |

### Partition Sharding

Run workers targeting specific partitions to isolate workloads:

```bash
WORKER_PARTITION_KEY=us-east dotnet run --project src/DispatchCore.Worker
WORKER_PARTITION_KEY=eu-west dotnet run --project src/DispatchCore.Worker
```

Workers without a partition key process all jobs regardless of partition.

## Rate Limiting

Rate limiting uses a Redis token bucket algorithm. The default is **10 jobs per minute per tenant**.

Override per-tenant limits in the `tenant_rate_limits` table:

```sql
INSERT INTO tenant_rate_limits (tenant_id, max_per_minute)
VALUES ('high-volume-tenant', 100)
ON CONFLICT (tenant_id) DO UPDATE SET max_per_minute = 100, updated_at = now();
```

When a tenant exceeds their limit, the job is **rescheduled without incrementing the attempt counter** — it retries transparently after a short delay.

## Migrations

SQL migration scripts live in `/migrations` and are applied idempotently on startup by both the API and Worker:

| Script | Purpose |
|--------|---------|
| `001_create_jobs_table.sql` | Jobs table, `job_status` enum, indexes |
| `002_create_rate_limit_config.sql` | Per-tenant rate limit overrides |
| `003_create_migration_history.sql` | Migration tracking table |

To add a new migration, create `004_your_migration.sql` in the `/migrations` directory. It will be applied automatically on next startup.

## Testing

```bash
# Unit tests (no infrastructure needed)
dotnet test tests/DispatchCore.Tests.Unit

# Integration tests (requires Docker for Testcontainers)
dotnet test tests/DispatchCore.Tests.Integration
```

**Unit tests** (19 tests) cover:
- Retry policy — exponential backoff calculation, dead letter threshold
- Idempotency — deduplication logic, tenant isolation
- Job executor — rate limit reschedule, handler dispatch, retry/dead letter flow

**Integration tests** (9 tests) use Testcontainers to spin up real Postgres and Redis:
- Job CRUD and round-trip persistence
- Idempotency key enforcement and tenant isolation
- Atomic claiming with `FOR UPDATE SKIP LOCKED`
- Partition-based polling
- Lock reaper behavior
- Token bucket rate limiter enforcement

## CI/CD

GitHub Actions runs on every push and pull request:

- **Build & Unit Tests** — restore, build, run unit tests, upload `.trx` artifacts
- **Integration Tests** — spins up Postgres 16 + Redis 7 service containers, runs integration tests

## Tech Stack

| Component | Technology |
|-----------|------------|
| Runtime | .NET 8 |
| API | ASP.NET Core Minimal APIs |
| Worker | `BackgroundService` |
| Dashboard | Blazor Server |
| Database | PostgreSQL 16 |
| Cache/Locking | Redis 7 |
| ORM | Dapper + Npgsql |
| Logging | Serilog |
| Tracing | OpenTelemetry |
| Testing | xUnit, FluentAssertions, NSubstitute, Testcontainers |

## License

[MIT](LICENSE)
