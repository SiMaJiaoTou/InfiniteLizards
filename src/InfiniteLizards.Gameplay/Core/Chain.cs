using System.Numerics;

namespace DesktopLizard.Core;

/// <summary>
/// A distance-constrained 2D joint chain. The angle-constrained resolve and
/// two-pass FABRIK solve are adapted from argonautcode/animal-proc-anim (MIT).
/// </summary>
internal sealed class Chain
{
    public List<Vector2> Joints { get; }
    public List<float> Angles { get; }
    public float LinkSize { get; }
    public float AngleConstraint { get; }

    public Chain(Vector2 origin, int jointCount, float linkSize, float angleConstraint = MathEx.TwoPi)
    {
        if (jointCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(jointCount));
        }

        LinkSize = linkSize;
        AngleConstraint = angleConstraint;
        Joints = new List<Vector2>(jointCount);
        Angles = new List<float>(jointCount);

        for (var i = 0; i < jointCount; i++)
        {
            Joints.Add(origin + new Vector2(0f, linkSize * i));
            Angles.Add(0f);
        }
    }

    public void Reset(Vector2 head, float heading)
    {
        var forward = MathEx.FromAngle(heading);
        for (var i = 0; i < Joints.Count; i++)
        {
            Joints[i] = head - forward * (LinkSize * i);
            Angles[i] = MathEx.SimplifyAngle(heading);
        }
    }

    public void Translate(Vector2 delta)
    {
        for (var i = 0; i < Joints.Count; i++)
        {
            Joints[i] += delta;
        }
    }

    public void SetPose(IReadOnlyList<Vector2> positions)
    {
        if (positions.Count != Joints.Count)
        {
            throw new ArgumentException("Pose joint count does not match the chain.", nameof(positions));
        }

        for (var i = 0; i < Joints.Count; i++)
        {
            Joints[i] = positions[i];
        }

        for (var i = 0; i < Joints.Count; i++)
        {
            var direction = i == 0
                ? Joints[0] - Joints[1]
                : Joints[i - 1] - Joints[i];
            Angles[i] = MathF.Atan2(direction.Y, direction.X);
        }
    }

    public void SetPose(Vector2 first, Vector2 second, Vector2 third)
    {
        if (Joints.Count != 3)
        {
            throw new InvalidOperationException("The three-point pose overload requires a three-joint chain.");
        }

        Joints[0] = first;
        Joints[1] = second;
        Joints[2] = third;
        Angles[0] = MathF.Atan2(first.Y - second.Y, first.X - second.X);
        Angles[1] = MathF.Atan2(first.Y - second.Y, first.X - second.X);
        Angles[2] = MathF.Atan2(second.Y - third.Y, second.X - third.X);
    }

    public void ApplyRestCurve(float amplitude)
    {
        if (Joints.Count < 3 || MathF.Abs(amplitude) < 0.0001f)
        {
            return;
        }

        for (var i = 1; i < Joints.Count; i++)
        {
            var t = i / (float)(Joints.Count - 1);
            var normal = MathEx.Perpendicular(MathEx.FromAngle(Angles[i]));
            Joints[i] += normal * (amplitude * t * t);
        }

        Resolve(Joints[0]);
    }

    public void Resolve(Vector2 position)
    {
        var initialDirection = position - Joints[0];
        if (initialDirection.LengthSquared() > 0.000001f)
        {
            Angles[0] = MathF.Atan2(initialDirection.Y, initialDirection.X);
        }

        Joints[0] = position;
        for (var i = 1; i < Joints.Count; i++)
        {
            var direction = Joints[i - 1] - Joints[i];
            var currentAngle = MathF.Atan2(direction.Y, direction.X);
            Angles[i] = ConstrainAngle(currentAngle, Angles[i - 1], AngleConstraint);
            Joints[i] = Joints[i - 1] - MathEx.FromAngle(Angles[i]) * LinkSize;
        }
    }

    public void FabrikResolve(Vector2 position, Vector2 anchor)
    {
        Joints[0] = position;
        for (var i = 1; i < Joints.Count; i++)
        {
            Joints[i] = ConstrainDistance(Joints[i], Joints[i - 1], LinkSize);
        }

        Joints[^1] = anchor;
        for (var i = Joints.Count - 2; i >= 0; i--)
        {
            Joints[i] = ConstrainDistance(Joints[i], Joints[i + 1], LinkSize);
        }
    }

    private static Vector2 ConstrainDistance(Vector2 position, Vector2 anchor, float distance)
    {
        var direction = MathEx.SafeNormalize(position - anchor, Vector2.UnitX);
        return anchor + direction * distance;
    }

    private static float ConstrainAngle(float angle, float anchor, float constraint)
    {
        var difference = MathEx.DeltaAngle(angle, anchor);
        if (MathF.Abs(difference) <= constraint)
        {
            return MathEx.SimplifyAngle(angle);
        }

        return MathEx.SimplifyAngle(anchor - MathF.CopySign(constraint, difference));
    }
}
