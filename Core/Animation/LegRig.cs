using System.Numerics;

namespace DesktopLizard.Core;

internal sealed class LegRig
{
    private readonly Chain _chain;
    private readonly GaitConfiguration _configuration;
    private Vector2 _stepFrom;
    private Vector2 _stepTo;
    private Vector2 _stepNormal;
    private float _stepDuration;
    private float _stepTime;
    private float _stepDelay;
    private float _stepHeight;
    private Vector2 _bodyForward = Vector2.UnitX;
    private Vector2 _bodyRight = Vector2.UnitY;

    public int Index { get; }
    public int Side { get; }
    public int BodyIndex { get; }
    public int Pair { get; }
    public bool IsFront { get; }
    public Vector2 Shoulder { get; private set; }
    public Vector2 Elbow => _chain.Joints[1];
    public Vector2 Foot { get; private set; }
    public Vector2 StepFrom => _stepFrom;
    public Vector2 StepTo => _stepTo;
    public Vector2 StepNormal => _stepNormal;
    public float Lift { get; private set; }
    public bool IsStepping => _stepDelay > 0f || _stepTime < _stepDuration;
    public bool IsSwingMoving => _stepDelay <= 0f && _stepTime < _stepDuration;
    public float StepDuration => _stepDuration;
    public float StepProgress => _stepDuration > 0.0001f
        ? MathEx.Clamp01(_stepTime / _stepDuration)
        : 1f;
    public float StepDelayRemaining => _stepDelay;
    public float StepSpan => Vector2.Distance(_stepFrom, _stepTo);
    public float StepHeight => _stepHeight;
    // Use almost the full two-bone reach for the long high-speed stride. The
    // tiny remaining bend prevents a perfectly straight visual lock while
    // avoiding planted-paw projection near the end of a support phase.
    public float MaximumReach => _chain.LinkSize * _configuration.MaximumReachFactor;
    public float LastReachCorrection { get; private set; }
    public int ReachProjectionSerial { get; private set; }
    public int StepTargetClampSerial { get; private set; }
    public int SwingClampSerial { get; private set; }
    public float MaximumKinematicCorrection { get; private set; }
    public int StepSerial { get; private set; }

    public LegRig(int index, Vector2 origin, GaitConfiguration configuration)
    {
        _configuration = configuration;
        Index = index;
        Side = index % 2 == 0 ? 1 : -1;
        IsFront = index < 2;
        BodyIndex = IsFront ? 1 : 4;
        Pair = index is 0 or 3 ? 0 : 1;
        _chain = new Chain(
            origin,
            3,
            IsFront ? configuration.FrontLegLinkLength : configuration.RearLegLinkLength);
        Shoulder = origin;
        Foot = origin;
        _stepFrom = origin;
        _stepTo = origin;
        _stepDuration = 0f;
        _stepTime = 0f;
    }

    public void Reset(Vector2 shoulder, Vector2 foot)
    {
        Shoulder = shoulder;
        Foot = foot;
        _stepFrom = foot;
        _stepTo = foot;
        _stepTime = 1f;
        _stepDuration = 0f;
        _stepDelay = 0f;
        Lift = 0f;
        Solve();
    }

    public void ShiftGround(Vector2 delta)
    {
        Foot += delta;
        _stepFrom += delta;
        _stepTo += delta;
    }

    public void SetShoulder(Vector2 shoulder, Vector2 bodyForward)
    {
        Shoulder = shoulder;
        _bodyForward = bodyForward;
        _bodyRight = MathEx.Perpendicular(bodyForward);
    }

    public void StartStep(
        Vector2 destination,
        Vector2 normal,
        float duration,
        float delay = 0f,
        float heightScale = 1f)
    {
        StepSerial++;
        var maximumReach = MaximumReach;
        var fromShoulder = destination - Shoulder;
        if (fromShoulder.LengthSquared() > maximumReach * maximumReach)
        {
            var constrained = Shoulder + MathEx.SafeNormalize(fromShoulder, _bodyForward) * maximumReach;
            var correction = Vector2.Distance(destination, constrained);
            if (correction > 0.0001f)
            {
                StepTargetClampSerial++;
                MaximumKinematicCorrection = Math.Max(MaximumKinematicCorrection, correction);
            }
            destination = constrained;
        }

        _stepFrom = Foot;
        _stepTo = destination;
        _stepNormal = normal * Side;
        // Keep every swing readable even at the crawl speed ceiling. The
        // normal high-speed duration is about eight 120 Hz simulation steps;
        // this floor only protects malformed or recovery inputs.
        _stepDuration = Math.Max(_configuration.MinimumStepDuration, duration);
        _stepTime = 0f;
        _stepDelay = Math.Max(0f, delay);
        // A slower cadence needs a more legible swing. Give longer strides a
        // clearly visible outward arc while preserving the low, crawling feel.
        _stepHeight = Math.Clamp(
                          _configuration.StepHeightBase +
                          Vector2.Distance(_stepFrom, _stepTo) * _configuration.StepHeightDistanceFactor,
                          _configuration.MinimumStepHeight,
                          _configuration.MaximumStepHeight) *
                      Math.Clamp(heightScale, _configuration.MinimumStepHeightScale, 1f);
    }

