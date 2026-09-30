using MedReminder.Application.Abstractions;

namespace MedReminder.Application.Tests.Support;

internal sealed class FakeMasterRole : IMasterRole
{
    public bool SendsEmail { get; set; } = true;

    public Task<bool> SendsEmailAsync(CancellationToken cancellationToken) => Task.FromResult(SendsEmail);
}
