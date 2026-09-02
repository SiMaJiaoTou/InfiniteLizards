using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using InfiniteLizards.Gameplay.Phenotypes;

namespace InfiniteLizards.Desktop.Management;

/// <summary>
/// A square, deterministic phenotype portrait. It deliberately draws from the
/// same player-facing phenotype values shown beside it instead of using a
/// decorative stock image that could disagree with the selected lizard.
/// </summary>
internal sealed class LizardPortraitView : Control
{
    internal const int MaximumRenderedLegs =
        BreedableLimbMorphology.MaximumPresentationLegCount;
    internal const int MaximumRenderedLegSegments =
        BreedableLimbMorphology.MaximumVisibleLegSegmentCount;
    internal const int MaximumToesPerFoot = 7;
    internal const int MaximumRenderedToes =
        MaximumRenderedLegs * MaximumToesPerFoot;
    internal const int MaximumRenderedSkinMarks = 36;
    internal const int MaximumRenderedTailSegments = 18;
    internal const int MaximumRenderedTailSpikes = BreedableTailSpikes.MaximumCount;
    internal const int MaximumRenderedPatternMarks = 18;

    public static readonly StyledProperty<LizardPortraitModel> ModelProperty =
        AvaloniaProperty.Register<LizardPortraitView, LizardPortraitModel>(
            nameof(Model),
            LizardPortraitModel.Default);

    public LizardPortraitModel Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    static LizardPortraitView()
    {
        AffectsRender<LizardPortraitView>(ModelProperty);
    }

    public LizardPortraitView()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        Focusable = false;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var available = Math.Min(availableSize.Width, availableSize.Height);
        if (!double.IsFinite(available) || available <= 0d)
        {
            available = 260d;
        }
        return new Size(available, available);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (!double.IsFinite(side) || side <= 0d)
        {
            return;
        }

