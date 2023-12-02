-- 003_create_migration_history.sql
-- Tracks which migrations have been applied

CREATE TABLE IF NOT EXISTS migration_history (
    script_name  TEXT PRIMARY KEY,
    applied_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
