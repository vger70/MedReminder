using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Abstractions;

// Webcam barcode acquisition (A2 phase 2, variant W). The adapter owns
// the camera and the decoder; callers see only decoded payloads and
// preview pixels, never camera or decoder types.
// See docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §5A.
public interface ICameraCaptureService
{
    // Opens the first colour camera, decodes frames until a barcode
    // passes accept, and releases the camera before returning. accept
    // is called on a worker thread; it runs the parser, so a code the
    // parser rejects (for example an unrelated Code 39 on the box)
    // does not end the session. onFrame is invoked on the caller's
    // synchronization context, at most one call pending at a time; the
    // preview pixels are valid only during the call, because the
    // buffer is reused. Cancellation returns Available with no barcode.
    // Never throws for a missing, blocked or failing camera: those end
    // as the matching CameraAvailability.
    Task<CameraScanResult> ScanAsync(
        Func<RawBarcode, bool> accept,
        Action<CameraFrameStatus>? onFrame,
        CancellationToken cancellationToken);
}

// Values are explicit because they are logged; never renumber them.
public enum CameraAvailability : int
{
    Available = 0,
    NoDeviceFound = 1,
    // The Windows privacy panel denies camera access to desktop apps.
    PermissionDenied = 2,
    // Driver or hardware fault, or the camera went away mid-scan.
    InitializationFailed = 3,
}

public sealed record CameraScanResult(CameraAvailability Availability, RawBarcode? Barcode)
{
    public static readonly CameraScanResult Cancelled = new(CameraAvailability.Available, null);
}

// Reported for frames shown in the preview. FramesDecoded counts the
// frames handed to the decoder so far in this session.
public sealed record CameraFrameStatus(int FramesDecoded, CameraPreviewFrame Preview);

// 32-bit BGRA pixels, top-down, Width * 4 bytes per row.
public sealed record CameraPreviewFrame(int Width, int Height, byte[] Bgra32);
