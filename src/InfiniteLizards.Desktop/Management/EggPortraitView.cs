using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using InfiniteLizards.Gameplay.Phenotypes;

namespace InfiniteLizards.Desktop.Management;

/// <summary>
/// Square, deterministic egg portrait. Shell hue and pattern come from the
/// offspring genome; incubation progress only adds light and bounded cracks.
/// </summary>
internal sealed class EggPortraitView : Control
{
    internal const int MaximumFineSpeckles = 18;
    internal const int MaximumLargeBlotches = 8;
    internal const int MaximumRings = 4;
    internal const int MaximumPatternCrackSegments = 8;
    internal const int MaximumStars = 8;
    internal const int MaximumHatchCrackSegments = 12;

    public static readonly StyledProperty<EggPortraitModel> ModelProperty =
        AvaloniaProperty.Register<EggPortraitView, EggPortraitModel>(
            nameof(Model),
            EggPortraitModel.Default);

    public EggPortraitModel Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    static EggPortraitView()
    {
        AffectsRender<EggPortraitView>(ModelProperty);
    }

    public EggPortraitView()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        Focusable = false;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(availableSize.Width, availableSize.Height);
        if (!double.IsFinite(side) || side <= 0d)
        {
            side = 104d;
        }
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (!double.IsFinite(side) || side <= 0d)
        {
            return;
        }

