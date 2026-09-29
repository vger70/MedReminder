using System.Data.Common;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using MedReminder.Infrastructure.Catalogue.Parsers;
using MedReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Catalogue;

// Generic CSV importer that delegates parsing to a strategy per
// country (ANALYSIS-DRUG-CATALOGUE.md §2.3). The importer owns:
//   - version detection: an import with the same snapshot_version
//     already recorded in the DB is a no-op.
//   - transactional replace: on a newer snapshot the country's rows
//     are deleted and re-inserted in one transaction.
//   - active-ingredient interning: same (country, name_norm) yields
//     one reference_active_ingredients row shared across every
//     medicine that lists it.
public sealed class CsvReferenceCatalogueImporter : IReferenceCatalogueImporter
{
    private readonly MedReminderDbContext _db;
    private readonly IReadOnlyList<IReferenceSnapshotParser> _parsers;
    private readonly TimeProvider _clock;

    internal CsvReferenceCatalogueImporter(
        MedReminderDbContext db,
        IEnumerable<IReferenceSnapshotParser> parsers,
        TimeProvider clock)
    {
        _db = db;
        _parsers = parsers.ToArray();
        _clock = clock;
    }

    public async Task<ImportReport> ImportAsync(
        Stream snapshot,
        CountryCode expectedCountry,
        string snapshotVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshotVersion))
        {
            throw new ArgumentException("Snapshot version is required.", nameof(snapshotVersion));
        }

        var parser = _parsers.FirstOrDefault(p => p.SupportedCountries.Contains(expectedCountry))
            ?? throw new NotSupportedException(
                $"No parser strategy registered for country '{expectedCountry}'.");

        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        // The DDL step in DatabaseInitializer normally runs at boot.
        // Running it here too keeps the importer usable in isolation
        // (tests that instantiate it against a fresh DbContext), and
        // it is a cheap no-op afterwards (CREATE * IF NOT EXISTS).
        await CatalogueSchema.ApplyAsync(connection, cancellationToken);

        if (await SameVersionAlreadyImportedAsync(connection, expectedCountry, snapshotVersion, cancellationToken))
        {
            return new ImportReport(
                Inserted: 0, Updated: 0, Deleted: 0, Skipped: 0,
                SnapshotVersion: snapshotVersion,
                CompletedAt: _clock.GetUtcNow());
        }

        var report = new ParseReport();
        var rows = new List<ReferenceMedicineRow>();
        await foreach (var row in parser.ParseAsync(snapshot, report, cancellationToken))
        {
            if (row.Country != expectedCountry)
            {
                throw new InvalidDataException(
                    $"Parser yielded a row for '{row.Country}' but the importer was called for '{expectedCountry}'.");
            }
            rows.Add(row);
        }

        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        var deleted = await DeleteCountryAsync(connection, tx, expectedCountry, cancellationToken);
        var inserted = await InsertAsync(connection, tx, rows, snapshotVersion, cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return new ImportReport(
            Inserted: inserted,
            Updated: 0,
            Deleted: deleted,
            Skipped: report.Skipped,
            SnapshotVersion: snapshotVersion,
            CompletedAt: _clock.GetUtcNow());
    }

    private static async Task<bool> SameVersionAlreadyImportedAsync(
        DbConnection connection,
        CountryCode country,
        string snapshotVersion,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT 1
              FROM ""reference_medicines""
             WHERE ""country"" = $country
               AND ""snapshot_version"" = $version
             LIMIT 1;";
        AddParameter(cmd, "$country", country.Value);
        AddParameter(cmd, "$version", snapshotVersion);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    private static async Task<int> DeleteCountryAsync(
        DbConnection connection,
        DbTransaction tx,
        CountryCode country,
        CancellationToken cancellationToken)
    {
        int deleted;

        await using (var count = connection.CreateCommand())
        {
            count.Transaction = tx;
            count.CommandText = @"SELECT COUNT(*) FROM ""reference_medicines"" WHERE ""country"" = $country;";
            AddParameter(count, "$country", country.Value);
            deleted = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken) ?? 0);
        }

        // ON DELETE CASCADE on reference_medicine_ingredients drops
        // the join rows. Active ingredients are also purged for the
        // same country so a re-import cannot leak stale substances
        // that no medicine references any more.
        await using (var delMed = connection.CreateCommand())
        {
            delMed.Transaction = tx;
            delMed.CommandText = @"DELETE FROM ""reference_medicines"" WHERE ""country"" = $country;";
            AddParameter(delMed, "$country", country.Value);
            await delMed.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var delIng = connection.CreateCommand())
        {
            delIng.Transaction = tx;
            delIng.CommandText = @"DELETE FROM ""reference_active_ingredients"" WHERE ""country"" = $country;";
            AddParameter(delIng, "$country", country.Value);
            await delIng.ExecuteNonQueryAsync(cancellationToken);
        }

        return deleted;
    }

    private static async Task<int> InsertAsync(
        DbConnection connection,
        DbTransaction tx,
        IReadOnlyList<ReferenceMedicineRow> rows,
        string snapshotVersion,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        var country = rows[0].Country.Value;

        // Intern ingredients: same (country, name_norm) → one row
        // regardless of how many medicines mention it.
        var ingredientIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var ingredientAtc = new Dictionary<string, AtcCode?>(StringComparer.Ordinal);

        await using (var ingredientCmd = connection.CreateCommand())
        {
            ingredientCmd.Transaction = tx;
            ingredientCmd.CommandText = @"
                INSERT INTO ""reference_active_ingredients""
                    (""id"", ""country"", ""name"", ""name_norm"", ""atc_code"")
                VALUES ($id, $country, $name, $nameNorm, $atc);";
            var pId = AddParameter(ingredientCmd, "$id", string.Empty);
            AddParameter(ingredientCmd, "$country", country);
            var pName = AddParameter(ingredientCmd, "$name", string.Empty);
            var pNorm = AddParameter(ingredientCmd, "$nameNorm", string.Empty);
            var pAtc = AddParameter(ingredientCmd, "$atc", (object)DBNull.Value);

            foreach (var row in rows)
            {
                foreach (var ingredient in row.ActiveIngredients)
                {
                    var norm = CatalogueTextNormalizer.Normalize(ingredient.Name);
                    if (ingredientIds.ContainsKey(norm))
                    {
                        // Promote the ATC if a later medicine carries
                        // it while the earlier occurrence had none.
                        if (ingredient.Atc.HasValue && ingredientAtc[norm] is null)
                        {
                            ingredientAtc[norm] = ingredient.Atc;
                        }
                        continue;
                    }

                    var id = Guid.NewGuid();
                    ingredientIds[norm] = id;
                    ingredientAtc[norm] = ingredient.Atc;

                    pId.Value = id.ToString();
                    pName.Value = ingredient.Name;
                    pNorm.Value = norm;
                    pAtc.Value = ingredient.Atc is { } value ? value.Value : DBNull.Value;
                    await ingredientCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        // Insert the medicines and their join rows.
        var inserted = 0;
        await using var medCmd = connection.CreateCommand();
        medCmd.Transaction = tx;
        medCmd.CommandText = @"
            INSERT INTO ""reference_medicines"" (
                ""id"", ""country"", ""national_code"",
                ""commercial_name"", ""commercial_name_norm"",
                ""pharmaceutical_form"", ""dosage"", ""mah"",
                ""marketing_status"", ""dispensing_regime"",
                ""link_leaflet"", ""link_spc"", ""snapshot_version""
            ) VALUES (
                $id, $country, $nationalCode,
                $name, $nameNorm,
                $form, $dosage, $mah,
                $status, $regime,
                $leaflet, $spc, $version);";
        var mpId = AddParameter(medCmd, "$id", string.Empty);
        AddParameter(medCmd, "$country", country);
        var mpNational = AddParameter(medCmd, "$nationalCode", string.Empty);
        var mpName = AddParameter(medCmd, "$name", string.Empty);
        var mpNorm = AddParameter(medCmd, "$nameNorm", string.Empty);
        var mpForm = AddParameter(medCmd, "$form", (object)DBNull.Value);
        var mpDosage = AddParameter(medCmd, "$dosage", (object)DBNull.Value);
        var mpMah = AddParameter(medCmd, "$mah", (object)DBNull.Value);
        var mpStatus = AddParameter(medCmd, "$status", (object)DBNull.Value);
        var mpRegime = AddParameter(medCmd, "$regime", (object)DBNull.Value);
        var mpLeaflet = AddParameter(medCmd, "$leaflet", (object)DBNull.Value);
        var mpSpc = AddParameter(medCmd, "$spc", (object)DBNull.Value);
        AddParameter(medCmd, "$version", snapshotVersion);

        await using var joinCmd = connection.CreateCommand();
        joinCmd.Transaction = tx;
        joinCmd.CommandText = @"
            INSERT INTO ""reference_medicine_ingredients""
                (""medicine_id"", ""ingredient_id"")
            VALUES ($medicine, $ingredient);";
        var jMed = AddParameter(joinCmd, "$medicine", string.Empty);
        var jIng = AddParameter(joinCmd, "$ingredient", string.Empty);

        foreach (var row in rows)
        {
            var medicineId = Guid.NewGuid();
            mpId.Value = medicineId.ToString();
            mpNational.Value = row.NationalCode;
            mpName.Value = row.CommercialName;
            mpNorm.Value = CatalogueTextNormalizer.Normalize(row.CommercialName);
            mpForm.Value = (object?)row.PharmaceuticalForm ?? DBNull.Value;
            mpDosage.Value = (object?)row.Dosage ?? DBNull.Value;
            mpMah.Value = (object?)row.MarketingAuthorisationHolder ?? DBNull.Value;
            mpStatus.Value = (object?)row.MarketingStatus ?? DBNull.Value;
            mpRegime.Value = (object?)row.DispensingRegime ?? DBNull.Value;
            mpLeaflet.Value = (object?)row.LinkLeaflet ?? DBNull.Value;
            mpSpc.Value = (object?)row.LinkSpc ?? DBNull.Value;
            await medCmd.ExecuteNonQueryAsync(cancellationToken);
            inserted++;

            var seenForThisMedicine = new HashSet<Guid>();
            foreach (var ingredient in row.ActiveIngredients)
            {
                var norm = CatalogueTextNormalizer.Normalize(ingredient.Name);
                if (!ingredientIds.TryGetValue(norm, out var ingredientId))
                {
                    continue;
                }
                if (!seenForThisMedicine.Add(ingredientId))
                {
                    continue;
                }

                jMed.Value = medicineId.ToString();
                jIng.Value = ingredientId.ToString();
                await joinCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        return inserted;
    }

    private static DbParameter AddParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
        return p;
    }
}
