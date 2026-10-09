using MedReminder.Application.Export;

namespace MedReminder.MobileSpikes.Spikes;

// S1b — decrypt an archive exported by the desktop (§13 Phase 0, S1:
// "decrypt a desktop archive") with the production IArchiveReader.
// Reports format facts and row counts only, never names, notes or the
// profile display name.
internal static class S1DesktopArchive
{
    private const string Spike = "S1";

    public static void Run(SpikeReport report, IArchiveReader reader, byte[] archive, char[] passphrase)
    {
        SpikeRunner.Check(report, Spike, "Desktop archive: manifest", () =>
        {
            using var stream = new MemoryStream(archive, writable: false);
            var m = reader.ReadManifest(stream);
            return (true,
                $"format {m.Format} v{m.FormatVersion}, app {m.AppVersion}, scope {m.Scope}, "
                + $"kdf {m.Kdf.Algorithm} t={m.Kdf.Iterations} m={m.Kdf.MemoryKiB} KiB p={m.Kdf.Parallelism}, "
                + $"cipher {m.Cipher.Algorithm}-{m.Cipher.KeyBits}, payload {m.Payload.SizeBytes} bytes");
        });

        SpikeRunner.Check(report, Spike, "Desktop archive: decrypt and parse payload", () =>
        {
            using var stream = new MemoryStream(archive, writable: false);
            using var decrypted = reader.Decrypt(stream, passphrase);
            var p = decrypted.Payload;
            return (true,
                $"schema {p.SchemaVersion}; medicines {p.Medicines.Count}, stock movements {p.StockMovements.Count}, "
                + $"schedule rows {p.MedicationScheduleHistory.Count}, slot sets {p.MedicationAdministrationSlotSets.Count}, "
                + $"slots {p.MedicationAdministrationSlots.Count}, suspensions {p.MedicationSuspensions.Count}, "
                + $"intakes {p.MedicationIntakes.Count}, stock counts {p.StockCounts.Count}, "
                + $"prescriptions {p.Prescriptions.Count}, deadlines {p.Deadlines.Count}, packages {p.StockPackages.Count}");
        });
    }
}
