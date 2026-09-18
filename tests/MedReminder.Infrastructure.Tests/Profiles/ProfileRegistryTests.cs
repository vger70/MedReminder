using System.Text.Json;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Profiles;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Profiles;

// Unit tests for ProfileRegistry (docs/ANALYSIS-MULTI-USER.md §15a).
// The registry writes plain JSON with PBKDF2 hashes — nothing
// Windows-specific — so these tests run on any platform even though
// the containing project is TFM'd to net10.0-windows.
public sealed class ProfileRegistryTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _registryPath;
    private readonly string _profilesRoot;
    private readonly TimeProvider _clock;

    public ProfileRegistryTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"medreminder-registry-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _registryPath = Path.Combine(_tempDirectory, "profiles.json");
        _profilesRoot = Path.Combine(_tempDirectory, "profiles");
        Directory.CreateDirectory(_profilesRoot);
        _clock = TimeProvider.System;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); }
        catch { /* best-effort */ }
    }

    private ProfileRegistry NewRegistry() =>
        new(_registryPath, _profilesRoot, _clock);

    [Fact]
    public void First_profile_in_empty_registry_is_forced_to_admin()
    {
        var sut = NewRegistry();

        // Even though the caller asks for User, the registry ignores
        // the requested role on the first profile.
        var created = sut.Create("Owner", ProfileRole.User);

        created.Role.Should().Be(ProfileRole.Admin);
        sut.ListProfiles().Should().ContainSingle()
            .Which.Role.Should().Be(ProfileRole.Admin);
    }

    [Fact]
    public void Subsequent_profile_keeps_requested_role()
    {
        var sut = NewRegistry();
        sut.Create("Owner", ProfileRole.Admin);

        var second = sut.Create("Grandma", ProfileRole.User);

        second.Role.Should().Be(ProfileRole.User);
        sut.ListProfiles().Should().HaveCount(2);
    }

    [Fact]
    public void Rename_updates_display_name_and_persists()
    {
        var sut = NewRegistry();
        var created = sut.Create("Old Name", ProfileRole.Admin);

        sut.Rename(created.Id, "New Name");

        var reloaded = NewRegistry().GetById(created.Id);
        reloaded!.DisplayName.Should().Be("New Name");
    }

    [Fact]
    public void Delete_last_admin_throws_invalid_operation()
    {
        var sut = NewRegistry();
        var admin = sut.Create("Owner", ProfileRole.Admin);
        sut.Create("Grandma", ProfileRole.User);

        var act = () => sut.Delete(admin.Id, deleteData: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*last admin*");
        sut.ListProfiles().Should().HaveCount(2);
    }

    [Fact]
    public void Delete_admin_when_another_admin_exists_succeeds()
    {
        var sut = NewRegistry();
        var admin1 = sut.Create("Owner1", ProfileRole.Admin);
        sut.Create("Owner2", ProfileRole.Admin);

        sut.Delete(admin1.Id, deleteData: false);

        sut.ListProfiles().Should().ContainSingle();
    }

    [Fact]
    public void Delete_with_deleteData_true_removes_folder()
    {
        var sut = NewRegistry();
        var admin = sut.Create("Owner", ProfileRole.Admin);
        var user = sut.Create("Grandma", ProfileRole.User);
        var folder = Path.Combine(_profilesRoot, user.Id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "medreminder.db"), "x");

        sut.Delete(user.Id, deleteData: true);

        Directory.Exists(folder).Should().BeFalse();
        sut.GetById(admin.Id).Should().NotBeNull();
    }

    [Fact]
    public void Delete_with_deleteData_false_keeps_folder()
    {
        var sut = NewRegistry();
        sut.Create("Owner", ProfileRole.Admin);
        var user = sut.Create("Grandma", ProfileRole.User);
        var folder = Path.Combine(_profilesRoot, user.Id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "medreminder.db"), "x");

        sut.Delete(user.Id, deleteData: false);

        Directory.Exists(folder).Should().BeTrue();
        sut.GetById(user.Id).Should().BeNull();
    }

    [Fact]
    public void SetActiveProfileHint_persists_across_reload()
    {
        var sut = NewRegistry();
        sut.Create("Owner", ProfileRole.Admin);
        var user = sut.Create("Grandma", ProfileRole.User);

        sut.SetActiveProfileHint(user.Id);

        NewRegistry().ActiveProfileIdHint.Should().Be(user.Id);
    }

    [Fact]
    public void SetActiveProfileHint_unknown_id_throws()
    {
        var sut = NewRegistry();
        sut.Create("Owner", ProfileRole.Admin);

        var act = () => sut.SetActiveProfileHint("does-not-exist");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetPin_then_VerifyPin_returns_true_for_correct_pin()
    {
        var sut = NewRegistry();
        var admin = sut.Create("Owner", ProfileRole.Admin);

        sut.SetPin(admin.Id, "1234");

        sut.HasPin(admin.Id).Should().BeTrue();
        sut.VerifyPin(admin.Id, "1234").Should().BeTrue();
    }

    [Fact]
    public void VerifyPin_wrong_pin_returns_false()
    {
        var sut = NewRegistry();
        var admin = sut.Create("Owner", ProfileRole.Admin);
        sut.SetPin(admin.Id, "1234");

        sut.VerifyPin(admin.Id, "9999").Should().BeFalse();
    }

    [Fact]
    public void SetPin_null_clears_hash()
    {
        var sut = NewRegistry();
        var admin = sut.Create("Owner", ProfileRole.Admin);
        sut.SetPin(admin.Id, "1234");

        sut.SetPin(admin.Id, null);

        sut.HasPin(admin.Id).Should().BeFalse();
        sut.VerifyPin(admin.Id, "1234").Should().BeFalse();
    }

    [Fact]
    public void SetPin_uses_pbkdf2_with_expected_iterations()
    {
        var sut = NewRegistry();
        var admin = sut.Create("Owner", ProfileRole.Admin);
        sut.SetPin(admin.Id, "1234");

        var json = File.ReadAllText(_registryPath);
        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement
            .GetProperty("Profiles").EnumerateArray()
            .Single(p => p.GetProperty("Id").GetString() == admin.Id);

        entry.GetProperty("PinIterations").GetInt32().Should().Be(100_000);
        entry.GetProperty("PinHash").GetString().Should().NotBeNullOrEmpty();
        entry.GetProperty("PinSalt").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Atomic_write_leaves_no_tmp_file_on_success()
    {
        var sut = NewRegistry();
        sut.Create("Owner", ProfileRole.Admin);

        File.Exists(_registryPath).Should().BeTrue();
        File.Exists(_registryPath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void Unknown_role_in_json_is_deserialized_as_user()
    {
        // Handcrafted JSON with an out-of-vocabulary role — must
        // fall back to User, never silently promote.
        var handWritten = """
        {
          "SchemaVersion": 1,
          "ActiveProfileId": "abc",
          "Profiles": [
            {
              "Id": "abc",
              "DisplayName": "Ambiguous",
              "Role": "root",
              "CreatedAt": "2026-09-16T09:00:00+00:00",
              "LastUsedAt": "2026-09-16T09:00:00+00:00",
              "PinHash": null,
              "PinSalt": null,
              "PinIterations": 0
            }
          ]
        }
        """;
        File.WriteAllText(_registryPath, handWritten);

        var sut = NewRegistry();
        var profile = sut.GetById("abc");

        profile!.Role.Should().Be(ProfileRole.User);
    }

    [Fact]
    public void Missing_registry_file_lists_no_profiles()
    {
        var sut = NewRegistry();

        sut.ListProfiles().Should().BeEmpty();
        sut.ActiveProfileIdHint.Should().BeNull();
    }

    [Fact]
    public void Corrupt_registry_file_lists_no_profiles_and_does_not_throw()
    {
        File.WriteAllText(_registryPath, "{ not json ");

        var sut = NewRegistry();

        sut.ListProfiles().Should().BeEmpty();
    }

    [Fact]
    public void Roundtrip_preserves_all_fields()
    {
        var sut = NewRegistry();
        var admin = sut.Create("Owner", ProfileRole.Admin);
        var user = sut.Create("Grandma", ProfileRole.User);
        sut.SetPin(user.Id, "5678");
        sut.SetActiveProfileHint(user.Id);

        var reloaded = NewRegistry();
        reloaded.ActiveProfileIdHint.Should().Be(user.Id);
        var reloadedAdmin = reloaded.GetById(admin.Id);
        var reloadedUser = reloaded.GetById(user.Id);
        reloadedAdmin!.Role.Should().Be(ProfileRole.Admin);
        reloadedAdmin.HasPin.Should().BeFalse();
        reloadedUser!.Role.Should().Be(ProfileRole.User);
        reloadedUser.HasPin.Should().BeTrue();
        reloaded.VerifyPin(user.Id, "5678").Should().BeTrue();
    }

    [Fact]
    public void First_profile_becomes_active_hint_automatically()
    {
        var sut = NewRegistry();

        var admin = sut.Create("Owner", ProfileRole.Admin);

        sut.ActiveProfileIdHint.Should().Be(admin.Id);
    }

    [Fact]
    public void Delete_active_profile_clears_or_moves_the_hint()
    {
        var sut = NewRegistry();
        var admin = sut.Create("Owner", ProfileRole.Admin);
        var user = sut.Create("Grandma", ProfileRole.User);
        sut.SetActiveProfileHint(user.Id);

        sut.Delete(user.Id, deleteData: false);

        sut.ActiveProfileIdHint.Should().Be(admin.Id);
    }
}
