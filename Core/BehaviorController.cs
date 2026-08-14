using System.Numerics;

namespace DesktopLizard.Core;

internal sealed partial class BehaviorController
{
    private readonly Random _random;
    private readonly LizardProfile _profile;
    private readonly BehaviorConfiguration _configuration;
    private readonly PointerChaseController _pointerChase;
    private readonly BoundaryNavigator _boundaryNavigator;
    private readonly SCurveMotionController _sCurve = new();
    private EmotionBlend _emotionTarget;
    private float _emotionTimer;
    private float _stateTimer;
    private float _boutTimer;
    private float _turnRemaining;
    private float _turnDirection = 1f;
    private float _cruiseSpeed;
    private bool _forwardExtended;
    private Vector2 _target;
    private float _desiredSpeed;
    private float _turnVelocity;
    private float _desiredTurnVelocity;
    private float _lookAngleOffset;
    private float _lookAngleTarget;
    private float _motionWatchdog;
    private Vector2 _watchdogPosition;
    private bool _restPending;
    private bool _idleMayObserve = true;
    private float _sprintDistance;
    private float _sprintTargetDistance;
    private float _sprintMinimumTimer;
    private float _lostGripStartY;
    private float _lostGripTargetY;
    private float _lostGripDistance;
    private float _lostGripTargetDistance;
    private float _lostGripVerticalVelocity;
    private float _lostGripRegripProgress;
    private LostGripSafetyContext _lostGripSafety;

    public Vector2 Position { get; private set; }
    public float Heading { get; private set; }
    public float Speed { get; private set; }
    public float TurnVelocity => _turnVelocity;
    public float NormalizedSpeed => MathEx.Clamp01(Speed / _configuration.Speed.AnimationNormalization);
    public RoamingState State { get; private set; } = RoamingState.Spawn;
    public StateTransitionReason LastTransitionReason { get; private set; } = StateTransitionReason.Command;
    public int TransitionSerial { get; private set; }
    public EmotionBlend Emotion { get; private set; }
    public LizardProfile Profile => _profile;
    public BehaviorConfiguration Configuration => _configuration;
    public bool IsPaused { get; set; }
    public float DropProgress { get; private set; }
    public LostGripFallPhase LostGripPhase { get; private set; }
    public float LostGripFallProgress => _lostGripTargetDistance > 0f
        ? MathEx.Clamp01(_lostGripDistance / _lostGripTargetDistance)
        : 0f;
    /// <summary>
    /// Normalized pre-contact reach intent while the window is still falling.
    /// The application layer explicitly latches 1 for the final moving step,
    /// after behavior has atomically switched to Regripping.
    /// </summary>
    public float LostGripReachProgress
    {
        get
        {
            if (LostGripPhase != LostGripFallPhase.Falling)
            {
                return 0f;
            }

            var effectiveLeadDistance = Math.Min(
                _configuration.LostGripFall.ReachLeadDistance,
                _lostGripTargetDistance);
            if (effectiveLeadDistance <= 0f)
            {
                return 0f;
            }

            var remainingDistance = Math.Max(0f, _lostGripTargetY - Position.Y);
            return 1f - MathEx.Clamp01(
                remainingDistance / effectiveLeadDistance);
        }
    }
    public float LostGripRegripProgress => _lostGripRegripProgress;
    public LostGripCatchReason LostGripCatchReason { get; private set; }
    public float LostGripVerticalVelocity => _lostGripVerticalVelocity;
    public float LostGripDistance => _lostGripDistance;
    public float LostGripTargetDistance => _lostGripTargetDistance;
    public Vector2 LookTarget => _pointerChase.HasTarget
        ? _pointerChase.Target
        : Position + MathEx.FromAngle(Heading + _lookAngleOffset) * _configuration.Decisions.LookTargetDistance;

    public BehaviorController(int seed, LizardProfile? profile = null)
    {
        _profile = profile ?? LizardProfile.Default;
        _configuration = _profile.Behavior;
        var configurationFailures = new List<string>();
        _configuration.Validate(configurationFailures);
        if (configurationFailures.Count > 0)
        {
            throw new ArgumentException(
                string.Join(Environment.NewLine, configurationFailures),
                nameof(profile));
        }

        _random = new Random(seed);
        _pointerChase = new PointerChaseController(_configuration.Pointer);
        _boundaryNavigator = new BoundaryNavigator(_configuration.Boundary);
        _cruiseSpeed = _configuration.Speed.ReferenceMinimumCrawl;
        var emotion = _configuration.Emotion;
        Emotion = EmotionBlend.Normalize(new EmotionBlend(
            emotion.InitialCalm,
            emotion.InitialCurious,
            emotion.InitialPlayful,
            emotion.InitialWary));
        _emotionTarget = Emotion;
        _emotionTimer = emotion.InitialRetargetDelay;
        _stateTimer = _configuration.Timing.InitialSpawnDuration;
    }

