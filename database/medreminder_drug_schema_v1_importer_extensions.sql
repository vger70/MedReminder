-- MedReminder pharmaceutical schema V1: importer extensions
-- Apply after medreminder_drug_schema_v1.sql.
-- This keeps staging diagnostics per run and adds fields required for
-- accepted/rejected record accounting; no existing core data is removed.

BEGIN;

ALTER TABLE source.import_run
    ADD COLUMN IF NOT EXISTS records_accepted bigint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS records_rejected bigint NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS source.import_run_file (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    import_run_id bigint NOT NULL REFERENCES source.import_run(id),
    source_file_name text NOT NULL,
    source_file_size bigint NOT NULL,
    source_sha256 text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (import_run_id, source_file_name)
);

-- The source schema has no natural key for substances. This index makes
-- source-name upserts idempotent while preserving the original spelling.
CREATE UNIQUE INDEX IF NOT EXISTS ux_core_substance_preferred_name
    ON core.substance (lower(preferred_name));

COMMIT;
