using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopLizard.Core;
using DesktopLizard.Rendering;

namespace DesktopLizard.Diagnostics;

internal static class PreviewExporter
{
    public static void Export(string directory)
    {
        Directory.CreateDirectory(directory);
        var profile = LizardProfile.Default;
        var lizard = new ProceduralLizard(profile);
        var behavior = profile.Behavior;
        var referenceSpeed = (behavior.Speed.ReferenceMinimumCrawl +
                              behavior.Speed.ReferenceMaximumCrawl) * 0.5f;
        var bendSpeed = MathEx.Lerp(
            behavior.Speed.ReferenceMinimumCrawl,
            behavior.Speed.ReferenceMaximumCrawl,
            0.25f);
        var (view, board) = CreateBoard(lizard);

        var mood = EmotionBlend.Normalize(new EmotionBlend(0.32f, 0.34f, 0.25f, 0.09f));

        Simulate(lizard, view, 60, 0f, 0f, LizardPoseMode.Rest, mood, Vector2.UnitX);
        Save(board, Path.Combine(directory, "01-idle.png"));

        Simulate(lizard, view, 72, 0f, referenceSpeed, LizardPoseMode.Locomotion, mood, Vector2.UnitX);
        Save(board, Path.Combine(directory, "02-walk.png"));

        for (var frame = 0; frame < 72; frame++)
        {
            Simulate(lizard, view, 1, 0f, referenceSpeed, LizardPoseMode.Locomotion, mood, Vector2.UnitX);
            Save(board, Path.Combine(directory, $"gait-{frame:000}.png"));
        }

        // A continuous reference-speed bend exposes the inner/outer landing
        // asymmetry; a single final turn frame cannot verify foot placement.
        var previewTurnHeading = 0f;
        for (var frame = 0; frame < 90; frame++)
        {
            previewTurnHeading += 0.55f / 60f;
            Simulate(
                lizard,
                view,
                1,
                previewTurnHeading,
                bendSpeed,
                LizardPoseMode.Locomotion,
                mood,
                MathEx.FromAngle(previewTurnHeading));
            Save(board, Path.Combine(directory, $"turn-gait-{frame:000}.png"));
        }

        SimulateTurning(lizard, view, 52, 0f, MathF.PI * 0.66f, bendSpeed, LizardPoseMode.Locomotion, mood);
        Save(board, Path.Combine(directory, "03-turn.png"));

        var normalSCurve = lizard.Profile.Behavior.SCurve.Normal;
        var sCurvePreviewCycles = normalSCurve.MaximumCycleCount;
        var sCurvePreviewCycleDuration =
            (normalSCurve.MinimumCycleDuration + normalSCurve.MaximumCycleDuration) * 0.5f;
        var sCurvePreviewFrames = (int)MathF.Round(
            (sCurvePreviewCycleDuration * sCurvePreviewCycles + lizard.Profile.Behavior.SCurve.SettleDuration) * 60f);
        SimulateSCurve(
            lizard,
            view,
            board,
            directory,
            "s-curve",
            sCurvePreviewFrames,
            MathF.PI * 0.66f,
            bendSpeed,
            LizardPoseMode.Locomotion,
            (normalSCurve.MinimumAmplitude + normalSCurve.MaximumAmplitude) * 0.5f,
            sCurvePreviewCycleDuration,
            sCurvePreviewCycles,
            mood);
        Save(board, Path.Combine(directory, "04-s-curve.png"));

        SimulateTurning(
            lizard,
            view,
            52,
            MathF.PI * 0.66f,
            MathF.PI * 1.35f,
            referenceSpeed * behavior.Locomotion.TurnAroundSpeedFactor,
            LizardPoseMode.Locomotion,
            mood);
        Save(board, Path.Combine(directory, "05-turn-around.png"));

        var boundarySpeed =
            (behavior.Boundary.RecoveryMinimumSpeed + behavior.Boundary.RecoveryMaximumSpeed) *
            0.5f * behavior.Speed.BoundaryRecoverySpeedMultiplier;
        SimulateTurning(lizard, view, 24, MathF.PI * 1.35f, MathF.PI * 1.62f, boundarySpeed, LizardPoseMode.Locomotion, mood);
        Save(board, Path.Combine(directory, "06-edge-turn.png"));

        Simulate(lizard, view, 36, MathF.PI * 1.62f, 0f, LizardPoseMode.Observe, mood, Vector2.UnitY);
        Save(board, Path.Combine(directory, "07-observe.png"));

        Simulate(
            lizard,
            view,
            90,
            0f,
            lizard.Profile.Behavior.Speed.MaximumCrawl,
            LizardPoseMode.Locomotion,
            mood,
            Vector2.UnitX);
        Save(board, Path.Combine(directory, "09-fast-forward.png"));

        var fastSCurve = lizard.Profile.Behavior.SCurve.Fast;
        var fastSCurvePreviewCycles = fastSCurve.MinimumCycleCount;
        var fastSCurvePreviewCycleDuration =
            (fastSCurve.MinimumCycleDuration + fastSCurve.MaximumCycleDuration) * 0.5f;
        var fastSCurvePreviewFrames = (int)MathF.Round(
            (fastSCurvePreviewCycleDuration * fastSCurvePreviewCycles + lizard.Profile.Behavior.SCurve.SettleDuration) * 60f);
        SimulateSCurve(
            lizard,
            view,
            board,
            directory,
            "fast-s-curve",
            fastSCurvePreviewFrames,
            MathF.PI * 0.35f,
            lizard.Profile.Behavior.Speed.MaximumCrawl,
            LizardPoseMode.FastSCurve,
            (fastSCurve.MinimumAmplitude + fastSCurve.MaximumAmplitude) * 0.5f,
            fastSCurvePreviewCycleDuration,
            fastSCurvePreviewCycles,
            mood);
        Save(board, Path.Combine(directory, "10-fast-s-curve.png"));

        Simulate(lizard, view, 60, MathF.PI * 0.5f, 0f, LizardPoseMode.Grabbed, mood, Vector2.UnitY);
        Save(board, Path.Combine(directory, "08-drag.png"));

        ExportGrabSequence(directory, mood);
        ExportLostGripSequence(directory, mood);
    }

