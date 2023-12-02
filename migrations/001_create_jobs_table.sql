-- 001_create_jobs_table.sql
-- Idempotent migration: creates the jobs table and indexes

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'job_status') THEN
        CREATE TYPE job_status AS ENUM (
            'Pending', 'Scheduled', 'Running', 'Succeeded', 'Failed', 'DeadLetter'
        );
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS jobs (
    job_id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id      TEXT NOT NULL,
    type           TEXT NOT NULL,
    payload        JSONB NOT NULL DEFAULT '{}',
    status         job_status NOT NULL DEFAULT 'Pending',
    run_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    attempts       INT NOT NULL DEFAULT 0,
    max_attempts   INT NOT NULL DEFAULT 3,
    last_error     TEXT,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    locked_by      TEXT,
    lock_until     TIMESTAMPTZ,
    partition_key  TEXT,
    idempotency_key TEXT
);

-- Unique constraint for idempotency per tenant
CREATE UNIQUE INDEX IF NOT EXISTS ix_jobs_tenant_idempotency
    ON jobs (tenant_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;

-- Index for polling due jobs
CREATE INDEX IF NOT EXISTS ix_jobs_poll
    ON jobs (status, run_at)
    WHERE status IN ('Pending', 'Scheduled');

-- Index for tenant queries
CREATE INDEX IF NOT EXISTS ix_jobs_tenant_id
    ON jobs (tenant_id, created_at DESC);

-- Index for partition-based polling
CREATE INDEX IF NOT EXISTS ix_jobs_partition_poll
    ON jobs (partition_key, status, run_at)
    WHERE status IN ('Pending', 'Scheduled');

-- Index for lock reaper
CREATE INDEX IF NOT EXISTS ix_jobs_lock_reaper
    ON jobs (status, lock_until)
    WHERE status = 'Running';
