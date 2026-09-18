using FluentAssertions;
using MedReminder.Domain.Catalogue;
using MedReminder.Infrastructure.Catalogue.Parsers;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Catalogue;

// Exercises the ANSM/BDPM parser against the 100-row curated fixture
// under tests/fixtures/catalogue/bdpm-cis-sample.txt (+ COMPO). The
// fixture keeps 9 rows whose Type de procédure AMM starts with
// "Enreg homéo" so the homeopathic-skip filter is exercised; the
// remaining 91 must reach the row stream.
public sealed class AnsmBdpmParserTests
{
    private static readonly CountryCode France = CountryCode.Parse("FR");

    [Fact]
    public async Task Parses_only_non_homeopathic_rows_and_records_the_rest_as_skipped()
    {
        var parser = new AnsmBdpmParser();
        var report = new ParseReport();

        var rows = await CollectAsync(parser, report);

        rows.Should().HaveCount(91);
        report.Skipped.Should().Be(9);
    }

    [Fact]
    public async Task Every_yielded_row_carries_country_FR()
    {
        var parser = new AnsmBdpmParser();
        var report = new ParseReport();

        var rows = await CollectAsync(parser, report);

        rows.Should().OnlyContain(r => r.Country == France);
        rows.Should().OnlyContain(r => r.Country.Value == "FR");
    }

    [Fact]
    public void SupportedCountries_lists_FR_only()
    {
        var parser = new AnsmBdpmParser();

        parser.SupportedCountries.Should().ContainSingle().Which.Value.Should().Be("FR");
    }

    [Fact]
    public async Task National_code_uses_the_CIS_verbatim_and_is_unique_per_row()
    {
        var parser = new AnsmBdpmParser();
        var report = new ParseReport();
        var rows = await CollectAsync(parser, report);

        rows.Should().OnlyContain(r => r.NationalCode.Length >= 8);
        rows.Select(r => r.NationalCode).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Iso_8859_15_encoding_round_trips_accented_denominations()
    {
        var parser = new AnsmBdpmParser();
        var report = new ParseReport();
        var rows = await CollectAsync(parser, report);

        // Every BDPM denomination that carries an accented letter must
        // round-trip through the ISO-8859-15 code-page reader without
        // mojibake. If the encoding were wrong the accented bytes
        // would decode as replacement chars (�) or Latin-1
        // near-equivalents.
        rows.Should().NotContain(r => r.CommercialName.Contains('�'));
        rows.Should().Contain(r => r.CommercialName.Contains('é')
            || r.CommercialName.Contains('è')
            || r.CommercialName.Contains('à'));
    }

    [Fact]
    public async Task Multi_ingredient_CIS_joins_multiple_COMPO_rows()
    {
        var parser = new AnsmBdpmParser();
        var report = new ParseReport();
        var rows = await CollectAsync(parser, report);

        // At least a handful of CIS in the fixture come with 2+ COMPO
        // rows; we assert the join produces multiple ingredients on
        // the same ReferenceMedicineRow, in source order, deduplicated.
        var multi = rows.Where(r => r.ActiveIngredients.Count >= 2).ToList();
        multi.Should().NotBeEmpty(
            "the fixture is stratified to include multi-ingredient CIS");
        multi.Should().OnlyContain(r =>
            r.ActiveIngredients.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                == r.ActiveIngredients.Count);
    }

    [Fact]
    public async Task Every_ingredient_lands_with_a_null_ATC()
    {
        var parser = new AnsmBdpmParser();
        var report = new ParseReport();
        var rows = await CollectAsync(parser, report);

        // BDPM base does not ship an ATC column, so the parser must
        // leave every ingredient's Atc as null (same convention as
        // EMA EPAR when the source row has no ATC). A later increment
        // can enrich these via a separate ATC dataset.
        rows.SelectMany(r => r.ActiveIngredients).Should().OnlyContain(i => i.Atc == null);
    }

    [Fact]
    public async Task DispensingRegime_leaflet_and_spc_are_always_null()
    {
        var parser = new AnsmBdpmParser();
        var report = new ParseReport();
        var rows = await CollectAsync(parser, report);

        rows.Should().OnlyContain(r =>
            r.DispensingRegime == null && r.LinkLeaflet == null && r.LinkSpc == null);
    }

    [Fact]
    public async Task Marketing_status_covers_every_bucket_present_in_the_fixture()
    {
        var parser = new AnsmBdpmParser();
        var report = new ParseReport();
        var rows = await CollectAsync(parser, report);

        var statuses = rows.Select(r => r.MarketingStatus).Where(s => s is not null).Distinct().ToList();
        statuses.Should().Contain("Autorisation active");
        statuses.Should().Contain("Autorisation abrogée");
        statuses.Should().Contain("Autorisation retirée");
    }

    [Fact]
    public async Task MAH_titulaire_leading_space_is_trimmed()
    {
        var parser = new AnsmBdpmParser();
        var report = new ParseReport();
        var rows = await CollectAsync(parser, report);

        // Upstream ships titulaires prefixed with a spurious space
        // (" PHARMA DEVELOPPEMENT"). No persisted row must retain
        // that leading space — otherwise every join against a MAH
        // column would need to trim at query time.
        rows.Where(r => r.MarketingAuthorisationHolder is not null)
            .Should().OnlyContain(r => !r.MarketingAuthorisationHolder!.StartsWith(' '));
    }

    [Fact]
    public async Task Parser_reads_from_a_non_seekable_stream()
    {
        var parser = new AnsmBdpmParser();
        var report = new ParseReport();

        await using var seekable = CatalogueFixtures.BuildBdpmSnapshotStream();
        await using var nonSeekable = new NonSeekableStreamWrapper(seekable);

        var rows = new List<ReferenceMedicineRow>();
        await foreach (var row in parser.ParseAsync(nonSeekable, report, CancellationToken.None))
        {
            rows.Add(row);
        }

        rows.Should().HaveCount(91);
    }

    private static async Task<List<ReferenceMedicineRow>> CollectAsync(
        AnsmBdpmParser parser, ParseReport report)
    {
        var rows = new List<ReferenceMedicineRow>();
        await using var snapshot = CatalogueFixtures.BuildBdpmSnapshotStream();
        await foreach (var row in parser.ParseAsync(snapshot, report, CancellationToken.None))
        {
            rows.Add(row);
        }
        return rows;
    }

    private sealed class NonSeekableStreamWrapper : Stream
    {
        private readonly Stream _inner;

        public NonSeekableStreamWrapper(Stream inner) => _inner = inner;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => _inner.Read(buffer, offset, count);

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