    private static void ExportGrabSequence(string directory, EmotionBlend mood)
    {
        var lizard = new ProceduralLizard(LizardProfile.Default);
        var (view, board) = CreateBoard(lizard);
        lizard.BeginGrab(lizard.GetBodyPoint(2, 1f));

        for (var frame = 0; frame < 90; frame++)
        {
            for (var substep = 0; substep < 2; substep++)
            {
                var screenDelta = frame < 50
                    ? new Vector2(1.8f, 0.3f)
                    : Vector2.Zero;
                lizard.Update(
                    1f / 120f,
                    new LizardAnimationInput(
                        0f,
                        0f,
                        LizardPoseMode.Grabbed,
                        mood,
                        0f,
                        screenDelta));
            }
            view.Present(lizard.CaptureRenderFrame(), Vector2.UnitX);
            Save(board, Path.Combine(directory, $"grab-sequence-{frame:000}.png"));
        }
    }

    private static void ExportLostGripSequence(string directory, EmotionBlend mood)
    {
        const int fallFrames = 52;
        const int regripFrames = 42;
        var lizard = new ProceduralLizard(LizardProfile.Default);
        var (view, board) = CreateBoard(lizard);
        var fall = lizard.Profile.Behavior.LostGripFall;

        // This is a normalized minimum-distance fall. Deriving the lead-in
        // from the public profile keeps the preview aligned with runtime when
        // an individual changes either distance instead of copying an
        // animation-only frame count.
        var reachStartProgress = MathEx.Clamp01(
            1f - fall.ReachLeadDistance / fall.MinimumDistance);
        var reachStartFrame = Math.Clamp(
            (int)MathF.Floor(reachStartProgress * fallFrames),
            0,
            fallFrames - 1);
        var reachMidFrame = Math.Clamp(
            (int)MathF.Floor(MathEx.Lerp(reachStartProgress, 1f, 0.5f) * fallFrames),
            reachStartFrame,
            fallFrames - 1);
        var contactFrame = fallFrames - 1;
        var holdFrame = fallFrames + Math.Clamp(
            (int)MathF.Floor(
                regripFrames *
                lizard.Profile.Physics.RegripContactHoldFraction *
                0.5f),
            0,
            regripFrames - 1);
        var recoveryProgress = MathEx.Lerp(
            lizard.Profile.Physics.RegripContactHoldFraction,
            1f,
            0.55f);
        var recoveryFrame = fallFrames + Math.Clamp(
            (int)MathF.Floor(regripFrames * recoveryProgress),
            0,
            regripFrames - 1);

        // Establish a planted crawling pose before the unexpected loss of
        // support. FreeFall then enters the production particle rig without a
        // grab pin; Regrip exits through the same recovery blend as runtime.
        Simulate(
            lizard,
            view,
            36,
            0f,
            lizard.Profile.Behavior.Speed.ReferenceMaximumCrawl,
            LizardPoseMode.Locomotion,
            mood,
            Vector2.UnitX);
        for (var frame = 0; frame < fallFrames + regripFrames; frame++)
        {
            var falling = frame < fallFrames;
            var phaseFrame = falling ? frame : frame - fallFrames;
            var phaseCount = falling ? fallFrames : regripFrames;
            var dropProgress = MathEx.Clamp01((phaseFrame + 1f) / phaseCount);
            var catchPreparationProgress = falling && dropProgress > reachStartProgress
                ? MathEx.Clamp01(
                    (dropProgress - reachStartProgress) /
                    Math.Max(0.0001f, 1f - reachStartProgress))
                : 0f;
            lizard.Update(
                1f / 60f,
                new LizardAnimationInput(
                    0f,
                    0f,
                    falling ? LizardPoseMode.FreeFall : LizardPoseMode.Regrip,
                    mood,
                    dropProgress,
                    Vector2.Zero)
                {
                    CatchPreparationProgress = catchPreparationProgress
                });
            view.Present(lizard.CaptureRenderFrame(), Vector2.UnitY);
            Save(board, Path.Combine(directory, $"lost-grip-sequence-{frame:000}.png"));
            if (frame == fallFrames / 2)
            {
                Save(board, Path.Combine(directory, "11-lost-grip-fall.png"));
            }
            if (frame == fallFrames + regripFrames / 2)
            {
                Save(board, Path.Combine(directory, "12-regrip.png"));
            }
            if (frame == reachStartFrame)
            {
                Save(board, Path.Combine(directory, "11-lost-grip-reach-start.png"));
            }
            if (frame == reachMidFrame)
            {
                Save(board, Path.Combine(directory, "12-lost-grip-reach-mid.png"));
            }
            if (frame == contactFrame)
            {
                Save(board, Path.Combine(directory, "13-lost-grip-contact.png"));
            }
            if (frame == holdFrame)
            {
                Save(board, Path.Combine(directory, "14-regrip-contact-hold.png"));
            }
            if (frame == recoveryFrame)
            {
                Save(board, Path.Combine(directory, "15-regrip-recovery.png"));
            }
        }
    }

