using System.Collections.Immutable;
using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// Render-ready diagnostics retained by the compatibility WPF host. The
/// portable gameplay assembly exposes world-model diagnostic seams only and
/// deliberately does not own this display-oriented DTO.
/// </summary>
internal sealed record DebugFrameSnapshot(
    long FrameId,
    IndividualTraits Individual,
    BehaviorDebugSnapshot Behavior,
    LizardDebugSnapshot Lizard,
    Vector2 CanvasCenter,
    ImmutableArray<Vector2> TrailPoints,
    Vector2 TargetPoint,
    Vector2 LookTargetPoint,
    Vector2 MousePoint,
    bool PointerAvailable,
    bool PointerBlocked,
    float PointerDistance,
    float FramesPerSecond,
    float DpiScale);
