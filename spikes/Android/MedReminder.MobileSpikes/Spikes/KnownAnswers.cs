namespace MedReminder.MobileSpikes.Spikes;

// Known-answer vectors, computed off-device with independent
// implementations: argon2-cffi 25.1.0 (reference Argon2, version 0x13)
// and pyca/cryptography 41.0.7 (AESGCM). A match on the phone shows the
// Android build of the production cipher computes the same bytes as the
// references, not only that it round-trips its own output.
internal static class KnownAnswers
{
    public const string Passphrase = "correct horse battery staple";

    // 00 01 02 ... 0f
    public static byte[] Salt { get; } = Sequence(0, 16);

    // Argon2id t=3, m=65536 KiB, p=1 (Argon2Params.Default), 32 bytes.
    public const string Argon2idDefaultHex = "0d1a3c6523c8f06e4e0af9c515aa5b5448cfebd6838f2d52c3d8b6ef8ddc3c2e";

    // Argon2id t=2, m=1024 KiB, p=2, 32 bytes: cheap, and exercises lanes.
    public const string Argon2idSmallHex = "8a771af79764de58b1a47299b9e39f04473da40db38fbf891e88c55cb6100805";

    // AES-256-GCM, 16-byte tag.
    public static byte[] AesKey { get; } = Sequence(0, 32);

    public static byte[] AesNonce { get; } = Sequence(100, 12);

    public const string AesPlaintext = "MedReminder S1 known-answer plaintext";

    public const string AesAssociatedData = "S1-aad";

    public const string AesCiphertextHex = "057eba341c843ff05a072dc889544a962cad7164a60d9d01d0b4de688bcfc421fa9d25b838";

    public const string AesTagHex = "b1e97599096100c49a9d095007f7cceb";

    private static byte[] Sequence(int start, int count)
        => [.. Enumerable.Range(start, count).Select(i => (byte)i)];
}
