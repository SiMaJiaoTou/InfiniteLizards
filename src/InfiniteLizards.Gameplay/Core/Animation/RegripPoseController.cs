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
    internal const float ContactTolerance = 0.25f;
    private const int LegCount = DanglingTopology2D.LegCount;
    private const int AllLegContactMask = (1 << LegCount) - 1;
    private readonly Vector2[] _contactSpinePose;
    private readonly Vector2[] _blendSpinePose;
    private readonly Vector2[] _contactElbows = new Vector2[LegCount];
    private readonly Vector2[] _contactFeet = new Vector2[LegCount];
    private readonly Vector2[] _contactTargets = new Vector2[LegCount];
    private Vector2 _contactSpineCenter;
    private bool _hasContactPose;

    public RegripAnimationPhase Phase { get; private set; }
    public bool ReachActive => Phase == RegripAnimationPhase.Seeking;
    public float ReachProgress { get; private set; }
    public Vector2 ContactTarget0 => _contactTargets[0];
    public Vector2 ContactTarget1 => _contactTargets[1];
    public Vector2 ContactTarget2 => _contactTargets[2];
    public Vector2 ContactTarget3 => _contactTargets[3];
    public int ContactLegMask { get; private set; }
    public bool Contacted => ContactLegMask != 0;
    public float ContactError { get; private set; }
    public float ContactSpineDrift { get; private set; }
    public bool HasContactPose => _hasContactPose;

    public Vector2 ContactTarget(int legIndex)
    {
        if ((uint)legIndex >= LegCount)
        {
            throw new ArgumentOutOfRangeException(nameof(legIndex));
        }

        return _contactTargets[legIndex];
    }

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
        ReadOnlySpan<Vector2> targets,
        float targetError,
        bool contactAllowed)
    {
        CopyTargets(targets);
        _hasContactPose = false;
        Phase = RegripAnimationPhase.Seeking;
        ReachProgress = float.IsFinite(progress) ? MathEx.Clamp01(progress) : 0f;
        ContactError = float.IsFinite(targetError) ? Math.Max(0f, targetError) : 0f;
        // The final moving frame has physically reached all four support points.
        // Keep the animation phase Seeking until the next stationary frame,
        // but expose contact immediately so stopping follows contact rather
        // than appearing to precede it.
        ContactLegMask = contactAllowed &&
                         ReachProgress >= 1f &&
                         ContactError <= ContactTolerance
            ? AllLegContactMask
            : 0;
        ContactSpineDrift = 0f;
    }

    public void ObserveFreeFallWithoutReach()
    {
        Reset();
    }

    public bool BeginContact(
        Chain spine,
        IReadOnlyList<LegRig> legs,
        ReadOnlySpan<Vector2> targets)
    {
        if (spine.Joints.Count != _contactSpinePose.Length || legs.Count != LegCount)
        {
            throw new InvalidOperationException("Regrip pose topology changed unexpectedly.");
        }

        CopyTargets(targets);

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
        ReachProgress = 1f;
        ContactError = CalculateContactError(_contactFeet);
        ContactSpineDrift = 0f;
        _hasContactPose = ContactError <= ContactTolerance;
        ContactLegMask = _hasContactPose ? AllLegContactMask : 0;
        Phase = _hasContactPose
            ? RegripAnimationPhase.ContactHold
            : RegripAnimationPhase.None;
        return _hasContactPose;
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
            ContactLegMask = AllLegContactMask;
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

        ContactError = 0f;
        for (var legIndex = 0; legIndex < LegCount; legIndex++)
        {
            ContactError = Math.Max(
                ContactError,
                Vector2.Distance(
                    legs[legIndex].Foot,
                    _contactTargets[legIndex]));
        }
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
        Array.Clear(_contactTargets);
        ContactLegMask = 0;
        ContactError = 0f;
        ContactSpineDrift = 0f;
        _hasContactPose = false;
    }

    private void CopyTargets(ReadOnlySpan<Vector2> targets)
    {
        if (targets.Length != LegCount)
        {
            throw new ArgumentException(
                $"Regrip contact requires exactly {LegCount} targets.",
                nameof(targets));
        }

        targets.CopyTo(_contactTargets);
    }

    private float CalculateContactError(ReadOnlySpan<Vector2> feet)
    {
        var error = 0f;
        for (var legIndex = 0; legIndex < LegCount; legIndex++)
        {
            error = Math.Max(
                error,
                Vector2.Distance(feet[legIndex], _contactTargets[legIndex]));
        }
        return error;
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
