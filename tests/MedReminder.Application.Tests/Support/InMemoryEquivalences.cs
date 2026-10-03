using System.Text;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryEquivalenceListStore : IEquivalenceListStore
{
    public byte[]? Stored { get; private set; }

    public EquivalenceList? List { get; set; }

    public EquivalenceList? Load() => List;

    public void Save(byte[] listJson)
    {
        Stored = listJson;
        List = EquivalenceFeedParser.TryParseList(Encoding.UTF8.GetString(listJson), out var list, out _) ? list : null;
    }
}
