using System.Collections.Immutable;
using System.Numerics;
using System.Windows.Media;
using DesktopLizard.Core;

namespace DesktopLizard.Rendering;

/// <summary>
/// Builds the vector geometry shared by painting and hit testing. Keeping the
/// silhouette construction here prevents the WPF view from becoming a second
/// owner of lizard pose rules.
/// </summary>
internal static class LizardGeometryBuilder
{
    public static StreamGeometry BuildBody(in LizardRenderFrame frame) =>
        BuildClosedSpline(frame.BodyOutline);

    public static StreamGeometry BuildLegs(in LizardRenderFrame frame)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            foreach (var leg in frame.Legs)
            {
                context.BeginFigure(ToPoint(leg.Shoulder), false, false);
                context.BezierTo(
                    ToPoint(leg.Elbow),
                    ToPoint(leg.Elbow),
                    ToPoint(leg.Foot),
                    true,
                    false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    public static Geometry BuildHitGeometry(
        in LizardRenderFrame frame,
        Geometry body,
        Geometry legs,
        Pen hitLimbPen,
        float footRadius,
        float eyeRadius)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(body);
        group.Children.Add(legs.GetWidenedPathGeometry(hitLimbPen));

        foreach (var leg in frame.Legs)
        {
            group.Children.Add(new EllipseGeometry(ToPoint(leg.Foot), footRadius, footRadius));
        }

        group.Children.Add(new EllipseGeometry(
            ToPoint(frame.NegativeEyeCenter),
            eyeRadius,
            eyeRadius));
        group.Children.Add(new EllipseGeometry(
            ToPoint(frame.PositiveEyeCenter),
            eyeRadius,
            eyeRadius));

        group.Freeze();
        return group;
    }

    private static StreamGeometry BuildClosedSpline(ImmutableArray<Vector2> points)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(points[0]), true, true);
            for (var index = 0; index < points.Length; index++)
            {
                var p0 = points[(index - 1 + points.Length) % points.Length];
                var p1 = points[index];
                var p2 = points[(index + 1) % points.Length];
                var p3 = points[(index + 2) % points.Length];
                var control1 = p1 + (p2 - p0) / 6f;
                var control2 = p2 - (p3 - p1) / 6f;
                context.BezierTo(ToPoint(control1), ToPoint(control2), ToPoint(p2), true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static System.Windows.Point ToPoint(Vector2 point) => new(point.X, point.Y);
}
