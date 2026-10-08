using System.Runtime.InteropServices.WindowsRuntime;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;

namespace MedReminder.UI.Camera;

// Webcam barcode acquisition through Windows.Media.Capture
// (MediaCapture + MediaFrameReader) and ZXing.Net. Lives in the UI
// project because it is the only project whose TFM carries the Windows
// SDK projections (docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §5A.2).
//
// Singleton: a semaphore serializes sessions. Each session opens the
// camera, and releases it in a finally block whatever the outcome, so
// the Windows camera indicator goes off as soon as the scan ends (§5A.4).
internal sealed class WindowsCameraCaptureService : ICameraCaptureService
{
    // The preview does not need the camera frame rate: 20 frames per
    // second look fluid and bound the copies made on the UI thread.
    private const int PreviewIntervalMilliseconds = 50;

    private readonly BarcodeCaptureOptions _options;
    private readonly ILogger<WindowsCameraCaptureService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WindowsCameraCaptureService(
        BarcodeCaptureOptions options,
        ILogger<WindowsCameraCaptureService> log)
    {
        _options = options;
        _log = log;
    }

    public async Task<CameraScanResult> ScanAsync(
        Func<RawBarcode, bool> accept,
        Action<CameraFrameStatus>? onFrame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accept);
        var callerContext = SynchronizationContext.Current;
        try
        {
            await _gate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return CameraScanResult.Cancelled;
        }

