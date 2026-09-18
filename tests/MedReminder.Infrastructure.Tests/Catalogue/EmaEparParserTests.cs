using FluentAssertions;
using MedReminder.Domain.Catalogue;
using MedReminder.Infrastructure.Catalogue.Parsers;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Catalogue;

// Exercises the EPAR parser against the 73-row curated fixture
// under tests/fixtures/catalogue/ema-epar-sample.csv (3 Veterinary
// rows are skipped, 70 Human rows are kept, spanning every
// Medicine status EMA emits).
public sealed class EmaEparParserTests
{
    private static readonly CountryCode Eu = CountryCode.Parse("EU");

    [Fact]
    public async Task Parses_only_the_human_rows_and_records_veterinary_as_skipped()
    {
        var parser = new EmaEparParser();
        var report = new ParseReport();

        var rows = await CollectAsync(parser, report);

        rows.Should().HaveCount(70);
        report.Skipped.Should().Be(3);
    }

    [Fact]
    public async Task Every_yielded_row_carries_country_EU()
    {
        var parser = new EmaEparParser();
        var report = new ParseReport();

        var rows = await CollectAsync(parser, report);

        // The `country = "EU"` invariant is what M3 §3.4 requires:
        // no matter what the row's Marketing Authorisation Holder or
        // any other free-text column says, the persisted country
        // stays "EU". This proves the invariant on the parse boundary,
        // before the importer even sees the rows.
        rows.Should().OnlyContain(r => r.Country == Eu);
        rows.Should().OnlyContain(r => r.Country.Value == "EU");
    }

    [Fact]
    public void Country_code_normalises_the_long_form_European_Union()
    {
        // Sanity-check that the parser's country hard-coding goes
        // through CountryCode.Parse("European Union") and lands as
        // the 2-char "EU" code, not the long form.
        var parsed = CountryCode.Parse("European Union");
        parsed.Value.Should().Be("EU");
        parsed.IsSupranational.Should().BeTrue();

        var parser = new EmaEparParser();
        parser.SupportedCountries.Should().ContainSingle().Which.Value.Should().Be("EU");
    }

    [Fact]
    public async Task Multi_ingredient_row_splits_active_substance_on_semicolon()
    {
        var parser = new EmaEparParser();
        var report = new ParseReport();
        var rows = await CollectAsync(parser, report);

        // Symtuza is a fixed-dose combination: darunavir + cobicistat
        // + emtricitabine + tenofovir alafenamide, separated by ';'
        // in the EMA "Active substance" column.
        var symtuza = rows.SingleOrDefault(r => r.NationalCode == "EMEA/H/C/004391");
        symtuza.Should().NotBeNull("Symtuza should be present in the fixture");
        symtuza!.CommercialName.Should().Be("Symtuza");
        symtuza.ActiveIngredients.Select(i => i.Name).Should().BeEquivalentTo(new[]
        {
            "darunavir",
            "cobicistat",
            "emtricitabine",
            "tenofovir alafenamide",
        });
    }

    [Fact]
    public async Task Two_ingredient_combination_row_is_parsed_correctly()
    {
        var parser = new EmaEparParser();
        var report = new ParseReport();
        var rows = await CollectAsync(parser, report);

        // DuoPlavin: clopidogrel + acetylsalicylic acid.
        var duoplavin = rows.SingleOrDefault(r => r.NationalCode == "EMEA/H/C/001143");
        duoplavin.Should().NotBeNull("DuoPlavin should be present in the fixture");
        duoplavin!.ActiveIngredients.Select(i => i.Name).Should().BeEquivalentTo(new[]
        {
            "clopidogrel",
            "acetylsalicylic acid",
        });
    }