    private static (LizardView View, Grid Board) CreateBoard(ProceduralLizard lizard)
    {
        var view = new LizardView(
            lizard.Profile,
            lizard.CaptureRenderFrame(),
            cancelMonitorDpi: false);
        var board = new Grid
        {
            Width = lizard.Profile.Appearance.RenderCanvasSize * lizard.Profile.Appearance.VisualScale,
            Height = lizard.Profile.Appearance.RenderCanvasSize * lizard.Profile.Appearance.VisualScale,
            Background = new SolidColorBrush(Color.FromRgb(249, 249, 218))
        };
        board.Children.Add(view);
        return (view, board);
    }

    private static void Simulate(
        ProceduralLizard lizard,
        LizardView view,
        int frames,
        float heading,
        float displaySpeed,
        LizardPoseMode poseMode,
        EmotionBlend mood,
        Vector2 lookDirection)
    {
        for (var i = 0; i < frames; i++)
        {
            var screenDelta = poseMode.IsLocomoting()
                ? MathEx.FromAngle(heading) *
                  (displaySpeed / 60f / lizard.Profile.Appearance.VisualScale)
                : Vector2.Zero;
            lizard.Update(
                1f / 60f,
                new LizardAnimationInput(
                    heading,
                    MathEx.Clamp01(displaySpeed / lizard.Profile.Behavior.Speed.AnimationNormalization),
                    poseMode,
                    mood,
                    poseMode == LizardPoseMode.ReleaseSettle ? i / (float)frames : 0f,
                    screenDelta));
        }
        view.Present(lizard.CaptureRenderFrame(), lookDirection);
    }