        try
        {
            return await RunSessionAsync(accept, onFrame, callerContext, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CameraScanResult> RunSessionAsync(
        Func<RawBarcode, bool> accept,
        Action<CameraFrameStatus>? onFrame,
        SynchronizationContext? callerContext,
        CancellationToken cancellationToken)
    {
        MediaCapture? capture = null;
        MediaFrameReader? reader = null;
        Session? session = null;
        try
        {
            var picked = await FindColourSourceAsync();
            if (picked is null)
            {
                _log.LogWarning("Camera scan: no colour camera found.");
                return new CameraScanResult(CameraAvailability.NoDeviceFound, null);
            }
            if (cancellationToken.IsCancellationRequested) return CameraScanResult.Cancelled;

            capture = new MediaCapture();
            try
            {
                await capture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    SourceGroup = picked.Value.Group,
                    SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                });
            }
            catch (UnauthorizedAccessException)
            {
                // Settings → Privacy & security → Camera denies desktop apps (§7.1).
                _log.LogWarning("Camera scan: access denied by the Windows privacy settings.");
                return new CameraScanResult(CameraAvailability.PermissionDenied, null);
            }

            var source = capture.FrameSources[picked.Value.SourceId];
            var format = PickFormat(source.SupportedFormats, _options.CameraMaxWidthPixels);
            if (format is not null) await source.SetFormatAsync(format);

            session = new Session(this, accept, onFrame, callerContext);
            capture.Failed += session.OnCaptureFailed;

            // Bgra8 asks the reader to convert whatever the camera
            // delivers (NV12, YUY2, MJPG), so every frame arrives in the
            // layout the decoder and the preview both read.
            reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            reader.FrameArrived += session.OnFrameArrived;

            var status = await reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                _log.LogError("Camera scan: frame reader did not start ({Status}).", status);
                return new CameraScanResult(CameraAvailability.InitializationFailed, null);
            }
            await EnableContinuousFocusIfAvailableAsync(capture);
            _log.LogInformation(
                "Camera scan started: {Width}x{Height}.",
                format?.VideoFormat.Width ?? 0, format?.VideoFormat.Height ?? 0);

            using (cancellationToken.Register(() => session.Complete(CameraScanResult.Cancelled)))
            {
                return await session.Completion;
            }
        }
        catch (Exception ex)
        {
            // A driver fault, a camera held by another application in
            // exclusive mode, or a device removed during start-up.
            _log.LogError(ex, "Camera scan: the camera could not be started.");
            return new CameraScanResult(CameraAvailability.InitializationFailed, null);
        }
        finally
        {
            session?.Close();
            if (reader is not null)
            {
                if (session is not null) reader.FrameArrived -= session.OnFrameArrived;
                try { await reader.StopAsync(); }
                catch (Exception ex) { _log.LogWarning(ex, "Camera scan: stopping the frame reader failed."); }
                reader.Dispose();
            }
            if (capture is not null)
            {
                if (session is not null) capture.Failed -= session.OnCaptureFailed;
                capture.Dispose();
            }
        }
    }

    // First source group with a colour video source, preferring the
    // preview stream. Device names and ids are not logged.
    private static async Task<(MediaFrameSourceGroup Group, string SourceId)?> FindColourSourceAsync()
    {
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        foreach (var group in groups)
        {
            var info = group.SourceInfos
                .Where(i => i.SourceKind == MediaFrameSourceKind.Color)
                .OrderBy(i => i.MediaStreamType == MediaStreamType.VideoPreview ? 0 : 1)
                .FirstOrDefault(i => i.MediaStreamType is MediaStreamType.VideoPreview or MediaStreamType.VideoRecord);
            if (info is not null) return (group, info.Id);
        }
        return null;
    }

    // Widest format not wider than maxWidth, then the highest frame
    // rate. Null keeps the camera's current format.
    private static MediaFrameFormat? PickFormat(IReadOnlyList<MediaFrameFormat> formats, int maxWidth) =>
        formats
            .Where(f => f.VideoFormat is not null && f.VideoFormat.Width <= (uint)Math.Max(320, maxWidth))
            .OrderByDescending(f => f.VideoFormat.Width)
            .ThenByDescending(f => f.FrameRate.Denominator == 0 ? 0 : (double)f.FrameRate.Numerator / f.FrameRate.Denominator)
            .FirstOrDefault();

    // Some desktop webcams do not enable continuous focus for video by
    // default. Enable it only when supported; a focus-control failure
    // must not prevent the user from scanning with the current image.
    private async Task EnableContinuousFocusIfAvailableAsync(MediaCapture capture)
    {
        var focus = capture.VideoDeviceController.FocusControl;
        if (!focus.Supported || !focus.SupportedFocusModes.Contains(FocusMode.Continuous)) return;

        try
        {
            await focus.UnlockAsync();
            focus.Configure(new FocusSettings
            {
                Mode = FocusMode.Continuous,
                AutoFocusRange = AutoFocusRange.FullRange,
            });
            await focus.FocusAsync();
            _log.LogInformation("Camera scan: continuous autofocus enabled.");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Camera scan: continuous autofocus could not be enabled; continuing with the current focus.");
        }
    }

    // State of one scan. Frame events arrive on worker threads and may
    // still fire after the session completed; every path checks
    // _completed first, so late frames are dropped.
    private sealed class Session
    {
        private readonly WindowsCameraCaptureService _owner;
        private readonly Func<RawBarcode, bool> _accept;
        private readonly Action<CameraFrameStatus>? _onFrame;
        private readonly SynchronizationContext? _callerContext;
        private readonly FrameBarcodeDecoder _decoder = new();
        private readonly TaskCompletionSource<CameraScanResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly long _decodeIntervalMs;
        private readonly object _frameLock = new();

        private byte[] _work = [];
        private byte[] _preview = [];
        private long _lastDecodeMs = long.MinValue / 2;
        private long _lastPreviewMs = long.MinValue / 2;
        private int _framesDecoded;
        private int _previewPending;
        private volatile bool _completed;

        public Session(
            WindowsCameraCaptureService owner,
            Func<RawBarcode, bool> accept,
            Action<CameraFrameStatus>? onFrame,
            SynchronizationContext? callerContext)
        {
            _owner = owner;
            _accept = accept;
            _onFrame = onFrame;
            _callerContext = callerContext;
            _decodeIntervalMs = 1000 / Math.Clamp(owner._options.MaxDecodeFps, 1, 30);
        }

        public Task<CameraScanResult> Completion => _completion.Task;

        public void Complete(CameraScanResult result)
        {
            _completed = true;
            _completion.TrySetResult(result);
        }

        public void Close() => _completed = true;

        public void OnCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs args)
        {
            _owner._log.LogError("Camera scan: capture failed (code {Code}).", args.Code);
            Complete(new CameraScanResult(CameraAvailability.InitializationFailed, null));
        }

        public void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            // An exception must not escape a WinRT event on a worker
            // thread: it would end the process.
            try
            {
                HandleFrame(sender);
            }
            catch (Exception ex)
            {
                _owner._log.LogError(ex, "Camera scan: a frame could not be processed.");
                Complete(new CameraScanResult(CameraAvailability.InitializationFailed, null));
            }
        }

        private void HandleFrame(MediaFrameReader reader)
        {
            if (_completed) return;
            // The reader may raise the event again while a slow decode
            // runs; skip that frame instead of queueing.
            if (!Monitor.TryEnter(_frameLock)) return;
            try
            {
                var now = Environment.TickCount64;
                var decodeDue = now - _lastDecodeMs >= _decodeIntervalMs;
                var previewDue = _onFrame is not null
                    && now - _lastPreviewMs >= PreviewIntervalMilliseconds
                    && Volatile.Read(ref _previewPending) == 0;
                if (!decodeDue && !previewDue) return;

                using var frame = reader.TryAcquireLatestFrame();
                var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
                if (bitmap is null) return;

                SoftwareBitmap? converted = null;
                try
                {
                    if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
                    {
                        converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
                        bitmap = converted;
                    }
                    var width = bitmap.PixelWidth;
                    var height = bitmap.PixelHeight;
                    var length = width * height * 4;
                    if (_work.Length != length) _work = new byte[length];
                    bitmap.CopyToBuffer(_work.AsBuffer());

                    if (previewDue)
                    {
                        _lastPreviewMs = now;
                        PostPreview(width, height, length);
                    }
                    if (decodeDue)
                    {
                        _lastDecodeMs = now;
                        Decode(width, height);
                    }
                }
                finally
                {
                    converted?.Dispose();
                }
            }
            finally
            {
                Monitor.Exit(_frameLock);
            }
        }

        private void Decode(int width, int height)
        {
            _framesDecoded++;
            var raw = _decoder.Decode(_work, width, height);
            if (raw is null || _completed) return;
            if (_accept(raw.Value))
            {
                Complete(new CameraScanResult(CameraAvailability.Available, raw));
            }
        }

        // The preview buffer is written only while no preview is
        // pending, so the UI reads it without a copy of its own.
        private void PostPreview(int width, int height, int length)
        {
            if (_preview.Length != length) _preview = new byte[length];
            Buffer.BlockCopy(_work, 0, _preview, 0, length);
            var status = new CameraFrameStatus(_framesDecoded, new CameraPreviewFrame(width, height, _preview));

            Volatile.Write(ref _previewPending, 1);
            void Deliver(object? _)
            {
                try
                {
                    if (!_completed) _onFrame!(status);
                }
                finally
                {
                    Volatile.Write(ref _previewPending, 0);
                }
            }
            if (_callerContext is null) Deliver(null);
            else _callerContext.Post(Deliver, null);
        }
    }
}
