using System.Data.Common;
using System.Text;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using MedReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Catalogue;

// Read adapter for the reference catalogue. Uses raw SQL against the
// tables owned by CatalogueSchema so the query path does not depend
// on the catalogue being registered in the EF model (a deliberate
// consequence of §2.4 "no EnsureCreated shortcut" for these tables).
//
// LIKE queries hit the indexed `commercial_name_norm` /
// `name_norm` columns, and the country filter uses a fixed-arity IN
// clause built from the caller-supplied scope so the SQLite query
// planner can pick the (country, *_norm) index.
public sealed class SqliteReferenceCatalogueQueryService : IReferenceCatalogueQueryService
{
    private readonly MedReminderDbContext _db;

    public SqliteReferenceCatalogueQueryService(MedReminderDbContext db)
    {
        _db = db;
    }

    public Task<IReadOnlyList<ReferenceMedicine>> SearchByCommercialNameAsync(
        string prefix,
        IReadOnlyCollection<CountryCode> countryScope,
        int limit,
        CancellationToken cancellationToken)
    {
        Validate(prefix, countryScope, limit);
        var normalized = CatalogueTextNormalizer.Normalize(prefix);
        if (normalized.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<ReferenceMedicine>>(Array.Empty<ReferenceMedicine>());
        }

        return SearchAsync(
            column: "m.\"commercial_name_norm\"",
            normalizedPrefix: normalized,
            countryScope: countryScope,
            limit: limit,
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceMedicine>> SearchByActiveIngredientAsync(
        string prefix,
        IReadOnlyCollection<CountryCode> countryScope,
        int limit,
        CancellationToken cancellationToken)
    {
        Validate(prefix, countryScope, limit);
        var normalized = CatalogueTextNormalizer.Normalize(prefix);
        if (normalized.Length == 0)
        {
            return Array.Empty<ReferenceMedicine>();
        }

        var connection = await OpenAsync(cancellationToken);
        var countries = countryScope.ToList();

        // Join through the M2M to find the medicines whose ingredients
        // match. GROUP BY collapses medicines that match through
        // multiple ingredients into a single row before the limit is
        // applied.
        var sql = new StringBuilder();
        sql.Append(@"SELECT m.""id"" FROM ""reference_medicines"" m ");
        sql.Append(@"JOIN ""reference_medicine_ingredients"" mi ON mi.""medicine_id"" = m.""id"" ");
        sql.Append(@"JOIN ""reference_active_ingredients"" i ON i.""id"" = mi.""ingredient_id"" ");
        sql.Append(@"WHERE m.""country"" IN (");
        AppendCountryPlaceholders(sql, countries.Count);
        sql.Append(@") AND i.""name_norm"" LIKE $prefix ");
        sql.Append(@"GROUP BY m.""id"" ");
        sql.Append(@"ORDER BY CASE WHEN i.""name_norm"" = $exact THEN 0 ELSE 1 END, m.""commercial_name"" ");
        sql.Append("LIMIT $limit;");

        var ids = new List<Guid>(capacity: limit);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sql.ToString();
            AddCountryParameters(cmd, countries);
            AddParameter(cmd, "$prefix", normalized + "%");
            AddParameter(cmd, "$exact", normalized);
            AddParameter(cmd, "$limit", limit);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        if (ids.Count == 0)
        {
            return Array.Empty<ReferenceMedicine>();
        }

        return await HydrateAsync(connection, ids, cancellationToken);
    }

    public async Task<IReadOnlyList<CountryCode>> ListAvailableCountriesAsync(
        CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        var codes = new List<CountryCode>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT DISTINCT ""country"" FROM ""reference_medicines"" ORDER BY ""country"";";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var raw = reader.GetString(0);
            if (CountryCode.TryParse(raw, out var code))
            {
                codes.Add(code);
            }
        }
        return codes;
    }

    public async Task<ReferenceMedicine?> GetByNationalCodeAsync(
        CountryCode country, string nationalCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nationalCode))
        {
            return null;
        }