        var frame = new Rect(
            (Bounds.Width - side) * 0.5d,
            (Bounds.Height - side) * 0.5d,
            side,
            side);
        DrawBackdrop(context, frame);
        var portraitFrame = frame.Deflate(side * 0.07d);
        DrawNest(context, portraitFrame);
        DrawEgg(context, portraitFrame, Model);
    }

    private static void DrawBackdrop(DrawingContext context, Rect frame)
    {
        var side = frame.Width;
        var radius = side * 0.105d;
        var shadow = new Rect(
            frame.X + side * 0.012d,
            frame.Y + side * 0.024d,
            frame.Width - side * 0.024d,
            frame.Height - side * 0.028d);
        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(38, 104, 70, 58)),
            null,
            new RoundedRect(shadow, radius));

        var outer = frame.Deflate(side * 0.012d);
        context.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(255, 250, 243)),
            new Pen(
                new SolidColorBrush(Color.FromRgb(133, 96, 82)),
                Math.Max(1.1d, side * 0.007d)),
            new RoundedRect(outer, radius));

        var inner = outer.Deflate(side * 0.038d);
        context.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(255, 253, 248)),
            new Pen(
                new SolidColorBrush(Color.FromRgb(222, 205, 196)),
                Math.Max(1d, side * 0.005d)),
            new RoundedRect(inner, side * 0.075d));
        context.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(240, 235, 251)),
            null,
            new Point(
                inner.X + inner.Width * 0.29d,
                inner.Y + inner.Height * 0.27d),
            inner.Width * 0.22d,
            inner.Height * 0.20d);
        context.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(226, 244, 235)),
            null,
            new Point(
                inner.X + inner.Width * 0.74d,
                inner.Y + inner.Height * 0.68d),
            inner.Width * 0.20d,
            inner.Height * 0.18d);
        context.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(255, 227, 214)),
            null,
            new Point(
                inner.X + inner.Width * 0.82d,
                inner.Y + inner.Height * 0.23d),
            inner.Width * 0.105d,
            inner.Height * 0.095d);
        DrawSparkle(
            context,
            new Point(
                inner.X + inner.Width * 0.14d,
                inner.Y + inner.Height * 0.17d),
            side * 0.019d);
    }

    private static void DrawNest(DrawingContext context, Rect frame)
    {
        var center = new Point(
            frame.Center.X,
            frame.Y + frame.Height * 0.79d);
        context.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(38, 84, 104, 76)),
            null,
            new Point(center.X, center.Y + frame.Height * 0.035d),
            frame.Width * 0.30d,
            frame.Height * 0.065d);

        var leafFill = new SolidColorBrush(Color.FromRgb(132, 190, 128));
        var leafLight = new SolidColorBrush(Color.FromRgb(180, 220, 156));
        var leafLine = new Pen(
            new SolidColorBrush(Color.FromRgb(85, 119, 72)),
            Math.Max(1d, frame.Width * 0.009d),
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);
        DrawLeaf(
            context,
            new Point(center.X - frame.Width * 0.17d, center.Y),
            new Point(center.X - frame.Width * 0.34d, center.Y - frame.Height * 0.12d),
            frame.Width * 0.075d,
            leafFill,
            leafLine);
        DrawLeaf(
            context,
            new Point(center.X + frame.Width * 0.17d, center.Y),
            new Point(center.X + frame.Width * 0.34d, center.Y - frame.Height * 0.11d),
            frame.Width * 0.072d,
            leafLight,
            leafLine);

        context.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(233, 186, 116)),
            new Pen(
                new SolidColorBrush(Color.FromRgb(142, 100, 65)),
                Math.Max(1d, frame.Width * 0.008d)),
            center,
            frame.Width * 0.23d,
            frame.Height * 0.055d);
        context.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(255, 221, 158)),
            null,
            new Point(center.X, center.Y - frame.Height * 0.012d),
            frame.Width * 0.18d,
            frame.Height * 0.027d);
    }

    private static void DrawLeaf(
        DrawingContext context,
        Point start,
        Point tip,
        double halfWidth,
        IBrush fill,
        Pen outline)
    {
        var dx = tip.X - start.X;
        var dy = tip.Y - start.Y;
        var length = Math.Max(1d, Math.Sqrt(dx * dx + dy * dy));
        var normal = new Point(-dy / length * halfWidth, dx / length * halfWidth);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(start, true);
            path.CubicBezierTo(
                new Point(start.X + dx * 0.30d + normal.X, start.Y + dy * 0.30d + normal.Y),
                new Point(tip.X - dx * 0.28d + normal.X, tip.Y - dy * 0.28d + normal.Y),
                tip);
            path.CubicBezierTo(
                new Point(tip.X - dx * 0.28d - normal.X, tip.Y - dy * 0.28d - normal.Y),
                new Point(start.X + dx * 0.30d - normal.X, start.Y + dy * 0.30d - normal.Y),
                start);
            path.EndFigure(true);
        }
        context.DrawGeometry(fill, outline, geometry);
        context.DrawLine(
            new Pen(outline.Brush, Math.Max(0.8d, outline.Thickness * 0.62d)),
            start,
            tip);
    }

    private static void DrawSparkle(
        DrawingContext context,
        Point center,
        double radius)
    {
        var pen = new Pen(
            new SolidColorBrush(Color.FromArgb(184, 202, 145, 76)),
            Math.Max(0.8d, radius * 0.20d),
            lineCap: PenLineCap.Round);
        context.DrawLine(
            pen,
            new Point(center.X - radius, center.Y),
            new Point(center.X + radius, center.Y));
        context.DrawLine(
            pen,
            new Point(center.X, center.Y - radius),
            new Point(center.X, center.Y + radius));
    }

    private static void DrawEgg(
        DrawingContext context,
        Rect frame,
        EggPortraitModel model)
    {
        var progress = double.IsFinite(model.Progress)
            ? Math.Clamp(model.Progress, 0d, 1d)
            : 0d;
        var hue = double.IsFinite(model.Appearance.HueDegrees)
            ? model.Appearance.HueDegrees
            : 112d;
        var shellColor = FromHsl(hue, 0.48d, 0.68d);
        var highlightColor = FromHsl(hue + 18d, 0.34d, 0.86d);
        var shadowColor = FromHsl(hue - 12d, 0.54d, 0.29d);
        var accentColor = FromHsl(hue + 155d, 0.62d, 0.46d);
        var center = new Point(frame.Center.X, frame.Center.Y + frame.Height * 0.025d);
        var radiusX = frame.Width * 0.285d;
        var radiusY = frame.Height * 0.385d;

        using (context.PushOpacity(0.10d + progress * 0.28d))
        {
            context.DrawEllipse(
                new SolidColorBrush(highlightColor),
                null,
                center,
                radiusX * (1.14d + progress * 0.08d),
                radiusY * (1.08d + progress * 0.06d));
        }

        var outline = new Pen(
            new SolidColorBrush(Color.FromArgb(226, 83, 57, 49)),
            Math.Max(1.3d, frame.Width * 0.014d),
            lineJoin: PenLineJoin.Round);
        context.DrawEllipse(new SolidColorBrush(shellColor), outline, center, radiusX, radiusY);
        context.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(
                74,
                highlightColor.R,
                highlightColor.G,
                highlightColor.B)),
            null,
            new Point(center.X - radiusX * 0.25d, center.Y - radiusY * 0.22d),
            radiusX * 0.46d,
            radiusY * 0.58d);

        DrawShellPattern(
            context,
            center,
            radiusX,
            radiusY,
            model.Appearance.Pattern,
            new SolidColorBrush(shadowColor),
            new SolidColorBrush(accentColor));
        DrawHatchCracks(context, center, radiusX, radiusY, progress, shadowColor, highlightColor);
        context.DrawEllipse(null, outline, center, radiusX, radiusY);
    }

    private static void DrawShellPattern(
        DrawingContext context,
        Point center,
        double radiusX,
        double radiusY,
        BreedableEggPatternKind pattern,
        IBrush darkBrush,
        IBrush accentBrush)
    {
        using var opacity = context.PushOpacity(0.58d);
        switch (pattern)
        {
            case BreedableEggPatternKind.FineSpeckles:
                for (var index = 0; index < MaximumFineSpeckles; index++)
                {
                    var point = EggPoint(center, radiusX, radiusY, index, MaximumFineSpeckles, 0.70d);
                    var size = radiusX * (0.022d + (index % 3) * 0.007d);
                    context.DrawEllipse(darkBrush, null, point, size, size);
                }
                break;
            case BreedableEggPatternKind.LargeBlotches:
                for (var index = 0; index < MaximumLargeBlotches; index++)
                {
                    var point = EggPoint(center, radiusX, radiusY, index, MaximumLargeBlotches, 0.60d);
                    var size = radiusX * (0.10d + (index % 3) * 0.035d);
                    context.DrawEllipse(
                        index % 2 == 0 ? darkBrush : accentBrush,
                        null,
                        point,
                        size,
                        size * 0.72d);
                }
                break;
            case BreedableEggPatternKind.Rings:
                for (var index = 0; index < MaximumRings; index++)
                {
                    var y = center.Y - radiusY * 0.48d +
                        index * radiusY * 0.32d;
                    var yRatio = (y - center.Y) / radiusY;
                    var width = radiusX *
                        Math.Sqrt(Math.Max(0d, 1d - yRatio * yRatio)) * 0.82d;
                    context.DrawEllipse(
                        null,
                        new Pen(
                            index % 2 == 0 ? darkBrush : accentBrush,
                            Math.Max(1d, radiusX * 0.045d)),
                        new Point(center.X, y),
                        width,
                        radiusY * 0.075d);
                }
                break;
            case BreedableEggPatternKind.Cracks:
                DrawPatternCracks(context, center, radiusX, radiusY, darkBrush);
                break;
            case BreedableEggPatternKind.Gradient:
                for (var index = 0; index < 4; index++)
                {
                    using var bandOpacity = context.PushOpacity(0.13d + index * 0.10d);
                    context.DrawEllipse(
                        accentBrush,
                        null,
                        new Point(center.X, center.Y + radiusY * (0.30d + index * 0.08d)),
                        radiusX * (0.82d - index * 0.10d),
                        radiusY * (0.35d - index * 0.045d));
                }
                break;
            case BreedableEggPatternKind.Stars:
                for (var index = 0; index < MaximumStars; index++)
                {
                    DrawStar(
                        context,
                        EggPoint(center, radiusX, radiusY, index, MaximumStars, 0.62d),
                        radiusX * (0.07d + (index % 2) * 0.025d),
                        index % 2 == 0 ? accentBrush : darkBrush);
                }
                break;
        }
    }

    private static void DrawPatternCracks(
        DrawingContext context,
        Point center,
        double radiusX,
        double radiusY,
        IBrush brush)
    {
        var pen = new Pen(
            brush,
            Math.Max(1d, radiusX * 0.035d),
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);
        var previous = new Point(center.X - radiusX * 0.42d, center.Y - radiusY * 0.34d);
        for (var index = 0; index < MaximumPatternCrackSegments; index++)
        {
            var t = (index + 1d) / MaximumPatternCrackSegments;
            var next = new Point(
                center.X - radiusX * 0.42d + radiusX * 0.82d * t,
                center.Y - radiusY * 0.34d + radiusY * 0.70d * t +
                (index % 2 == 0 ? -1d : 1d) * radiusY * 0.08d);
            context.DrawLine(pen, previous, next);
            previous = next;
        }
    }

    private static void DrawHatchCracks(
        DrawingContext context,
        Point center,
        double radiusX,
        double radiusY,
        double progress,
        Color shadowColor,
        Color highlightColor)
    {
        var count = Math.Clamp(
            (int)Math.Floor((progress - 0.28d) / 0.72d * MaximumHatchCrackSegments),
            0,
            MaximumHatchCrackSegments);
        if (count == 0)
        {
            return;
        }

        var shadowPen = new Pen(
            new SolidColorBrush(Color.FromArgb(210, shadowColor.R, shadowColor.G, shadowColor.B)),
            Math.Max(1.2d, radiusX * 0.035d),
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);
        var lightPen = new Pen(
            new SolidColorBrush(Color.FromArgb(145, highlightColor.R, highlightColor.G, highlightColor.B)),
            Math.Max(0.7d, radiusX * 0.014d),
            lineCap: PenLineCap.Round);
        var previous = new Point(center.X, center.Y - radiusY * 0.88d);
        for (var index = 0; index < count; index++)
        {
            var t = (index + 1d) / MaximumHatchCrackSegments;
            var direction = index % 2 == 0 ? -1d : 1d;
            var next = new Point(
                center.X + direction * radiusX * (0.10d + 0.23d * t),
                center.Y - radiusY * 0.88d + radiusY * 1.15d * t);
            context.DrawLine(shadowPen, previous, next);
            context.DrawLine(lightPen, previous, new Point(next.X + 1d, next.Y));
            previous = next;
        }
    }

    private static Point EggPoint(
        Point center,
        double radiusX,
        double radiusY,
        int index,
        int count,
        double maximumRadius)
    {
        var phase = index * 2.399963229728653d + 0.4d;
        var radial = Math.Sqrt((index + 0.7d) / (count + 0.7d)) * maximumRadius;
        return new Point(
            center.X + Math.Cos(phase) * radiusX * radial,
            center.Y + Math.Sin(phase) * radiusY * radial);
    }

    private static void DrawStar(
        DrawingContext context,
        Point center,
        double outerRadius,
        IBrush brush)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            for (var index = 0; index < 10; index++)
            {
                var angle = -Math.PI * 0.5d + index * Math.PI / 5d;
                var radius = index % 2 == 0 ? outerRadius : outerRadius * 0.42d;
                var point = new Point(
                    center.X + Math.Cos(angle) * radius,
                    center.Y + Math.Sin(angle) * radius);
                if (index == 0) path.BeginFigure(point, true);
                else path.LineTo(point);
            }
            path.EndFigure(true);
        }
        context.DrawGeometry(brush, null, geometry);
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        hue = ((hue % 360d) + 360d) % 360d / 360d;
        saturation = Math.Clamp(saturation, 0d, 1d);
        lightness = Math.Clamp(lightness, 0d, 1d);
        var q = lightness < 0.5d
            ? lightness * (1d + saturation)
            : lightness + saturation - lightness * saturation;
        var p = 2d * lightness - q;
        static double Channel(double p, double q, double t)
        {
            if (t < 0d) t += 1d;
            if (t > 1d) t -= 1d;
            if (t < 1d / 6d) return p + (q - p) * 6d * t;
            if (t < 0.5d) return q;
            if (t < 2d / 3d) return p + (q - p) * (2d / 3d - t) * 6d;
            return p;
        }
        return Color.FromRgb(
            (byte)Math.Round(Channel(p, q, hue + 1d / 3d) * 255d),
            (byte)Math.Round(Channel(p, q, hue) * 255d),
            (byte)Math.Round(Channel(p, q, hue - 1d / 3d) * 255d));
    }
}
