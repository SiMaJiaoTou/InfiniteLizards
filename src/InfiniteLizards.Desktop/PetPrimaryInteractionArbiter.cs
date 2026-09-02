using System.Numerics;

namespace InfiniteLizards.Desktop;

internal enum PetPrimaryInteractionState
{
    Idle,
    PendingClick,
    Dragging
}

internal readonly record struct PetPrimaryInteractionDecision(
    bool BeginDrag,
    bool RaiseClick,
    bool EndDrag);

/// <summary>
/// Pure click/drag gesture state. A primary press remains observational until
/// it exceeds the DIP drag threshold, so clicking a pet can open its details
/// without briefly mutating gameplay into the grabbed state.
/// </summary>
internal sealed class PetPrimaryInteractionArbiter
{
    public const float DefaultDragThreshold = 4f;

    private Vector2 _pressPosition;

    public PetPrimaryInteractionState State { get; private set; }

    public bool Begin(Vector2 position)
    {
        if (State != PetPrimaryInteractionState.Idle || !IsFinite(position))
        {
            return false;
        }

        _pressPosition = position;
        State = PetPrimaryInteractionState.PendingClick;
        return true;
    }

    public PetPrimaryInteractionDecision Move(
        Vector2 position,
        float dragThreshold = DefaultDragThreshold)
    {
        if (State != PetPrimaryInteractionState.PendingClick ||
            !IsFinite(position) ||
            !float.IsFinite(dragThreshold) ||
            dragThreshold <= 0f)
        {
            return default;
        }

        var delta = position - _pressPosition;
        if (delta.LengthSquared() < dragThreshold * dragThreshold)
        {
            return default;
        }

        State = PetPrimaryInteractionState.Dragging;
        return new PetPrimaryInteractionDecision(
            BeginDrag: true,
            RaiseClick: false,
            EndDrag: false);
    }

    public PetPrimaryInteractionDecision Release()
    {
        var decision = State switch
        {
            PetPrimaryInteractionState.PendingClick =>
                new PetPrimaryInteractionDecision(false, true, false),
            PetPrimaryInteractionState.Dragging =>
                new PetPrimaryInteractionDecision(false, false, true),
            _ => default
        };
        State = PetPrimaryInteractionState.Idle;
        return decision;
    }

    public PetPrimaryInteractionDecision Cancel()
    {
        var decision = State == PetPrimaryInteractionState.Dragging
            ? new PetPrimaryInteractionDecision(false, false, true)
            : default;
        State = PetPrimaryInteractionState.Idle;
        return decision;
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);
}
