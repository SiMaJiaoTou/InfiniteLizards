using System.Collections.Immutable;
using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// A render-ready, immutable debug frame. Application code converts world
/// coordinates to model-canvas coordinates before either renderer consumes it.
/// Keeping this DTO in Core preserves the one-way Core -> Rendering dependency.
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