        var origin = new Point(
            (Bounds.Width - side) * 0.5d,
            (Bounds.Height - side) * 0.5d);
        var frame = new Rect(origin, new Size(side, side));
        DrawBackdrop(context, frame);
        DrawLizard(context, frame.Deflate(side * 0.055d), Model);
    }

    private static void DrawBackdrop(DrawingContext context, Rect frame)
    {
        var side = frame.Width;
        var outerRadius = side * 0.105d;
        var warmBrown = new SolidColorBrush(Color.FromRgb(133, 96, 82));
        var paper = new SolidColorBrush(Color.FromRgb(255, 250, 243));
        var cream = new SolidColorBrush(Color.FromRgb(255, 253, 248));
        var mint = new SolidColorBrush(Color.FromRgb(222, 244, 234));
        var peach = new SolidColorBrush(Color.FromRgb(255, 226, 213));
        var lavender = new SolidColorBrush(Color.FromRgb(238, 232, 249));
        var sky = new SolidColorBrush(Color.FromRgb(226, 242, 250));

        // A few offset paper layers imply a soft drop shadow without relying
        // on platform-specific effects. The result stays crisp at every DPI.
        var shadow = new Rect(
            frame.X + side * 0.012d,
            frame.Y + side * 0.024d,
            frame.Width - side * 0.024d,
            frame.Height - side * 0.028d);
        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(38, 104, 70, 58)),
            null,
            new RoundedRect(shadow, outerRadius));
        var outer = frame.Deflate(side * 0.012d);
        context.DrawRectangle(
            paper,
            new Pen(warmBrown, Math.Max(1.1d, side * 0.007d)),
            new RoundedRect(outer, outerRadius));

        var inner = outer.Deflate(side * 0.038d);
        context.DrawRectangle(
            cream,
            new Pen(
                new SolidColorBrush(Color.FromRgb(222, 205, 196)),
                Math.Max(1d, side * 0.005d)),
            new RoundedRect(inner, side * 0.075d));

        // Layered pastel habitat shapes echo the generated soft-clay icons.
        // They are deliberately low-contrast so even very pale or dark genes
        // remain readable against the warm-brown creature outline.
        context.DrawEllipse(
            sky,
            null,
            At(inner, 0.24d, 0.27d),
            inner.Width * 0.20d,
            inner.Height * 0.18d);
        context.DrawEllipse(
            lavender,
            null,
            At(inner, 0.79d, 0.25d),
            inner.Width * 0.15d,
            inner.Height * 0.14d);
        context.DrawEllipse(
            peach,
            null,
            At(inner, 0.79d, 0.74d),
            inner.Width * 0.16d,
            inner.Height * 0.13d);
        context.DrawEllipse(
            mint,
            null,
            At(inner, 0.40d, 0.76d),
            inner.Width * 0.38d,
            inner.Height * 0.16d);

        // A quiet terrarium floor anchors every body morphology instead of
        // leaving it floating in a dark vignette.
        context.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(42, 71, 119, 92)),
            null,
            At(inner, 0.48d, 0.765d),
            inner.Width * 0.33d,
            inner.Height * 0.055d);

        DrawBackdropSparkle(context, At(inner, 0.13d, 0.16d), side * 0.022d);
        DrawBackdropSparkle(context, At(inner, 0.88d, 0.48d), side * 0.014d);
        context.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(244, 190, 116)),
            null,
            At(inner, 0.13d, 0.57d),
            side * 0.010d,
            side * 0.010d);
    }

    private static void DrawBackdropSparkle(
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
        context.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(150, 255, 247, 206)),
            null,
            center,
            radius * 0.34d,
            radius * 0.34d);
    }

    private static void DrawLizard(
        DrawingContext context,
        Rect frame,
        LizardPortraitModel model)
    {
        var visual = model.Visual;
        var body = visual.Body;
        var skin = visual.Skin;
        var bodyColor = ToColor(visual.Palette.Primary);
        var darkColor = Blend(
            bodyColor,
            Color.FromRgb(74, 49, 44),
            0.40d + Math.Clamp(visual.Palette.Melanin, 0f, 1f) * 0.30d);
        var lightColor = Blend(ToColor(visual.Palette.Belly), Colors.White, 0.22d);
        var accentColor = ToColor(visual.Palette.Secondary);
        var eyeColor = ToColor(visual.Palette.Eye);
        var bodyBrush = new SolidColorBrush(bodyColor);
        var darkBrush = new SolidColorBrush(darkColor);
        var lightBrush = new SolidColorBrush(lightColor);
        var accentBrush = new SolidColorBrush(accentColor);
        var outlinePen = new Pen(
            new SolidColorBrush(Color.FromArgb(224, 76, 52, 46)),
            Math.Max(
                1.4d,
                frame.Width * (0.010d + Math.Clamp(skin.Roughness, 0f, 1f) * 0.004d)),
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);
        var limbScale = Math.Clamp(visual.Limbs.ThicknessRatio, 0.3f, 1.9f);
        var limbPen = new Pen(
            bodyBrush,
            Math.Max(2.5d, frame.Width * (0.017d + 0.012d * limbScale)),
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);
        var limbOutlinePen = new Pen(
            darkBrush,
            limbPen.Thickness + Math.Max(2d, frame.Width * 0.011d),
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);

        var lengthScale = Math.Clamp(body.LengthRatio, 0.5f, 1.8f);
        var widthScale = Math.Clamp(body.WidthRatio, 0.45f, 1.7f);
        var heightScale = Math.Clamp(body.HeightRatio, 0.45f, 1.7f);
        var spineArch = Math.Clamp(body.SpineArch, -0.35f, 0.65f);
        var bodyCenter = At(frame, 0.555d, 0.54d - spineArch * 0.035d);
        var bodyRadiusX = frame.Width * (0.175d + 0.052d * lengthScale);
        var bodyRadiusY = frame.Height * (
            0.058d +
            0.027d * widthScale +
            0.018d * heightScale +
            0.018d * Math.Clamp(body.BellyRoundness, 0f, 1f));
        var headRadius = frame.Width * (
            0.052d + 0.020d * Math.Clamp(body.HeadSizeRatio, 0.5f, 1.8f));
        var headRadiusX = headRadius * Math.Clamp(body.HeadWidthRatio, 0.5f, 1.8f);
        var neckReach = 0.72d + 0.17d *
            Normalize(body.NeckLengthRatio, 0.35d, 1.8d);
        var headCenter = new Point(
            bodyCenter.X + bodyRadiusX * neckReach,
            bodyCenter.Y - bodyRadiusY * (0.20d + spineArch * 0.22d));
        var tailBase = new Point(bodyCenter.X - bodyRadiusX * 0.92d, bodyCenter.Y);
        var tailLength = frame.Width * (
            0.12d + 0.08d * Normalize(visual.Tail.LengthRatio, 0.4d, 3d));
        var tailTip = new Point(
            tailBase.X - tailLength,
            tailBase.Y + frame.Height * (0.035d + 0.075d * (1d - visual.Tail.Flexibility)));

        DrawTail(context, tailBase, tailTip, frame, model, darkBrush, bodyBrush, accentBrush, outlinePen);
        DrawSideFins(
            context,
            bodyCenter,
            bodyRadiusX,
            bodyRadiusY,
            frame,
            model,
            accentBrush,
            outlinePen);
        DrawLegs(
            context,
            bodyCenter,
            bodyRadiusX,
            bodyRadiusY,
            frame,
            model,
            limbOutlinePen,
            limbPen,
            darkBrush,
            accentBrush);

        var shoulderMass = Normalize(body.ShoulderMassRatio, 0.4d, 1.8d);
        var hipMass = Normalize(body.HipMassRatio, 0.4d, 1.8d);
        var taper = Math.Clamp(body.Taper, 0f, 1f);
        context.DrawEllipse(
            bodyBrush,
            null,
            new Point(bodyCenter.X + bodyRadiusX * 0.48d, bodyCenter.Y - spineArch * bodyRadiusY * 0.18d),
            bodyRadiusX * (0.42d + shoulderMass * 0.14d),
            bodyRadiusY * (0.72d + shoulderMass * 0.27d));
        context.DrawEllipse(
            bodyBrush,
            null,
            new Point(bodyCenter.X - bodyRadiusX * 0.48d, bodyCenter.Y + spineArch * bodyRadiusY * 0.12d),
            bodyRadiusX * (0.42d + hipMass * 0.14d - taper * 0.07d),
            bodyRadiusY * (0.72d + hipMass * 0.27d - taper * 0.08d));
        context.DrawEllipse(bodyBrush, outlinePen, bodyCenter, bodyRadiusX, bodyRadiusY);
        context.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round(54d + 54d * Math.Clamp(skin.Translucency, 0f, 0.8f) / 0.8d),
                lightColor.R,
                lightColor.G,
                lightColor.B)),
            null,
            new Point(bodyCenter.X + bodyRadiusX * 0.12d, bodyCenter.Y - bodyRadiusY * 0.22d),
            bodyRadiusX * 0.72d,
            bodyRadiusY * 0.46d);
        DrawPattern(
            context,
            bodyCenter,
            bodyRadiusX,
            bodyRadiusY,
            model,
            accentBrush,
            darkBrush);
        DrawSkinTexture(
            context,
            bodyCenter,
            bodyRadiusX,
            bodyRadiusY,
            frame,
            visual,
            lightColor,
            accentColor);
        DrawDorsalFin(context, bodyCenter, bodyRadiusX, bodyRadiusY, frame, model, accentBrush, outlinePen);

        DrawNeckFrill(
            context,
            headCenter,
            headRadius,
            frame,
            visual.Appendages.NeckFrill,
            accentBrush,
            outlinePen);
        DrawHeadAppendages(
            context,
            headCenter,
            headRadius,
            frame,
            model,
            accentBrush,
            outlinePen);
        context.DrawEllipse(bodyBrush, outlinePen, headCenter, headRadiusX, headRadius);
        var snout = DrawSnout(
            context,
            headCenter,
            headRadius,
            headRadiusX,
            body,
            lightBrush,
            outlinePen);
        var eyeSize = headRadius * (
            0.15d + 0.10d * Normalize(body.EyeSizeRatio, 0.45d, 1.9d));
        var eye = new Point(
            headCenter.X + headRadiusX * (
                0.12d + 0.22d * Normalize(body.EyeSpacingRatio, 0.5d, 1.6d)),
            headCenter.Y - headRadius * 0.30d);
        context.DrawEllipse(new SolidColorBrush(eyeColor), outlinePen, eye, eyeSize, eyeSize);
        DrawPupil(context, eye, headRadius, model.PupilShape);
        context.DrawEllipse(Brushes.White, null,
            new Point(eye.X - headRadius * 0.055d, eye.Y - headRadius * 0.075d),
            headRadius * 0.045d,
            headRadius * 0.045d);
        context.DrawEllipse(darkBrush, null,
            new Point(snout.X - headRadius * 0.08d, snout.Y - headRadius * 0.02d),
            headRadius * 0.045d,
            headRadius * 0.035d);
        DrawFriendlyFace(context, headCenter, headRadius, headRadiusX, snout);
        DrawGillTufts(context, headCenter, headRadius, frame, model, accentBrush, outlinePen);
        DrawWhiskers(context, snout, headRadius, frame, model, accentBrush);

        var gloss = Math.Clamp(skin.Gloss, 0f, 1f);
        if (gloss > 0.02d)
        {
            using var opacity = context.PushOpacity(0.10d + gloss * 0.52d);
            context.DrawEllipse(
                Brushes.White,
                null,
                new Point(
                    bodyCenter.X + bodyRadiusX * 0.18d,
                    bodyCenter.Y - bodyRadiusY * 0.43d),
                bodyRadiusX * (0.18d + gloss * 0.20d),
                Math.Max(1.2d, bodyRadiusY * (0.035d + gloss * 0.05d)));
        }

        context.DrawEllipse(null, outlinePen, bodyCenter, bodyRadiusX, bodyRadiusY);
        context.DrawEllipse(null, outlinePen, headCenter, headRadiusX, headRadius);
    }

    private static void DrawFriendlyFace(
        DrawingContext context,
        Point headCenter,
        double headRadius,
        double headRadiusX,
        Point snout)
    {
        // The expression is presentation-only: it adds warmth without
        // replacing the genetic eye, pupil, snout or head proportions.
        context.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(72, 242, 137, 127)),
            null,
            new Point(
                headCenter.X + headRadiusX * 0.45d,
                headCenter.Y + headRadius * 0.19d),
            headRadius * 0.15d,
            headRadius * 0.085d);

        var mouthStart = new Point(
            headCenter.X + headRadiusX * 0.21d,
            headCenter.Y + headRadius * 0.28d);
        var mouthEnd = new Point(
            snout.X - headRadius * 0.08d,
            snout.Y + headRadius * 0.10d);
        var mouth = new StreamGeometry();
        using (var path = mouth.Open())
        {
            path.BeginFigure(mouthStart, false);
            path.CubicBezierTo(
                new Point(
                    mouthStart.X + (mouthEnd.X - mouthStart.X) * 0.30d,
                    mouthStart.Y + headRadius * 0.14d),
                new Point(
                    mouthStart.X + (mouthEnd.X - mouthStart.X) * 0.73d,
                    mouthEnd.Y + headRadius * 0.12d),
                mouthEnd);
            path.EndFigure(false);
        }
        context.DrawGeometry(
            null,
            new Pen(
                new SolidColorBrush(Color.FromArgb(205, 103, 59, 57)),
                Math.Max(0.9d, headRadius * 0.045d),
                lineCap: PenLineCap.Round,
                lineJoin: PenLineJoin.Round),
            mouth);
    }

    private static Point DrawSnout(
        DrawingContext context,
        Point headCenter,
        double headRadius,
        double headRadiusX,
        BreedableBodyMorphology body,
        IBrush brush,
        Pen outlinePen)
    {
        var length = headRadius * (
            0.32d + 0.54d * Normalize(body.SnoutLengthRatio, 0.3d, 2d));
        var baseX = headCenter.X + headRadiusX * 0.58d;
        var tip = new Point(baseX + length, headCenter.Y + headRadius * 0.08d);
        var halfHeight = headRadius * 0.34d;

        switch (body.SnoutShape)
        {
            case BreedableSnoutShape.Rounded:
                context.DrawEllipse(
                    brush,
                    outlinePen,
                    new Point(baseX + length * 0.48d, tip.Y),
                    length * 0.56d,
                    halfHeight);
                break;
            case BreedableSnoutShape.Wedge:
                DrawPolygon(
                    context,
                    brush,
                    outlinePen,
                    [
                        new Point(baseX, tip.Y - halfHeight),
                        new Point(tip.X, tip.Y - halfHeight * 0.48d),
                        new Point(tip.X, tip.Y + halfHeight * 0.48d),
                        new Point(baseX, tip.Y + halfHeight)
                    ]);
                break;
            case BreedableSnoutShape.Pointed:
                DrawPolygon(
                    context,
                    brush,
                    outlinePen,
                    [
                        new Point(baseX, tip.Y - halfHeight),
                        new Point(tip.X, tip.Y),
                        new Point(baseX, tip.Y + halfHeight)
                    ]);
                break;
            case BreedableSnoutShape.Shovel:
                DrawPolygon(
                    context,
                    brush,
                    outlinePen,
                    [
                        new Point(baseX, tip.Y - halfHeight * 0.88d),
                        new Point(tip.X - length * 0.12d, tip.Y - halfHeight),
                        new Point(tip.X, tip.Y - halfHeight * 0.65d),
                        new Point(tip.X, tip.Y + halfHeight * 0.65d),
                        new Point(tip.X - length * 0.12d, tip.Y + halfHeight),
                        new Point(baseX, tip.Y + halfHeight * 0.88d)
                    ]);
                break;
            case BreedableSnoutShape.Beak:
                DrawPolygon(
                    context,
                    brush,
                    outlinePen,
                    [
                        new Point(baseX, tip.Y - halfHeight),
                        new Point(tip.X - length * 0.12d, tip.Y - halfHeight * 0.45d),
                        new Point(tip.X, tip.Y + halfHeight * 0.08d),
                        new Point(tip.X - length * 0.20d, tip.Y + halfHeight * 0.50d),
                        new Point(baseX, tip.Y + halfHeight)
                    ]);
                break;
        }

        return tip;
    }

    private static void DrawTail(
        DrawingContext context,
        Point tailBase,
        Point tailTip,
        Rect frame,
        LizardPortraitModel model,
        IBrush darkBrush,
        IBrush bodyBrush,
        IBrush accentBrush,
        Pen outlinePen)
    {
        var tail = model.Visual.Tail;
        var segmentCount = Math.Clamp(
            tail.SegmentCount,
            3,
            MaximumRenderedTailSegments);
        var flexibility = Normalize(tail.Flexibility, 0.15d, 1d);
        var control1 = new Point(
            tailBase.X + (tailTip.X - tailBase.X) * 0.34d,
            tailBase.Y - frame.Height * (0.025d + flexibility * 0.075d));
        var control2 = new Point(
            tailBase.X + (tailTip.X - tailBase.X) * 0.76d,
            tailTip.Y + frame.Height * (0.045d + flexibility * 0.08d));
        var points = new Point[segmentCount + 1];
        for (var index = 0; index <= segmentCount; index++)
        {
            points[index] = CubicPoint(
                tailBase,
                control1,
                control2,
                tailTip,
                index / (double)segmentCount);
        }

        var baseThickness = frame.Width * (
            0.045d + 0.038d * Normalize(tail.BaseThicknessRatio, 0.25d, 1.9d));
        var taper = Math.Clamp(tail.Taper, 0f, 1f);
        for (var index = 0; index < segmentCount; index++)
        {
            var t = index / (double)Math.Max(1, segmentCount - 1);
            var thickness = baseThickness *
                (1d - t * (0.42d + taper * 0.48d));
            var outline = new Pen(
                darkBrush,
                Math.Max(3d, thickness + frame.Width * 0.018d),
                lineCap: PenLineCap.Round);
            var fill = new Pen(
                bodyBrush,
                Math.Max(2d, thickness),
                lineCap: PenLineCap.Round);
            context.DrawLine(outline, points[index], points[index + 1]);
            context.DrawLine(fill, points[index], points[index + 1]);
        }

        using (context.PushOpacity(0.16d + flexibility * 0.16d))
        {
            for (var index = 1; index < segmentCount; index++)
            {
                var radius = Math.Max(0.9d, baseThickness * (0.10d - index * 0.002d));
                context.DrawEllipse(accentBrush, null, points[index], radius, radius * 0.72d);
            }
        }

        if (tail.Sail.IsPresent)
        {
            var sailHeight = frame.Height * (
                0.035d + 0.095d * Normalize(tail.Sail.HeightRatio, 0d, 2d));
            var sailSegments = Math.Clamp(
                (int)Math.Ceiling(segmentCount * Math.Clamp(tail.Sail.LengthRatio, 0f, 1f)),
                1,
                segmentCount);
            var sail = new StreamGeometry();
            using (var path = sail.Open())
            {
                path.BeginFigure(points[0], true);
                for (var index = 1; index <= sailSegments; index++)
                {
                    var t = index / (double)sailSegments;
                    var height = sailHeight * Math.Sin(Math.PI * Math.Pow(t, 0.72d));
                    path.LineTo(new Point(points[index].X, points[index].Y - height));
                }
                path.LineTo(points[sailSegments]);
                path.EndFigure(true);
            }
            context.DrawGeometry(accentBrush, outlinePen, sail);
        }

        if (tail.Spikes.IsPresent)
        {
            var visibleSpikeCount = Math.Clamp(
                tail.Spikes.Count,
                1,
                MaximumRenderedTailSpikes);
            var spikeHeight = frame.Height * (
                0.018d + 0.066d * Normalize(tail.Spikes.SizeRatio, 0d, 1.8d));
            for (var index = 0; index < visibleSpikeCount; index++)
            {
                var amount = CalculateTailSpikeParameter(index, visibleSpikeCount);
                var point = CubicPoint(
                    tailBase,
                    control1,
                    control2,
                    tailTip,
                    amount);
                var tangent = CubicTangent(
                    tailBase,
                    control1,
                    control2,
                    tailTip,
                    amount);
                var tangentLength = Math.Sqrt(
                    tangent.X * tangent.X + tangent.Y * tangent.Y);
                var tangentX = tangentLength > 0.000001d
                    ? tangent.X / tangentLength
                    : -1d;
                var tangentY = tangentLength > 0.000001d
                    ? tangent.Y / tangentLength
                    : 0d;
                var normalX = -tangentY;
                var normalY = tangentX;
                if (normalY > 0d)
                {
                    normalX = -normalX;
                    normalY = -normalY;
                }
                var halfBase = frame.Width * 0.011d;
                var spike = new StreamGeometry();
                using (var path = spike.Open())
                {
                    path.BeginFigure(new Point(
                        point.X - tangentX * halfBase,
                        point.Y - tangentY * halfBase), true);
                    path.LineTo(new Point(
                        point.X + normalX * spikeHeight,
                        point.Y + normalY * spikeHeight));
                    path.LineTo(new Point(
                        point.X + tangentX * halfBase,
                        point.Y + tangentY * halfBase));
                    path.EndFigure(true);
                }
                context.DrawGeometry(accentBrush, outlinePen, spike);
            }
        }

        if (tail.Club.IsPresent)
        {
            var size = frame.Width * (
                0.023d + 0.029d * Normalize(tail.Club.SizeRatio, 0d, 2.2d));
            context.DrawEllipse(accentBrush, outlinePen, tailTip, size, size * 0.82d);
            var spikeExtension = 0.15d +
                0.60d * Normalize(tail.Club.SpikeLengthRatio, 0d, 1.6d);
            for (var index = 0; index < 6; index++)
            {
                var angle = index * Math.PI / 3d;
                var start = new Point(
                    tailTip.X + Math.Cos(angle) * size * 0.75d,
                    tailTip.Y + Math.Sin(angle) * size * 0.60d);
                var end = new Point(
                    tailTip.X + Math.Cos(angle) * size * (1d + spikeExtension),
                    tailTip.Y + Math.Sin(angle) * size * (0.82d + spikeExtension));
                context.DrawLine(outlinePen, start, end);
                context.DrawLine(
                    new Pen(accentBrush, Math.Max(1d, outlinePen.Thickness * 0.42d)),
                    start,
                    end);
            }
        }

        if (tail.Fork.IsPresent)
        {
            var forkLength = frame.Width * (
                0.025d + 0.13d * Normalize(tail.Fork.LengthRatio, 0d, 0.58d));
            var forkOrigin = points[Math.Max(0, segmentCount - 1)];
            foreach (var direction in new[] { -1d, 1d })
            {
                var forkTip = new Point(
                    tailTip.X - forkLength * 0.26d,
                    tailTip.Y + direction * forkLength * 0.58d);
                context.DrawLine(
                    new Pen(
                        darkBrush,
                        Math.Max(3d, baseThickness * 0.38d),
                        lineCap: PenLineCap.Round),
                    forkOrigin,
                    forkTip);
                context.DrawLine(
                    new Pen(
                        bodyBrush,
                        Math.Max(2d, baseThickness * 0.24d),
                        lineCap: PenLineCap.Round),
                    forkOrigin,
                    forkTip);
            }
        }
    }

    private static void DrawLegs(
        DrawingContext context,
        Point bodyCenter,
        double bodyRadiusX,
        double bodyRadiusY,
        Rect frame,
        LizardPortraitModel model,
        Pen outlinePen,
        Pen limbPen,
        IBrush footBrush,
        IBrush accentBrush)
    {
        var limbs = model.Visual.Limbs;
        var pairCount = Math.Clamp(
            limbs.LegPairCount,
            BreedableLimbMorphology.MinimumLegPairCount,
            Math.Min(
                BreedableLimbMorphology.MaximumLegPairCount,
                MaximumRenderedLegs / 2));
        var jointCount = Math.Clamp(
            limbs.VisibleJointCount,
            BreedableLimbMorphology.MinimumVisibleJointCount,
            Math.Min(
                BreedableLimbMorphology.MaximumVisibleJointCount,
                MaximumRenderedLegSegments / MaximumRenderedLegs));
        var lengthScale = Normalize(limbs.LengthRatio, 0.4d, 2d);
        var frontRearRatio = Normalize(limbs.FrontRearRatio, 0.5d, 1.5d);
        var asymmetry = Math.Clamp(limbs.LeftRightAsymmetry, 0f, 0.28f);
        var bodyFlexibility = Normalize(
            model.Visual.Body.Flexibility,
            0.25d,
            1d);
        for (var pair = 0; pair < pairCount; pair++)
        {
            var bodyT = pairCount == 1 ? 0.5d : pair / (double)(pairCount - 1);
            var shoulderX = bodyCenter.X - bodyRadiusX * 0.68d + bodyRadiusX * 1.34d * bodyT;
            foreach (var side in new[] { -1d, 1d })
            {
                var shoulder = new Point(shoulderX, bodyCenter.Y + side * bodyRadiusY * 0.55d);
                var endBias = Math.Abs(bodyT - 0.5d) * 2d;
                var pairScale = 1d + (frontRearRatio - 0.5d) *
                    (bodyT >= 0.5d ? 0.28d : -0.28d);
                var sideScale = side < 0d ? 1d + asymmetry : 1d - asymmetry;
                var outward = frame.Height *
                    (0.063d + 0.070d * lengthScale + 0.006d * pairCount) *
                    pairScale * sideScale;
                var forward = frame.Width * (
                    0.016d + 0.018d * endBias) *
                    (pair % 2 == 0 ? 1d : -1d);
                var foot = new Point(
                    shoulder.X + forward,
                    shoulder.Y + side * outward);
                var points = new Point[jointCount + 1];
                points[0] = shoulder;
                for (var joint = 1; joint <= jointCount; joint++)
                {
                    var t = joint / (double)jointCount;
                    var zig = joint == jointCount
                        ? 0d
                        : (joint % 2 == 0 ? -1d : 1d) *
                          frame.Width *
                          (0.017d + bodyFlexibility * 0.016d);
                    points[joint] = new Point(
                        shoulder.X + (foot.X - shoulder.X) * t + zig,
                        shoulder.Y + (foot.Y - shoulder.Y) * t);
                }
                for (var joint = 0; joint < points.Length - 1; joint++)
                {
                    context.DrawLine(outlinePen, points[joint], points[joint + 1]);
                }
                for (var joint = 0; joint < points.Length - 1; joint++)
                {
                    context.DrawLine(limbPen, points[joint], points[joint + 1]);
                }
                DrawFoot(
                    context,
                    foot,
                    side,
                    frame,
                    limbs,
                    footBrush,
                    accentBrush);
            }
        }
    }

    private static void DrawFoot(
        DrawingContext context,
        Point foot,
        double side,
        Rect frame,
        BreedableLimbMorphology limbs,
        IBrush footBrush,
        IBrush accentBrush)
    {
        var size = Normalize(limbs.FootSizeRatio, 0.4d, 1.9d);
        var radiusX = frame.Width * (0.014d + 0.015d * size);
        var radiusY = frame.Height * (0.009d + 0.010d * size);
        var toeCount = Math.Clamp(limbs.ToeCount, 2, MaximumToesPerFoot);
        var toeLength = frame.Width * (0.010d + 0.014d * size);

        switch (limbs.FootShape)
        {
            case BreedableFootShape.Rounded:
                context.DrawEllipse(footBrush, null, foot, radiusX, radiusY);
                break;
            case BreedableFootShape.Toed:
                context.DrawEllipse(footBrush, null, foot, radiusX * 1.12d, radiusY * 0.84d);
                break;
            case BreedableFootShape.Webbed:
                DrawPolygon(
                    context,
                    footBrush,
                    null,
                    [
                        new Point(foot.X - radiusX, foot.Y),
                        new Point(foot.X, foot.Y + side * radiusY * 2.15d),
                        new Point(foot.X + radiusX, foot.Y)
                    ]);
                break;
            case BreedableFootShape.Clawed:
                context.DrawEllipse(footBrush, null, foot, radiusX * 1.18d, radiusY * 0.70d);
                break;
            case BreedableFootShape.AdhesivePads:
                context.DrawEllipse(footBrush, null, foot, radiusX * 1.24d, radiusY);
                context.DrawEllipse(
                    accentBrush,
                    null,
                    foot,
                    radiusX * 0.52d,
                    radiusY * 0.55d);
                break;
        }

        var toeEnds = new Point[toeCount];
        for (var toe = 0; toe < toeCount; toe++)
        {
            var spread = toeCount == 1
                ? 0d
                : -0.72d + toe * 1.44d / (toeCount - 1);
            toeEnds[toe] = new Point(
                foot.X + Math.Sin(spread) * toeLength,
                foot.Y + side * Math.Cos(spread) * toeLength);
        }

        if (limbs.HasWebbing && limbs.WebbingAmount > 0f)
        {
            var membrane = new StreamGeometry();
            var amount = Math.Clamp(limbs.WebbingAmount, 0f, 1f);
            using (var path = membrane.Open())
            {
                path.BeginFigure(foot, true);
                foreach (var toeEnd in toeEnds)
                {
                    path.LineTo(new Point(
                        foot.X + (toeEnd.X - foot.X) * (0.36d + amount * 0.62d),
                        foot.Y + (toeEnd.Y - foot.Y) * (0.36d + amount * 0.62d)));
                }
                path.EndFigure(true);
            }
            using var opacity = context.PushOpacity(0.28d + amount * 0.48d);
            context.DrawGeometry(accentBrush, null, membrane);
        }

        var toePen = new Pen(
            footBrush,
            Math.Max(0.8d, frame.Width * 0.006d),
            lineCap: PenLineCap.Round);
        var clawPen = new Pen(
            accentBrush,
            Math.Max(0.7d, frame.Width * 0.004d),
            lineCap: PenLineCap.Round);
        var clawLength = frame.Width * 0.022d *
            Normalize(limbs.ClawLengthRatio, 0d, 1.7d);
        var padSize = frame.Width * (0.003d + 0.007d *
            Normalize(limbs.GripPadSizeRatio, 0.15d, 1.9d));
        for (var toe = 0; toe < toeEnds.Length; toe++)
        {
            var toeEnd = toeEnds[toe];
            context.DrawLine(toePen, foot, toeEnd);
            if (limbs.ClawLengthRatio > 0.04f)
            {
                var dx = toeEnd.X - foot.X;
                var dy = toeEnd.Y - foot.Y;
                var magnitude = Math.Max(0.001d, Math.Sqrt(dx * dx + dy * dy));
                context.DrawLine(
                    clawPen,
                    toeEnd,
                    new Point(
                        toeEnd.X + dx / magnitude * clawLength,
                        toeEnd.Y + dy / magnitude * clawLength));
            }
            if (limbs.GripPadSizeRatio > 0.2f)
            {
                using var opacity = context.PushOpacity(
                    limbs.FootShape == BreedableFootShape.AdhesivePads ? 0.92d : 0.50d);
                context.DrawEllipse(accentBrush, null, toeEnd, padSize, padSize * 0.72d);
            }
        }
    }

    private static void DrawSideFins(
        DrawingContext context,
        Point bodyCenter,
        double bodyRadiusX,
        double bodyRadiusY,
        Rect frame,
        LizardPortraitModel model,
        IBrush accentBrush,
        Pen outlinePen)
    {
        if (!model.HasSideFins)
        {
            return;
        }

        var pairCount = Math.Clamp(model.SideFinPairs, 1, 4);
        var size = Math.Clamp(model.SideFinSize, 0d, 1.6d) / 1.6d;
        for (var pair = 0; pair < pairCount; pair++)
        {
            var t = pairCount == 1 ? 0.5d : pair / (double)(pairCount - 1);
            var x = bodyCenter.X - bodyRadiusX * 0.62d + bodyRadiusX * 1.15d * t;
            foreach (var side in new[] { -1d, 1d })
            {
                var basePoint = new Point(
                    x,
                    bodyCenter.Y + side * bodyRadiusY * 0.62d);
                var finLength = frame.Height * (0.045d + size * 0.085d);
                var finWidth = frame.Width * (0.024d + size * 0.035d);
                var fin = new StreamGeometry();
                using (var path = fin.Open())
                {
                    path.BeginFigure(
                        new Point(basePoint.X - finWidth, basePoint.Y),
                        true);
                    path.LineTo(new Point(
                        basePoint.X,
                        basePoint.Y + side * finLength));
                    path.LineTo(new Point(
                        basePoint.X + finWidth,
                        basePoint.Y + side * finLength * 0.18d));
                    path.EndFigure(true);
                }
                context.DrawGeometry(accentBrush, outlinePen, fin);
            }
        }
    }

    private static void DrawPattern(
        DrawingContext context,
        Point center,
        double radiusX,
        double radiusY,
        LizardPortraitModel model,
        IBrush accentBrush,
        IBrush darkBrush)
    {
        var pattern = model.Visual.Pattern;
        var density = Math.Clamp(pattern.Density, 0f, 1f);
        var strength = Math.Clamp(pattern.Strength, 0f, 1f);
        var scale = Math.Clamp(pattern.ElementScale, 0.05f, 3f);
        var symmetry = Math.Clamp(pattern.Symmetry, 0f, 1f);
        var edgeSoftness = Math.Clamp(pattern.EdgeSoftness, 0f, 1f);
        var patternCenter = new Point(
            center.X,
            center.Y + (1d - symmetry) * radiusY * 0.17d);
        var patternBrush = pattern.Kind is
            BreedablePatternKind.CrossBands or
            BreedablePatternKind.Reticulation or
            BreedablePatternKind.Marbled
                ? darkBrush
                : accentBrush;
        var lineWidth = Math.Max(
            1.2d,
            radiusY *
            (0.024d +
             0.034d * Math.Min(scale, 2d) +
             0.042d * Normalize(pattern.StripeWidth, 0.05d, 2.5d)));
        var pen = new Pen(
            patternBrush,
            lineWidth,
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);

        var glowOpacityValue = CalculatePatternGlowOpacity(pattern);
        if (glowOpacityValue > 0d)
        {
            using var glowOpacity = context.PushOpacity(glowOpacityValue);
            context.DrawEllipse(
                null,
                new Pen(
                    patternBrush,
                    lineWidth * (2.6d + edgeSoftness),
                    lineCap: PenLineCap.Round),
                patternCenter,
                radiusX * 0.76d,
                radiusY * 0.61d);
        }

        // Glow is an independent inherited dimension. A genetically solid
        // lizard still needs its glow halo even though it has no pattern marks.
        if (pattern.Kind == BreedablePatternKind.Solid)
        {
            return;
        }

        using (context.PushOpacity(
                   0.15d + strength * (0.72d - edgeSoftness * 0.12d)))
        {
            switch (pattern.Kind)
            {
                case BreedablePatternKind.LongitudinalStripes:
                    DrawLongitudinalStripes(
                        context,
                        patternCenter,
                        radiusX,
                        radiusY,
                        pattern.StripeCount,
                        symmetry,
                        pen);
                    break;
                case BreedablePatternKind.CrossBands:
                    DrawCrossBands(
                        context,
                        patternCenter,
                        radiusX,
                        radiusY,
                        pattern.StripeCount,
                        symmetry,
                        pen);
                    break;
                case BreedablePatternKind.LeopardSpots:
                    DrawSpots(context, patternCenter, radiusX, radiusY, density, scale, patternBrush);
                    break;
                case BreedablePatternKind.Reticulation:
                    DrawReticulation(context, patternCenter, radiusX, radiusY, density, pen);
                    break;
                case BreedablePatternKind.Marbled:
                    DrawMarbling(context, patternCenter, radiusX, radiusY, density, pen);
                    break;
                case BreedablePatternKind.StarSpeckles:
                    DrawStarSpeckles(context, patternCenter, radiusX, radiusY, density, scale, patternBrush);
                    break;
                case BreedablePatternKind.ColorBlocks:
                    DrawColorBlocks(context, patternCenter, radiusX, radiusY, density, scale, patternBrush);
                    break;
            }
        }
    }

    private static void DrawLongitudinalStripes(
        DrawingContext context,
        Point center,
        double radiusX,
        double radiusY,
        int requestedCount,
        double symmetry,
        Pen pen)
    {
        var count = Math.Clamp(requestedCount, 1, MaximumRenderedPatternMarks);
        for (var index = 0; index < count; index++)
        {
            var yRatio = count == 1 ? 0d : -0.62d + index * 1.24d / (count - 1);
            var halfWidth = radiusX * Math.Sqrt(Math.Max(0d, 1d - yRatio * yRatio)) * 0.82d;
            var y = center.Y + yRatio * radiusY;
            var stripe = new StreamGeometry();
            using (var path = stripe.Open())
            {
                path.BeginFigure(new Point(center.X - halfWidth, y), false);
                var wobble = radiusY * (0.05d + (1d - symmetry) * 0.22d);
                path.CubicBezierTo(
                    new Point(center.X - halfWidth * 0.34d, y - wobble),
                    new Point(center.X + halfWidth * 0.34d, y + wobble),
                    new Point(center.X + halfWidth, y));
                path.EndFigure(false);
            }
            context.DrawGeometry(null, pen, stripe);
        }
    }

    private static void DrawCrossBands(
        DrawingContext context,
        Point center,
        double radiusX,
        double radiusY,
        int requestedCount,
        double symmetry,
        Pen pen)
    {
        var count = Math.Clamp(requestedCount, 1, MaximumRenderedPatternMarks);
        for (var index = 0; index < count; index++)
        {
            var xRatio = -0.76d + index * 1.52d / Math.Max(1, count - 1);
            var halfHeight = radiusY * Math.Sqrt(Math.Max(0d, 1d - xRatio * xRatio)) * 0.82d;
            var x = center.X + xRatio * radiusX +
                (index % 2 == 0 ? -1d : 1d) *
                (1d - symmetry) * radiusX * 0.035d;
            context.DrawLine(
                pen,
                new Point(x, center.Y - halfHeight),
                new Point(x, center.Y + halfHeight));
        }
    }

    private static void DrawSpots(
        DrawingContext context,
        Point center,
        double radiusX,
        double radiusY,
        double density,
        double scale,
        IBrush brush)
    {
        var count = Math.Clamp(
            (int)Math.Round(4d + density * 14d),
            4,
            MaximumRenderedPatternMarks);
        var size = Math.Max(1.3d, radiusY * (0.055d + 0.045d * Math.Min(scale, 2d)));
        for (var index = 0; index < count; index++)
        {
            var phase = index * 2.399963229728653d;
            var radial = Math.Sqrt((index + 0.7d) / (count + 0.7d)) * 0.76d;
            context.DrawEllipse(
                brush,
                null,
                new Point(
                    center.X + Math.Cos(phase) * radiusX * radial,
                    center.Y + Math.Sin(phase) * radiusY * radial),
                size,
                size * 0.72d);
        }
    }

    private static void DrawReticulation(
        DrawingContext context,
        Point center,
        double radiusX,
        double radiusY,
        double density,
        Pen pen)
    {
        var columns = Math.Clamp(2 + (int)Math.Round(density * 3d), 2, 5);
        var cellWidth = radiusX * 1.35d / columns;
        var cellHeight = radiusY * 0.55d;
        for (var column = -columns; column <= columns; column++)
        {
            var x = center.X + column * cellWidth * 0.5d;
            var xRatio = (x - center.X) / radiusX;
            var halfHeight = radiusY * Math.Sqrt(Math.Max(0d, 1d - xRatio * xRatio)) * 0.72d;
            if (halfHeight <= 0d) continue;
            var top = center.Y - halfHeight;
            var bottom = center.Y + halfHeight;
            context.DrawLine(pen, new Point(x - cellWidth * 0.5d, center.Y), new Point(x, top));
            context.DrawLine(pen, new Point(x - cellWidth * 0.5d, center.Y), new Point(x, bottom));
            context.DrawLine(pen, new Point(x, top), new Point(x + cellWidth * 0.5d, center.Y));
            context.DrawLine(pen, new Point(x, bottom), new Point(x + cellWidth * 0.5d, center.Y));
        }
    }

    private static void DrawMarbling(
        DrawingContext context,
        Point center,
        double radiusX,
        double radiusY,
        double density,
        Pen pen)
    {
        var count = Math.Clamp(
            2 + (int)Math.Round(density * 6d),
            2,
            Math.Min(8, MaximumRenderedPatternMarks));
        for (var index = 0; index < count; index++)
        {
            var yRatio = -0.58d + index * 1.16d / Math.Max(1, count - 1);
            var halfWidth = radiusX * Math.Sqrt(Math.Max(0d, 1d - yRatio * yRatio)) * 0.76d;
            var y = center.Y + yRatio * radiusY;
            var marble = new StreamGeometry();
            using (var path = marble.Open())
            {
                path.BeginFigure(new Point(center.X - halfWidth, y), false);
                path.CubicBezierTo(
                    new Point(center.X - halfWidth * 0.46d, y + radiusY * 0.45d),
                    new Point(center.X + halfWidth * 0.08d, y - radiusY * 0.45d),
                    new Point(center.X + halfWidth, y));
                path.EndFigure(false);
            }
            context.DrawGeometry(null, pen, marble);
        }
    }

    private static void DrawStarSpeckles(
        DrawingContext context,
        Point center,
        double radiusX,
        double radiusY,
        double density,
        double scale,
        IBrush brush)
    {
        var count = Math.Clamp(
            (int)Math.Round(4d + density * 14d),
            4,
            MaximumRenderedPatternMarks);
        var outer = Math.Max(1.5d, radiusY * (0.06d + 0.035d * Math.Min(scale, 2d)));
        for (var item = 0; item < count; item++)
        {
            var phase = item * 2.399963229728653d + 0.35d;
            var radial = Math.Sqrt((item + 0.6d) / (count + 0.6d)) * 0.72d;
            var starCenter = new Point(
                center.X + Math.Cos(phase) * radiusX * radial,
                center.Y + Math.Sin(phase) * radiusY * radial);
            var star = new StreamGeometry();
            using (var path = star.Open())
            {
                for (var pointIndex = 0; pointIndex < 10; pointIndex++)
                {
                    var angle = -Math.PI * 0.5d + pointIndex * Math.PI / 5d;
                    var pointRadius = pointIndex % 2 == 0 ? outer : outer * 0.42d;
                    var point = new Point(
                        starCenter.X + Math.Cos(angle) * pointRadius,
                        starCenter.Y + Math.Sin(angle) * pointRadius);
                    if (pointIndex == 0) path.BeginFigure(point, true);
                    else path.LineTo(point);
                }
                path.EndFigure(true);
            }
            context.DrawGeometry(brush, null, star);
        }
    }

    private static void DrawColorBlocks(
        DrawingContext context,
        Point center,
        double radiusX,
        double radiusY,
        double density,
        double scale,
        IBrush brush)
    {
        var count = Math.Clamp(
            2 + (int)Math.Round(density * 5d),
            2,
            Math.Min(7, MaximumRenderedPatternMarks));
        var width = radiusX * Math.Clamp(0.20d + scale * 0.08d, 0.2d, 0.46d);
        var height = radiusY * Math.Clamp(0.28d + scale * 0.10d, 0.3d, 0.62d);
        for (var index = 0; index < count; index++)
        {
            var xRatio = -0.62d + index * 1.24d / Math.Max(1, count - 1);
            var yRatio = index % 2 == 0 ? -0.2d : 0.22d;
            context.DrawEllipse(
                brush,
                null,
                new Point(
                    center.X + xRatio * radiusX,
                    center.Y + yRatio * radiusY),
                width,
                height);
        }
    }

    private static void DrawSkinTexture(
        DrawingContext context,
        Point center,
        double radiusX,
        double radiusY,
        Rect frame,
        BreedableVisualPhenotype visual,
        Color lightColor,
        Color accentColor)
    {
        var skin = visual.Skin;
        var roughness = Math.Clamp(skin.Roughness, 0f, 1f);
        var scaleSize = Normalize(skin.ScaleSizeRatio, 0.25d, 2d);
        var markCount = Math.Clamp(
            (int)Math.Round(7d + roughness * 19d + (1d - scaleSize) * 10d),
            7,
            MaximumRenderedSkinMarks);
        var markRadius = frame.Width * (0.0025d + scaleSize * 0.0085d);
        var markColor = Blend(lightColor, accentColor, visual.Palette.Iridescence * 0.72d);
        var markPen = new Pen(
            new SolidColorBrush(WithAlpha(
                markColor,
                (byte)Math.Round(22d + roughness * 72d))),
            Math.Max(0.6d, frame.Width * (0.002d + roughness * 0.0025d)));
        for (var index = 0; index < markCount; index++)
        {
            var phase = index * 2.399963229728653d + 0.18d;
            var radial = Math.Sqrt((index + 0.65d) / (markCount + 0.65d)) * 0.76d;
            var point = new Point(
                center.X + Math.Cos(phase) * radiusX * radial,
                center.Y + Math.Sin(phase) * radiusY * radial);
            context.DrawEllipse(
                null,
                markPen,
                point,
                markRadius,
                markRadius * (0.52d + roughness * 0.28d));
        }

        var translucency = Math.Clamp(skin.Translucency, 0f, 0.8f) / 0.8d;
        if (translucency > 0.01d)
        {
            context.DrawEllipse(
                null,
                new Pen(
                    new SolidColorBrush(WithAlpha(
                        accentColor,
                        (byte)Math.Round(18d + translucency * 78d))),
                    frame.Width * (0.006d + translucency * 0.010d)),
                center,
                radiusX * (0.88d - translucency * 0.05d),
                radiusY * (0.72d - translucency * 0.04d));
        }

        var iridescence = Math.Clamp(visual.Palette.Iridescence, 0f, 1f);
        if (iridescence > 0.01d)
        {
            using var opacity = context.PushOpacity(0.05d + iridescence * 0.24d);
            context.DrawEllipse(
                new SolidColorBrush(accentColor),
                null,
                new Point(center.X - radiusX * 0.16d, center.Y - radiusY * 0.28d),
                radiusX * (0.18d + iridescence * 0.20d),
                radiusY * 0.20d);
        }
    }

    private static void DrawNeckFrill(
        DrawingContext context,
        Point headCenter,
        double headRadius,
        Rect frame,
        BreedableNeckFrill frill,
        IBrush accentBrush,
        Pen outlinePen)
    {
        if (!frill.IsPresent)
        {
            return;
        }

        var size = Normalize(frill.SizeRatio, 0d, 2.2d);
        var lobeCount = Math.Clamp(5 + (int)Math.Round(size * 4d), 5, 9);
        var radius = headRadius + frame.Width * (0.025d + size * 0.07d);
        var baseAngle = Math.PI * 0.58d;
        var span = Math.PI * 0.90d;
        var frillGeometry = new StreamGeometry();
        using (var path = frillGeometry.Open())
        {
            path.BeginFigure(
                new Point(
                    headCenter.X + Math.Cos(baseAngle) * headRadius * 0.72d,
                    headCenter.Y + Math.Sin(baseAngle) * headRadius * 0.72d),
                true);
            for (var index = 0; index < lobeCount; index++)
            {
                var t = lobeCount == 1 ? 0.5d : index / (double)(lobeCount - 1);
                var angle = baseAngle + span * t;
                var ripple = index % 2 == 0 ? 1d : 0.82d;
                path.LineTo(new Point(
                    headCenter.X + Math.Cos(angle) * radius * ripple,
                    headCenter.Y + Math.Sin(angle) * radius * ripple));
            }
            var endAngle = baseAngle + span;
            path.LineTo(new Point(
                headCenter.X + Math.Cos(endAngle) * headRadius * 0.72d,
                headCenter.Y + Math.Sin(endAngle) * headRadius * 0.72d));
            path.EndFigure(true);
        }
        using var opacity = context.PushOpacity(0.62d + size * 0.26d);
        context.DrawGeometry(accentBrush, outlinePen, frillGeometry);
    }

    private static void DrawDorsalFin(
        DrawingContext context,
        Point center,
        double radiusX,
        double radiusY,
        Rect frame,
        LizardPortraitModel model,
        IBrush accentBrush,
        Pen outlinePen)
    {
        if (!model.HasDorsalFin)
        {
            return;
        }
        var height = frame.Height * (0.045d + 0.06d * Math.Clamp(model.DorsalFinSize, 0d, 1.8d) / 1.8d);
        var shape = model.DorsalFinShape;
        var finCount = shape switch
        {
            BreedableDorsalFinShape.RoundedSail => 7,
            BreedableDorsalFinShape.TriangularSerration => 9,
            BreedableDorsalFinShape.Wave => 8,
            BreedableDorsalFinShape.Feathered => 5,
            _ => 7
        };
        var halfSpan = radiusX * (0.24d + 0.68d * Math.Clamp(model.DorsalFinLength, 0d, 1d));
        for (var index = 0; index < finCount; index++)
        {
            var t = finCount == 1 ? 0.5d : index / (double)(finCount - 1);
            var x = center.X - halfSpan + halfSpan * 2d * t;
            var baseY = center.Y - radiusY * Math.Sqrt(Math.Max(0d, 1d - Math.Pow((x - center.X) / radiusX, 2d)));
            var shapeFactor = shape switch
            {
                BreedableDorsalFinShape.RoundedSail => 0.58d + 0.42d * Math.Sin(Math.PI * t),
                BreedableDorsalFinShape.TriangularSerration => 0.82d + 0.18d * (index % 2),
                BreedableDorsalFinShape.Wave => 0.58d + 0.36d * (0.5d + 0.5d * Math.Sin(t * Math.PI * 3d)),
                BreedableDorsalFinShape.Feathered => 0.64d + 0.34d * (1d - t),
                _ => 0.48d + 0.5d * Math.Max(
                    Math.Exp(-Math.Pow((t - 0.27d) / 0.18d, 2d)),
                    Math.Exp(-Math.Pow((t - 0.73d) / 0.18d, 2d)))
            };
            var slant = shape == BreedableDorsalFinShape.Feathered
                ? frame.Width * 0.028d
                : 0d;
            var fin = new StreamGeometry();
            using (var path = fin.Open())
            {
                path.BeginFigure(new Point(x - frame.Width * 0.018d, baseY + 1d), true);
                path.LineTo(new Point(x + slant, baseY - height * shapeFactor));
                path.LineTo(new Point(x + frame.Width * 0.018d, baseY + 1d));
                path.EndFigure(true);
            }
            context.DrawGeometry(accentBrush, outlinePen, fin);
        }
    }

    private static void DrawHeadAppendages(
        DrawingContext context,
        Point headCenter,
        double headRadius,
        Rect frame,
        LizardPortraitModel model,
        IBrush accentBrush,
        Pen outlinePen)
    {
        if (model.HasHeadCrest)
        {
            var crestHeight = frame.Height * (
                0.035d + 0.07d * NormalizeHeadCrestHeight(model.HeadCrestHeight));
            for (var index = 0; index < 4; index++)
            {
                var t = index / 3d;
                var x = headCenter.X - headRadius * 0.78d + headRadius * 0.95d * t;
                var crest = new StreamGeometry();
                using (var path = crest.Open())
                {
                    path.BeginFigure(new Point(x - headRadius * 0.22d, headCenter.Y - headRadius * 0.58d), true);
                    path.LineTo(new Point(
                        x,
                        headCenter.Y - headRadius * 0.78d - crestHeight * (0.72d + 0.28d * Math.Sin(Math.PI * t))));
                    path.LineTo(new Point(x + headRadius * 0.22d, headCenter.Y - headRadius * 0.58d));
                    path.EndFigure(true);
                }
                context.DrawGeometry(accentBrush, outlinePen, crest);
            }
        }

        if (!model.HasHeadHorns)
        {
            return;
        }

        var hornLength = frame.Height * (
            0.045d + 0.095d * Math.Clamp(model.HornLength, 0d, 1.8d) / 1.8d);
        var curvature = Math.Clamp(model.HornCurvature, 0d, 1d);
        foreach (var offset in new[] { -0.46d, 0.18d })
        {
            var basePoint = new Point(
                headCenter.X + offset * headRadius,
                headCenter.Y - headRadius * 0.66d);
            var tip = new Point(
                basePoint.X - hornLength * (0.12d + curvature * 0.62d),
                basePoint.Y - hornLength * (0.9d - curvature * 0.12d));
            var horn = new StreamGeometry();
            using (var path = horn.Open())
            {
                path.BeginFigure(
                    new Point(basePoint.X - headRadius * 0.16d, basePoint.Y),
                    true);
                path.CubicBezierTo(
                    new Point(basePoint.X - hornLength * curvature, basePoint.Y - hornLength * 0.34d),
                    new Point(tip.X - headRadius * 0.08d, tip.Y + hornLength * 0.22d),
                    tip);
                path.CubicBezierTo(
                    new Point(tip.X + headRadius * 0.08d, tip.Y + hornLength * 0.24d),
                    new Point(basePoint.X + headRadius * 0.14d, basePoint.Y - hornLength * 0.16d),
                    new Point(basePoint.X + headRadius * 0.16d, basePoint.Y));
                path.EndFigure(true);
            }
            context.DrawGeometry(accentBrush, outlinePen, horn);
        }
    }

    private static void DrawPupil(
        DrawingContext context,
        Point eye,
        double headRadius,
        BreedablePupilShape pupilShape)
    {
        switch (pupilShape)
        {
            case BreedablePupilShape.Round:
                context.DrawEllipse(
                    Brushes.Black,
                    null,
                    eye,
                    headRadius * 0.12d,
                    headRadius * 0.12d);
                return;
            case BreedablePupilShape.VerticalSlit:
                context.DrawEllipse(
                    Brushes.Black,
                    null,
                    eye,
                    headRadius * 0.075d,
                    headRadius * 0.16d);
                return;
            case BreedablePupilShape.HorizontalSlit:
                context.DrawEllipse(
                    Brushes.Black,
                    null,
                    eye,
                    headRadius * 0.16d,
                    headRadius * 0.065d);
                return;
        }

        var points = pupilShape == BreedablePupilShape.Diamond ? 4 : 10;
        var pupil = new StreamGeometry();
        using (var path = pupil.Open())
        {
            for (var index = 0; index < points; index++)
            {
                var angle = -Math.PI * 0.5d + index * Math.PI * 2d / points;
                var alternating = points == 10 && index % 2 == 1;
                var radiusX = headRadius * (alternating ? 0.06d : 0.15d);
                var radiusY = headRadius * (alternating ? 0.06d : 0.16d);
                var point = new Point(
                    eye.X + Math.Cos(angle) * radiusX,
                    eye.Y + Math.Sin(angle) * radiusY);
                if (index == 0) path.BeginFigure(point, true);
                else path.LineTo(point);
            }
            path.EndFigure(true);
        }
        context.DrawGeometry(Brushes.Black, null, pupil);
    }

    private static void DrawGillTufts(
        DrawingContext context,
        Point headCenter,
        double headRadius,
        Rect frame,
        LizardPortraitModel model,
        IBrush accentBrush,
        Pen outlinePen)
    {
        if (!model.HasGillTufts)
        {
            return;
        }

        var length = frame.Width * (
            0.035d + 0.075d * Math.Clamp(model.GillTuftLength, 0d, 1.7d) / 1.7d);
        var basePoint = new Point(
            headCenter.X - headRadius * 0.82d,
            headCenter.Y + headRadius * 0.05d);
        for (var branch = -2; branch <= 2; branch++)
        {
            var angle = Math.PI + branch * 0.22d;
            var end = new Point(
                basePoint.X + Math.Cos(angle) * length,
                basePoint.Y + Math.Sin(angle) * length);
            context.DrawLine(outlinePen, basePoint, end);
            context.DrawLine(
                new Pen(
                    accentBrush,
                    Math.Max(1.2d, frame.Width * 0.009d),
                    lineCap: PenLineCap.Round),
                basePoint,
                end);
            context.DrawEllipse(
                accentBrush,
                null,
                end,
                frame.Width * 0.008d,
                frame.Height * 0.008d);
        }
    }

    private static void DrawWhiskers(
        DrawingContext context,
        Point snout,
        double headRadius,
        Rect frame,
        LizardPortraitModel model,
        IBrush accentBrush)
    {
        if (!model.HasWhiskers)
        {
            return;
        }
        var length = frame.Width * (0.06d + 0.09d * Math.Clamp(model.WhiskerLength, 0d, 2.4d) / 2.4d);
        var pen = new Pen(
            accentBrush,
            Math.Max(1d, frame.Width * 0.006d),
            lineCap: PenLineCap.Round);
        var pairCount = Math.Clamp(model.WhiskerPairs, 1, 4);
        for (var pair = 0; pair < pairCount; pair++)
        {
            var spread = pairCount == 1
                ? 0.42d
                : 0.18d + pair * 0.48d / (pairCount - 1);
            foreach (var side in new[] { -1d, 1d })
            {
                var vertical = side * spread;
                var start = new Point(
                    snout.X - headRadius * 0.08d,
                    snout.Y + vertical * headRadius * 0.38d);
                var horizontalLength = Math.Min(
                    length * (0.66d - pair * 0.035d),
                    Math.Max(0d, frame.Right - start.X - frame.Width * 0.018d));
                var end = new Point(
                    start.X + horizontalLength,
                    start.Y + vertical * frame.Height * 0.055d);
                context.DrawLine(pen, start, end);
            }
        }
    }

    /// <summary>
    /// Conservative normalized bounds for the same layout equations used by
    /// the renderer. Tests gate the all-low/all-high extremes through this
    /// method so newly added ornaments cannot silently disappear behind the
    /// square portrait clip.
    /// </summary>
    internal static Rect CalculatePortraitBounds(LizardPortraitModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var visual = model.Visual;
        var body = visual.Body;
        var tail = visual.Tail;
        var spineArch = Math.Clamp(body.SpineArch, -0.35f, 0.65f);
        var centerX = 0.555d;
        var centerY = 0.54d - spineArch * 0.035d;
        var radiusX = 0.175d + 0.052d *
            Math.Clamp(body.LengthRatio, 0.5f, 1.8f);
        var radiusY =
            0.058d +
            0.027d * Math.Clamp(body.WidthRatio, 0.45f, 1.7f) +
            0.018d * Math.Clamp(body.HeightRatio, 0.45f, 1.7f) +
            0.018d * Math.Clamp(body.BellyRoundness, 0f, 1f);
        var headRadius = 0.052d + 0.020d *
            Math.Clamp(body.HeadSizeRatio, 0.5f, 1.8f);
        var headRadiusX = headRadius *
            Math.Clamp(body.HeadWidthRatio, 0.5f, 1.8f);
        var neckReach = 0.72d + 0.17d *
            Normalize(body.NeckLengthRatio, 0.35d, 1.8d);
        var headX = centerX + radiusX * neckReach;
        var headY = centerY - radiusY * (0.20d + spineArch * 0.22d);
        var snoutLength = headRadius * (
            0.32d + 0.54d * Normalize(body.SnoutLengthRatio, 0.3d, 2d));
        var snoutTipX = headX + headRadiusX * 0.58d + snoutLength;

        var tailBaseX = centerX - radiusX * 0.92d;
        var tailLength = 0.12d + 0.08d *
            Normalize(tail.LengthRatio, 0.4d, 3d);
        var tailTipX = tailBaseX - tailLength;
        var tailTipY = centerY + 0.035d +
            0.075d * (1d - Math.Clamp(tail.Flexibility, 0.15f, 1f));
        var clubExtent = tail.Club.IsPresent
            ? (0.023d + 0.029d * Normalize(tail.Club.SizeRatio, 0d, 2.2d)) *
              (1.15d + 0.60d * Normalize(tail.Club.SpikeLengthRatio, 0d, 1.6d))
            : 0d;
        var forkLength = tail.Fork.IsPresent
            ? 0.025d + 0.13d * Normalize(tail.Fork.LengthRatio, 0d, 0.58d)
            : 0d;
        var minX = Math.Min(
            centerX - radiusX,
            tailTipX - Math.Max(clubExtent, forkLength * 0.26d)) - 0.012d;
        var maxX = Math.Max(centerX + radiusX, snoutTipX) + 0.014d;

        var dorsalHeight = visual.Appendages.DorsalFin.IsPresent
            ? 0.045d + 0.06d *
              Normalize(visual.Appendages.DorsalFin.HeightRatio, 0d, 1.8d)
            : 0d;
        var hornHeight = model.HasHeadHorns
            ? 0.045d + 0.095d * Normalize(model.HornLength, 0d, 1.8d)
            : 0d;
        var frillRadius = visual.Appendages.NeckFrill.IsPresent
            ? headRadius + 0.025d +
              Normalize(visual.Appendages.NeckFrill.SizeRatio, 0d, 2.2d) * 0.07d
            : headRadius;
        var minY = Math.Min(
            centerY - radiusY - dorsalHeight,
            Math.Min(headY - headRadius - hornHeight, headY - frillRadius)) - 0.014d;

        // The limb formula is deliberately over-approximated here: maximum
        // pair/front-rear/asymmetry scaling plus seven toes and full claws.
        var limbReach = 0.238d;
        var footReach = 0.066d;
        var forkReach = forkLength * 0.58d;
        var maxY = Math.Max(
            centerY + radiusY * 0.55d + limbReach + footReach,
            tailTipY + Math.Max(clubExtent, forkReach)) + 0.012d;
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    internal static double NormalizeHeadCrestHeight(double value) =>
        Normalize(value, 0d, 1.5d);

    internal static double CalculatePatternGlowOpacity(BreedablePattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return pattern.IsGlowing && pattern.GlowIntensity > 0f
            ? 0.06d + Math.Clamp(pattern.GlowIntensity, 0f, 1f) * 0.24d
            : 0d;
    }

    internal static double CalculateTailSpikeParameter(
        int index,
        int requestedCount)
    {
        var count = Math.Clamp(requestedCount, 1, MaximumRenderedTailSpikes);
        if (index < 0 || index >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        // Continuous interior samples keep every inherited spike count
        // perceptible even when the tail itself uses only three display segments.
        return (index + 1d) / (count + 1d);
    }

    private static void DrawPolygon(
        DrawingContext context,
        IBrush? fill,
        Pen? pen,
        IReadOnlyList<Point> points)
    {
        if (points.Count < 3)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(points[0], true);
            for (var index = 1; index < points.Count; index++)
            {
                path.LineTo(points[index]);
            }
            path.EndFigure(true);
        }
        context.DrawGeometry(fill, pen, geometry);
    }

    private static Point CubicPoint(
        Point start,
        Point firstControl,
        Point secondControl,
        Point end,
        double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        var inverse = 1d - amount;
        var firstWeight = inverse * inverse * inverse;
        var secondWeight = 3d * inverse * inverse * amount;
        var thirdWeight = 3d * inverse * amount * amount;
        var fourthWeight = amount * amount * amount;
        return new Point(
            start.X * firstWeight +
            firstControl.X * secondWeight +
            secondControl.X * thirdWeight +
            end.X * fourthWeight,
            start.Y * firstWeight +
            firstControl.Y * secondWeight +
            secondControl.Y * thirdWeight +
            end.Y * fourthWeight);
    }

    private static Point CubicTangent(
        Point start,
        Point firstControl,
        Point secondControl,
        Point end,
        double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        var inverse = 1d - amount;
        return new Point(
            3d * inverse * inverse * (firstControl.X - start.X) +
            6d * inverse * amount * (secondControl.X - firstControl.X) +
            3d * amount * amount * (end.X - secondControl.X),
            3d * inverse * inverse * (firstControl.Y - start.Y) +
            6d * inverse * amount * (secondControl.Y - firstControl.Y) +
            3d * amount * amount * (end.Y - secondControl.Y));
    }

    private static double Normalize(double value, double minimum, double maximum)
    {
        if (!double.IsFinite(value) || maximum <= minimum)
        {
            return 0.5d;
        }
        return Math.Clamp((value - minimum) / (maximum - minimum), 0d, 1d);
    }

    private static Color ToColor(BreedableColor color) =>
        Color.FromRgb(color.Red, color.Green, color.Blue);

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Blend(Color first, Color second, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        static byte Channel(byte first, byte second, double amount) =>
            (byte)Math.Clamp(
                (int)Math.Round(first + (second - first) * amount),
                0,
                255);
        return Color.FromRgb(
            Channel(first.R, second.R, amount),
            Channel(first.G, second.G, amount),
            Channel(first.B, second.B, amount));
    }

    private static Point At(Rect frame, double x, double y) => new(
        frame.X + frame.Width * x,
        frame.Y + frame.Height * y);

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        hue = ((hue % 360d) + 360d) % 360d / 360d;
        saturation = Math.Clamp(saturation, 0d, 1d);
        lightness = Math.Clamp(lightness, 0d, 1d);
        if (saturation <= 0.00001d)
        {
            var gray = (byte)Math.Round(lightness * 255d);
            return Color.FromRgb(gray, gray, gray);
        }

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
