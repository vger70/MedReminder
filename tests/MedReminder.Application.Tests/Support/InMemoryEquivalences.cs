using System.Security.Cryptography;
using System.Text;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryEquivalenceListStore : IEquivalenceListStore
{
    public byte[]? Stored { get; private set; }

    public EquivalenceList? List { get; set; }

    public EquivalenceList? Load() => List;

    // The hash of the last saved file, as the file store reports it.
    public string? StoredSha256() => Stored is null || List is null ? null : Convert.ToHexStringLower(SHA256.HashData(Stored));

    public void Save(byte[] listJson)
    {
        Stored = listJson;
        List = EquivalenceFeedParser.TryParseList(Encoding.UTF8.GetString(listJson), out var list, out _) ? list : null;
    }
}
