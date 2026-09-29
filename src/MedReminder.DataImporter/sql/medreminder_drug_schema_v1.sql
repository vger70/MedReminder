-- MedReminder - Pharmaceutical Database V1
-- PostgreSQL 18
-- Primary source: AIFA Open Data (CC BY 4.0)
--
-- Scope:
--   * current-state pharmaceutical master data
--   * AIFA packages/products/substances/ATC
--   * source provenance and idempotent import support
--   * no historical versions of pharmaceutical data
--
-- NOTE:
--   staging tables intentionally preserve AIFA values as text.
--   Normalization into core is performed by the import process.

S\set ON_ERROR_STOP on

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_roles
        WHERE rolname = 'pharmaceutical'
    ) THEN
        CREATE ROLE pharmaceutical
            LOGIN
            PASSWORD 'pharmaceutical';
    END IF;
END
$$;

SELECT 'CREATE DATABASE pharmaceutical OWNER pharmaceutical'
WHERE NOT EXISTS (
    SELECT 1
    FROM pg_database
    WHERE datname = 'pharmaceutical'
)
\gexec

\connect pharmaceutical

SET ROLE pharmaceutical;

CREATE SCHEMA IF NOT EXISTS source AUTHORIZATION pharmaceutical;
CREATE SCHEMA IF NOT EXISTS staging AUTHORIZATION pharmaceutical;
CREATE SCHEMA IF NOT EXISTS core AUTHORIZATION pharmaceutical;
CREATE SCHEMA IF NOT EXISTS app AUTHORIZATION pharmaceutical;

ALTER DATABASE pharmaceutical
SET search_path = app, core, staging, source, public;

BEGIN;

-- ============================================================
-- SOURCE / PROVENANCE
-- ============================================================

