using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// Owns the deterministic diagonal-pair scheduler while the surrounding
/// lizard model remains responsible for spine pose and stance geometry.
/// </summary>
internal sealed class DiagonalGaitController
{
    private readonly LegRig[] _legs;
    private readonly GaitConfiguration _configuration;
    private readonly SpeedConfiguration _speed;
    private readonly float _visualScale;
    private readonly Func<LegRig, float, Vector2> _getFootHome;
    private readonly Func<LegRig, float> _getBodyAngle;
    private int _nextPair;
    private float _supportTimer;
    private float _allFeetDownAge;
    private float _stepWaitTimer;
    private readonly float[] _pairErrorMemory = new float[2];
    private float _lastGaitDisplaySpeed;

    internal int NextPair => _nextPair;
    internal float SupportTimeRemaining => _supportTimer;
    internal float AllFeetDownAge => _allFeetDownAge;
    internal float StepWaitTime => _stepWaitTimer;
    internal float PairError0 => _pairErrorMemory[0];
    internal float PairError1 => _pairErrorMemory[1];
    internal float LastGaitDisplaySpeed => _lastGaitDisplaySpeed;

    internal DiagonalGaitController(
        LegRig[] legs,
        GaitConfiguration configuration,
        SpeedConfiguration speed,
        float visualScale,
        Func<LegRig, float, Vector2> getFootHome,
        Func<LegRig, float> getBodyAngle)
    {
        _legs = legs;
        _configuration = configuration;
        _speed = speed;
        _visualScale = visualScale;
        _getFootHome = getFootHome;
        _getBodyAngle = getBodyAngle;
    }

    internal void ResetTracking()
    {
        Array.Clear(_pairErrorMemory);
        _lastGaitDisplaySpeed = 0f;
        _allFeetDownAge = 0f;
    }

    internal void ResetSupportState()
    {
        _supportTimer = 0f;
        _stepWaitTimer = 0f;
        _allFeetDownAge = 0f;
    }

