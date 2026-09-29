using FluentAssertions;
using MedReminder.Application.Prescriptions;
using MedReminder.Application.Tests.Support;
using MedReminder.Domain.Medicines;
using Xunit;

namespace MedReminder.Application.Tests.Prescriptions;

// Prescription request draft (EVOLUTION-PROPOSALS §3.4): product
// identification only, optional fields omitted when absent, rendered
// in every supported language without missing keys.
public class PrescriptionRequestTextsTests
{
    private static Medicine FullMedicine() => new()
    {
        Name = "Enalapril Teva",
        Package = "20 mg, 28 tablets",
        NationalCode = "035712017",
        DoctorName = "Dr. Rossi",
        Unit = "tablets",
        DosePerAdministration = 1m,
        AdministrationsPerDay = 2,
        Notes = "take with food",
        ActiveIngredient = "enalapril maleate",
    };

    private static Medicine MinimalMedicine() => new()
    {
        Name = "Enalapril Teva",
        Unit = "tablets",
    };

    [Fact]
    public void Includes_every_identifying_field_when_present()
    {
        var loc = new JsonDictionaryLocalizationService("en");

        var draft = PrescriptionRequestTexts.Build(FullMedicine(), "Mario Bianchi", loc);

        draft.Subject.Should().Be("Prescription request: Enalapril Teva");
        draft.Body.Should().Be(
            "Dear Dr. Rossi,\n" +
            "\n" +
            "I would like to request a new prescription for the following medicine:\n" +
            "\n" +
            "  - Medicine: Enalapril Teva\n" +
            "  - Package: 20 mg, 28 tablets\n" +
            "  - Product code: 035712017\n" +
            "\n" +
            "Thank you in advance.\n" +
            "Kind regards,\n" +
            "Mario Bianchi");
        draft.ExplicitRecipient.Should().BeNull();
    }

    [Fact]
    public void Omits_clinical_details()
    {
        var loc = new JsonDictionaryLocalizationService("en");

        var draft = PrescriptionRequestTexts.Build(FullMedicine(), "Mario Bianchi", loc);

        draft.Body.Should().NotContain("take with food")
            .And.NotContain("enalapril maleate");
    }

    [Fact]
    public void Omits_optional_fields_when_absent()
    {
        var loc = new JsonDictionaryLocalizationService("en");

        var draft = PrescriptionRequestTexts.Build(MinimalMedicine(), "Mario Bianchi", loc);

        draft.Body.Should().StartWith("Dear Doctor,\n");
        draft.Body.Should().NotContain("Package:");
        draft.Body.Should().NotContain("Product code:");
        draft.Body.Should().Contain("  - Medicine: Enalapril Teva\n");
    }

    [Fact]
    public void Omits_signature_when_profile_name_is_blank()
    {
        var loc = new JsonDictionaryLocalizationService("en");

        var draft = PrescriptionRequestTexts.Build(MinimalMedicine(), "  ", loc);

        draft.Body.Should().EndWith("Kind regards,");
    }

    [Fact]
    public void Treats_whitespace_optional_fields_as_absent()
    {
        var loc = new JsonDictionaryLocalizationService("en");
        var medicine = MinimalMedicine();
        medicine.Package = "  ";
        medicine.NationalCode = "";
        medicine.DoctorName = " ";

        var draft = PrescriptionRequestTexts.Build(medicine, "Mario Bianchi", loc);

        draft.Body.Should().StartWith("Dear Doctor,\n");
        draft.Body.Should().NotContain("Package:").And.NotContain("Product code:");
    }

    [Fact]
    public void Uses_the_ui_language()
    {
        var loc = new JsonDictionaryLocalizationService("it");

        var draft = PrescriptionRequestTexts.Build(FullMedicine(), "Mario Bianchi", loc);

        draft.Subject.Should().Be("Richiesta di ricetta: Enalapril Teva");
        draft.Body.Should().Contain("Codice AIC: 035712017");
        draft.Body.Should().StartWith("Gentile Dr. Rossi,");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Renders_in_every_supported_language_without_missing_keys(string languageCode)
    {
        var loc = new JsonDictionaryLocalizationService(languageCode);

        var full = PrescriptionRequestTexts.Build(FullMedicine(), "Mario Bianchi", loc);
        var minimal = PrescriptionRequestTexts.Build(MinimalMedicine(), "Mario Bianchi", loc);

        loc.FallbackHits.Should().BeEmpty($"'{languageCode}' must define every key the draft uses");
        foreach (var text in new[] { full.Subject, full.Body, minimal.Subject, minimal.Body })
        {
            text.Should().NotContain("[PrescriptionRequest.");
            text.Should().NotContain("{0}");
        }
        full.Subject.Should().Contain("Enalapril Teva");
        full.Body.Should().Contain("Enalapril Teva")
            .And.Contain("20 mg, 28 tablets")
            .And.Contain("035712017")
            .And.Contain("Dr. Rossi")
            .And.Contain("Mario Bianchi");
    }
}
