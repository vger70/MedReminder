using FluentAssertions;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Domain.Tests.Stock;

public class PackageExpiryRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    private static StockPackage Package(
        DateOnly? expiresOn = null, int? useWithinDays = null, DateOnly? openedOn = null) => new()
    {
        MedicineId = Guid.NewGuid(),
        Quantity = 28m,
        ExpiresOn = expiresOn,
        UseWithinDays = useWithinDays,
        OpenedOn = openedOn,
    };

    [Theory]
    [InlineData(2027, 1, 31)]
    [InlineData(2027, 2, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2027, 4, 30)]
    [InlineData(2027, 12, 31)]
    public void A_printed_month_expires_on_its_last_day(int year, int month, int lastDay)
        => PackageExpiryRules.EndOfMonth(year, month).Should().Be(new DateOnly(year, month, lastDay));

    [Fact]
    public void The_in_use_period_counts_the_opening_day_as_day_one()
        => PackageExpiryRules.InUseUntil(Package(useWithinDays: 28, openedOn: new DateOnly(2027, 3, 1)))
            .Should().Be(new DateOnly(2027, 3, 28));

    [Fact]
    public void A_sealed_package_expires_on_its_printed_date()
    {
        var package = Package(expiresOn: new DateOnly(2027, 3, 31), useWithinDays: 28);

        PackageExpiryRules.EffectiveExpiry(package).Should().Be(new DateOnly(2027, 3, 31));
        PackageExpiryRules.EndsWithInUsePeriod(package).Should().BeFalse();
    }

    [Fact]
    public void An_opened_package_expires_on_the_earlier_of_the_two_dates()
    {
        var early = Package(expiresOn: new DateOnly(2027, 3, 31), useWithinDays: 28, openedOn: new DateOnly(2026, 10, 1));
        var late = Package(expiresOn: new DateOnly(2026, 10, 15), useWithinDays: 28, openedOn: new DateOnly(2026, 10, 1));

        PackageExpiryRules.EffectiveExpiry(early).Should().Be(new DateOnly(2026, 10, 28));
        PackageExpiryRules.EndsWithInUsePeriod(early).Should().BeTrue();
        PackageExpiryRules.EffectiveExpiry(late).Should().Be(new DateOnly(2026, 10, 15));
        PackageExpiryRules.EndsWithInUsePeriod(late).Should().BeFalse();
    }

    [Fact]
    public void An_opened_package_without_printed_expiry_expires_at_the_end_of_its_in_use_period()
        => PackageExpiryRules.EffectiveExpiry(Package(useWithinDays: 10, openedOn: new DateOnly(2026, 10, 1)))
            .Should().Be(new DateOnly(2026, 10, 10));

    [Fact]
    public void A_package_with_no_date_has_no_expiry_and_stays_valid()
    {
        var package = Package(useWithinDays: 28);

        PackageExpiryRules.EffectiveExpiry(package).Should().BeNull();
        PackageExpiryRules.StatusOn(package, Today, 28m, PackageLeadDays.Default)
            .Should().Be(PackageExpiryStatus.Valid);
    }

    [Theory]
    [InlineData(-31, PackageExpiryStatus.Valid)]
    [InlineData(-30, PackageExpiryStatus.ExpiringSoon)]
    [InlineData(0, PackageExpiryStatus.ExpiringSoon)]
    [InlineData(1, PackageExpiryStatus.Expired)]
    public void A_printed_expiry_takes_the_printed_lead(int daysFromExpiry, PackageExpiryStatus expected)
    {
        var expiry = new DateOnly(2027, 3, 31);

        PackageExpiryRules.StatusOn(Package(expiresOn: expiry), expiry.AddDays(daysFromExpiry), 28m,
            PackageLeadDays.Default).Should().Be(expected);
    }

    [Theory]
    [InlineData(-4, PackageExpiryStatus.Valid)]
    [InlineData(-3, PackageExpiryStatus.ExpiringSoon)]
    [InlineData(1, PackageExpiryStatus.Expired)]
    public void The_end_of_an_in_use_period_takes_the_in_use_lead(int daysFromExpiry, PackageExpiryStatus expected)
    {
        // Opened on 1 October, 28 days: last day 28 October.
        var package = Package(expiresOn: new DateOnly(2027, 3, 31), useWithinDays: 28, openedOn: new DateOnly(2026, 10, 1));
        var expiry = new DateOnly(2026, 10, 28);

        PackageExpiryRules.StatusOn(package, expiry.AddDays(daysFromExpiry), 28m, PackageLeadDays.Default)
            .Should().Be(expected);
    }

    [Fact]
    public void Custom_leads_move_the_expiring_soon_window()
    {
        var expiry = new DateOnly(2027, 3, 31);
        var leads = new PackageLeadDays(Printed: 60, InUse: 7);

        PackageExpiryRules.StatusOn(Package(expiresOn: expiry), expiry.AddDays(-60), 28m, leads)
            .Should().Be(PackageExpiryStatus.ExpiringSoon);
        PackageExpiryRules.StatusOn(Package(expiresOn: expiry), expiry.AddDays(-61), 28m, leads)
            .Should().Be(PackageExpiryStatus.Valid);
    }

    [Fact]
    public void A_lead_of_zero_keeps_only_the_expired_stage()
    {
        var expiry = new DateOnly(2027, 3, 31);
        var leads = new PackageLeadDays(Printed: 0, InUse: 0);

        PackageExpiryRules.StatusOn(Package(expiresOn: expiry), expiry, 28m, leads)
            .Should().Be(PackageExpiryStatus.Valid);
        PackageExpiryRules.StatusOn(Package(expiresOn: expiry), expiry.AddDays(1), 28m, leads)
            .Should().Be(PackageExpiryStatus.Expired);
    }

    [Fact]
    public void Leads_outside_the_range_are_clamped()
        => new PackageLeadDays(Printed: 999, InUse: -5).Clamped()
            .Should().Be(new PackageLeadDays(PackageLeadDays.MaxPrinted, 0));

    [Fact]
    public void A_closed_or_used_up_package_is_never_expired()
    {
        var expired = Package(expiresOn: new DateOnly(2026, 1, 31));

        PackageExpiryRules.StatusOn(expired, Today, 0m, PackageLeadDays.Default)
            .Should().Be(PackageExpiryStatus.UsedUp);
        expired.ClosedOn = Today;
        expired.Closure = PackageClosure.Discarded;
        PackageExpiryRules.StatusOn(expired, Today, 28m, PackageLeadDays.Default)
            .Should().Be(PackageExpiryStatus.Closed);
    }

    [Fact]
    public void A_consistent_package_is_valid()
        => PackageExpiryRules.Validate(
                Package(expiresOn: new DateOnly(2027, 3, 31), useWithinDays: 28, openedOn: Today), Today)
            .Should().BeNull();

    [Fact]
    public void Inconsistent_packages_are_refused()
    {
        PackageExpiryRules.Validate(new StockPackage { MedicineId = Guid.NewGuid(), Quantity = 0m }, Today)
            .Should().Be(PackageError.Quantity);
        PackageExpiryRules.Validate(Package(useWithinDays: 0), Today).Should().Be(PackageError.UseWithinDays);
        PackageExpiryRules.Validate(Package(useWithinDays: 366), Today).Should().Be(PackageError.UseWithinDays);
        PackageExpiryRules.Validate(Package(openedOn: Today.AddDays(1)), Today).Should().Be(PackageError.FutureDate);

        var closedWithoutReason = Package();
        closedWithoutReason.ClosedOn = Today;
        PackageExpiryRules.Validate(closedWithoutReason, Today).Should().Be(PackageError.Closure);

        var closedBeforeOpened = Package(openedOn: Today);
        closedBeforeOpened.ClosedOn = Today.AddDays(-1);
        closedBeforeOpened.Closure = PackageClosure.Finished;
        PackageExpiryRules.Validate(closedBeforeOpened, Today).Should().Be(PackageError.ClosedBeforeOpened);

        PackageExpiryRules.Validate(Package(expiresOn: new DateOnly(1, 1, 31)), Today).Should().Be(PackageError.ExpiresOn);
        PackageExpiryRules.Validate(Package(expiresOn: new DateOnly(2207, 3, 31)), Today).Should().Be(PackageError.ExpiresOn);
        PackageExpiryRules.Validate(Package(expiresOn: Today.AddYears(-20)), Today).Should().BeNull();
        PackageExpiryRules.Validate(Package(expiresOn: Today.AddYears(20)), Today).Should().BeNull();

        var longBatch = Package();
        longBatch.Batch = new string('L', PackageExpiryRules.MaxBatchLength + 1);
        PackageExpiryRules.Validate(longBatch, Today).Should().Be(PackageError.Batch);
    }
}
