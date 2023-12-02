-- 002_create_rate_limit_config.sql
-- Tenant rate limit configuration overrides

CREATE TABLE IF NOT EXISTS tenant_rate_limits (
    tenant_id       TEXT PRIMARY KEY,
    max_per_minute  INT NOT NULL DEFAULT 10,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
