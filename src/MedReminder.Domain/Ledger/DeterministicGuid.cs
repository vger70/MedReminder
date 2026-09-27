using System.Security.Cryptography;
using System.Text;

namespace MedReminder.Domain.Ledger;

// Name-based GUID (RFC 9562 version 5: SHA-1 over namespace + name).
// Every device computes the same id for the same derived row.
public static class DeterministicGuid
{
    public static Guid Create(Guid namespaceId, string name)
    {
        Span<byte> ns = stackalloc byte[16];
        namespaceId.TryWriteBytes(ns, bigEndian: true, out _);
        var nameBytes = Encoding.UTF8.GetBytes(name);

        var input = new byte[16 + nameBytes.Length];
        ns.CopyTo(input);
        nameBytes.CopyTo(input, 16);
        var hash = SHA1.HashData(input);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
