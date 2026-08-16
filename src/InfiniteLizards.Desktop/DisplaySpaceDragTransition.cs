using System.Numerics;
using DesktopPet.Engine;

namespace InfiniteLizards.Desktop;

/// <summary>
/// Applies the coordinate-frame part of a cross-display drag before applying
/// the user's physical drag movement. Keeping this ordering in one command
/// prevents a mixed-DPI frame change from entering gameplay as velocity.
/// </summary>
internal static class DisplaySpaceDragTransition
{
    public static Vector2 Apply<TSnapshot>(
        DesktopPetRuntime<TSnapshot> runtime,
        ActiveDisplaySpace displaySpace,
        in DisplaySpaceTransition transition,
        in WorldPoint desiredBefore,
        in DevicePoint pointerDevice)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(displaySpace);
        if (!transition.Changed)
        {
            throw new ArgumentException(
                "A drag display transition must change the active display.",
                nameof(transition));
        }
        if (!string.Equals(
                transition.Current.Id,
                displaySpace.Display.Id,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The transition target must be the active display.",
                nameof(transition));
        }

        var desiredBeforeVector = ToFiniteVector(desiredBefore, nameof(desiredBefore));
        var rebasedDesired = ToFiniteVector(
            transition.RebasedWorldPoint,
            nameof(transition));
        var pointerWorld = ToFiniteVector(
            displaySpace.DeviceToWorld(pointerDevice),
            nameof(pointerDevice));
        var rebaseDelta = rebasedDesired - desiredBeforeVector;
        EnsureFinite(rebaseDelta, nameof(transition));

        // Translate every persistent absolute gameplay value first. DragTo then
        // contributes only the actual user movement that was already measured
        // in the previous display frame; applying DragTo first would either
        // double-move the center or inject the DPI-frame jump into physics.
        runtime.RebaseWorldPosition(rebaseDelta);
        runtime.DragTo(rebasedDesired);

        var dragOffset = pointerWorld - runtime.Position;
        EnsureFinite(dragOffset, nameof(pointerDevice));
        return dragOffset;
    }

    private static Vector2 ToFiniteVector(in WorldPoint point, string name)
    {
        var result = new Vector2((float)point.X, (float)point.Y);
        EnsureFinite(result, name);
        return result;
    }

    private static void EnsureFinite(Vector2 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(
                name,
                "The display-space drag coordinate must remain finite.");
        }
    }
}
