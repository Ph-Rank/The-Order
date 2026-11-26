using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT;

namespace TheOrder.WinUI.Materials;

public sealed partial class RefractionGallery
{
    private OpticalRenderer? optical;
    private readonly bool ownedSceneEnabled = Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_PROFILE_KIND") == "Test" &&
        Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_VISUAL_TEST") == "1" &&
        Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_MATERIAL_SOURCE") == "owned-scene";
    private GalleryScene? scene;
    private bool scenePattern, sceneOpticalActive;
    private long sceneUpdatesReleased;
    private bool UsesOwnedScene => ownedSceneEnabled && UsesOptical;
    private OpticalRenderer? failedOpticalRelease;
    private RenderTargetBitmap? snapshotBitmap;
    private bool snapshotRetiring;
    private static long snapshotObjectsCreated, snapshotObjectsReleased;
    private long snapshotsDiscarded, snapshotRaceChecks;
    // Explicit Test-command isolation only. Normal material uses stage 5.
    private int captureStage = 5;
    private bool captureIsolation;
    private long backgroundBuilds, renderRequests, renderCompletions, pixelsRequests, pixelsCompletions, pixelCopies;
    private int capturePeak, livePixelBuffers;
    private bool snapshotBusy, snapshotDirty = true, renderingSubscribed, dispersionEnabled = true;
    private int snapshotGeneration;
    private long snapshotCount, opticalDrawsReleased, opticalUploadsReleased;
    private int snapshotWidth, snapshotHeight;
    private double snapshotScale;
    private double opticalCpuReleased, opticalMaxCpuReleased;
    private long motionStarted, previousFrame;
    private double motionSeconds, motionStartPosition, frameIntervalTotal, frameIntervalMax;
    private long frameIntervals;
    private readonly float[] opticalConstants = new float[28];
    // Body modes: 0 = accepted C1 (clear), 1 = rev1 clear rim + frosted centre, 2 = rev2 continuous glass.
    private int bodyMode;
    private double frostSigmaApplied;
    private bool frostCurrent;
    private double FrostSigmaDip => bodyMode == 2 ? 12 : 20;
    private bool frostRequested => bodyMode is 1 or 2;
    private bool FrostActive => frostRequested && frostCurrent && UsesOwnedScene && sceneOpticalActive;
    // Mode 3 = 完全清透: continuous material over the sharp scene; no blurred copy needed.
    private bool ClearActive => bodyMode == 3 && UsesOwnedScene && sceneOpticalActive;
    private bool UsesOptical => ComparisonMode.OptionIndex is 3 or 4;
    private bool MaterialActive => lens is not null || (optical?.Ready == true && (!UsesOwnedScene || sceneOpticalActive));
    private OpticalProfile Profile => ComparisonMode.OptionIndex == 3 ? OpticalProfile.Reference : OpticalProfile.Daily;
    private object OpticalEvidence => new
    {
        ProcessGraphics = OpticalRenderer.Diagnostics(),
        Isolation = new { Enabled = captureIsolation, Stage = captureStage, BackgroundBuilds = backgroundBuilds,
            RenderRequests = renderRequests, RenderCompletions = renderCompletions,
            PixelsRequests = pixelsRequests, PixelsCompletions = pixelsCompletions, PixelCopies = pixelCopies,
            InFlightPeak = capturePeak, LivePixelBuffers = livePixelBuffers,
            Qpc = Stopwatch.GetTimestamp(), QpcFrequency = Stopwatch.Frequency },
        Active = MaterialActive, SnapshotPending = snapshotBusy, SnapshotDirty = snapshotDirty,
        SourcePath = UsesOwnedScene ? "owned-scene" : "xaml-capture", Scene = optical?.SceneEvidence,
        EquivalentSceneUpdates = sceneUpdatesReleased + (optical?.SceneUpdates ?? 0),
        BackgroundDeviceRetained = UsesOwnedScene && !sceneOpticalActive && optical is not null,
        CachedSnapshotCount = snapshotBitmap is null ? 0 : 1, SnapshotRetiring = snapshotRetiring,
        SnapshotObjectsCreated = snapshotObjectsCreated, SnapshotObjectsReleased = snapshotObjectsReleased,
        SnapshotsDiscarded = snapshotsDiscarded, SnapshotRaceChecks = snapshotRaceChecks,
        SnapshotCount = snapshotCount, SourceTextureBytes = optical?.SourceBytes ?? 0,
        SnapshotWidth = snapshotWidth, SnapshotHeight = snapshotHeight, SnapshotScale = snapshotScale,
        SwapchainBufferBytes = optical?.BufferBytes ?? 0, DeviceCount = optical is null ? 0 : 1,
        RenderingSubscription = renderingSubscribed ? 1 : 0,
        Draws = opticalDrawsReleased + (optical?.Draws ?? 0),
        Uploads = opticalUploadsReleased + (optical?.Uploads ?? 0),
        DrawCpuTotalMs = opticalCpuReleased + (optical?.DrawCpuMilliseconds ?? 0),
        DrawCpuMaxMs = Math.Max(opticalMaxCpuReleased, optical?.MaxDrawCpuMilliseconds ?? 0),
        FrameIntervals = frameIntervals, FrameIntervalMeanMs = frameIntervals == 0 ? 0 : frameIntervalTotal / frameIntervals,
        FrameIntervalMaxMs = frameIntervalMax,
        Profile.BevelWidth, Profile.Depth, Dispersion = dispersionEnabled ? Profile.Dispersion : 0, Profile.Gloss,
        RetainedFailedDevice = failedOpticalRelease is null ? 0 : 1,
        Source = UsesOwnedScene ? "Gallery-owned Direct2D/DirectWrite/WIC scene on same-device D3D surface; every revision rendered; foreground excluded" : "Gallery-only XAML background snapshot at rasterization scale; finite shader translation; foreground excluded",
        Passes = 1, TextureReadsOutside = 1, TextureReadsLensMaximum = FrostActive ? 5 : 4, BlurPasses = 0,
        BodyMode = bodyMode, FrostRequested = frostRequested, FrostActive, ClearActive, FrostSigmaDip, Frost = optical?.FrostEvidence
    };