    public void UpdateStep(float dt)
    {
        if (!IsStepping)
        {
            Lift = 0f;
            Solve();
            return;
        }

        // The source footage does not move both paws of a diagonal pair as a
        // perfectly welded unit: the front paw commonly follows the rear paw
        // by a fraction of a frame. Keep the delayed paw planted in world
        // space, then consume any remainder of this simulation step so the
        // timing is independent of the display refresh rate.
        if (_stepDelay > 0f)
        {
            var remainingDt = Math.Max(0f, dt - _stepDelay);
            _stepDelay = Math.Max(0f, _stepDelay - dt);
            Lift = 0f;
            Solve();
            if (remainingDt <= 0f)
            {
                return;
            }
            dt = remainingDt;
        }

        _stepTime = Math.Min(_stepDuration, _stepTime + dt);
        var t = MathEx.Clamp01(_stepTime / _stepDuration);
        var arc = MathF.Sin(MathF.PI * t);
        // P(t) = (1-t)P0 + tP1 + n·H·sin(πt)
        var unconstrainedFoot = _stepFrom * (1f - t) + _stepTo * t + _stepNormal * (_stepHeight * arc);
        Foot = ClampToReach(unconstrainedFoot);
        var correction = Vector2.Distance(unconstrainedFoot, Foot);
        if (correction > 0.0001f)
        {
            SwingClampSerial++;
            MaximumKinematicCorrection = Math.Max(MaximumKinematicCorrection, correction);
        }
        Lift = arc;
        Solve();
    }

    public void ForceFoot(Vector2 foot)
    {
        _stepDuration = 0f;
        _stepTime = 1f;
        _stepDelay = 0f;
        _stepFrom = foot;
        _stepTo = foot;
        Foot = foot;
        Lift = 0f;
        Solve();
    }

    public void RelaxToward(Vector2 destination, float factor)
    {
        _stepDuration = 0f;
        _stepTime = 1f;
        _stepDelay = 0f;
        Foot = Vector2.Lerp(Foot, destination, factor);
        Lift = 0f;
        Solve();
    }

    public void ConstrainToReach()
    {
        var constrained = ClampToReach(Foot);
        LastReachCorrection = Vector2.Distance(Foot, constrained);
        if (LastReachCorrection > 0.0001f)
        {
            ReachProjectionSerial++;
        }
        Foot = constrained;
        Solve();
    }

    public void SetDanglingPose(Vector2 shoulder, Vector2 elbow, Vector2 foot)
    {
        Shoulder = shoulder;
        Foot = foot;
        _stepDuration = 0f;
        _stepTime = 1f;
        _stepDelay = 0f;
        _stepFrom = foot;
        _stepTo = foot;
        Lift = 0f;
        _chain.SetPose(foot, elbow, shoulder);
        _bodyForward = MathEx.SafeNormalize(shoulder - elbow, _bodyForward);
        _bodyRight = MathEx.Perpendicular(_bodyForward);
    }

    private void Solve()
    {
        _chain.Joints[0] = Foot;
        _chain.Joints[2] = Shoulder;

        // The reference is deliberately soft and graphic rather than a strict
        // anatomical IK drawing. A controlled forward/back knee produces its
        // characteristic small L-shaped limbs while keeping the paw planted.
        var elbow = Vector2.Lerp(Shoulder, Foot, _configuration.ElbowMidpointFactor) +
                    _bodyForward * (IsFront
                        ? _configuration.FrontElbowLongitudinalOffset
                        : _configuration.RearElbowLongitudinalOffset) +
                    MathEx.Perpendicular(_bodyForward) * (Side * _configuration.ElbowLateralOffset);
        _chain.Joints[1] = elbow;
    }

    private Vector2 ClampToReach(Vector2 point)
    {
        var maximumReach = MaximumReach;
        var fromShoulder = point - Shoulder;
        return fromShoulder.LengthSquared() <= maximumReach * maximumReach
            ? point
            : Shoulder + MathEx.SafeNormalize(fromShoulder, _bodyForward) * maximumReach;
    }
}
