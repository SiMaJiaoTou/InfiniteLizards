using System.Numerics;

namespace DesktopLizard.Core;

internal sealed partial class BehaviorController
{
    private void UpdateSpeed(float targetSpeed, float dt)
    {
        UpdateSpeed(
            targetSpeed,
            dt,
            _configuration.Speed.CrawlAcceleration,
            _configuration.Speed.CrawlDeceleration);
    }

    private void UpdateSpeed(float targetSpeed, float dt, float acceleration, float deceleration)
    {
        // One hard ceiling for every locomotion state. Mouse curiosity and
        // future reactive states cannot silently exceed the configured cap.
        targetSpeed = Math.Clamp(targetSpeed, 0f, _configuration.Speed.MaximumCrawl);
        Speed = targetSpeed > Speed
            ? Math.Min(targetSpeed, Speed + acceleration * dt)
            : Math.Max(targetSpeed, Speed - deceleration * dt);
    }

    private float PickWalkSpeed()
    {
        var decisions = _configuration.Decisions;
        var temperament =
            Emotion.Playful * decisions.WalkPacePlayfulWeight +
            Emotion.Curious * decisions.WalkPaceCuriousWeight;
        // Normal roaming stays in the reference crawl band. The doubled cap is
        // intentionally reserved for explicit rare fast states and reactive
        // bursts; using it as the everyday pace would erase the contrast.
        var pace = decisions.WalkPaceBase +
                   temperament * decisions.WalkPaceEmotionFactor +
                   (float)_random.NextDouble() * decisions.WalkPaceRandomFactor;
        return MathEx.Lerp(
            _configuration.Speed.ReferenceMinimumCrawl,
            _configuration.Speed.ReferenceMaximumCrawl,
            MathEx.Clamp01(pace));
    }

    private void UpdateMotionWatchdog(float dt, FloatRect area)
    {
        if (State is RoamingState.ForwardCrawl or
            RoamingState.FastForwardCrawl or
            RoamingState.CurveCrawl or
            RoamingState.SCurveCrawl or
            RoamingState.FastSCurveCrawl or
            RoamingState.TurnAround or
            RoamingState.EdgeTurn)
        {
            _motionWatchdog += dt;
            if (_motionWatchdog >= _configuration.MotionWatchdogInterval)
            {
                var moved = Vector2.Distance(Position, _watchdogPosition);
                if (moved < _configuration.MotionWatchdogMinimumDistance &&
                    Speed > _configuration.MotionWatchdogMinimumSpeed)
                {
                    BeginEdgeTurn(area);
                }
                _motionWatchdog = 0f;
                _watchdogPosition = Position;
            }
        }
        else
        {
            _motionWatchdog = 0f;
            _watchdogPosition = Position;
        }
    }

    private float PickTurnSign(bool preferOpposite)
    {
        if (preferOpposite &&
            MathF.Abs(_turnVelocity) > _configuration.Locomotion.TurnDirectionMemoryThreshold)
        {
            return -MathF.Sign(_turnVelocity);
        }
        return _random.NextDouble() < 0.5 ? -1f : 1f;
    }

    private float PickSafeTurnSign(FloatRect area)
    {
        if (_boundaryNavigator.TryPickSaferTurnSign(area, Position, Heading, out var sign))
        {
            return sign;
        }

        return PickTurnSign(preferOpposite: false);
    }

    private void TransitionTo(RoamingState next, StateTransitionReason reason)
    {
        if (State == next)
        {
            return;
        }

        if (!RoamingStateMachine.IsLegal(State, next, reason))
        {
            throw new InvalidOperationException($"Illegal roaming transition: {State} -> {next} ({reason}).");
        }

        State = next;
        LastTransitionReason = reason;
        TransitionSerial++;
    }

    private float RandomSigned(float magnitude) => ((float)_random.NextDouble() * 2f - 1f) * magnitude;
}
