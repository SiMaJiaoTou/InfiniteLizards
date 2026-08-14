using System.Numerics;
using DesktopLizard.Core;

namespace DesktopLizard.Diagnostics;

internal static class DanglingJitterAnalyzer
{
    private const float SimulationDt = 1f / 120f;

    private readonly record struct MotionMetrics(
        float RmsSecondDifference,
        float MaximumSecondDifference,
        float RmsThirdDifference,
        int DirectionReversals,
        string WorstPoint,
        int WorstFrame,
        float HeldSegmentRotation,
        float MaximumGrabError,
        float MaximumConstraintError);

    public static string Run()
    {
        var lines = new List<string>();
        foreach (var site in Enum.GetValues<GrabSite>())
        {
            foreach (var pattern in Enum.GetValues<DragPattern>())
            {
                lines.Add($"{site}/{pattern}/early: {Format(Measure(pattern, site, 0, 90))}");
                lines.Add($"{site}/{pattern}/settled: {Format(Measure(pattern, site, 180, 300))}");
            }
        }
        lines.Add(MeasureJointHangDrops());
        return string.Join('\n', lines) + "\n";
    }

    private static string Format(MotionMetrics value) =>
        $"rms2={value.RmsSecondDifference:F4}, max2={value.MaximumSecondDifference:F4}, " +
        $"rms3={value.RmsThirdDifference:F4}, reversals={value.DirectionReversals}, " +
        $"worst={value.WorstPoint}@{value.WorstFrame}, " +
        $"heldRotation={value.HeldSegmentRotation:F4}, " +
        $"grab={value.MaximumGrabError:F4}, constraint={value.MaximumConstraintError:F4}";

    private static MotionMetrics Measure(
        DragPattern pattern,
        GrabSite site,
        int warmupFrames,
        int sampleFrames)
    {
        var lizard = new ProceduralLizard();
        var mood = EmotionBlend.Normalize(new EmotionBlend(0.25f, 0.35f, 0.30f, 0.10f));
        var grabPoint = site switch
        {
            GrabSite.SpineMid => Vector2.Lerp(lizard.Spine.Joints[6], lizard.Spine.Joints[7], 0.5f),
            GrabSite.BodySurface => lizard.GetBodyPoint(2, 1f),
            GrabSite.FrontUpper => Vector2.Lerp(lizard.Legs[0].Shoulder, lizard.Legs[0].Elbow, 0.5f),
            GrabSite.FrontLower => Vector2.Lerp(lizard.Legs[0].Elbow, lizard.Legs[0].Foot, 0.5f),
            GrabSite.RearFoot => lizard.Legs[2].Foot,
            _ => lizard.Spine.Joints[0]
        };
        lizard.BeginGrab(grabPoint);
        var initialHeldAngle = MathF.Atan2(
            lizard.Spine.Joints[7].Y - lizard.Spine.Joints[6].Y,
            lizard.Spine.Joints[7].X - lizard.Spine.Joints[6].X);
        var worldOffset = Vector2.Zero;
        Vector2[]? previous = null;
        Vector2[]? previousDelta = null;
        Vector2[]? previousSecond = null;
        double secondSum = 0d;
        double thirdSum = 0d;
        var samples = 0;
        var reversals = 0;
        var maximumSecond = 0f;
        var worstPoint = "none";
        var worstFrame = -1;
        var maximumGrab = 0f;
        var maximumConstraint = 0f;

        for (var displayFrame = 0; displayFrame < warmupFrames + sampleFrames; displayFrame++)
        {
            var displayDelta = pattern == DragPattern.Stationary
                ? Vector2.Zero
                : new Vector2(2.2f, 0.38f);
            for (var substep = 0; substep < 2; substep++)
            {
                var delta = pattern switch
                {
                    DragPattern.Stationary => Vector2.Zero,
                    DragPattern.Distributed => displayDelta * 0.5f,
                    DragPattern.Pulsed when substep == 0 => displayDelta,
                    _ => Vector2.Zero
                };
                worldOffset += delta;
                lizard.Update(
                    SimulationDt,
                    new LizardAnimationInput(
                        0f,
                        0f,
                        LizardPoseMode.Grabbed,
                        mood,
                        0f,
                        delta));
            }

            maximumGrab = Math.Max(maximumGrab, lizard.DanglingGrabError);
            maximumConstraint = Math.Max(maximumConstraint, lizard.DanglingConstraintError);
            var current = CaptureWorldPose(lizard, worldOffset);
            if (displayFrame >= warmupFrames && previous is not null && previousDelta is not null)
            {
                for (var index = 0; index < current.Length; index++)
                {
                    var delta = current[index] - previous[index];
                    var second = delta - previousDelta[index];
                    secondSum += second.LengthSquared();
                    var secondMagnitude = second.Length();
                    if (secondMagnitude > maximumSecond)
                    {
                        maximumSecond = secondMagnitude;
                        worstPoint = GetPointName(index, lizard.Spine.Joints.Count);
                        worstFrame = displayFrame;
                    }
                    if (Vector2.Dot(delta, previousDelta[index]) < 0f &&
                        delta.LengthSquared() > 0.0009f &&
                        previousDelta[index].LengthSquared() > 0.0009f)
                    {
                        reversals++;
                    }
                    if (previousSecond is not null)
                    {
                        var third = second - previousSecond[index];
                        thirdSum += third.LengthSquared();
                    }
                    previousDelta[index] = delta;
                    if (previousSecond is not null)
                    {
                        previousSecond[index] = second;
                    }
                    samples++;
                }
            }
            else if (previous is not null)
            {
                previousDelta = new Vector2[current.Length];
                previousSecond = new Vector2[current.Length];
                for (var index = 0; index < current.Length; index++)
                {
                    previousDelta[index] = current[index] - previous[index];
                }
            }
            previous = current;
        }

        return new MotionMetrics(
            samples > 0 ? MathF.Sqrt((float)(secondSum / samples)) : 0f,
            maximumSecond,
            samples > 0 ? MathF.Sqrt((float)(thirdSum / samples)) : 0f,
            reversals,
            worstPoint,
            worstFrame,
            MathF.Abs(MathEx.DeltaAngle(
                initialHeldAngle,
                MathF.Atan2(
                    lizard.Spine.Joints[7].Y - lizard.Spine.Joints[6].Y,
                    lizard.Spine.Joints[7].X - lizard.Spine.Joints[6].X))),
            maximumGrab,
            maximumConstraint);
    }

