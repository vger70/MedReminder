using System.Security.Cryptography;
using System.Text;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryShortageListStore : IShortageListStore
{
    public byte[]? Stored { get; private set; }

    public ShortageList? List { get; set; }

    public ShortageList? Load() => List;

    // The hash of the last saved file, as the file store reports it.
    public string? StoredSha256() => Stored is null || List is null ? null : Convert.ToHexStringLower(SHA256.HashData(Stored));

    public void Save(byte[] listJson)
    {
        Stored = listJson;
        List = ShortageFeedParser.TryParseList(Encoding.UTF8.GetString(listJson), out var list, out _) ? list : null;
    }
}

internal sealed class InMemoryShortageNoticeEventRepository : IShortageNoticeEventRepository
{
    private readonly List<ShortageNoticeEvent> _items = new();

    public IReadOnlyList<ShortageNoticeEvent> All => _items;

    public Task<bool> ExistsAsync(Guid medicineId, string code, DateOnly start, CancellationToken cancellationToken)
        => Task.FromResult(_items.Any(e => e.MedicineId == medicineId && e.Code == code && e.Start == start));

    public Task AddAsync(ShortageNoticeEvent notice, CancellationToken cancellationToken)
    {
        _items.Add(notice);
        return Task.CompletedTask;
    }
}
