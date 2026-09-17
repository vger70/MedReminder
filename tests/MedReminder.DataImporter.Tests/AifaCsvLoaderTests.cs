using System.Text;
using FluentAssertions;
using MedReminder.DataImporter.Import;
using Xunit;

namespace MedReminder.DataImporter.Tests;

public sealed class AifaCsvLoaderTests
{
    [Fact]
    public async Task ReadsUtf8BomQuotedFieldsAndEmptyValues()
    {
        var path = CreateFile("\ufeffCODICE_AIC;PRINCIPIO_ATTIVO;QUANTITA;UNITA_MISURA\r\n0001;\"ACIDO; SALICILICO\";;mg\r\n");
        var rows = new List<IngredientRow>();
        var count = await new AifaCsvLoader().ReadIngredientsAsync(path, (row, _, _) => { rows.Add(row); return Task.CompletedTask; }, CancellationToken.None);
        count.Should().Be(1);
        rows.Should().ContainSingle().Which.Should().Be(new IngredientRow("0001", "ACIDO; SALICILICO", null, "mg"));
    }

    [Fact]
    public async Task RejectsUnexpectedHeader()
    {
        var path = CreateFile("AIC;PRINCIPIO_ATTIVO;QUANTITA;UNITA_MISURA\n");
        var act = () => new AifaCsvLoader().ReadIngredientsAsync(path, (_, _, _) => Task.CompletedTask, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public void PreservesNotAvailableIngredientsAndOnlyParsesUnambiguousQuantities()
    {
        AifaValueParser.IsNotAvailableIngredient("N.D.").Should().BeTrue();
        AifaValueParser.TryParseUnambiguousQuantity("1,5", out var value).Should().BeTrue();
        value.Should().Be(1.5m);
        AifaValueParser.TryParseUnambiguousQuantity("about one", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("A", null)]
    [InlineData("A01", "A")]
    [InlineData("A01A", "A01")]
    [InlineData("A01AA", "A01A")]
    [InlineData("A01AA01", "A01AA")]
    public void DerivesAtcParents(string code, string? expectedParent) => AifaValueParser.GetAtcParent(code).Should().Be(expectedParent);

    [Fact]
    public async Task HonorsCancellation()
    {
        var path = CreateFile("CODICE_AIC;PRINCIPIO_ATTIVO;QUANTITA;UNITA_MISURA\n0001;N.D.;0;N.D.\n");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var act = () => new AifaCsvLoader().ReadIngredientsAsync(path, (_, _, _) => Task.CompletedTask, cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static string CreateFile(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"medreminder-aifa-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
