using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace MedReminder.DataImporter.Import;

public sealed class AifaCsvLoader
{
    private static readonly string[] PackageHeaders = ["CODICE_AIC", "COD_FARMACO", "COD_CONFEZIONE", "DENOMINAZIONE", "DESCRIZIONE", "CODICE_DITTA", "RAGIONE_SOCIALE", "STATO_AMMINISTRATIVO", "TIPO_PROCEDURA", "FORMA", "CODICE_ATC", "PA_ASSOCIATI", "FORNITURA", "LINK_FI", "LINK_RCP"];
    private static readonly string[] IngredientHeaders = ["CODICE_AIC", "PRINCIPIO_ATTIVO", "QUANTITA", "UNITA_MISURA"];
    private static readonly string[] AtcHeaders = ["CODICE_ATC", "DESCRIZIONE"];

    public async Task<SourceFileMetadata> GetMetadataAsync(string path, CancellationToken cancellationToken)
    {
        EnsureFile(path);
        var info = new FileInfo(path);
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new SourceFileMetadata(info.Name, info.Length, Convert.ToHexString(hash).ToLowerInvariant());
    }

    public Task<long> ReadPackagesAsync(string path, Func<PackageRow, long, CancellationToken, Task> onRow, CancellationToken cancellationToken) =>
        ReadAsync(path, PackageHeaders, fields => new PackageRow(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6], fields[7], fields[8], fields[9], fields[10], fields[11], fields[12], fields[13], fields[14]), onRow, cancellationToken);

    public Task<long> ReadIngredientsAsync(string path, Func<IngredientRow, long, CancellationToken, Task> onRow, CancellationToken cancellationToken) =>
        ReadAsync(path, IngredientHeaders, fields => new IngredientRow(fields[0], fields[1], fields[2], fields[3]), onRow, cancellationToken);

    public Task<long> ReadAtcAsync(string path, Func<AtcRow, long, CancellationToken, Task> onRow, CancellationToken cancellationToken) =>
        ReadAsync(path, AtcHeaders, fields => new AtcRow(fields[0], fields[1]), onRow, cancellationToken);

    private static async Task<long> ReadAsync<T>(string path, IReadOnlyList<string> expectedHeaders, Func<string?[], T> map, Func<T, long, CancellationToken, Task> onRow, CancellationToken cancellationToken)
    {
        EnsureFile(path);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ";",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
            BadDataFound = args => throw new InvalidDataException($"Malformed CSV data at row {args.Context.Parser?.Row}."),
            MissingFieldFound = args => throw new InvalidDataException($"Missing CSV field at row {args.Context.Parser?.Row}.")
        };

        using var reader = new StreamReader(path, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, config);
        if (!await csv.ReadAsync()) throw new InvalidDataException($"CSV file '{path}' is empty.");
        csv.ReadHeader();
        ValidateHeaders(csv.HeaderRecord, expectedHeaders, path);

        long rowNumber = 1;
        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;
            var fields = new string?[expectedHeaders.Count];
            for (var index = 0; index < fields.Length; index++) fields[index] = Normalize(csv.GetField(index));
            await onRow(map(fields), rowNumber, cancellationToken);
        }
        return rowNumber - 1;
    }

    private static void ValidateHeaders(string[]? actual, IReadOnlyList<string> expected, string path)
    {
        if (actual is null || !actual.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unexpected header in '{path}'. Expected: {string.Join(";", expected)}.");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("AIFA input file was not found.", path);
    }
}