        var connection = await OpenAsync(cancellationToken);
        Guid id;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT ""id""
                  FROM ""reference_medicines""
                 WHERE ""country"" = $country
                   AND ""national_code"" = $code
                 LIMIT 1;";
            AddParameter(cmd, "$country", country.Value);
            AddParameter(cmd, "$code", nationalCode);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            if (result is not string idText || !Guid.TryParse(idText, out id))
            {
                return null;
            }
        }

        var rows = await HydrateAsync(connection, new[] { id }, cancellationToken);
        return rows.FirstOrDefault();
    }

    private async Task<IReadOnlyList<ReferenceMedicine>> SearchAsync(
        string column,
        string normalizedPrefix,
        IReadOnlyCollection<CountryCode> countryScope,
        int limit,
        CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        var countries = countryScope.ToList();

        var sql = new StringBuilder();
        sql.Append(@"SELECT m.""id"" FROM ""reference_medicines"" m ");
        sql.Append(@"WHERE m.""country"" IN (");
        AppendCountryPlaceholders(sql, countries.Count);
        sql.Append(") AND ").Append(column).Append(" LIKE $prefix ");
        sql.Append("ORDER BY CASE WHEN ").Append(column).Append(@" = $exact THEN 0 ELSE 1 END, m.""commercial_name"" ");
        sql.Append("LIMIT $limit;");

        var ids = new List<Guid>(capacity: limit);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sql.ToString();
            AddCountryParameters(cmd, countries);
            AddParameter(cmd, "$prefix", normalizedPrefix + "%");
            AddParameter(cmd, "$exact", normalizedPrefix);
            AddParameter(cmd, "$limit", limit);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        if (ids.Count == 0)
        {
            return Array.Empty<ReferenceMedicine>();
        }

        return await HydrateAsync(connection, ids, cancellationToken);
    }

    private static async Task<IReadOnlyList<ReferenceMedicine>> HydrateAsync(
        DbConnection connection, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var byId = new Dictionary<Guid, ReferenceMedicine>(capacity: ids.Count);
        var ingredientsById = new Dictionary<Guid, List<ReferenceActiveIngredient>>(capacity: ids.Count);
        var order = new List<Guid>(capacity: ids.Count);

        var sqlMed = new StringBuilder();
        sqlMed.Append(@"SELECT ""id"", ""country"", ""national_code"", ""commercial_name"", ");
        sqlMed.Append(@"       ""pharmaceutical_form"", ""dosage"", ""mah"", ""marketing_status"", ");
        sqlMed.Append(@"       ""dispensing_regime"", ""link_leaflet"", ""link_spc"", ""snapshot_version"" ");
        sqlMed.Append(@"  FROM ""reference_medicines"" ");
        sqlMed.Append(@" WHERE ""id"" IN (");
        AppendIdPlaceholders(sqlMed, ids.Count);
        sqlMed.Append(");");

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sqlMed.ToString();
            AddIdParameters(cmd, ids);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = Guid.Parse(reader.GetString(0));
                var medicine = new ReferenceMedicine
                {
                    Id = id,
                    Country = CountryCode.Parse(reader.GetString(1)),
                    NationalCode = reader.GetString(2),
                    CommercialName = reader.GetString(3),
                    PharmaceuticalForm = ReadNullable(reader, 4),
                    Dosage = ReadNullable(reader, 5),
                    MarketingAuthorisationHolder = ReadNullable(reader, 6),
                    MarketingStatus = ReadNullable(reader, 7),
                    DispensingRegime = ReadNullable(reader, 8),
                    LinkLeaflet = ReadNullable(reader, 9),
                    LinkSummaryOfProductCharacteristics = ReadNullable(reader, 10),
                    SnapshotVersion = reader.GetString(11),
                };
                byId[id] = medicine;
                ingredientsById[id] = new List<ReferenceActiveIngredient>();
            }
        }

        var sqlIng = new StringBuilder();
        sqlIng.Append(@"SELECT mi.""medicine_id"", i.""id"", i.""country"", i.""name"", i.""atc_code"" ");
        sqlIng.Append(@"  FROM ""reference_medicine_ingredients"" mi ");
        sqlIng.Append(@"  JOIN ""reference_active_ingredients"" i ON i.""id"" = mi.""ingredient_id"" ");
        sqlIng.Append(@" WHERE mi.""medicine_id"" IN (");
        AppendIdPlaceholders(sqlIng, ids.Count);
        sqlIng.Append(@") ORDER BY i.""name"";");

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sqlIng.ToString();
            AddIdParameters(cmd, ids);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var medicineId = Guid.Parse(reader.GetString(0));
                var ingredient = new ReferenceActiveIngredient
                {
                    Id = Guid.Parse(reader.GetString(1)),
                    Country = CountryCode.Parse(reader.GetString(2)),
                    Name = reader.GetString(3),
                    Atc = reader.IsDBNull(4) || !AtcCode.TryParse(reader.GetString(4), out var atc)
                        ? null
                        : atc,
                };
                if (ingredientsById.TryGetValue(medicineId, out var list))
                {
                    list.Add(ingredient);
                }
            }
        }

        // Preserve the id order the caller passed in (the search
        // step has already sorted them by prefix-match then name).
        foreach (var id in ids)
        {
            if (byId.ContainsKey(id))
            {
                order.Add(id);
            }
        }

        var results = new ReferenceMedicine[order.Count];
        for (var i = 0; i < order.Count; i++)
        {
            var id = order[i];
            var medicine = byId[id];
            results[i] = new ReferenceMedicine
            {
                Id = medicine.Id,
                Country = medicine.Country,
                NationalCode = medicine.NationalCode,
                CommercialName = medicine.CommercialName,
                PharmaceuticalForm = medicine.PharmaceuticalForm,
                Dosage = medicine.Dosage,
                MarketingAuthorisationHolder = medicine.MarketingAuthorisationHolder,
                MarketingStatus = medicine.MarketingStatus,
                DispensingRegime = medicine.DispensingRegime,
                LinkLeaflet = medicine.LinkLeaflet,
                LinkSummaryOfProductCharacteristics = medicine.LinkSummaryOfProductCharacteristics,
                SnapshotVersion = medicine.SnapshotVersion,
                ActiveIngredients = ingredientsById[id],
            };
        }
        return results;
    }

    private async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
        return connection;
    }

    private static void Validate(string prefix, IReadOnlyCollection<CountryCode> scope, int limit)
    {
        if (prefix is null)
        {
            throw new ArgumentNullException(nameof(prefix));
        }
        if (scope is null || scope.Count == 0)
        {
            throw new ArgumentException("Country scope cannot be empty.", nameof(scope));
        }
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");
        }
    }

    private static void AppendCountryPlaceholders(StringBuilder sql, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sql.Append(", ");
            sql.Append("$country").Append(i);
        }
    }

    private static void AddCountryParameters(DbCommand cmd, IReadOnlyList<CountryCode> countries)
    {
        for (var i = 0; i < countries.Count; i++)
        {
            AddParameter(cmd, "$country" + i, countries[i].Value);
        }
    }

    private static void AppendIdPlaceholders(StringBuilder sql, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sql.Append(", ");
            sql.Append("$id").Append(i);
        }
    }

    private static void AddIdParameters(DbCommand cmd, IReadOnlyList<Guid> ids)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            AddParameter(cmd, "$id" + i, ids[i].ToString());
        }
    }

    private static DbParameter AddParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
        return p;
    }

    private static string? ReadNullable(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
