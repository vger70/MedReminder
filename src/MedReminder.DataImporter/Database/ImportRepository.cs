using MedReminder.DataImporter.Configuration;
using MedReminder.DataImporter.Import;
using Npgsql;
using NpgsqlTypes;

namespace MedReminder.DataImporter.Database;

public sealed class ImportRepository(string connectionString, ImporterOptions options)
{
    private readonly string _connectionString = connectionString;
    private readonly int _timeout = options.CommandTimeoutSeconds;

    public async Task<long> CreateRunAsync(IReadOnlyList<SourceFileMetadata> files, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureAifaSourceAsync(connection, cancellationToken);
        const string sql = """
            INSERT INTO source.import_run (dataset_id, status, source_file_name, source_file_size, source_sha256)
            SELECT d.id, 'RUNNING', @name, @size, @sha
            FROM source.dataset d JOIN source.source s ON s.id = d.source_id
            WHERE s.code = 'AIFA' AND d.code = 'AIFA_CURRENT'
            RETURNING id;
            """;
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = _timeout };
        command.Parameters.AddWithValue("name", string.Join(", ", files.Select(file => file.Name)));
        command.Parameters.AddWithValue("size", files.Sum(file => file.Size));
        command.Parameters.AddWithValue("sha", string.Join(",", files.Select(file => file.Sha256)));
        var runId = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        foreach (var file in files)
        {
            await ExecuteAsync(connection, "INSERT INTO source.import_run_file (import_run_id, source_file_name, source_file_size, source_sha256) VALUES (@run, @name, @size, @sha);", cancellationToken,
                ("run", runId), ("name", file.Name), ("size", file.Size), ("sha", file.Sha256));
        }
        return runId;
    }

    public async Task<long> LoadPackagesAsync(long runId, AifaCsvLoader loader, string path, Action<long> progress, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var importer = await connection.BeginBinaryImportAsync("COPY staging.aifa_package (import_run_id, source_row_number, source_hash, codice_aic, cod_farmaco, cod_confezione, denominazione, descrizione, codice_ditta, ragione_sociale, stato_amministrativo, tipo_procedura, forma, codice_atc, pa_associati, fornitura, link_fi, link_rcp) FROM STDIN (FORMAT BINARY)", cancellationToken);
        var rows = await loader.ReadPackagesAsync(path, async (row, number, token) =>
        {
            await importer.StartRowAsync(token);
            await importer.WriteAsync(runId, NpgsqlDbType.Bigint, token);
            await importer.WriteAsync(number, NpgsqlDbType.Bigint, token);
            await importer.WriteAsync(string.Empty, NpgsqlDbType.Text, token);
            await WriteNullableAsync(importer, row.CodiceAic, token); await WriteNullableAsync(importer, row.CodFarmaco, token); await WriteNullableAsync(importer, row.CodConfezione, token); await WriteNullableAsync(importer, row.Denominazione, token); await WriteNullableAsync(importer, row.Descrizione, token); await WriteNullableAsync(importer, row.CodiceDitta, token); await WriteNullableAsync(importer, row.RagioneSociale, token); await WriteNullableAsync(importer, row.StatoAmministrativo, token); await WriteNullableAsync(importer, row.TipoProcedura, token); await WriteNullableAsync(importer, row.Forma, token); await WriteNullableAsync(importer, row.CodiceAtc, token); await WriteNullableAsync(importer, row.PaAssociati, token); await WriteNullableAsync(importer, row.Fornitura, token); await WriteNullableAsync(importer, row.LinkFi, token); await WriteNullableAsync(importer, row.LinkRcp, token);
            progress(number);
        }, cancellationToken);
        await importer.CompleteAsync(cancellationToken);
        return rows;
    }

    public async Task<long> LoadIngredientsAsync(long runId, AifaCsvLoader loader, string path, Action<long> progress, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var importer = await connection.BeginBinaryImportAsync("COPY staging.aifa_package_ingredient (import_run_id, source_row_number, source_hash, codice_aic, principio_attivo, quantita, unita_misura) FROM STDIN (FORMAT BINARY)", cancellationToken);
        var rows = await loader.ReadIngredientsAsync(path, async (row, number, token) =>
        {
            await importer.StartRowAsync(token); await importer.WriteAsync(runId, NpgsqlDbType.Bigint, token); await importer.WriteAsync(number, NpgsqlDbType.Bigint, token); await importer.WriteAsync(string.Empty, NpgsqlDbType.Text, token);
            await WriteNullableAsync(importer, row.CodiceAic, token); await WriteNullableAsync(importer, row.PrincipioAttivo, token); await WriteNullableAsync(importer, row.Quantita, token); await WriteNullableAsync(importer, row.UnitaMisura, token); progress(number);
        }, cancellationToken);
        await importer.CompleteAsync(cancellationToken);
        return rows;
    }

    public async Task<long> LoadAtcAsync(long runId, AifaCsvLoader loader, string path, Action<long> progress, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var importer = await connection.BeginBinaryImportAsync("COPY staging.aifa_atc (import_run_id, source_row_number, source_hash, codice_atc, descrizione) FROM STDIN (FORMAT BINARY)", cancellationToken);
        var rows = await loader.ReadAtcAsync(path, async (row, number, token) =>
        {
            await importer.StartRowAsync(token); await importer.WriteAsync(runId, NpgsqlDbType.Bigint, token); await importer.WriteAsync(number, NpgsqlDbType.Bigint, token); await importer.WriteAsync(string.Empty, NpgsqlDbType.Text, token);
            await WriteNullableAsync(importer, row.CodiceAtc, token); await WriteNullableAsync(importer, row.Descrizione, token); progress(number);
        }, cancellationToken);
        await importer.CompleteAsync(cancellationToken);
        return rows;
    }

    public async Task<ValidationResult> ValidateAsync(long runId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        const string sql = """
            WITH issues AS (
              SELECT 'Package rows with a missing AIC or product code' AS message, count(*) AS count FROM staging.aifa_package WHERE import_run_id = @run AND (nullif(codice_aic, '') IS NULL OR nullif(cod_farmaco, '') IS NULL)
              UNION ALL SELECT 'Duplicate package AIC values', count(*) FROM (SELECT codice_aic FROM staging.aifa_package WHERE import_run_id = @run AND codice_aic IS NOT NULL GROUP BY codice_aic HAVING count(*) > 1) x
              UNION ALL SELECT 'Package rows referencing an unknown ATC code', count(*) FROM staging.aifa_package p WHERE p.import_run_id = @run AND nullif(p.codice_atc, '') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM staging.aifa_atc a WHERE a.import_run_id = @run AND a.codice_atc = p.codice_atc)
              UNION ALL SELECT 'Ingredient rows with a missing AIC or active ingredient', count(*) FROM staging.aifa_package_ingredient WHERE import_run_id = @run AND (nullif(codice_aic, '') IS NULL OR nullif(principio_attivo, '') IS NULL)
              UNION ALL SELECT 'Ingredient rows not linked to a package AIC', count(*) FROM staging.aifa_package_ingredient i WHERE i.import_run_id = @run AND NOT EXISTS (SELECT 1 FROM staging.aifa_package p WHERE p.import_run_id = @run AND p.codice_aic = i.codice_aic)
              UNION ALL SELECT 'Duplicate ATC codes', count(*) FROM (SELECT codice_atc FROM staging.aifa_atc WHERE import_run_id = @run AND codice_atc IS NOT NULL GROUP BY codice_atc HAVING count(*) > 1) x
              UNION ALL SELECT 'ATC rows with an empty code or description', count(*) FROM staging.aifa_atc WHERE import_run_id = @run AND (nullif(codice_atc, '') IS NULL OR nullif(descrizione, '') IS NULL)
              UNION ALL SELECT 'ATC rows with an invalid hierarchy code', count(*) FROM staging.aifa_atc WHERE import_run_id = @run AND codice_atc !~ '^[A-Z]$|^[A-Z][0-9]{2}$|^[A-Z][0-9]{2}[A-Z]$|^[A-Z][0-9]{2}[A-Z]{2}$|^[A-Z][0-9]{2}[A-Z]{2}[0-9]{2}$'
            ) SELECT message, count FROM issues WHERE count > 0;
            """;
        var errors = new List<string>(); long rejected = 0;
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = _timeout };
        command.Parameters.AddWithValue("run", runId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) { var count = reader.GetInt64(1); rejected += count; errors.Add($"{reader.GetString(0)}: {count}."); }
        return new ValidationResult(errors.Count == 0, rejected, errors);
    }

    public async Task NormalizeAsync(long runId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            WITH aifa AS (SELECT id FROM source.source WHERE code = 'AIFA'), forms AS (SELECT DISTINCT forma FROM staging.aifa_package WHERE import_run_id = @run AND forma IS NOT NULL)
            INSERT INTO core.pharmaceutical_form (source_id, source_code, name) SELECT aifa.id, forms.forma, forms.forma FROM forms CROSS JOIN aifa ON CONFLICT (source_id, source_code) DO UPDATE SET name = EXCLUDED.name, is_active = true, updated_at = now();
            WITH aifa AS (SELECT id FROM source.source WHERE code = 'AIFA'), values AS (SELECT DISTINCT fornitura FROM staging.aifa_package WHERE import_run_id = @run AND fornitura IS NOT NULL)
            INSERT INTO core.supply_classification (source_id, source_code, name) SELECT aifa.id, values.fornitura, values.fornitura FROM values CROSS JOIN aifa ON CONFLICT (source_id, source_code) DO UPDATE SET name = EXCLUDED.name, is_active = true, updated_at = now();
            WITH aifa AS (SELECT id FROM source.source WHERE code = 'AIFA'), values AS (SELECT DISTINCT tipo_procedura FROM staging.aifa_package WHERE import_run_id = @run AND tipo_procedura IS NOT NULL)
            INSERT INTO core.authorization_procedure_type (source_id, source_code, name) SELECT aifa.id, values.tipo_procedura, values.tipo_procedura FROM values CROSS JOIN aifa ON CONFLICT (source_id, source_code) DO UPDATE SET name = EXCLUDED.name, is_active = true, updated_at = now();
            INSERT INTO core.organisation (name, country_code) SELECT DISTINCT ragione_sociale, 'IT' FROM staging.aifa_package WHERE import_run_id = @run AND ragione_sociale IS NOT NULL ON CONFLICT (lower(name), country_code) DO UPDATE SET is_active = true, updated_at = now();
            WITH aifa AS (SELECT id FROM source.source WHERE code = 'AIFA') INSERT INTO core.external_identifier (entity_type, entity_id, namespace, identifier_type, identifier_value) SELECT 'organisation', o.id, 'AIFA', 'CODICE_DITTA', p.codice_ditta FROM staging.aifa_package p JOIN core.organisation o ON o.name = p.ragione_sociale AND o.country_code = 'IT' WHERE p.import_run_id = @run AND p.codice_ditta IS NOT NULL ON CONFLICT (namespace, identifier_type, identifier_value) DO NOTHING;
            WITH aifa AS (SELECT id FROM source.source WHERE code = 'AIFA') INSERT INTO core.medicinal_product (source_id, source_code, name, organisation_id) SELECT aifa.id, p.cod_farmaco, coalesce(nullif(p.denominazione, ''), p.cod_farmaco), o.id FROM staging.aifa_package p CROSS JOIN aifa LEFT JOIN core.organisation o ON o.name = p.ragione_sociale AND o.country_code = 'IT' WHERE p.import_run_id = @run ON CONFLICT (source_id, source_code) DO UPDATE SET name = EXCLUDED.name, organisation_id = EXCLUDED.organisation_id, is_active = true, updated_at = now();
            INSERT INTO core.atc (code, parent_code, level, description, is_active) SELECT codice_atc, CASE length(codice_atc) WHEN 1 THEN NULL WHEN 3 THEN substring(codice_atc, 1, 1) WHEN 4 THEN substring(codice_atc, 1, 3) WHEN 5 THEN substring(codice_atc, 1, 4) WHEN 7 THEN substring(codice_atc, 1, 5) END, CASE length(codice_atc) WHEN 1 THEN 1 WHEN 3 THEN 2 WHEN 4 THEN 3 WHEN 5 THEN 4 WHEN 7 THEN 5 END, descrizione, true FROM staging.aifa_atc WHERE import_run_id = @run ON CONFLICT (code) DO UPDATE SET description = EXCLUDED.description, is_active = true, updated_at = now();
            WITH aifa AS (SELECT id FROM source.source WHERE code = 'AIFA') INSERT INTO core.package (medicinal_product_id, aic, package_code, description, pharmaceutical_form_id, atc_code, supply_classification_id, authorization_procedure_type_id, administrative_status, link_fi, link_rcp) SELECT mp.id, p.codice_aic, p.cod_confezione, p.descrizione, f.id, NULL, s.id, apt.id, p.stato_amministrativo, p.link_fi, p.link_rcp FROM staging.aifa_package p CROSS JOIN aifa JOIN core.medicinal_product mp ON mp.source_id = aifa.id AND mp.source_code = p.cod_farmaco LEFT JOIN core.pharmaceutical_form f ON f.source_id = aifa.id AND f.source_code = p.forma LEFT JOIN core.supply_classification s ON s.source_id = aifa.id AND s.source_code = p.fornitura LEFT JOIN core.authorization_procedure_type apt ON apt.source_id = aifa.id AND apt.source_code = p.tipo_procedura WHERE p.import_run_id = @run ON CONFLICT (aic) DO UPDATE SET medicinal_product_id = EXCLUDED.medicinal_product_id, package_code = EXCLUDED.package_code, description = EXCLUDED.description, pharmaceutical_form_id = EXCLUDED.pharmaceutical_form_id, atc_code = NULL, supply_classification_id = EXCLUDED.supply_classification_id, authorization_procedure_type_id = EXCLUDED.authorization_procedure_type_id, administrative_status = EXCLUDED.administrative_status, link_fi = EXCLUDED.link_fi, link_rcp = EXCLUDED.link_rcp, is_active = true, updated_at = now();
            DELETE FROM core.package_atc pa USING core.package p JOIN staging.aifa_package sp ON sp.import_run_id = @run AND sp.codice_aic = p.aic WHERE pa.package_id = p.id;
            WITH aifa AS (SELECT id FROM source.source WHERE code = 'AIFA') INSERT INTO core.package_atc (package_id, atc_code, source_id) SELECT p.id, sp.codice_atc, aifa.id FROM staging.aifa_package sp JOIN core.package p ON p.aic = sp.codice_aic CROSS JOIN aifa WHERE sp.import_run_id = @run AND sp.codice_atc IS NOT NULL ON CONFLICT DO NOTHING;
            INSERT INTO core.substance (preferred_name) SELECT DISTINCT principio_attivo FROM staging.aifa_package_ingredient WHERE import_run_id = @run AND principio_attivo IS NOT NULL AND upper(principio_attivo) <> 'N.D.' ON CONFLICT (lower(preferred_name)) DO UPDATE SET is_active = true, updated_at = now();
            DELETE FROM core.package_ingredient pi USING core.package p JOIN staging.aifa_package_ingredient si ON si.import_run_id = @run AND si.codice_aic = p.aic WHERE pi.package_id = p.id;
            INSERT INTO core.package_ingredient (package_id, substance_id, substance_name_source, strength_value, strength_unit, strength_raw, ingredient_status) SELECT p.id, s.id, si.principio_attivo, CASE WHEN trim(si.quantita) ~ '^[0-9]+([.,][0-9]+)?$' THEN replace(trim(si.quantita), ',', '.')::numeric ELSE NULL END, si.unita_misura, si.quantita, CASE WHEN upper(si.principio_attivo) = 'N.D.' THEN 'NOT_AVAILABLE' ELSE 'KNOWN' END FROM staging.aifa_package_ingredient si JOIN core.package p ON p.aic = si.codice_aic LEFT JOIN core.substance s ON lower(s.preferred_name) = lower(si.principio_attivo) AND upper(si.principio_attivo) <> 'N.D.' WHERE si.import_run_id = @run;
            DELETE FROM core.document d USING core.package p JOIN staging.aifa_package sp ON sp.import_run_id = @run AND sp.codice_aic = p.aic WHERE d.package_id = p.id AND d.source_id = (SELECT id FROM source.source WHERE code = 'AIFA');
            WITH aifa AS (SELECT id FROM source.source WHERE code = 'AIFA'), docs AS (SELECT p.id AS package_id, 'FI' AS type, sp.link_fi AS url FROM staging.aifa_package sp JOIN core.package p ON p.aic = sp.codice_aic WHERE sp.import_run_id = @run AND sp.link_fi IS NOT NULL UNION ALL SELECT p.id, 'RCP', sp.link_rcp FROM staging.aifa_package sp JOIN core.package p ON p.aic = sp.codice_aic WHERE sp.import_run_id = @run AND sp.link_rcp IS NOT NULL) INSERT INTO core.document (package_id, document_type, url, source_id) SELECT package_id, type, url, aifa.id FROM docs CROSS JOIN aifa ON CONFLICT (package_id, document_type, url) DO UPDATE SET updated_at = now();
            WITH aifa AS (SELECT id FROM source.source WHERE code = 'AIFA') UPDATE core.package p SET is_active = false, updated_at = now() FROM core.medicinal_product mp CROSS JOIN aifa WHERE p.medicinal_product_id = mp.id AND mp.source_id = aifa.id AND NOT EXISTS (SELECT 1 FROM staging.aifa_package sp WHERE sp.import_run_id = @run AND sp.codice_aic = p.aic);
            WITH aifa AS (SELECT id FROM source.source WHERE code = 'AIFA') UPDATE core.medicinal_product mp SET is_active = false, updated_at = now() FROM aifa WHERE mp.source_id = aifa.id AND NOT EXISTS (SELECT 1 FROM staging.aifa_package sp WHERE sp.import_run_id = @run AND sp.cod_farmaco = mp.source_code);
            """;
        await ExecuteAsync(connection, sql, cancellationToken, [("run", runId)], transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(long runId, ImportStatistics stats, string status, string? error, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "UPDATE source.import_run SET completed_at = now(), status = @status, records_read = @read, records_accepted = @accepted, records_rejected = @rejected, error_message = @error WHERE id = @run;", cancellationToken, ("status", status), ("read", stats.Packages + stats.Ingredients + stats.Atc), ("accepted", stats.Packages + stats.Ingredients + stats.Atc - stats.Rejected), ("rejected", stats.Rejected), ("error", error), ("run", runId));
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken) { var connection = new NpgsqlConnection(_connectionString); await connection.OpenAsync(cancellationToken); return connection; }
    private async Task EnsureAifaSourceAsync(NpgsqlConnection connection, CancellationToken cancellationToken) => await ExecuteAsync(connection, "INSERT INTO source.source (code, name, publisher, country_code, license_name, license_url, attribution_required, commercial_use_allowed, redistribution_allowed, modification_allowed) VALUES ('AIFA', 'AIFA Open Data', 'Agenzia Italiana del Farmaco', 'IT', 'Creative Commons Attribution 4.0 International', 'https://creativecommons.org/licenses/by/4.0/', true, true, true, true) ON CONFLICT (code) DO NOTHING; INSERT INTO source.dataset (source_id, code, name, format) SELECT id, 'AIFA_CURRENT', 'AIFA current pharmaceutical data', 'CSV' FROM source.source WHERE code = 'AIFA' ON CONFLICT (source_id, code) DO NOTHING;", cancellationToken);
    private async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters) => await ExecuteAsync(connection, sql, cancellationToken, parameters, null);
    private async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken, (string Name, object? Value)[] parameters, NpgsqlTransaction? transaction) { await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = _timeout }; foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value); await command.ExecuteNonQueryAsync(cancellationToken); }
    private static Task WriteNullableAsync(NpgsqlBinaryImporter importer, string? value, CancellationToken token) => value is null ? importer.WriteNullAsync(token) : importer.WriteAsync(value, NpgsqlDbType.Text, token);
}
