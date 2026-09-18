using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Medicines;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

public sealed class LinkMedicineToReferenceUseCaseTests
{
    private readonly FakeMedicineRepository _medicines = new();
    private readonly FakeQueryService _query = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly TimeProvider _clock = TimeProvider.System;

    private LinkMedicineToReferenceUseCase Build() =>
        new(_medicines, _query, _uow, _clock);

    [Fact]
    public async Task Links_medicine_and_populates_national_code_atc_and_reference_id()
    {
        var medicine = SeedMedicine();
        var reference = new ReferenceMedicine
        {
            Country = CountryCode.Parse("IT"),
            NationalCode = "023921048",
            CommercialName = "PIPEMID",
            SnapshotVersion = "202609",
            ActiveIngredients = new[]
            {
                new ReferenceActiveIngredient
                {
                    Country = CountryCode.Parse("IT"),
                    Name = "ACIDO PIPEMIDICO TRIIDRATO",
                    Atc = AtcCode.Parse("J01MB04"),
                },
            },
        };
        _query.Register(reference);

        var result = await Build().ExecuteAsync(
            medicine.Id, CountryCode.Parse("IT"), "023921048", CancellationToken.None);

        result.Should().Be(LinkResult.Linked);
        medicine.NationalCode.Should().Be("023921048");
        medicine.LinkedReferenceMedicineId.Should().Be(reference.Id);
        medicine.AtcCode.Should().Be(AtcCode.Parse("J01MB04"));
        _uow.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Populates_active_ingredient_only_when_medicine_had_none()
    {
        var medicine = SeedMedicine();
        var reference = MultiIngredientReference();
        _query.Register(reference);

        await Build().ExecuteAsync(
            medicine.Id, CountryCode.Parse("IT"), reference.NationalCode, CancellationToken.None);

        medicine.ActiveIngredient.Should().Be("AMOXICILLINA / ACIDO CLAVULANICO");
    }

    [Fact]
    public async Task Preserves_active_ingredient_typed_by_the_user()
    {
        var medicine = SeedMedicine(activeIngredient: "amoxi (mia nota)");
        var reference = MultiIngredientReference();
        _query.Register(reference);

        await Build().ExecuteAsync(
            medicine.Id, CountryCode.Parse("IT"), reference.NationalCode, CancellationToken.None);

        medicine.ActiveIngredient.Should().Be("amoxi (mia nota)");
    }

    [Fact]
    public async Task Preserves_notes_dose_and_schedule_metadata()
    {
        var medicine = SeedMedicine();
        var originalNotes = medicine.Notes;
        var originalDose = medicine.DosePerAdministration;
        var originalPerDay = medicine.AdministrationsPerDay;
        var originalEpoch = medicine.StockEpoch;
        var reference = MultiIngredientReference();
        _query.Register(reference);

        await Build().ExecuteAsync(
            medicine.Id, CountryCode.Parse("IT"), reference.NationalCode, CancellationToken.None);

        medicine.Notes.Should().Be(originalNotes);
        medicine.DosePerAdministration.Should().Be(originalDose);
        medicine.AdministrationsPerDay.Should().Be(originalPerDay);
        medicine.StockEpoch.Should().Be(originalEpoch);
    }

    [Fact]
    public async Task Returns_reference_not_found_when_national_code_is_missing_from_catalogue()
    {
        var medicine = SeedMedicine();

        var result = await Build().ExecuteAsync(
            medicine.Id, CountryCode.Parse("IT"), "999999999", CancellationToken.None);

        result.Should().Be(LinkResult.ReferenceNotFound);
        medicine.NationalCode.Should().BeNull();
        medicine.LinkedReferenceMedicineId.Should().BeNull();
        _uow.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Throws_when_medicine_does_not_exist()
    {
        Func<Task> act = () => Build().ExecuteAsync(
            Guid.NewGuid(), CountryCode.Parse("IT"), "023921048", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private Medicine SeedMedicine(string? activeIngredient = null)
    {
        var medicine = new Medicine
        {
            Name = "Custom",
            Unit = "tablets",
            ActiveIngredient = activeIngredient,
            Notes = "user notes stay",
            DosePerAdministration = 1m,
            AdministrationsPerDay = 2,
            StockEpoch = 3,
        };
        _medicines.Add(medicine);
        return medicine;
    }

    private static ReferenceMedicine MultiIngredientReference() => new()
    {
        Country = CountryCode.Parse("IT"),
        NationalCode = "029520018",
        CommercialName = "AUGMENTIN",
        SnapshotVersion = "202609",
        ActiveIngredients = new[]
        {
            new ReferenceActiveIngredient
            {
                Country = CountryCode.Parse("IT"),
                Name = "AMOXICILLINA",
                Atc = AtcCode.Parse("J01CR02"),
            },
            new ReferenceActiveIngredient
            {
                Country = CountryCode.Parse("IT"),
                Name = "ACIDO CLAVULANICO",
                Atc = null,
            },
        },
    };

    private sealed class FakeMedicineRepository : IMedicineRepository
    {
        private readonly Dictionary<Guid, Medicine> _rows = new();

        public void Add(Medicine medicine) => _rows[medicine.Id] = medicine;

        public Task<Medicine?> GetAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(_rows.GetValueOrDefault(id));

        public Task<IReadOnlyList<Medicine>> ListActiveAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Medicine>>(_rows.Values.Where(m => m.IsActive).ToArray());

        public Task<IReadOnlyList<Medicine>> ListAllAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Medicine>>(_rows.Values.ToArray());

        public Task AddAsync(Medicine medicine, CancellationToken cancellationToken)
        {
            _rows[medicine.Id] = medicine;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Medicine medicine, CancellationToken cancellationToken)
        {
            _rows[medicine.Id] = medicine;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeQueryService : IReferenceCatalogueQueryService
    {
        private readonly Dictionary<(string Country, string Code), ReferenceMedicine> _rows = new();

        public void Register(ReferenceMedicine row)
            => _rows[(row.Country.Value, row.NationalCode)] = row;

        public Task<IReadOnlyList<ReferenceMedicine>> SearchByCommercialNameAsync(
            string prefix, IReadOnlyCollection<CountryCode> countryScope, int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ReferenceMedicine>>(Array.Empty<ReferenceMedicine>());

        public Task<IReadOnlyList<ReferenceMedicine>> SearchByActiveIngredientAsync(
            string prefix, IReadOnlyCollection<CountryCode> countryScope, int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ReferenceMedicine>>(Array.Empty<ReferenceMedicine>());

        public Task<ReferenceMedicine?> GetByNationalCodeAsync(
            CountryCode country, string nationalCode, CancellationToken cancellationToken)
            => Task.FromResult(_rows.GetValueOrDefault((country.Value, nationalCode)));
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCalls++;
            return Task.FromResult(0);
        }
    }
}