CREATE TABLE IF NOT EXISTS source.source (
    id                       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code                     text NOT NULL UNIQUE,
    name                     text NOT NULL,
    publisher                text,
    country_code             char(2),
    license_name             text,
    license_url              text,
    attribution_required     boolean NOT NULL DEFAULT false,
    commercial_use_allowed   boolean,
    redistribution_allowed  boolean,
    modification_allowed     boolean,
    created_at               timestamptz NOT NULL DEFAULT now(),
    updated_at               timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS source.dataset (
    id                       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source_id                bigint NOT NULL REFERENCES source.source(id),
    code                     text NOT NULL,
    name                     text NOT NULL,
    uri                      text,
    format                   text,
    license_name             text,
    license_url              text,
    created_at               timestamptz NOT NULL DEFAULT now(),
    updated_at               timestamptz NOT NULL DEFAULT now(),
    UNIQUE (source_id, code)
);

CREATE TABLE IF NOT EXISTS source.import_run (
    id                       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    dataset_id               bigint NOT NULL REFERENCES source.dataset(id),
    started_at               timestamptz NOT NULL DEFAULT now(),
    completed_at             timestamptz,
    status                   text NOT NULL,
    source_last_updated_at   timestamptz,
    source_file_name         text,
    source_file_size         bigint,
    source_sha256             text,
    records_read             bigint NOT NULL DEFAULT 0,
    records_inserted         bigint NOT NULL DEFAULT 0,
    records_updated          bigint NOT NULL DEFAULT 0,
    records_deactivated      bigint NOT NULL DEFAULT 0,
    error_message             text
);

-- ============================================================
-- STAGING
-- ============================================================

CREATE TABLE IF NOT EXISTS staging.aifa_package (
    import_run_id             bigint NOT NULL REFERENCES source.import_run(id),
    source_row_number         bigint NOT NULL,
    source_hash               text NOT NULL,

    codice_aic                text,
    cod_farmaco               text,
    cod_confezione            text,
    denominazione             text,
    descrizione               text,
    codice_ditta              text,
    ragione_sociale           text,
    stato_amministrativo      text,
    tipo_procedura            text,
    forma                     text,
    codice_atc                text,
    pa_associati              text,
    fornitura                 text,
    link_fi                   text,
    link_rcp                  text,

    loaded_at                 timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (import_run_id, source_row_number)
);

CREATE INDEX IF NOT EXISTS ix_aifa_package_import_aic
    ON staging.aifa_package (import_run_id, codice_aic);

CREATE INDEX IF NOT EXISTS ix_aifa_package_import_drug
    ON staging.aifa_package (import_run_id, cod_farmaco);

CREATE INDEX IF NOT EXISTS ix_aifa_package_import_atc
    ON staging.aifa_package (import_run_id, codice_atc);

CREATE TABLE IF NOT EXISTS staging.aifa_package_ingredient (
    import_run_id             bigint NOT NULL REFERENCES source.import_run(id),
    source_row_number         bigint NOT NULL,
    source_hash               text NOT NULL,

    codice_aic                text,
    principio_attivo          text,
    quantita                  text,
    unita_misura              text,

    loaded_at                 timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (import_run_id, source_row_number)
);

CREATE INDEX IF NOT EXISTS ix_aifa_pa_import_aic
    ON staging.aifa_package_ingredient (import_run_id, codice_aic);

CREATE TABLE IF NOT EXISTS staging.aifa_atc (
    import_run_id             bigint NOT NULL REFERENCES source.import_run(id),
    source_row_number         bigint NOT NULL,
    source_hash               text NOT NULL,

    codice_atc                text,
    descrizione               text,

    loaded_at                 timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (import_run_id, source_row_number)
);

CREATE INDEX IF NOT EXISTS ix_aifa_atc_import_code
    ON staging.aifa_atc (import_run_id, codice_atc);

-- ============================================================
-- CORE - REFERENCE DATA
-- ============================================================

CREATE TABLE IF NOT EXISTS core.organisation (
    id                       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name                     text NOT NULL,
    country_code             char(2),
    is_active                boolean NOT NULL DEFAULT true,
    created_at               timestamptz NOT NULL DEFAULT now(),
    updated_at               timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_core_organisation_name_country
    ON core.organisation (lower(name), country_code);

CREATE TABLE IF NOT EXISTS core.external_identifier (
    id                       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    entity_type              text NOT NULL,
    entity_id                bigint NOT NULL,
    namespace                text NOT NULL,
    identifier_type          text NOT NULL,
    identifier_value         text NOT NULL,
    created_at               timestamptz NOT NULL DEFAULT now(),
    UNIQUE (namespace, identifier_type, identifier_value)
);

CREATE INDEX IF NOT EXISTS ix_core_external_identifier_entity
    ON core.external_identifier (entity_type, entity_id);

CREATE TABLE IF NOT EXISTS core.substance (
    id                       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    preferred_name           text NOT NULL,
    substance_type           text NOT NULL DEFAULT 'ACTIVE',
    is_active                boolean NOT NULL DEFAULT true,
    created_at               timestamptz NOT NULL DEFAULT now(),
    updated_at               timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS core.substance_name (
    id                       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    substance_id             bigint NOT NULL REFERENCES core.substance(id),
    name                     text NOT NULL,
    language_code             varchar(10),
    name_type                 text NOT NULL DEFAULT 'SYNONYM',
    source_id                 bigint REFERENCES source.source(id),
    created_at                timestamptz NOT NULL DEFAULT now(),
    UNIQUE (substance_id, name, language_code)
);

CREATE INDEX IF NOT EXISTS ix_core_substance_name_lookup
    ON core.substance_name (lower(name));

CREATE TABLE IF NOT EXISTS core.atc (
    code                     varchar(10) PRIMARY KEY,
    parent_code              varchar(10) REFERENCES core.atc(code),
    level                    smallint NOT NULL,
    description              text NOT NULL,
    is_active                boolean NOT NULL DEFAULT true,
    updated_at               timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_core_atc_parent
    ON core.atc (parent_code);

CREATE TABLE IF NOT EXISTS core.pharmaceutical_form (
    id                       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source_id                bigint REFERENCES source.source(id),
    source_code              text,
    name                     text NOT NULL,
    is_active                boolean NOT NULL DEFAULT true,
    updated_at               timestamptz NOT NULL DEFAULT now(),
    UNIQUE (source_id, source_code)
);

CREATE TABLE IF NOT EXISTS core.supply_classification (
    id                       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source_id                bigint REFERENCES source.source(id),
    source_code              text,
    name                     text NOT NULL,
    is_active                boolean NOT NULL DEFAULT true,
    updated_at               timestamptz NOT NULL DEFAULT now(),
    UNIQUE (source_id, source_code)
);

CREATE TABLE IF NOT EXISTS core.authorization_procedure_type (
    id                       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source_id                bigint REFERENCES source.source(id),
    source_code              text,
    name                     text NOT NULL,
    is_active                boolean NOT NULL DEFAULT true,
    updated_at               timestamptz NOT NULL DEFAULT now(),
    UNIQUE (source_id, source_code)
);

-- ============================================================
-- CORE - PRODUCTS / PACKAGES
-- ============================================================

CREATE TABLE IF NOT EXISTS core.medicinal_product (
    id                       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source_id                bigint REFERENCES source.source(id),
    source_code              text,
    name                     text NOT NULL,
    organisation_id          bigint REFERENCES core.organisation(id),
    is_active                boolean NOT NULL DEFAULT true,
    created_at               timestamptz NOT NULL DEFAULT now(),
    updated_at               timestamptz NOT NULL DEFAULT now(),
    UNIQUE (source_id, source_code)
);

CREATE INDEX IF NOT EXISTS ix_core_medicinal_product_name
    ON core.medicinal_product (lower(name));

CREATE TABLE IF NOT EXISTS core.package (
    id                       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    medicinal_product_id     bigint NOT NULL REFERENCES core.medicinal_product(id),

    aic                      varchar(9),
    package_code             text,
    description              text,
    pharmaceutical_form_id  bigint REFERENCES core.pharmaceutical_form(id),
    atc_code                 varchar(10) REFERENCES core.atc(code),
    supply_classification_id bigint REFERENCES core.supply_classification(id),
    authorization_procedure_type_id bigint
                             REFERENCES core.authorization_procedure_type(id),

    administrative_status    text,
    is_active                boolean NOT NULL DEFAULT true,

    link_fi                  text,
    link_rcp                 text,

    created_at               timestamptz NOT NULL DEFAULT now(),
    updated_at               timestamptz NOT NULL DEFAULT now(),

    UNIQUE (aic)
);

CREATE INDEX IF NOT EXISTS ix_core_package_product
    ON core.package (medicinal_product_id);

CREATE INDEX IF NOT EXISTS ix_core_package_atc
    ON core.package (atc_code);

CREATE INDEX IF NOT EXISTS ix_core_package_active
    ON core.package (is_active);

CREATE TABLE IF NOT EXISTS core.package_ingredient (
    id                       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    package_id               bigint NOT NULL REFERENCES core.package(id),
    substance_id             bigint REFERENCES core.substance(id),

    substance_name_source    text,
    strength_value            numeric,
    strength_unit             text,
    strength_raw              text,

    role                     text NOT NULL DEFAULT 'ACTIVE',
    ingredient_status        text NOT NULL DEFAULT 'KNOWN',

    created_at               timestamptz NOT NULL DEFAULT now(),
    updated_at               timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_core_package_ingredient_package
    ON core.package_ingredient (package_id);

CREATE INDEX IF NOT EXISTS ix_core_package_ingredient_substance
    ON core.package_ingredient (substance_id);

CREATE TABLE IF NOT EXISTS core.package_atc (
    package_id               bigint NOT NULL REFERENCES core.package(id),
    atc_code                 varchar(10) NOT NULL REFERENCES core.atc(code),
    source_id                bigint REFERENCES source.source(id),
    PRIMARY KEY (package_id, atc_code)
);

CREATE TABLE IF NOT EXISTS core.document (
    id                       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    package_id               bigint NOT NULL REFERENCES core.package(id),
    document_type            text NOT NULL,
    url                      text NOT NULL,
    language_code            varchar(10),
    source_id                bigint REFERENCES source.source(id),
    updated_at               timestamptz NOT NULL DEFAULT now(),
    UNIQUE (package_id, document_type, url)
);

-- ============================================================
-- APP - PLACEHOLDER FOR MEDREMINDER USER DATA
-- ============================================================

-- Intentionally empty in V1.
-- app.* will reference core.package/core.substance without
-- contaminating the pharmaceutical master-data model.

-- ============================================================
-- INITIAL SOURCE REGISTRATION
-- ============================================================

INSERT INTO source.source (
    code, name, publisher, country_code,
    license_name, license_url,
    attribution_required,
    commercial_use_allowed,
    redistribution_allowed,
    modification_allowed
)
VALUES (
    'AIFA',
    'AIFA Open Data',
    'Agenzia Italiana del Farmaco',
    'IT',
    'Creative Commons Attribution 4.0 International',
    'https://creativecommons.org/licenses/by/4.0/',
    true,
    true,
    true,
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    publisher = EXCLUDED.publisher,
    country_code = EXCLUDED.country_code,
    license_name = EXCLUDED.license_name,
    license_url = EXCLUDED.license_url,
    attribution_required = EXCLUDED.attribution_required,
    commercial_use_allowed = EXCLUDED.commercial_use_allowed,
    redistribution_allowed = EXCLUDED.redistribution_allowed,
    modification_allowed = EXCLUDED.modification_allowed,
    updated_at = now();

COMMIT;