    internal void Update(
        float dt,
        float normalizedSpeed,
        bool walking,
        bool dropping,
        bool fastSCurve,
        float locomotionSpeed,
        float signedTurnRate)
    {
        foreach (var leg in _legs)
        {
            leg.UpdateStep(dt);
        }

        var displaySpeed = locomotionSpeed * _visualScale;
        if (walking)
        {
            // Match the controller's asymmetric transition rates: build the
            // rare 96.2 px/s burst gradually, then let its visual cadence fall
            // as quickly as the body returns to ordinary crawl. Keeping 140 in
            // both directions left a short burst of obsolete rapid footsteps.
            var trackingRate = displaySpeed > _lastGaitDisplaySpeed
                ? _speed.FastCrawlAcceleration
                : _speed.CrawlDeceleration;
            var maximumSpeedChange = trackingRate * dt;
            _lastGaitDisplaySpeed = displaySpeed > _lastGaitDisplaySpeed
                ? Math.Min(displaySpeed, _lastGaitDisplaySpeed + maximumSpeedChange)
                : Math.Max(displaySpeed, _lastGaitDisplaySpeed - maximumSpeedChange);
            displaySpeed = _lastGaitDisplaySpeed;
        }
        else
        {
            _lastGaitDisplaySpeed = 0f;
        }
        var speedT = MathEx.Clamp01(
            (displaySpeed - _speed.ReferenceMinimumCrawl) /
            (_speed.MaximumCrawl - _speed.ReferenceMinimumCrawl));
        var gaitT = speedT * speedT * (3f - 2f * speedT);

        // Remember persistent footprint error while forgetting one-frame body
        // wiggles. The reference replaces a footprint only after the body has
        // genuinely advanced; a single shoulder twitch must not fire a step.
        Span<float> minimumPairError = stackalloc float[2] { float.MaxValue, float.MaxValue };
        Span<float> maximumPairError = stackalloc float[2];
        foreach (var leg in _legs)
        {
            var error = Vector2.Distance(leg.Foot, _getFootHome(leg, 0f));
            minimumPairError[leg.Pair] = Math.Min(minimumPairError[leg.Pair], error);
            maximumPairError[leg.Pair] = Math.Max(maximumPairError[leg.Pair], error);
        }
        var memoryResponse = 1f / MathEx.Lerp(
            _configuration.SlowPairErrorMemoryDuration,
            _configuration.FastPairErrorMemoryDuration,
            gaitT);
        var memoryBlend = MathEx.ExpLerpFactor(memoryResponse, dt);
        for (var pair = 0; pair < 2; pair++)
        {
            var persistentError =
                maximumPairError[pair] * _configuration.MaximumPairErrorWeight +
                minimumPairError[pair] * _configuration.MinimumPairErrorWeight;
            _pairErrorMemory[pair] = MathEx.Lerp(_pairErrorMemory[pair], persistentError, memoryBlend);
        }

        // The reference uses a diagonal alternating gait. Both paws in one
        // diagonal pair lift together, then all four paws remain planted for a
        // visible support phase before the opposite pair may start.
        if (_legs.Any(leg => leg.IsStepping))
        {
            _allFeetDownAge = 0f;
            _stepWaitTimer = 0f;
            // The opposite diagonal remains a planted support pair throughout
            // the swing. Keep its finite two-link rig safe without disturbing
            // the two paws currently following P(t).
            foreach (var leg in _legs)
            {
                if (!leg.IsStepping)
                {
                    leg.ConstrainToReach();
                }
            }
            return;
        }

        _allFeetDownAge += dt;
        _supportTimer = Math.Max(0f, _supportTimer - dt);
        var pairLegs = _legs.Where(leg => leg.Pair == _nextPair).ToArray();
        var reachUrgency = pairLegs.Max(leg => Vector2.Distance(leg.Shoulder, leg.Foot) / leg.MaximumReach);

        if (!walking && !dropping)
        {
            _stepWaitTimer = 0f;
            if (_supportTimer > 0f)
            {
                return;
            }

            var settleError = _legs
                .Where(leg => leg.Pair == _nextPair)
                .Max(leg => Vector2.Distance(leg.Foot, _getFootHome(leg, 0f)));
            if (settleError > _configuration.IdleSettleError)
            {
                BeginDiagonalStep(_nextPair, _configuration.IdleStepDuration, 0f, 0f, 0f);
                _nextPair = 1 - _nextPair;
                _supportTimer = _configuration.IdleSupportDuration;
            }
            return;
        }

        var minimumSupport = MathEx.Lerp(
            _configuration.SlowMinimumSupportDuration,
            _configuration.FastMinimumSupportDuration,
            gaitT);
        if (_supportTimer > 0f && !dropping &&
            (_allFeetDownAge < minimumSupport || reachUrgency < _configuration.HardReachRatio))
        {
            _stepWaitTimer = 0f;
            return;
        }

        _stepWaitTimer += dt;

        var pairError = _pairErrorMemory[_nextPair];
        var threshold = dropping
            ? _configuration.DropStepError
            : MathEx.Lerp(_configuration.SlowStepError, _configuration.FastStepError, gaitT);
        // The reference has a clearly readable all-feet-down beat between
        // diagonal swings. Keep the gait spatially driven, with this deadline
        // only preventing a stalled animation when the body barely moves.
        // The source demo is deliberately sparse: after one diagonal pair
        // lands, the footprint remains locked for roughly half a second at
        // reference crawl speed. The hard deadline remains distance-aware so
        // reactive sprint states can still protect the short two-link legs.
        var maximumWait = MathEx.Lerp(
            _configuration.SlowMaximumStepWait,
            _configuration.FastMaximumStepWait,
            gaitT);
        if (!dropping &&
            pairError < threshold &&
            reachUrgency < _configuration.SoftReachRatio &&
            _stepWaitTimer < maximumWait)
        {
            return;
        }

        var tightTurnCadence = MathEx.Clamp01(
            (MathF.Abs(signedTurnRate) - _configuration.TightTurnCadenceStart) /
            _configuration.TightTurnCadenceRange);
        var duration = dropping
            ? _configuration.DropStepDuration
            : MathEx.Lerp(_configuration.SlowStepDuration, _configuration.FastStepDuration, gaitT) *
              MathEx.Lerp(1f, _configuration.TightTurnDurationFactor, tightTurnCadence);
        if (fastSCurve && !dropping)
        {
            // Finish the airborne diagonal before the accelerating body's
            // reversal can exhaust the opposing support pair. At the ceiling
            // this is still about 7--8 fixed 120 Hz steps, so the swing stays
            // readable instead of returning to the old high-frequency twitch.
            duration *= _configuration.FastSCurveDurationFactor;
        }
        // Freeze the landing well ahead of its shoulder/hip. The rear legs in
        // particular need this forward reserve to remain planted through the
        // long all-feet-down beat instead of being pulled onto the reach limit.
        // These values also reproduce the source landing zones: front paws
        // about 25--50 model px ahead, rear paws about 10--15 model px ahead.
        var lead = walking
            ? MathEx.Lerp(_configuration.MinimumLead, _configuration.MaximumLead, gaitT) *
              MathEx.Lerp(1f, _configuration.TightTurnLeadFactor, tightTurnCadence)
            : 0f;
        // The two paws belong to one diagonal event, but a tiny front-paw
        // delay preserves the organic 0--1 source-frame offset visible in the
        // demo instead of making the pair look mechanically welded together.
        var pairStagger = dropping
            ? 0f
            : MathEx.Lerp(_configuration.PairStaggerMaximum, _configuration.PairStaggerMinimum, gaitT) *
              MathEx.Lerp(1f, _configuration.TightTurnStaggerFactor, tightTurnCadence);
        BeginDiagonalStep(_nextPair, duration, lead, signedTurnRate, pairStagger);
        _pairErrorMemory[_nextPair] = 0f;
        _nextPair = 1 - _nextPair;
        // At the normal 22--26 px/s crawl this yields the source clip's
        // 0.62--0.72 s diagonal-pair interval and a roughly 33 px same-foot
        // world stride. Reactive high-speed states shorten the interval while
        // preserving the same spatial landing scale.
        var targetPairGap = displaySpeed > 1f
            ? Math.Clamp(
                _configuration.ReferencePairSpacing / displaySpeed,
                _configuration.MinimumPairGap,
                _configuration.MaximumPairGap)
            : _configuration.MaximumPairGap;
        // A full U-turn rotates the rear shoulders much faster than a normal
        // curve. Shorten only that extreme-turn support window so the planted
        // pair stays physically reachable; ordinary reference crawling keeps
        // the measured 0.62--0.72 s rhythm above.
        targetPairGap *= MathEx.Lerp(1f, _configuration.TightTurnPairGapFactor, tightTurnCadence);
        _supportTimer = dropping
            ? _configuration.DropSupportDuration
            : Math.Max(
                MathEx.Lerp(
                    _configuration.NormalSupportFloor,
                    _configuration.TightTurnSupportFloor,
                    tightTurnCadence),
                targetPairGap - duration - pairStagger);
        _stepWaitTimer = 0f;
    }

