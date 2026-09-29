namespace MedReminder.DataImporter.Import;

public sealed record AifaImportFiles(string PackagesPath, string IngredientsPath, string AtcPath);

public sealed record SourceFileMetadata(string Name, long Size, string Sha256);

public sealed record PackageRow(
    string? CodiceAic, string? CodFarmaco, string? CodConfezione, string? Denominazione,
    string? Descrizione, string? CodiceDitta, string? RagioneSociale, string? StatoAmministrativo,
    string? TipoProcedura, string? Forma, string? CodiceAtc, string? PaAssociati,
    string? Fornitura, string? LinkFi, string? LinkRcp);

public sealed record IngredientRow(string? CodiceAic, string? PrincipioAttivo, string? Quantita, string? UnitaMisura);

public sealed record AtcRow(string? CodiceAtc, string? Descrizione);

public sealed record ImportStatistics(long Packages, long Ingredients, long Atc, long Rejected, TimeSpan Duration);

public sealed record ValidationResult(bool IsValid, long RejectedRows, IReadOnlyList<string> Errors);

public sealed class ValidationException(IReadOnlyList<string> errors) : Exception(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
