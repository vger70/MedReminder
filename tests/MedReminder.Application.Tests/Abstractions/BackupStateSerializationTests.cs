using System.Text.Json;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Abstractions;

// backup.state.json files written before SignInRequired and
// FailureNotifiedOn existed must keep loading: BackupStateStore returns
// an empty state on any deserialization error, which would lose the
// last-backup history.
public class BackupStateSerializationTests
{
    [Fact]
    public void State_written_before_the_sign_in_fields_still_loads()
    {
        const string json = """
            {
              "LastSuccessfulBackupAt": "2026-10-20T03:00:00+00:00",
              "LastAttemptAt": "2026-10-21T03:00:00+00:00",
              "LastError": "cloud: Sign in to OneDrive again to continue.",
              "LastBackupFile": "backup.db"
            }
            """;

        var state = JsonSerializer.Deserialize<BackupState>(json);

        state.Should().NotBeNull();
        state!.LastSuccessfulBackupAt.Should().Be(new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero));
        state.LastBackupFile.Should().Be("backup.db");
        state.SignInRequired.Should().BeNull();
        state.FailureNotifiedOn.Should().BeNull();
    }

    [Fact]
    public void Sign_in_fields_round_trip()
    {
        var state = BackupState.Empty with
        {
            SignInRequired = CloudProvider.GoogleDrive,
            FailureNotifiedOn = new DateOnly(2026, 10, 22),
        };

        JsonSerializer.Deserialize<BackupState>(JsonSerializer.Serialize(state)).Should().Be(state);
    }
}