    private static void SimulateTurning(
        ProceduralLizard lizard,
        LizardView view,
        int frames,
        float fromHeading,
        float toHeading,
        float displaySpeed,
        LizardPoseMode poseMode,
        EmotionBlend mood)
    {
        for (var i = 0; i < frames; i++)
        {
            var t = (i + 1f) / frames;
            var heading = MathEx.SimplifyAngle(MathEx.Lerp(fromHeading, toHeading, t));
            var screenDelta = MathEx.FromAngle(heading) *
                              (displaySpeed / 60f / lizard.Profile.Appearance.VisualScale);
            lizard.Update(
                1f / 60f,
                new LizardAnimationInput(
                    heading,
                    MathEx.Clamp01(displaySpeed / lizard.Profile.Behavior.Speed.AnimationNormalization),
                    poseMode,
                    mood,
                    0f,
                    screenDelta));
        }
        view.Present(lizard.CaptureRenderFrame(), MathEx.FromAngle(toHeading));
    }

    private static void SimulateSCurve(
        ProceduralLizard lizard,
        LizardView view,
        FrameworkElement board,
        string directory,
        string filePrefix,
        int frames,
        float startHeading,
        float displaySpeed,
        LizardPoseMode poseMode,
        float amplitude,
        float cycleDuration,
        int cycleCount,
        EmotionBlend mood)
    {
        var heading = startHeading;
        for (var i = 0; i < frames; i++)
        {
            var elapsed = (i + 1f) / 60f;
            var turnVelocity = SCurveTrajectory.ComputeTurnVelocity(
                elapsed,
                cycleDuration,
                cycleCount,
                amplitude,
                1f);
            heading = MathEx.SimplifyAngle(heading + turnVelocity / 60f);
            var screenDelta = MathEx.FromAngle(heading) *
                              (displaySpeed / 60f / lizard.Profile.Appearance.VisualScale);
            lizard.Update(
                1f / 60f,
                new LizardAnimationInput(
                    heading,
                    MathEx.Clamp01(displaySpeed / lizard.Profile.Behavior.Speed.AnimationNormalization),
                    poseMode,
                    mood,
                    0f,
                    screenDelta));
            var phaseSampleInterval = Math.Max(1, (int)MathF.Round(cycleDuration * 60f / 4f));
            var waveFrames = (int)MathF.Round(cycleDuration * cycleCount * 60f);
            if ((i + 1) % phaseSampleInterval == 0 && i < waveFrames)
            {
                view.Present(lizard.CaptureRenderFrame(), MathEx.FromAngle(heading));
                Save(
                    board,
                    Path.Combine(directory, $"{filePrefix}-phase-{i + 1:000}.png"));
            }
        }
        view.Present(lizard.CaptureRenderFrame(), MathEx.FromAngle(heading));
    }

    private static void Save(FrameworkElement element, string path)
    {
        var size = new Size(element.Width, element.Height);
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width),
            (int)Math.Ceiling(size.Height),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
