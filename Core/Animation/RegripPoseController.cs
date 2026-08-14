using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// Presentation-only substates for a lost-grip catch. They intentionally do
/// not mirror behavior states: Seeking happens during FreeFall, while contact
/// hold and recovery both happen during the behavior Regripping phase.
/// </summary>
internal enum RegripAnimationPhase
{
    None,
    Seeking,
    ContactHold,
    Recovering
}

/// <summary>
/// Owns the stationary contact pose and the deterministic transition back to
/// the normal procedural pose. Mouse release recovery keeps its independent
/// duration-based path in <see cref="ProceduralLizard"/>.
/// </summary>
internal sealed class RegripPoseController
{
    private readonly Vector2[] _contactSpinePose;
    private readonly Vector2[] _blendSpinePose;
    private readonly Vector2[] _contactElbows = new Vector2[4];
    private readonly Vector2[] _contactFeet = new Vector2[4];
    private Vector2 _contactSpineCenter;
    private bool _hasContactPose;

    public RegripAnimationPhase Phase { get; private set; }
    public bool ReachActive => Phase == RegripAnimationPhase.Seeking;
    public float ReachProgress { get; private set; }
    public Vector2 ContactTarget0 { get; private set; }
    public Vector2 ContactTarget1 { get; private set; }
    public int ContactLegMask { get; private set; }
    public bool Contacted => ContactLegMask != 0;
    public float ContactError { get; private set; }
    public float ContactSpineDrift { get; private set; }
    public bool HasContactPose => _hasContactPose;

    public RegripPoseController(int spineCount)
    {
        if (spineCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(spineCount));
        }

        _contactSpinePose = new Vector2[spineCount];
        _blendSpinePose = new Vector2[spineCount];
    }

    public void ObserveSeeking(
        float progress,
        Vector2 target0,
        Vector2 target1,
        float targetError)
    {
        _hasContactPose = false;
        Phase = RegripAnimationPhase.Seeking;
        ReachProgress = float.IsFinite(progress) ? MathEx.Clamp01(progress) : 0f;
        ContactTarget0 = target0;
        ContactTarget1 = target1;
        // The final moving frame has physically reached both support points.
        // Keep the animation phase Seeking until the next stationary frame,
        // but expose contact immediately so stopping follows contact rather
        // than appearing to precede it.
        ContactLegMask = ReachProgress >= 1f ? 0b0011 : 0;
        ContactError = float.IsFinite(targetError) ? Math.Max(0f, targetError) : 0f;
        ContactSpineDrift = 0f;
    }

    public void ObserveFreeFallWithoutReach()
    {
        Reset();
    }

    public void BeginContact(
        Chain spine,
        IReadOnlyList<LegRig> legs,
        Vector2 target0,
        Vector2 target1)
    {
        if (spine.Joints.Count != _contactSpinePose.Length || legs.Count != 4)
        {
            throw new InvalidOperationException("Regrip pose topology changed unexpectedly.");
        }

        for (var index = 0; index < spine.Joints.Count; index++)
        {
            _contactSpinePose[index] = spine.Joints[index];
        }
        for (var index = 0; index < legs.Count; index++)
        {
            _contactElbows[index] = legs[index].Elbow;
            _contactFeet[index] = legs[index].Foot;
        }

        _contactSpineCenter = GetSpineCenter(_contactSpinePose);
        ContactTarget0 = target0;
        ContactTarget1 = target1;
        ReachProgress = 1f;
        ContactError = Math.Max(
            Vector2.Distance(_contactFeet[0], ContactTarget0),
            Vector2.Distance(_contactFeet[1], ContactTarget1));
        ContactSpineDrift = 0f;
        ContactLegMask = 0b0011;
        Phase = RegripAnimationPhase.ContactHold;
        _hasContactPose = true;
    }

    /// <summary>
    /// Returns the blend from the captured contact pose to the normal pose.
    /// A zero blend keeps every joint frozen while the host window has stopped.
    /// </summary>
    public float PrepareRecovery(float regripProgress, float holdFraction)
    {
        if (!_hasContactPose)
        {
            return 1f;
        }

        var progress = float.IsFinite(regripProgress)
            ? MathEx.Clamp01(regripProgress)
            : 0f;
        var hold = float.IsFinite(holdFraction)
            ? Math.Clamp(holdFraction, 0f, 0.8f)
            : 0f;
        if (progress <= hold && hold > 0f)
        {
            Phase = RegripAnimationPhase.ContactHold;
            ContactLegMask = 0b0011;
            ContactSpineDrift = 0f;
            return 0f;
        }

        Phase = RegripAnimationPhase.Recovering;
        ContactLegMask = 0;
        ContactSpineDrift = 0f;
        var recoveryProgress = MathEx.Clamp01(
            (progress - hold) / Math.Max(0.0001f, 1f - hold));
        return SmoothStep(recoveryProgress);
    }

    public void ApplySpineBlend(Chain spine, float blend)
    {
        var amount = MathEx.Clamp01(blend);
        for (var index = 0; index < spine.Joints.Count; index++)
        {
            _blendSpinePose[index] = Vector2.Lerp(
                _contactSpinePose[index],
                spine.Joints[index],
                amount);
        }
        spine.SetPose(_blendSpinePose);
    }

    public void ApplyLegBlend(
        LegRig leg,
        Vector2 targetElbow,
        Vector2 targetFoot,
        float blend)
    {
        var amount = MathEx.Clamp01(blend);
        leg.SetDanglingPose(
            leg.Shoulder,
            Vector2.Lerp(_contactElbows[leg.Index], targetElbow, amount),
            Vector2.Lerp(_contactFeet[leg.Index], targetFoot, amount));
    }

    public void UpdateContactMetrics(Chain spine, IReadOnlyList<LegRig> legs)
    {
        if (Phase != RegripAnimationPhase.ContactHold)
        {
            ContactSpineDrift = 0f;
            return;
        }

        ContactError = Math.Max(
            Vector2.Distance(legs[0].Foot, ContactTarget0),
            Vector2.Distance(legs[1].Foot, ContactTarget1));
        var sum = Vector2.Zero;
        for (var index = 0; index < spine.Joints.Count; index++)
        {
            sum += spine.Joints[index];
        }
        ContactSpineDrift = Vector2.Distance(
            sum / spine.Joints.Count,
            _contactSpineCenter);
    }

    public void Reset()
    {
        Phase = RegripAnimationPhase.None;
        ReachProgress = 0f;
        ContactTarget0 = Vector2.Zero;
        ContactTarget1 = Vector2.Zero;
        ContactLegMask = 0;
        ContactError = 0f;
        ContactSpineDrift = 0f;
        _hasContactPose = false;
    }

    private static Vector2 GetSpineCenter(ReadOnlySpan<Vector2> pose)
    {
        var sum = Vector2.Zero;
        for (var index = 0; index < pose.Length; index++)
        {
            sum += pose[index];
        }
        return sum / pose.Length;
    }

    private static float SmoothStep(float value) =>
        value * value * (3f - 2f * value);
}