    public void Reset(Vector2 position, float heading = 0f)
    {
        Position = position;
        Heading = MathEx.SimplifyAngle(heading);
        Speed = 0f;
        _desiredSpeed = 0f;
        _turnVelocity = 0f;
        _desiredTurnVelocity = 0f;
        _target = position;
        State = RoamingState.Spawn;
        _stateTimer = _configuration.Timing.ResetSpawnDuration;
        _boutTimer = 0f;
        _turnRemaining = 0f;
        _sCurve.Reset();
        _cruiseSpeed = _configuration.Speed.ReferenceMinimumCrawl;
        _forwardExtended = false;
        _lookAngleOffset = 0f;
        _lookAngleTarget = 0f;
        _restPending = false;
        _idleMayObserve = true;
        _motionWatchdog = 0f;
        _watchdogPosition = position;
        _pointerChase.Reset(position);
        _sprintDistance = 0f;
        _sprintTargetDistance = 0f;
        _sprintMinimumTimer = 0f;
        ClearLostGripFallState();
        LastTransitionReason = StateTransitionReason.Command;
        TransitionSerial = 0;
        DropProgress = 0f;
    }

    public void BeginGrab()
    {
        if (State == RoamingState.Grabbed)
        {
            return;
        }

        SuppressMouseChase(_configuration.Timing.GrabPointerSuppressionDuration, requireLeave: true);
        TransitionTo(RoamingState.Grabbed, StateTransitionReason.Grab);
        Speed = 0f;
        _desiredSpeed = 0f;
        _turnVelocity = 0f;
        _desiredTurnVelocity = 0f;
        _lookAngleTarget = 0f;
        _target = Position;
        DropProgress = 0f;
        ClearLostGripFallState();
    }

    public void DragTo(Vector2 position)
    {
        if (State == RoamingState.Grabbed)
        {
            Position = position;
        }
    }

    public void EndGrab(Vector2 position)
    {
        if (State != RoamingState.Grabbed)
        {
            return;
        }

        SuppressMouseChase(_configuration.Timing.GrabPointerSuppressionDuration, requireLeave: true);
        Position = position;
        Speed = 0f;
        _desiredSpeed = 0f;
        _turnVelocity = 0f;
        _desiredTurnVelocity = 0f;
        ClearLostGripFallState();
        if (IsPaused)
        {
            TransitionTo(RoamingState.Idle, StateTransitionReason.Pause);
            _stateTimer = _configuration.Timing.PauseIdleDuration;
            _idleMayObserve = true;
            DropProgress = 0f;
            return;
        }

        TransitionTo(RoamingState.ReleaseSettle, StateTransitionReason.Release);
        _stateTimer = _configuration.Timing.ReleaseSettleDuration;
        _lookAngleTarget = 0f;
        _restPending = false;
        DropProgress = 0f;
    }

    public void MoveToCenter(Vector2 center)
    {
        if (State == RoamingState.Grabbed)
        {
            return;
        }

        SuppressMouseChase(_configuration.Timing.GrabPointerSuppressionDuration, requireLeave: true);
        Position = center;
        Speed = 0f;
        _desiredSpeed = 0f;
        _turnVelocity = 0f;
        _desiredTurnVelocity = 0f;
        _target = center;
        TransitionTo(RoamingState.Idle, StateTransitionReason.Command);
        _stateTimer = _configuration.Timing.CenterIdleDuration;
        _idleMayObserve = true;
        _lookAngleTarget = 0f;
        _restPending = false;
        DropProgress = 0f;
        ClearLostGripFallState();
    }

    public void Update(
        float dt,
        FloatRect navigationArea,
        PointerObservation pointer = default)
    {
        var safety = CreateDerivedLostGripSafety(navigationArea);
        Update(dt, navigationArea, safety, pointer);
    }

