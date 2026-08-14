using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// Small position-based dynamics rig used while the pet is either held or in
/// accidental free fall. Both modes share particle integration and articulated
/// constraints; only the held mode owns a material grab point.
/// </summary>
internal sealed partial class DanglingRig2D
{
    private enum SimulationMode
    {
        Grabbed,
        FreeFall
    }

    private const int LegCount = DanglingTopology2D.LegCount;
    private readonly PhysicsConfiguration _configuration;
    private readonly int _spineCount;
    private readonly float _maximumCoordinate;
    private readonly PhysicsParticle2D[] _spine;
    private readonly PhysicsParticle2D[] _elbows = new PhysicsParticle2D[LegCount];
    private readonly PhysicsParticle2D[] _feet = new PhysicsParticle2D[LegCount];
    private readonly float[] _spineLengths;
    private readonly float[] _upperLegLengths = new float[LegCount];
    private readonly float[] _lowerLegLengths = new float[LegCount];
    private readonly Vector2[] _lastValidSpine;
    private readonly Vector2[] _lastValidElbows = new Vector2[LegCount];
    private readonly Vector2[] _lastValidFeet = new Vector2[LegCount];
    private readonly Vector2[] _freeFallReachStartElbows = new Vector2[2];
    private readonly Vector2[] _freeFallReachStartFeet = new Vector2[2];
    private readonly Vector2[] _freeFallReachTargets = new Vector2[2];
    private readonly float[] _freeFallReachBendSigns = new float[2];
    private readonly Vector2[] _outputSpinePose;
    private Vector2 _anchor;
    private GrabBinding _grabBinding;
    private float _activeTime;
    private bool _active;
    private SimulationMode _mode;
    private Vector2 _freeFallReferenceSpineCenter;
    private bool _freeFallReachCaptured;
    private float _freeFallReachCaptureInputProgress;

    public bool IsActive => _active;
    public bool IsFreeFallActive => _active && _mode == SimulationMode.FreeFall;
    public float MaximumConstraintError { get; private set; }
    public float SpineConstraintError { get; private set; }
    public float UpperLegConstraintError { get; private set; }
    public float LowerLegConstraintError { get; private set; }
    public float GrabConstraintError { get; private set; }
    public float FreeFallReferenceCorrectionTotal { get; private set; }
    public float FreeFallReferenceCorrectionMaximum { get; private set; }
    public float FreeFallReferenceCenterError =>
        _active && _mode == SimulationMode.FreeFall
            ? Vector2.Distance(
                GetSpineCenter(),
                _freeFallReferenceSpineCenter)
            : 0f;
    public bool FreeFallReachActive => IsFreeFallActive && _freeFallReachCaptured;
    public float FreeFallReachProgress { get; private set; }
    public Vector2 FreeFallReachTarget0 => _freeFallReachTargets[0];
    public Vector2 FreeFallReachTarget1 => _freeFallReachTargets[1];
    public float FreeFallReachTargetError { get; private set; }