    private static string GetPointName(int index, int spineCount)
    {
        if (index < spineCount)
        {
            return $"spine{index}";
        }

        var legPoint = index - spineCount;
        return legPoint % 2 == 0
            ? $"leg{legPoint / 2}.elbow"
            : $"leg{legPoint / 2}.foot";
    }

    private static Vector2[] CaptureWorldPose(ProceduralLizard lizard, Vector2 worldOffset)
    {
        var result = new Vector2[lizard.Spine.Joints.Count + lizard.Legs.Count * 2];
        var index = 0;
        foreach (var point in lizard.Spine.Joints)
        {
            result[index++] = point + worldOffset;
        }
        foreach (var leg in lizard.Legs)
        {
            result[index++] = leg.Elbow + worldOffset;
            result[index++] = leg.Foot + worldOffset;
        }
        return result;
    }

    private static string MeasureJointHangDrops()
    {
        var values = new List<string>();
        var area = new FloatRect(110f, 110f, 1170f, 610f);
        for (var joint = 0; joint < 14; joint++)
        {
            var behavior = new BehaviorController(29011 + joint * 71);
            var lizard = new ProceduralLizard();
            behavior.Reset(area.Center, 0f);
            var grabPoint = lizard.Spine.Joints[joint];
            lizard.BeginGrab(grabPoint);
            behavior.BeginGrab();
            var previousScreenPosition = behavior.Position;
            for (var frame = 0; frame < 1.5f / SimulationDt; frame++)
            {
                if (frame < 0.55f / SimulationDt)
                {
                    var sway = new Vector2(
                        MathF.Sin(frame * 0.045f) * 0.8f,
                        MathF.Cos(frame * 0.027f) * 0.18f);
                    behavior.DragTo(behavior.Position + sway);
                }
                behavior.Update(SimulationDt, area);
                var modelDelta =
                    (behavior.Position - previousScreenPosition) /
                    lizard.Profile.Appearance.VisualScale;
                previousScreenPosition = behavior.Position;
                lizard.Update(
                    SimulationDt,
                    new LizardAnimationInput(
                        behavior.Heading,
                        0f,
                        LizardPoseMode.Grabbed,
                        behavior.Emotion,
                        behavior.DropProgress,
                        modelDelta));
            }
            var centerOfMassY = lizard.Spine.Joints.Average(point => point.Y);
            values.Add($"J{joint}:{centerOfMassY - grabPoint.Y:F1}");
        }
        return "jointDrops: " + string.Join(", ", values);
    }

    private enum DragPattern
    {
        Stationary,
        Distributed,
        Pulsed
    }

    private enum GrabSite
    {
        SpineMid,
        BodySurface,
        FrontUpper,
        FrontLower,
        RearFoot
    }
}
