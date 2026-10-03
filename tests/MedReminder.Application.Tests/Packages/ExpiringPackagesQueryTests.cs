using FluentAssertions;
using MedReminder.Application.Packages;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.Packages;

// Stock → Expiring packages (docs/analysis/ANALYSIS-PACKAGE-EXPIRY.md
// §5.4): every medicine, expired first, inactive medicines included,
// valid and used-up packages left out, lead days from the settings.
public class ExpiringPackagesQueryTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);
    private readonly ApplicationTestScope _scope = new();

    private ExpiringPackagesQuery Query()
        => new(_scope.Medicines, _scope.Packages, _scope.Stock, _scope.Clock, _scope.ProfileSettings);

    private Task<Guid> AddMedicineAsync(string name) => _scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
        Name: name, Unit: "ml", DosePerAdministration: 0.1m, AdministrationsPerDay: 2,
        StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Windows, EndDate: null,
        InitialQuantity: 30m), default);

    private Task<Guid> PackageAsync(Guid medicine, DateOnly expiresOn, decimal quantity = 5m)
        => _scope.SaveStockPackage.ExecuteAsync(
            new SaveStockPackageCommand(null, medicine, quantity, expiresOn, null, null, null), default);

    [Fact]
    public async Task Packages_needing_attention_are_listed_across_medicines_expired_first()
    {
        var first = await AddMedicineAsync("Bravo");
        var second = await AddMedicineAsync("Alfa");
        var soon = await PackageAsync(first, new DateOnly(2026, 9, 20));
        var expired = await PackageAsync(second, new DateOnly(2026, 9, 1));
        await PackageAsync(second, new DateOnly(2027, 12, 31));

        var items = await Query().LoadAsync(default);

        items.Select(i => (i.Item.Package.Id, i.Item.Status)).Should().Equal(
            (expired, PackageExpiryStatus.Expired),
            (soon, PackageExpiryStatus.ExpiringSoon));
        items[0].MedicineName.Should().Be("Alfa");
        items[0].Unit.Should().Be("ml");
    }

    [Fact]
    public async Task An_inactive_medicine_is_listed_and_a_used_up_package_is_not()
    {
        var inactive = await AddMedicineAsync("Old cream");
        await PackageAsync(inactive, new DateOnly(2026, 9, 1));
        (await _scope.Medicines.GetAsync(inactive, default))!.IsActive = false;
        // 30 in stock: the later package holds all of it, the earlier is used up.
        var other = await AddMedicineAsync("Other");
        await PackageAsync(other, new DateOnly(2026, 8, 31), 10m);
        await PackageAsync(other, new DateOnly(2027, 12, 31), 30m);

        var item = (await Query().LoadAsync(default)).Should().ContainSingle().Subject;

        item.MedicineId.Should().Be(inactive);
        item.MedicineIsActive.Should().BeFalse();
    }

    [Fact]
    public async Task The_lead_days_come_from_the_profile_settings()
    {
        // Expires in 17 days.
        var medicine = await AddMedicineAsync("Timolol");
        await PackageAsync(medicine, new DateOnly(2026, 9, 30));
        (await Query().LoadAsync(default)).Should().ContainSingle();

        _scope.ProfileSettings.Write(new Dictionary<string, string?> { [ProfileSetting.PackageExpiryLeadDays] = "10" });

        (await Query().LoadAsync(default)).Should().BeEmpty();
    }
}
