namespace MedReminder.Domain.Catalogue;

// Undecoded barcode payload as delivered by the capture source
// (scanner keystrokes or, in a later phase, a webcam decoder). The
// payload may contain an FMD serial number: never log it.
public readonly record struct RawBarcode(BarcodeSymbology Symbology, string Payload);
