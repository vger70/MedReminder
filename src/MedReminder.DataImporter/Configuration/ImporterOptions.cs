namespace MedReminder.DataImporter.Configuration;

public sealed class ImporterOptions
{
    public const string SectionName = "Importer";

    public int CommandTimeoutSeconds { get; init; } = 600;
    public int ProgressEveryRows { get; init; } = 10_000;
}
