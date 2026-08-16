using System.Numerics;
using Avalonia;
using Avalonia.Controls;

namespace InfiniteLizards.Desktop;

/// <summary>
/// Desktop-side rendering and hit-testing port consumed by the generic host.
/// Gameplay snapshots remain opaque to the window and are interpreted only by
/// the concrete presenter selected in the composition root.
/// </summary>
internal interface IDesktopPetPresenter<TSnapshot>
{
    Control View { get; }

    void Present(in TSnapshot snapshot, Vector2 lookDirection);
    void SetSpawnOpacity(float opacity);
    bool HitTest(Point viewPoint);
    Vector2 ViewToModel(Point viewPoint);
}

/// <summary>
/// Optional presenter capability for querying the geometry of the pose that
/// the compositor has actually committed.  The generic host uses this instead
/// of mixing a committed native center with newer retained-view geometry.
/// </summary>
internal interface IDesktopPetPresentationHitTester
{
    bool HitTestPresentation(long presentationVersion, Point viewPoint);

    void CommitPresentation(long presentationVersion);

    void DiscardUncommittedPresentations(long committedPresentationVersion);
}