    /// <summary>
    /// Advances behavior using a host-supplied full-render safety contract.
    /// Runtime callers derive this from the original physical work area so a
    /// previously capped normal navigation inset cannot masquerade as safe.
    /// </summary>
    public void Update(
        float dt,
        FloatRect navigationArea,
        LostGripSafetyContext lostGripSafety,
        PointerObservation pointer = default)
    {
        _lostGripSafety = lostGripSafety;
        dt = Math.Clamp(dt, 0f, 0.05f);
        UpdateEmotion(dt);
        _pointerChase.UpdateArmState(dt, Position, pointer);
        _lookAngleOffset = MathEx.Lerp(
            _lookAngleOffset,
            _lookAngleTarget,
            MathEx.ExpLerpFactor(_configuration.Emotion.LookResponse, dt));

        if (State == RoamingState.Grabbed)
        {
            return;
        }

        if (IsPaused)
        {
            SuppressMouseChase(_configuration.Timing.PausePointerSuppressionDuration, requireLeave: true);
            // Pause is an immediate, terminal interruption of the current
            // action. Do not let an escape-sprint velocity or landing pose
            // leak into Idle and resume as an unexplained glide later.
            Speed = 0f;
            _desiredSpeed = 0f;
            _turnVelocity = 0f;
            _desiredTurnVelocity = 0f;
            DropProgress = 0f;
            ClearLostGripFallState();
            _idleMayObserve = true;
            TransitionTo(RoamingState.Idle, StateTransitionReason.Pause);
            _stateTimer = _configuration.Timing.PauseIdleDuration;
            return;
        }

        if (UpdateMouseResponse(dt, navigationArea, pointer))
        {
            Position = navigationArea.Clamp(Position);
            return;
        }

        UpdateMotionWatchdog(dt, navigationArea);

        switch (State)
        {
            case RoamingState.Spawn:
                _stateTimer -= dt;
                if (_stateTimer <= 0f)
                {
                    EnterIdle(
                        _configuration.Timing.SpawnIdle.Minimum,
                        _configuration.Timing.SpawnIdle.Maximum);
                }
                break;

            case RoamingState.Idle:
                _stateTimer -= dt;
                Speed = Math.Max(0f, Speed - _configuration.Speed.RestDeceleration * dt);
                _turnVelocity = MathEx.Lerp(
                    _turnVelocity,
                    0f,
                    MathEx.ExpLerpFactor(_configuration.Locomotion.IdleTurnDampingResponse, dt));
                Heading = MathEx.SimplifyAngle(Heading + _turnVelocity * dt);
                Position += MathEx.FromAngle(Heading) * Speed * dt;
                if (_stateTimer <= 0f)
                {
                    var mayObserve = _idleMayObserve;
                    _idleMayObserve = true;
                    if (mayObserve)
                    {
                        PickNextAction(navigationArea);
                    }
                    else
                    {
                        // A natural Idle already owns the complete rest budget.
                        // Do not append Observe and accidentally exceed 65 s.
                        BeginWalk(navigationArea);
                    }
                }
                break;

            case RoamingState.Observe:
                _stateTimer -= dt;
                Speed = Math.Max(0f, Speed - _configuration.Speed.RestDeceleration * dt);
                _turnVelocity = MathEx.Lerp(
                    _turnVelocity,
                    0f,
                    MathEx.ExpLerpFactor(_configuration.Locomotion.IdleTurnDampingResponse, dt));
                Heading = MathEx.SimplifyAngle(Heading + _turnVelocity * dt);
                Position += MathEx.FromAngle(Heading) * Speed * dt;
                if (_stateTimer <= 0f)
                {
                    // Observe may extend a rest once, but always flows into a
                    // forward crawl so autonomous pauses cannot chain forever.
                    BeginWalk(navigationArea);
                }
                break;

            case RoamingState.ForwardCrawl:
            case RoamingState.FastForwardCrawl:
            case RoamingState.CurveCrawl:
            case RoamingState.SCurveCrawl:
            case RoamingState.FastSCurveCrawl:
            case RoamingState.TurnAround:
                UpdateLocomotionState(dt, navigationArea);
                break;

            case RoamingState.MouseChase:
                // MouseChase is consumed by UpdateMouseResponse above. This
                // fallback is intentionally defensive for unavailable input.
                FinishMouseChase(navigationArea);
                break;

            case RoamingState.EdgeTurn:
                _stateTimer -= dt;
                UpdateEdgeTurn(dt, navigationArea);
                break;

            case RoamingState.ReleaseSettle:
                _stateTimer -= dt;
                Speed = 0f;
                DropProgress = 1f - MathEx.Clamp01(
                    _stateTimer / _configuration.Timing.ReleaseSettleDuration);
                if (_stateTimer <= 0f)
                {
                    BeginEscapeSprint(navigationArea);
                }
                break;

            case RoamingState.EscapeSprint:
                UpdateEscapeSprint(dt, navigationArea);
                break;

            case RoamingState.LostGripFall:
                UpdateLostGripFall(dt, navigationArea);
                break;
        }

        Position = navigationArea.Clamp(Position);
    }

}
