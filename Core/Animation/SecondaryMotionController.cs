namespace DesktopLizard.Core;

/// <summary>
/// Owns render-only life signals: body bob, breathing, tail motion and blink.
/// It never moves the spine, shoulders or planted feet.
/// </summary>
internal sealed class SecondaryMotionController
{
    private readonly SecondaryMotionConfiguration _configuration;
    private readonly Random _random;
    private float _time;
    private float _idleBlend;
    private float _blinkTimer;
    private float _blinkTime;

    public float BlinkAmount { get; private set; }
    public float BodyBob { get; private set; }
    public float TailSwayOffset { get; private set; }
    public float BodyWidthScale { get; private set; } = 1f;

    public SecondaryMotionController(
        SecondaryMotionConfiguration configuration,
        int individualSeed)
    {
        _configuration = configuration;
        _random = new Random(unchecked(configuration.RandomSeed ^ individualSeed));
        _blinkTimer = configuration.BlinkInitialDelay;
    }

    public void Update(
        float dt,
        float normalizedSpeed,
        LizardPoseMode poseMode,
        EmotionBlend emotion,
        float dropProgress,
        bool walking)
    {
        _time += dt;
        if (poseMode.UsesParticleRig())
        {
            BodyBob = 0f;
            TailSwayOffset = 0f;
            BodyWidthScale = 1f;
            UpdateBlink(dt, emotion);
            return;
        }

        var gaitEnergy = walking ? normalizedSpeed : 0f;
        BodyBob = MathF.Sin(
                      _time * MathEx.Lerp(
                          _configuration.BodyBobMinimumFrequency,
                          _configuration.BodyBobMaximumFrequency,
                          normalizedSpeed)) *
                  (_configuration.BodyBobBaseAmplitude +
                   gaitEnergy * _configuration.BodyBobSpeedAmplitude);
        if (poseMode is LizardPoseMode.ReleaseSettle or LizardPoseMode.Regrip)
        {
            BodyBob += MathF.Sin(MathF.PI * MathEx.Clamp01(dropProgress)) *
                       _configuration.LandingBobAmplitude;
        }

        var calmMotion =
            !walking &&
            poseMode is not (LizardPoseMode.ReleaseSettle or LizardPoseMode.Regrip);
        _idleBlend = MathEx.Lerp(
            _idleBlend,
            calmMotion ? 1f : 0f,
            MathEx.ExpLerpFactor(
                calmMotion
                    ? _configuration.IdleBlendInResponse
                    : _configuration.IdleBlendOutResponse,
                dt));
        var tailAmplitude = poseMode == LizardPoseMode.Observe
            ? _configuration.ObserveTailAmplitude
            : _configuration.IdleTailAmplitude;
        var tailWave = MathF.Sin(_time * _configuration.TailPrimaryFrequency) +
                       MathF.Sin(
                           _time * _configuration.TailSecondaryFrequency +
                           _configuration.TailSecondaryPhase) *
                       _configuration.TailSecondaryWeight;
        TailSwayOffset = _idleBlend * tailAmplitude * tailWave;

        var breath = _idleBlend *
                     MathF.Sin(
                         _time * _configuration.BreathingFrequency +
                         _configuration.BreathingPhase) *
                     _configuration.BreathingAmplitude;
        var landingSquash = poseMode is LizardPoseMode.ReleaseSettle or LizardPoseMode.Regrip
            ? MathF.Sin(MathF.PI * MathEx.Clamp01(dropProgress)) *
              _configuration.LandingSquashAmplitude
            : 0f;
        BodyWidthScale = 1f + breath + landingSquash;

        UpdateBlink(dt, emotion);
    }

    private void UpdateBlink(float dt, EmotionBlend emotion)
    {
        _blinkTimer -= dt;
        if (_blinkTimer <= 0f && _blinkTime <= 0f)
        {
            _blinkTime = _configuration.BlinkDuration;
            _blinkTimer = MathEx.Lerp(
                              _configuration.BlinkMinimumInterval,
                              _configuration.BlinkMaximumInterval,
                              (float)_random.NextDouble()) *
                          MathEx.Lerp(
                              _configuration.BlinkMinimumCalmFactor,
                              _configuration.BlinkMaximumCalmFactor,
                              emotion.Calm);
        }

        if (_blinkTime > 0f)
        {
            _blinkTime = Math.Max(0f, _blinkTime - dt);
            var phase = 1f - _blinkTime / _configuration.BlinkDuration;
            BlinkAmount = MathF.Sin(MathF.PI * phase);
        }
        else
        {
            BlinkAmount = 0f;
        }
    }
}