    [Fact]
    public async Task Comma_inside_a_single_active_substance_description_is_not_split()
    {
        var parser = new EmaEparParser();
        var report = new ParseReport();
        var rows = await CollectAsync(parser, report);

        // Gardasil 9's "Active substance" contains commas inside a
        // single vaccine description (HPV types "6, 11, 16, 18, 31,
        // 33, 45, 52, 58"). The parser must NOT split on comma.
        var gardasil = rows.SingleOrDefault(r => r.NationalCode == "EMEA/H/C/003852");
        gardasil.Should().NotBeNull("Gardasil 9 should be present in the fixture");
        gardasil!.ActiveIngredients.Should().ContainSingle();
        gardasil.ActiveIngredients.Single().Name.Should().Contain("papillomavirus");
    }

    [Fact]
    public async Task National_code_uses_the_EMA_product_number_verbatim()
    {
        var parser = new EmaEparParser();
        var report = new ParseReport();
        var rows = await CollectAsync(parser, report);

        rows.Should().OnlyContain(r =>
            r.NationalCode.StartsWith("EMEA/H/", StringComparison.Ordinal));
        rows.Select(r => r.NationalCode).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Rows_include_all_medicine_status_values_present_in_the_fixture()
    {
        var parser = new EmaEparParser();
        var report = new ParseReport();
        var rows = await CollectAsync(parser, report);

        // The fixture is stratified across every non-veterinary
        // Medicine status EMA emits, so the parser must let all of
        // them through to the row stream. Filtering by status
        // (e.g. hiding "Withdrawn" in the UI) is not the parser's
        // job — it happens in MedicineAutocompleteBox's badge layer.
        var statuses = rows.Select(r => r.MarketingStatus).Where(s => s is not null).Distinct().ToList();
        statuses.Should().Contain("Authorised");
        statuses.Should().Contain("Withdrawn");
        statuses.Should().Contain("Refused");
    }

    [Fact]
    public async Task Row_without_ATC_still_produces_ingredients_with_null_ATC()
    {
        var parser = new EmaEparParser();
        var report = new ParseReport();
        var rows = await CollectAsync(parser, report);

        // Camcevi is one of the fixture rows without an ATC code
        // in the source. Its active ingredient must still be
        // captured; the ATC lookup on the ingredient stays null.
        var camcevi = rows.SingleOrDefault(r => r.NationalCode == "EMEA/H/C/005034");
        camcevi.Should().NotBeNull();
        camcevi!.ActiveIngredients.Should().ContainSingle();
        camcevi.ActiveIngredients.Single().Atc.Should().BeNull();
    }

    [Fact]
    public async Task MedicineUrl_is_mirrored_onto_both_leaflet_and_spc_links()
    {
        var parser = new EmaEparParser();
        var report = new ParseReport();
        var rows = await CollectAsync(parser, report);

        // EPAR only publishes one landing-page URL per product; the
        // parser mirrors it onto both fields so the query service
        // can hand the UI a link without recomputing which side the
        // user clicks.
        rows.Should().OnlyContain(r =>
            (r.LinkLeaflet == null && r.LinkSpc == null)
            || r.LinkLeaflet == r.LinkSpc);
    }

    [Fact]
    public async Task Parser_reads_from_a_non_seekable_stream()
    {
        var parser = new EmaEparParser();
        var report = new ParseReport();

        await using var seekable = CatalogueFixtures.BuildEmaEparSnapshotStream();
        await using var nonSeekable = new NonSeekableStream(seekable);

        var rows = new List<ReferenceMedicineRow>();
        await foreach (var row in parser.ParseAsync(nonSeekable, report, CancellationToken.None))
        {
            rows.Add(row);
        }

        rows.Should().HaveCount(70);
    }

    private static async Task<List<ReferenceMedicineRow>> CollectAsync(
        EmaEparParser parser, ParseReport report)
    {
        var rows = new List<ReferenceMedicineRow>();
        await using var snapshot = CatalogueFixtures.BuildEmaEparSnapshotStream();
        await foreach (var row in parser.ParseAsync(snapshot, report, CancellationToken.None))
        {
            rows.Add(row);
        }
        return rows;
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly Stream _inner;

        public NonSeekableStream(Stream inner) => _inner = inner;

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
