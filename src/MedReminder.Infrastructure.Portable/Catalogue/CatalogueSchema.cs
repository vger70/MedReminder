using System.Data.Common;

namespace MedReminder.Infrastructure.Catalogue;

// Additive, idempotent DDL for the reference-catalogue tables.
// Called from DatabaseInitializer on every boot (whether or not the
// DB was freshly created) so both new and pre-existing databases
// converge on the same schema without an EnsureCreated shortcut for
// these tables (ANALYSIS-DRUG-CATALOGUE.md §2.4).
//
// Column choices:
//   - Ids stored as TEXT (Guid string) for consistency with the
//     rest of the EF-managed schema; SQLite has no strict typing
//     so the design's "BLOB" spelling is functionally equivalent.
//   - _norm columns computed at import time so autocomplete queries
//     hit an indexed lowercase column without LOWER() at query time.
internal static class CatalogueSchema
{
    private const string ReferenceMedicinesTable = @"
        CREATE TABLE IF NOT EXISTS ""reference_medicines"" (
            ""id""                     TEXT NOT NULL PRIMARY KEY,
            ""country""                TEXT NOT NULL,
            ""national_code""          TEXT NOT NULL,
            ""commercial_name""        TEXT NOT NULL,
            ""commercial_name_norm""   TEXT NOT NULL,
            ""pharmaceutical_form""    TEXT NULL,
            ""dosage""                 TEXT NULL,
            ""mah""                    TEXT NULL,
            ""marketing_status""       TEXT NULL,
            ""dispensing_regime""      TEXT NULL,
            ""link_leaflet""           TEXT NULL,
            ""link_spc""               TEXT NULL,
            ""snapshot_version""       TEXT NOT NULL
        );";

    private const string ReferenceMedicinesUnique = @"
        CREATE UNIQUE INDEX IF NOT EXISTS ""ux_ref_med_country_national_code""
            ON ""reference_medicines""(""country"", ""national_code"");";

    private const string ReferenceMedicinesNameIndex = @"
        CREATE INDEX IF NOT EXISTS ""ix_ref_med_country_name""
            ON ""reference_medicines""(""country"", ""commercial_name_norm"");";

    private const string ReferenceActiveIngredientsTable = @"
        CREATE TABLE IF NOT EXISTS ""reference_active_ingredients"" (
            ""id""             TEXT NOT NULL PRIMARY KEY,
            ""country""        TEXT NOT NULL,
            ""name""           TEXT NOT NULL,
            ""name_norm""      TEXT NOT NULL,
            ""atc_code""       TEXT NULL
        );";

    private const string ReferenceActiveIngredientsUnique = @"
        CREATE UNIQUE INDEX IF NOT EXISTS ""ux_ref_ing_country_name""
            ON ""reference_active_ingredients""(""country"", ""name_norm"");";

    private const string ReferenceActiveIngredientsNameIndex = @"
        CREATE INDEX IF NOT EXISTS ""ix_ref_ing_country_name""
            ON ""reference_active_ingredients""(""country"", ""name_norm"");";

    private const string ReferenceMedicineIngredientsTable = @"
        CREATE TABLE IF NOT EXISTS ""reference_medicine_ingredients"" (
            ""medicine_id""     TEXT NOT NULL,
            ""ingredient_id""   TEXT NOT NULL,
            PRIMARY KEY (""medicine_id"", ""ingredient_id""),
            FOREIGN KEY (""medicine_id"")
                REFERENCES ""reference_medicines""(""id"") ON DELETE CASCADE,
            FOREIGN KEY (""ingredient_id"")
                REFERENCES ""reference_active_ingredients""(""id"")
        );";

    private const string ReferenceMedicineIngredientsIndex = @"
        CREATE INDEX IF NOT EXISTS ""ix_ref_med_ing_ingredient""
            ON ""reference_medicine_ingredients""(""ingredient_id"");";

    // Every statement is safe to re-run on a schema that already has
    // the object (CREATE * IF NOT EXISTS). Order matters only for
    // the foreign keys.
    public static readonly IReadOnlyList<string> Statements = new[]
    {
        ReferenceMedicinesTable,
        ReferenceMedicinesUnique,
        ReferenceMedicinesNameIndex,
        ReferenceActiveIngredientsTable,
        ReferenceActiveIngredientsUnique,
        ReferenceActiveIngredientsNameIndex,
        ReferenceMedicineIngredientsTable,
        ReferenceMedicineIngredientsIndex,
    };

    public static async Task ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        foreach (var sql in Statements)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