    private void BeginDiagonalStep(
        int pair,
        float duration,
        float lead,
        float signedTurnRate,
        float pairStagger)
    {
        foreach (var leg in _legs.Where(leg => leg.Pair == pair))
        {
            var localForward = MathEx.FromAngle(_getBodyAngle(leg));
            var localNormal = MathEx.Perpendicular(localForward);
            // On a turn, the inner paw renews a nearby footprint while the
            // outer paw reaches forward. The reference shows roughly a 20 px
            // inner step beside 50+ px outer steps; this bounded bias captures
            // the asymmetry without changing the stance or leg lengths.
            var turnSide = Math.Clamp(signedTurnRate / _configuration.TurnBiasRate, -1f, 1f);
            var innerAmount = Math.Max(0f, turnSide * leg.Side);
            var outerAmount = Math.Max(0f, -turnSide * leg.Side);
            // The clean reference turn uses the inside rear paw as a nearby
            // pivot (about 20 px) while the inside front paw still reaches
            // forward almost as far as the two outside paws (about 49--57 px).
            // A front/rear-specific bias captures that pattern; uniformly
            // shrinking both inside paws produces an unnatural skid-steer gait.
            var innerReduction = leg.IsFront
                ? _configuration.FrontInnerLeadReduction
                : _configuration.RearInnerLeadReduction;
            var outerBoost = leg.IsFront
                ? _configuration.FrontOuterLeadBoost
                : _configuration.RearOuterLeadBoost;
            var legLead = Math.Clamp(
                lead * (1f - innerAmount * innerReduction + outerAmount * outerBoost),
                0f,
                _configuration.MaximumLead);
            var delay = leg.IsFront ? pairStagger : 0f;
            var destination = _getFootHome(leg, legLead);
            var tightTurnBlend = MathEx.Clamp01(
                (MathF.Abs(signedTurnRate) - _configuration.TightTurnLiftStart) /
                _configuration.TightTurnLiftRange);
            var heightScale = MathEx.Lerp(1f, _configuration.TightTurnLiftHeightFactor, tightTurnBlend);
            leg.StartStep(destination, localNormal, duration, delay, heightScale);
        }
    }
}
