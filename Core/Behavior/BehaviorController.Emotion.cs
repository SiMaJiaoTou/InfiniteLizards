namespace DesktopLizard.Core;

internal sealed partial class BehaviorController
{
    private void UpdateEmotion(float dt)
    {
        var emotion = _configuration.Emotion;
        _emotionTimer -= dt;
        if (_emotionTimer <= 0f)
        {
            _emotionTarget = EmotionBlend.Normalize(new EmotionBlend(
                emotion.CalmTargetMinimum + (float)_random.NextDouble() * emotion.CalmTargetRange,
                emotion.CuriousTargetMinimum + (float)_random.NextDouble() * emotion.CuriousTargetRange,
                emotion.PlayfulTargetMinimum + (float)_random.NextDouble() * emotion.PlayfulTargetRange,
                emotion.WaryTargetMinimum + (float)_random.NextDouble() * emotion.WaryTargetRange));
            _emotionTimer = MathEx.Lerp(
                emotion.MinimumRetargetDuration,
                emotion.MaximumRetargetDuration,
                (float)_random.NextDouble());
        }

        Emotion = EmotionBlend.Lerp(
            Emotion,
            _emotionTarget,
            MathEx.ExpLerpFactor(emotion.BlendResponse, dt));
    }
}