    private void UpdateOptical()
    {
        if (UsesOwnedScene) { UpdateOwnedScene(true); return; }
        if (snapshotScale != 0 && snapshotScale != XamlRoot.RasterizationScale && !snapshotDirty) { snapshotGeneration++; snapshotDirty = true; }
        if (lens is not null) ReleaseMaterial();
        if (optical is null)
        {
            optical = new OpticalRenderer(OpticalPanel);
            allocationCycles++; snapshotDirty = true;
            if (failInitialization) throw new InvalidOperationException("Test partial optical initialization failure");
        }
        if (snapshotDirty) CaptureOpticalSource();
        else DrawOptical((float)(-patternPosition * 1.2), (float)(-patternPosition * .16));
    }

    private async void CaptureOpticalSource()
    {
        if (UsesOwnedScene) return;
        if (captureIsolation && captureStage < 2) return;
        if (snapshotBusy || snapshotRetiring || !snapshotDirty || optical is null || pendingCovers.Count != 0 || !loaded || disposed) return;
        snapshotBusy = true;
        capturePeak = 1;
        var generation = snapshotGeneration;
        var owner = optical;
        RenderTargetBitmap? bitmap = null;
        try
        {
            if (snapshotBitmap is null) { snapshotBitmap = new RenderTargetBitmap(); snapshotObjectsCreated++; }
            bitmap = snapshotBitmap;
            // Capture a fixed parent: Canvas render bounds alone include overflow.
            // PatternCapture never contains OpticalPanel or LensFrame.
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(PatternLayer).Offset = Vector3.Zero;
            renderRequests++;
            await bitmap.RenderAsync(PatternCapture);
            renderCompletions++;
            if (captureIsolation && captureStage == 2)
            {
                if (disposed || !loaded || generation != snapshotGeneration || optical != owner) { snapshotsDiscarded++; return; }
                if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) throw new InvalidDataException("Empty isolated Gallery snapshot");
                snapshotWidth = bitmap.PixelWidth; snapshotHeight = bitmap.PixelHeight;
                snapshotScale = XamlRoot.RasterizationScale;
                snapshotDirty = false; snapshotCount++; return;
            }
            pixelsRequests++;
            var buffer = await bitmap.GetPixelsAsync();
            pixelsCompletions++; livePixelBuffers++;
            try
            {
                if (disposed || !loaded || generation != snapshotGeneration || optical != owner) { snapshotsDiscarded++; return; }
                if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) throw new InvalidDataException("Empty Gallery snapshot");
                var pixels = buffer.ToArray(); pixelCopies++;
                if (!captureIsolation || captureStage >= 4) owner.Upload(pixels, bitmap.PixelWidth, bitmap.PixelHeight);
                snapshotWidth = bitmap.PixelWidth; snapshotHeight = bitmap.PixelHeight;
                snapshotScale = XamlRoot.RasterizationScale;
                snapshotDirty = false; snapshotCount++;
                DrawOptical((float)(-patternPosition * 1.2), (float)(-patternPosition * .16));
                UpdateState();
            }
            finally { ((IWinRTObject)buffer).NativeObject.Dispose(); livePixelBuffers--; }
        }
        catch (Exception exception) { if (!disposed && generation == snapshotGeneration) SetCompositionFailure(exception, "optical.snapshot"); }
        finally
        {
            snapshotBusy = false;
            if (snapshotRetiring) RetireSnapshot();
            if (!disposed && loaded && snapshotDirty && optical is not null && generation != snapshotGeneration)
                DispatcherQueue.TryEnqueue(CaptureOpticalSource);
        }
    }

    private void RetireSnapshot()
    {
        snapshotRetiring = true;
        // RenderAsync/GetPixelsAsync still own the bitmap until their finally.
        // A stale generation cannot upload to the new renderer; it retires here
        // before the single queued latest capture may allocate another bitmap.
        if (snapshotBusy) return;
        if (snapshotBitmap is null) { snapshotRetiring = false; return; }
        try
        {
            ((IWinRTObject)snapshotBitmap).NativeObject.Dispose();
            snapshotBitmap = null; snapshotRetiring = false; snapshotObjectsReleased++;
        }
        catch (Exception exception) { cleanupFailures++; RecordFailure("optical.snapshot.detach", exception); }
    }

    private void TestReleasePendingSnapshot()
    {
        if (!UsesOptical || optical?.Ready != true || snapshotBusy) throw new InvalidOperationException("Ready optical owner required for snapshot race");
        BackgroundMode.OptionIndex = BackgroundMode.OptionIndex == 0 ? 1 : 0;
        if (!snapshotBusy) throw new InvalidOperationException("Snapshot race did not enter an in-flight capture");
        snapshotRaceChecks++;
        ReleaseOptical(); // Old capture finishes/discards before the latest owner captures.
    }

    private void DrawOptical(float offsetX, float offsetY)
    {
        if (captureIsolation && captureStage < 5) return;
        if (snapshotDirty || optical?.Ready != true || !loaded || disposed || Stage.ActualWidth <= 0 || Stage.ActualHeight <= 0) return;
        var scale = (float)(XamlRoot?.RasterizationScale ?? 1);
        var p = LensHost.TransformToVisual(Stage).TransformPoint(new Windows.Foundation.Point());
        var model = Profile;
        var c = opticalConstants;
        c[0] = (float)Stage.ActualWidth; c[1] = (float)Stage.ActualHeight; c[2] = 1300; c[3] = 660;
        c[4] = (float)(p.X + LensHost.ActualWidth / 2); c[5] = (float)(p.Y + LensHost.ActualHeight / 2);
        c[6] = (float)LensHost.ActualWidth / 2; c[7] = (float)LensHost.ActualHeight / 2;
        c[8] = 23; c[9] = model.BevelWidth; c[10] = model.Depth; c[11] = dispersionEnabled ? model.Dispersion : 0;
        c[12] = model.Gloss; c[13] = ActualTheme == ElementTheme.Dark ? 1 : 0;
        c[14] = offsetX; c[15] = offsetY; c[16] = scale;
        var frost = FrostActive; var darkTint = ActualTheme == ElementTheme.Dark;
        var rev1 = frost && bodyMode == 1; var rev2 = frost && bodyMode == 2; var clear = ClearActive;
        c[18] = c[19] = 0;
        if (rev2 || clear)
        {
            // Continuous glass (连续玻璃) and fully clear (完全清透): one material, Fluent-neutral tint at a
            // minimum alpha only; the per-pixel luminance bound (.42 lift Light / .08 deepen Dark) keeps
            // primary text >= 7:1 without a panel. Clear samples the sharp scene and uses a lighter tint.
            c[17] = 0; c[18] = clear ? 1 : 0;
            c[20] = c[21] = c[22] = darkTint ? 32 / 255f : 250 / 255f;
            c[23] = clear ? (darkTint ? .16f : .10f) : (darkTint ? .30f : .22f);
            c[24] = 1; c[25] = darkTint ? .08f : .42f; c[26] = darkTint ? -1 : 1; c[27] = .20f;
        }
        else
        {
            // rev1 (preserved for comparison): tint alpha .80 bounds body text contrast.
            c[17] = rev1 ? 1 : 0;
            c[20] = darkTint ? 27 / 255f : 250 / 255f; c[21] = darkTint ? 35 / 255f : 251 / 255f;
            c[22] = darkTint ? 36 / 255f : 248 / 255f; c[23] = rev1 ? .8f : 0;
            c[24] = c[25] = c[27] = 0; c[26] = 1;
        }
        optical.Draw((int)Math.Ceiling(c[0] * scale), (int)Math.Ceiling(c[1] * scale), c, UsesOwnedScene && !sceneOpticalActive);
        OpticalPanel.Visibility = Visibility.Visible;
    }

    private void SetCaptureIsolation(int stage)
    {
        if (stage is < 0 or > 5 || snapshotBusy || advancedAllowed)
            throw new InvalidOperationException("Capture isolation requires disabled, drained Test Gallery");
        captureIsolation = true; captureStage = stage;
    }

    private void StartOpticalMotion(double seconds)
    {
        StopOpticalMotion();
        motionSeconds = seconds; motionStarted = Stopwatch.GetTimestamp(); previousFrame = 0;
        motionStartPosition = patternPosition;
        finiteMotionUntil = motionStarted + (long)(seconds * Stopwatch.Frequency);
        CompositionTarget.Rendering += OnOpticalFrame; renderingSubscribed = true;
    }
    private void OnOpticalFrame(object? sender, object args)
    {
        if (optical?.Ready != true || disposed || !loaded || !ownerAllowed || !ownerMotionAllowed)
        { StopOpticalMotion(); return; }
        try
        {
            var now = Stopwatch.GetTimestamp();
            if (previousFrame != 0) { var ms = Stopwatch.GetElapsedTime(previousFrame, now).TotalMilliseconds; frameIntervals++; frameIntervalTotal += ms; frameIntervalMax = Math.Max(frameIntervalMax, ms); }
            previousFrame = now;
            var t = Math.Clamp(Stopwatch.GetElapsedTime(motionStarted, now).TotalSeconds / motionSeconds, 0, 1);
            var position = t < .5 ? motionStartPosition + (100 - motionStartPosition) * t * 2 : (1 - t) * 200;
            DrawOptical((float)(-position * 1.2), (float)(-position * .16));
            if (t >= 1) { StopOpticalMotion(); patternPosition = 0; SyncPatternSlider(); }
        }
        catch (Exception exception) { SetCompositionFailure(exception, "optical.render"); }
    }
    private void StopOpticalMotion()
    {
        if (renderingSubscribed) { CompositionTarget.Rendering -= OnOpticalFrame; renderingSubscribed = false; }
        finiteMotionUntil = 0;
    }
    private void ReleaseOptical()
    {
        StopOpticalMotion(); snapshotGeneration++; snapshotDirty = true; sceneOpticalActive = false; frostCurrent = false;
        OpticalPanel.Visibility = Visibility.Collapsed;
        if (optical is not null)
        {
            opticalDrawsReleased += optical.Draws; opticalUploadsReleased += optical.Uploads;
            sceneUpdatesReleased += optical.SceneUpdates;
            opticalCpuReleased += optical.DrawCpuMilliseconds; opticalMaxCpuReleased = Math.Max(opticalMaxCpuReleased, optical.MaxDrawCpuMilliseconds);
            try { optical.Dispose(); }
            catch (Exception exception) { cleanupFailures++; failedOpticalRelease = optical; RecordFailure("optical.detach", exception); }
            optical = null;
        }
        RetireSnapshot();
    }

    private void UpdateOwnedScene(bool effect)
    {
        if (!loaded || disposed || !ownerAllowed) return;
        sceneOpticalActive = effect;
        var scale = XamlRoot.RasterizationScale;
        if (optical is null)
        {
            optical = new OpticalRenderer(OpticalPanel); allocationCycles++; snapshotDirty = true;
            if (failInitialization) throw new InvalidOperationException("Test partial optical initialization failure");
        }
        if (snapshotScale != scale) snapshotDirty = true;
        if (snapshotDirty)
        {
            scene ??= GalleryScene.Create(BackgroundMode.OptionIndex,
                Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_MATERIAL_REFERENCE"),
                Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_PERFORMANCE_COVERS"));
            frostCurrent = false; // any scene revision invalidates the native copy
            optical.RenderScene(scene, scale);
            snapshotWidth = (int)Math.Ceiling(GalleryScene.Width * scale);
            snapshotHeight = (int)Math.Ceiling(GalleryScene.Height * scale);
            snapshotScale = scale; snapshotDirty = false;
        }
        // Event-driven: one blur pass per scene revision or toggle, never per frame.
        var wantFrost = effect && frostRequested;
        if (wantFrost && (!frostCurrent || frostSigmaApplied != FrostSigmaDip))
        { optical.Frost(FrostSigmaDip); frostCurrent = true; frostSigmaApplied = FrostSigmaDip; }
        else if (!wantFrost && optical.FrostBytes != 0) { optical.Frost(0); frostCurrent = false; }
        DrawOptical((float)(-patternPosition * 1.2), (float)(-patternPosition * .16));
    }
}