    public DanglingRig2D(
        PhysicsConfiguration configuration,
        int spineCount,
        float maximumCoordinate)
    {
        if (spineCount < 5)
        {
            throw new ArgumentOutOfRangeException(nameof(spineCount));
        }
        if (!float.IsFinite(maximumCoordinate) || maximumCoordinate <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCoordinate));
        }

        _configuration = configuration;
        _spineCount = spineCount;
        _maximumCoordinate = maximumCoordinate;
        _spine = new PhysicsParticle2D[_spineCount];
        _spineLengths = new float[_spineCount - 1];
        _lastValidSpine = new Vector2[_spineCount];
        _outputSpinePose = new Vector2[_spineCount];
    }

    public void Begin(ProceduralLizard lizard, Vector2 grabPoint)
    {
        ResetFreeFallReach();
        InitializePose(lizard);
        _anchor = IsFinite(grabPoint) ? grabPoint : lizard.Spine.Joints[0];
        _grabBinding = FindClosestBinding(lizard, _anchor);
        Activate(SimulationMode.Grabbed);
    }

    private void BeginFreeFall(ProceduralLizard lizard)
    {
        ResetFreeFallReach();
        InitializePose(lizard);
        _freeFallReferenceSpineCenter = GetSpineCenter();
        FreeFallReferenceCorrectionTotal = 0f;
        FreeFallReferenceCorrectionMaximum = 0f;
        Activate(SimulationMode.FreeFall);
    }

    private void InitializePose(ProceduralLizard lizard)
    {
        for (var i = 0; i < _spineCount; i++)
        {
            _spine[i] = ParticleSolver2D.Create(lizard.Spine.Joints[i]);
        }
        for (var i = 0; i < LegCount; i++)
        {
            _elbows[i] = ParticleSolver2D.Create(lizard.Legs[i].Elbow);
            _feet[i] = ParticleSolver2D.Create(lizard.Legs[i].Foot);
        }
        for (var i = 0; i < _spineCount - 1; i++)
        {
            _spineLengths[i] = Math.Max(
                _configuration.MinimumBoneLength,
                Vector2.Distance(_spine[i].Position, _spine[i + 1].Position));
        }
        for (var i = 0; i < LegCount; i++)
        {
            var bodyIndex = DanglingTopology2D.GetBodyIndex(i);
            var shoulder = GetShoulderPosition(lizard, i, bodyIndex);
            _upperLegLengths[i] = Math.Max(
                _configuration.MinimumBoneLength,
                Vector2.Distance(shoulder, _elbows[i].Position));
            _lowerLegLengths[i] = Math.Max(
                _configuration.MinimumBoneLength,
                Vector2.Distance(_elbows[i].Position, _feet[i].Position));
            // The rendered crawl elbow is deliberately stylized and is not
            // always exactly one nominal Chain.LinkSize from both endpoints.
            // A grab must preserve the visible material pose: growing those
            // two measured segments toward the nominal chain length over the
            // first 220 ms made every paw pump outward just after pickup and
            // was perceived as twitching. Treat the lengths measured on the
            // grab frame as the ragdoll's fixed bone lengths instead.
        }
    }

    private void Activate(SimulationMode mode)
    {
        _mode = mode;
        _active = true;
        _activeTime = 0f;
        MaximumConstraintError = 0f;
        SpineConstraintError = 0f;
        UpperLegConstraintError = 0f;
        LowerLegConstraintError = 0f;
        GrabConstraintError = 0f;
        SaveValidPose();
    }

    public void End() => _active = false;

    public void Update(
        ProceduralLizard lizard,
        float dt,
        Vector2 anchor,
        Vector2 screenDelta)
    {
        if (!_active || _mode != SimulationMode.Grabbed)
        {
            Begin(lizard, anchor);
        }

        if (IsFinite(anchor))
        {
            _anchor = anchor;
        }
        dt = PrepareSimulationStep(
            dt,
            screenDelta,
            out var upperIterationBlend,
            out var lowerIterationBlend);

        for (var iteration = 0; iteration < _configuration.ConstraintIterations; iteration++)
        {
            GrabConstraintError = DanglingConstraintSolver2D.SolveIteration(
                lizard,
                _configuration,
                _spine,
                _elbows,
                _feet,
                _spineLengths,
                _upperLegLengths,
                _lowerLegLengths,
                _grabBinding,
                _anchor,
                upperIterationBlend,
                lowerIterationBlend,
                GrabConstraintError);
        }

        GrabConstraintError = DanglingConstraintSolver2D.FinalizeConstrainedPose(
            lizard,
            _configuration,
            _spine,
            _elbows,
            _feet,
            _spineLengths,
            _upperLegLengths,
            _lowerLegLengths,
            _grabBinding,
            _anchor,
            GrabConstraintError);

        UpdateParticleVelocities(dt);
        if (!IsValid(lizard))
        {
            RestoreValidPose(lizard);
            // Report the pose that will actually be rendered, rather than the
            // discarded unstable candidate that triggered this recovery.
            IsValid(lizard);
        }
        else
        {
            SaveValidPose();
        }

        ApplyTo(lizard);
    }

    private float PrepareSimulationStep(
        float dt,
        Vector2 screenDelta,
        out float upperIterationBlend,
        out float lowerIterationBlend)
    {
        dt = float.IsFinite(dt)
            ? Math.Clamp(
                dt,
                _configuration.MinimumSimulationStep,
                _configuration.MaximumSimulationStep)
            : _configuration.FallbackSimulationStep;
        if (!IsFinite(screenDelta))
        {
            screenDelta = Vector2.Zero;
        }

        _activeTime += dt;
        ShiftForWindow(screenDelta);
        ParticleSolver2D.Integrate(_spine, dt, _configuration);
        ParticleSolver2D.Integrate(_elbows, dt, _configuration);
        ParticleSolver2D.Integrate(_feet, dt, _configuration);

        // Convert per-step spring responses to a per-iteration blend. The
        // delayed envelope lets inertia establish the pose before the limbs
        // begin settling toward a readable silhouette.
        var angularSpringEnvelope = SmoothStep(MathEx.Clamp01(
            (_activeTime - _configuration.AngularSpringDelay) /
            _configuration.AngularSpringRampDuration));
        var upperStepBlend = 1f - MathF.Exp(
            -_configuration.UpperLimbAngularSpring * angularSpringEnvelope * dt);
        var lowerStepBlend = 1f - MathF.Exp(
            -_configuration.LowerLimbAngularSpring * angularSpringEnvelope * dt);
        upperIterationBlend = 1f - MathF.Pow(
            1f - upperStepBlend,
            1f / _configuration.ConstraintIterations);
        lowerIterationBlend = 1f - MathF.Pow(
            1f - lowerStepBlend,
            1f / _configuration.ConstraintIterations);
        return dt;
    }

    private void UpdateParticleVelocities(float dt)
    {
        var constraintVelocityResponse = 1f - MathF.Exp(
            -_configuration.ConstraintVelocityResponseRate * dt);
        ParticleSolver2D.UpdateVelocities(
            _spine,
            dt,
            constraintVelocityResponse,
            _configuration.MaximumSpeed);
        ParticleSolver2D.UpdateVelocities(
            _elbows,
            dt,
            constraintVelocityResponse,
            _configuration.MaximumSpeed);
        ParticleSolver2D.UpdateVelocities(
            _feet,
            dt,
            constraintVelocityResponse,
            _configuration.MaximumSpeed);
    }

    private void ApplyTo(ProceduralLizard lizard)
    {
        for (var i = 0; i < _spineCount; i++) _outputSpinePose[i] = _spine[i].Position;
        lizard.Spine.SetPose(_outputSpinePose);
        for (var i = 0; i < LegCount; i++)
        {
            var leg = lizard.Legs[i];
            var bodyIndex = leg.BodyIndex;
            var shoulder = GetShoulderPosition(lizard, i, bodyIndex);
            leg.SetDanglingPose(shoulder, _elbows[i].Position, _feet[i].Position);
        }
    }

    private Vector2 GetShoulderPosition(
        ProceduralLizard lizard,
        int legIndex,
        int bodyIndex) =>
        DanglingTopology2D.GetShoulderPosition(
            _spine,
            legIndex,
            bodyIndex,
            lizard.Spine.Angles[bodyIndex],
            lizard.GetBodyWidth(bodyIndex),
            _configuration.ShoulderAttachmentRadiusFactor);

    private GrabBinding FindClosestBinding(ProceduralLizard lizard, Vector2 point) =>
        DanglingTopology2D.FindClosestBinding(
            lizard,
            _spine,
            _elbows,
            _feet,
            point,
            _configuration.ShoulderAttachmentRadiusFactor);

    private Vector2 GetBindingPoint(ProceduralLizard lizard) =>
        DanglingTopology2D.GetBindingPoint(
            lizard,
            _spine,
            _elbows,
            _feet,
            _grabBinding,
            _configuration.ShoulderAttachmentRadiusFactor);

    private void ShiftForWindow(Vector2 screenDelta)
    {
        if (screenDelta.LengthSquared() <= 0.000001f)
        {
            return;
        }

        // A normal cursor delta is applied in full so unpinned joints retain
        // world-space inertia. A monitor jump/very long frame is treated as a
        // teleport: the pose follows the window rigidly and velocities are
        // cleared instead of stretching hundreds of model pixels.
        if (screenDelta.LengthSquared() >
            _configuration.TeleportThreshold * _configuration.TeleportThreshold)
        {
            ParticleSolver2D.ClearVelocities(_spine);
            ParticleSolver2D.ClearVelocities(_elbows);
            ParticleSolver2D.ClearVelocities(_feet);
            return;
        }

        var shift = screenDelta;
        for (var i = 0; i < _spineCount; i++) ParticleSolver2D.Shift(ref _spine[i], shift);
        for (var i = 0; i < LegCount; i++)
        {
            ParticleSolver2D.Shift(ref _elbows[i], shift);
            ParticleSolver2D.Shift(ref _feet[i], shift);
        }
        TranslateFreeFallReachFrame(-shift);
    }

    private bool IsValid(ProceduralLizard lizard)
    {
        var validation = DanglingPoseValidator.Validate(
            lizard,
            _spine,
            _elbows,
            _feet,
            _spineLengths,
            _upperLegLengths,
            _lowerLegLengths,
            _configuration.ShoulderAttachmentRadiusFactor,
            _maximumCoordinate,
            _configuration.MaximumConstraintError,
            GrabConstraintError,
            _configuration.MaximumGrabError);
        MaximumConstraintError = validation.MaximumConstraintError;
        SpineConstraintError = validation.SpineConstraintError;
        UpperLegConstraintError = validation.UpperLegConstraintError;
        LowerLegConstraintError = validation.LowerLegConstraintError;
        return validation.IsValid;
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private void SaveValidPose()
    {
        for (var i = 0; i < _spineCount; i++) _lastValidSpine[i] = _spine[i].Position;
        for (var i = 0; i < LegCount; i++)
        {
            _lastValidElbows[i] = _elbows[i].Position;
            _lastValidFeet[i] = _feet[i].Position;
        }
    }

    private void RestoreValidPose(ProceduralLizard lizard)
    {
        for (var i = 0; i < _spineCount; i++) _spine[i] = ParticleSolver2D.Create(_lastValidSpine[i]);
        for (var i = 0; i < LegCount; i++)
        {
            _elbows[i] = ParticleSolver2D.Create(_lastValidElbows[i]);
            _feet[i] = ParticleSolver2D.Create(_lastValidFeet[i]);
        }
        var correction = _anchor - GetBindingPoint(lizard);
        for (var i = 0; i < _spineCount; i++) ParticleSolver2D.Translate(ref _spine[i], correction);
        for (var i = 0; i < LegCount; i++)
        {
            ParticleSolver2D.Translate(ref _elbows[i], correction);
            ParticleSolver2D.Translate(ref _feet[i], correction);
        }
        GrabConstraintError = Vector2.Distance(GetBindingPoint(lizard), _anchor);
    }

    private static float SmoothStep(float value) => value * value * (3f - 2f * value);
}
