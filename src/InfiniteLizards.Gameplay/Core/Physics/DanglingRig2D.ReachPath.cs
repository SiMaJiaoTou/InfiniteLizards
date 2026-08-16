using System.Numerics;

namespace DesktopLizard.Core;

internal sealed partial class DanglingRig2D
{
    private const float ReachPathTwoPi = MathF.PI * 2f;
    private const float ReachPathClearanceTolerance = 1.5f;
    private const float ReachPathContactClearanceTolerance = 0f;
    private const float ReachPathGeometryEpsilon = 0.0001f;
    private const int ReachPathProgressSamples = 32;
    private const int ReachPathEnvelopeSamples = 24;
    private const float ReachBranchExtensionEnd = 0.12f;
    private const float ReachBranchSwitchAmount = 0.18f;
    private const float ReachBranchPlateauEnd = 0.25f;
    private const float ReachBranchMinimumStraightness = 0.99f;
    private const float ReachBranchMaximumElbowJump = 0.75f;
    private const int ReachSafetyProjectionSamples = 24;
    private const float ReachSafetyProjectionStep = MathF.PI / 180f;
    private const float ReachSafetyProjectionMaximumFrameJump = 4f;

    private readonly record struct FreeFallReachPathPlan(
        bool UsesWaypoint,
        Vector2 WaypointDirection,
        int FirstWinding,
        int SecondWinding,
        float Split,
        float WaypointRadius,
        bool FlipsBend);

    private readonly record struct ReachPathEvaluation(
        int ReentryCount,
        int ClearanceViolationCount,
        int MotionViolationCount,
        float MinimumClearance,
        float AngularTravel);

    private FreeFallReachPathPlan SelectFreeFallReachPath(
        ProceduralLizard lizard,
        int legIndex,
        int bodyIndex,
        Vector2 shoulder,
        Vector2 startFoot,
        Vector2 targetFoot,
        float startBendSign,
        float targetBendSign)
    {
        return SelectFreeFallReachPath(
            lizard,
            legIndex,
            bodyIndex,
            shoulder,
            startFoot,
            targetFoot,
            startBendSign,
            targetBendSign,
            out _);
    }

    private FreeFallReachPathPlan SelectFreeFallReachPath(
        ProceduralLizard lizard,
        int legIndex,
        int bodyIndex,
        Vector2 shoulder,
        Vector2 startFoot,
        Vector2 targetFoot,
        float startBendSign,
        float targetBendSign,
        out bool contactSafe)
    {
        var upperLength = _upperLegLengths[legIndex];
        var lowerLength = _lowerLegLengths[legIndex];
        var physicalMinimum = MathF.Abs(upperLength - lowerLength) + 0.001f;
        var physicalMaximum = upperLength + lowerLength - 0.001f;
        var startOffset = startFoot - shoulder;
        var targetOffset = targetFoot - shoulder;
        var startRadius = Math.Clamp(
            startOffset.Length(),
            physicalMinimum,
            Math.Max(physicalMinimum, physicalMaximum));
        var targetRadius = Math.Clamp(
            targetOffset.Length(),
            physicalMinimum,
            Math.Max(physicalMinimum, physicalMaximum));
        var startDirection = MathEx.SafeNormalize(startOffset, -Vector2.UnitY);
        var targetDirection = MathEx.SafeNormalize(targetOffset, -Vector2.UnitY);
        var startAngle = MathF.Atan2(startDirection.Y, startDirection.X);
        var targetAngle = MathF.Atan2(targetDirection.Y, targetDirection.X);

        if (Vector2.DistanceSquared(startFoot, targetFoot) <= 0.000001f)
        {
            contactSafe = true;
            return new FreeFallReachPathPlan(
                false,
                Vector2.Zero,
                0,
                0,
                0.45f,
                startRadius,
                false);
        }

        var hasBest = false;
        var bestPlan = default(FreeFallReachPathPlan);
        var bestEvaluation = default(ReachPathEvaluation);
        var flipsBend = startBendSign != targetBendSign;
        var headward = MathEx.SafeNormalize(
            _spine[Math.Max(0, bodyIndex - 1)].Position -
            _spine[bodyIndex].Position,
            MathEx.FromAngle(lizard.Spine.Angles[bodyIndex]));
        var bodyOutward = MathEx.SafeNormalize(
            shoulder - _spine[bodyIndex].Position,
            MathEx.Perpendicular(headward) *
            DanglingTopology2D.GetSide(legIndex));
        if (flipsBend)
        {
            for (var directionMode = -1; directionMode <= 1; directionMode++)
            {
                var winding = ResolveWinding(
                    MathEx.DeltaAngle(startAngle, targetAngle),
                    directionMode);
                ConsiderReachPath(
                    new FreeFallReachPathPlan(
                        false,
                        Vector2.Zero,
                        winding,
                        0,
                        0.45f,
                        physicalMaximum,
                        true),
                    lizard,
                    legIndex,
                    bodyIndex,
                    shoulder,
                    startAngle,
                    targetAngle,
                    startRadius,
                    targetRadius,
                    physicalMinimum,
                    physicalMaximum,
                    startBendSign,
                    targetBendSign,
                    ref hasBest,
                    ref bestPlan,
                    ref bestEvaluation);
            }
        }
        else
        {
            var waypointAngle = MathF.Atan2(bodyOutward.Y, bodyOutward.X);
            for (var firstMode = -1; firstMode <= 1; firstMode++)
            {
                var firstWinding = ResolveWinding(
                    MathEx.DeltaAngle(startAngle, waypointAngle),
                    firstMode);
                for (var secondMode = -1; secondMode <= 1; secondMode++)
                {
                    var secondWinding = ResolveWinding(
                        MathEx.DeltaAngle(waypointAngle, targetAngle),
                        secondMode);
                    ConsiderReachPath(
                        new FreeFallReachPathPlan(
                            true,
                            bodyOutward,
                            firstWinding,
                            secondWinding,
                            0.32f,
                            physicalMaximum,
                            false),
                        lizard,
                        legIndex,
                        bodyIndex,
                        shoulder,
                        startAngle,
                        targetAngle,
                        startRadius,
                        targetRadius,
                        physicalMinimum,
                        physicalMaximum,
                        startBendSign,
                        targetBendSign,
                        ref hasBest,
                        ref bestPlan,
                        ref bestEvaluation);
                }
            }
        }

        contactSafe = hasBest && IsReachPathContactSafe(bestEvaluation);
        return hasBest
            ? bestPlan
            : new FreeFallReachPathPlan(
                true,
                bodyOutward,
                0,
                0,
                0.45f,
                Math.Max(startRadius, targetRadius),
                startBendSign != targetBendSign);
    }

    private void ConsiderReachPath(
        FreeFallReachPathPlan candidate,
        ProceduralLizard lizard,
        int legIndex,
        int bodyIndex,
        Vector2 shoulder,
        float startAngle,
        float targetAngle,
        float startRadius,
        float targetRadius,
        float physicalMinimum,
        float physicalMaximum,
        float startBendSign,
        float targetBendSign,
        ref bool hasBest,
        ref FreeFallReachPathPlan bestPlan,
        ref ReachPathEvaluation bestEvaluation)
    {
        var waypointDirection = MathEx.SafeNormalize(
            candidate.WaypointDirection,
            -Vector2.UnitY);
        if (ReachPathAngularTravel(
                candidate,
                waypointDirection,
                startAngle,
                targetAngle) > MathF.PI * 1.50f)
        {
            return;
        }
        var evaluation = EvaluateReachPathGeometry(
            candidate,
            lizard,
            legIndex,
            bodyIndex,
            shoulder,
            startAngle,
            targetAngle,
            startRadius,
            targetRadius,
            physicalMinimum,
            physicalMaximum,
            startBendSign,
            targetBendSign);
        if (!hasBest || IsBetterReachPath(evaluation, bestEvaluation))
        {
            hasBest = true;
            bestPlan = candidate;
            bestEvaluation = evaluation;
        }
    }

    private ReachPathEvaluation EvaluateReachPathGeometry(
        FreeFallReachPathPlan plan,
        ProceduralLizard lizard,
        int legIndex,
        int bodyIndex,
        Vector2 shoulder,
        float startAngle,
        float targetAngle,
        float startRadius,
        float targetRadius,
        float physicalMinimum,
        float physicalMaximum,
        float startBendSign,
        float targetBendSign)
    {
        var reentryCount = 0;
        var clearanceViolationCount = 0;
        var motionViolationCount = 0;
        var minimumClearance = float.PositiveInfinity;
        var limbRadius = lizard.Profile.Appearance.LimbWidth * 0.5f;
        var retentionFloor = Math.Min(
            targetRadius,
            Math.Max(physicalMinimum, startRadius * 0.72f));
        var waypointDirection = MathEx.SafeNormalize(
            plan.WaypointDirection,
            -Vector2.UnitY);
        var startFoot = shoulder + MathEx.FromAngle(startAngle) * startRadius;
        var targetFoot = shoulder + MathEx.FromAngle(targetAngle) * targetRadius;
        var catchVector = targetFoot - startFoot;
        var catchDistance = catchVector.Length();
        var catchDirection = MathEx.SafeNormalize(catchVector, -Vector2.UnitY);
        var previousCatchAdvance = 0f;

        for (var sample = 0; sample <= ReachPathProgressSamples; sample++)
        {
            var progress = sample / (float)ReachPathProgressSamples;
            var amount = SmoothStep(progress);
            var (angle, plannedRadius) = EvaluateFreeFallReachPath(
                plan,
                waypointDirection,
                amount,
                startAngle,
                targetAngle,
                startRadius,
                targetRadius,
                physicalMinimum,
                physicalMaximum);
            var radius = Math.Max(retentionFloor, plannedRadius);
            var foot = shoulder + MathEx.FromAngle(angle) * radius;
            var catchAdvance = Vector2.Dot(
                foot - startFoot,
                catchDirection);
            var minimumExpectedAdvance =
                catchDistance * progress * 0.12f;
            if (sample > 0 &&
                (catchAdvance + 0.05f < previousCatchAdvance ||
                 catchAdvance + 0.05f < minimumExpectedAdvance))
            {
                motionViolationCount++;
            }
            previousCatchAdvance = Math.Max(previousCatchAdvance, catchAdvance);
            var bendSign = plan.FlipsBend && amount <= ReachBranchPlateauEnd
                ? startBendSign
                : targetBendSign;
            var elbow = sample == 0
                ? _elbows[legIndex].Position
                : SolveReachElbow(
                    shoulder,
                    foot,
                    legIndex,
                    bendSign);
            AccumulateReachPoseGeometry(
                lizard,
                bodyIndex,
                shoulder,
                elbow,
                foot,
                limbRadius,
                ref reentryCount,
                ref clearanceViolationCount,
                ref minimumClearance);
        }

        if (plan.FlipsBend)
        {
            // A coarse fixed-step host may hold the captured branch at full
            // extension before committing the alternate branch. Score both
            // observable plateau poses; after the plateau only the alternate
            // branch is reachable.
            var (plateauAngle, plateauRadius) = EvaluateFreeFallReachPath(
                plan,
                waypointDirection,
                ReachBranchPlateauEnd,
                startAngle,
                targetAngle,
                startRadius,
                targetRadius,
                physicalMinimum,
                physicalMaximum);
            var plateauFoot = shoulder +
                              MathEx.FromAngle(plateauAngle) *
                              Math.Max(retentionFloor, plateauRadius);
            var plateauElbow = SolveReachElbow(
                shoulder,
                plateauFoot,
                legIndex,
                targetBendSign);
            AccumulateReachPoseGeometry(
                lizard,
                bodyIndex,
                shoulder,
                plateauElbow,
                plateauFoot,
                limbRadius,
                ref reentryCount,
                ref clearanceViolationCount,
                ref minimumClearance);
        }

        return new ReachPathEvaluation(
            reentryCount,
            clearanceViolationCount,
            motionViolationCount,
            minimumClearance,
            ReachPathAngularTravel(
                plan,
                waypointDirection,
                startAngle,
                targetAngle));
    }

    private bool IsFreeFallReachPathProjectionPrefixSafe(
        FreeFallReachPathPlan plan,
        ProceduralLizard lizard,
        int legIndex,
        int bodyIndex,
        Vector2 shoulder,
        Vector2 startFoot,
        Vector2 targetFoot,
        float startBendSign,
        float targetBendSign)
    {
        if (!plan.FlipsBend)
        {
            return true;
        }

        var upperLength = _upperLegLengths[legIndex];
        var lowerLength = _lowerLegLengths[legIndex];
        var physicalMinimum = MathF.Abs(upperLength - lowerLength) + 0.001f;
        var physicalMaximum = upperLength + lowerLength - 0.001f;
        var startOffset = startFoot - shoulder;
        var targetOffset = targetFoot - shoulder;
        var startRadius = Math.Clamp(
            startOffset.Length(),
            physicalMinimum,
            physicalMaximum);
        var targetRadius = Math.Clamp(
            targetOffset.Length(),
            physicalMinimum,
            physicalMaximum);
        var retentionFloor = Math.Min(
            targetRadius,
            Math.Max(physicalMinimum, startRadius * 0.72f));
        var startAngle = MathF.Atan2(startOffset.Y, startOffset.X);
        var targetAngle = MathF.Atan2(targetOffset.Y, targetOffset.X);
        var waypointDirection = MathEx.SafeNormalize(
            plan.WaypointDirection,
            -Vector2.UnitY);
        var limbRadius = lizard.Profile.Appearance.LimbWidth * 0.5f;
        var reentryCount = 0;
        var clearanceViolationCount = 0;
        var minimumClearance = float.PositiveInfinity;
        const int PrefixSamples = 16;

        for (var sample = 0; sample <= PrefixSamples; sample++)
        {
            var amount = ReachBranchPlateauEnd * sample / PrefixSamples;
            var (angle, plannedRadius) = EvaluateFreeFallReachPath(
                plan,
                waypointDirection,
                amount,
                startAngle,
                targetAngle,
                startRadius,
                targetRadius,
                physicalMinimum,
                physicalMaximum);
            var foot = shoulder +
                       MathEx.FromAngle(angle) *
                       Math.Max(retentionFloor, plannedRadius);
            var elbow = sample == 0
                ? _elbows[legIndex].Position
                : SolveReachElbow(
                    shoulder,
                    foot,
                    legIndex,
                    startBendSign);
            AccumulateReachPoseGeometry(
                lizard,
                bodyIndex,
                shoulder,
                elbow,
                foot,
                limbRadius,
                ref reentryCount,
                ref clearanceViolationCount,
                ref minimumClearance);
        }

        var (plateauAngle, plateauRadius) = EvaluateFreeFallReachPath(
            plan,
            waypointDirection,
            ReachBranchPlateauEnd,
            startAngle,
            targetAngle,
            startRadius,
            targetRadius,
            physicalMinimum,
            physicalMaximum);
        var plateauFoot = shoulder +
                          MathEx.FromAngle(plateauAngle) *
                          Math.Max(retentionFloor, plateauRadius);
        var plateauElbow = SolveReachElbow(
            shoulder,
            plateauFoot,
            legIndex,
            targetBendSign);
        AccumulateReachPoseGeometry(
            lizard,
            bodyIndex,
            shoulder,
            plateauElbow,
            plateauFoot,
            limbRadius,
            ref reentryCount,
            ref clearanceViolationCount,
            ref minimumClearance);

        return reentryCount == 0 &&
               minimumClearance >= ReachPathContactClearanceTolerance;
    }

    private void AccumulateReachPoseGeometry(
        ProceduralLizard lizard,
        int bodyIndex,
        Vector2 shoulder,
        Vector2 elbow,
        Vector2 foot,
        float limbRadius,
        ref int reentryCount,
        ref int clearanceViolationCount,
        ref float minimumClearance)
    {
        Span<Vector2> limbStarts = stackalloc Vector2[2]
        {
            shoulder,
            elbow
        };
        Span<Vector2> limbEnds = stackalloc Vector2[2]
        {
            elbow,
            foot
        };
        for (var limbSegment = 0;
             limbSegment < limbStarts.Length;
             limbSegment++)
        {
            for (var spineSegment = 0;
                 spineSegment < _spineCount - 1;
                 spineSegment++)
            {
                if (Math.Abs(spineSegment - bodyIndex) <= 2)
                {
                    continue;
                }
                var bodyRadius = Math.Max(
                    lizard.GetBodyWidth(spineSegment),
                    lizard.GetBodyWidth(spineSegment + 1));
                var clearance = ReachPathSegmentDistance(
                    limbStarts[limbSegment],
                    limbEnds[limbSegment],
                    _spine[spineSegment].Position,
                    _spine[spineSegment + 1].Position) -
                    bodyRadius - limbRadius;
                minimumClearance = Math.Min(minimumClearance, clearance);
                if (clearance < ReachPathClearanceTolerance)
                {
                    clearanceViolationCount++;
                }
            }

            if (ReachPathReentersOwnedEnvelope(
                    lizard,
                    bodyIndex,
                    limbStarts[limbSegment],
                    limbEnds[limbSegment],
                    limbRadius))
            {
                reentryCount++;
            }
        }
    }

    private static bool IsBetterReachPath(
        ReachPathEvaluation candidate,
        ReachPathEvaluation current)
    {
        var candidateMotionSafe = candidate.MotionViolationCount == 0;
        var currentMotionSafe = current.MotionViolationCount == 0;
        var candidateGeometrySafe = IsReachPathGeometrySafe(candidate);
        var currentGeometrySafe = IsReachPathGeometrySafe(current);
        if (candidateGeometrySafe != currentGeometrySafe)
        {
            return candidateGeometrySafe;
        }
        if (candidateGeometrySafe)
        {
            if (candidateMotionSafe != currentMotionSafe)
            {
                return candidateMotionSafe;
            }
            if (MathF.Abs(candidate.AngularTravel - current.AngularTravel) >
                0.01f)
            {
                return candidate.AngularTravel < current.AngularTravel;
            }
            return candidate.MinimumClearance > current.MinimumClearance;
        }

        var candidateNoReentry = candidate.ReentryCount == 0;
        var currentNoReentry = current.ReentryCount == 0;
        if (candidateNoReentry != currentNoReentry)
        {
            return candidateNoReentry;
        }
        if (MathF.Abs(
                candidate.MinimumClearance - current.MinimumClearance) >
            0.01f)
        {
            return candidate.MinimumClearance > current.MinimumClearance;
        }
        if (candidate.MotionViolationCount != current.MotionViolationCount)
        {
            return candidate.MotionViolationCount < current.MotionViolationCount;
        }
        if (candidate.ReentryCount != current.ReentryCount)
        {
            return candidate.ReentryCount < current.ReentryCount;
        }
        if (candidate.ClearanceViolationCount != current.ClearanceViolationCount)
        {
            return candidate.ClearanceViolationCount <
                   current.ClearanceViolationCount;
        }
        return candidate.AngularTravel < current.AngularTravel;
    }

    private static bool IsReachPathGeometrySafe(ReachPathEvaluation evaluation) =>
        evaluation.ReentryCount == 0 &&
        evaluation.MinimumClearance >= ReachPathClearanceTolerance;

    private static bool IsReachPathContactSafe(ReachPathEvaluation evaluation) =>
        evaluation.ReentryCount == 0 &&
        evaluation.MinimumClearance >= ReachPathContactClearanceTolerance;

    private static (
        float Angle,
        float Radius) EvaluateFreeFallReachPath(
        FreeFallReachPathPlan plan,
        Vector2 waypointDirection,
        float amount,
        float startAngle,
        float targetAngle,
        float startRadius,
        float targetRadius,
        float physicalMinimum,
        float physicalMaximum)
    {
        amount = MathEx.Clamp01(amount);
        if (plan.FlipsBend)
        {
            var branchWaypointAngle = plan.UsesWaypoint
                ? MathF.Atan2(waypointDirection.Y, waypointDirection.X)
                : startAngle;
            var branchFirstDelta = plan.UsesWaypoint
                ? MathEx.DeltaAngle(startAngle, branchWaypointAngle) +
                  plan.FirstWinding * ReachPathTwoPi
                : 0f;
            var branchSecondDelta = plan.UsesWaypoint
                ? MathEx.DeltaAngle(branchWaypointAngle, targetAngle) +
                  plan.SecondWinding * ReachPathTwoPi
                : MathEx.DeltaAngle(startAngle, targetAngle) +
                  plan.FirstWinding * ReachPathTwoPi;
            if (amount <= ReachBranchExtensionEnd)
            {
                var extension = amount / ReachBranchExtensionEnd;
                return (
                    startAngle + branchFirstDelta * extension,
                    Math.Clamp(
                        MathEx.Lerp(startRadius, physicalMaximum, extension),
                        physicalMinimum,
                        physicalMaximum));
            }
            if (amount <= ReachBranchPlateauEnd)
            {
                return (startAngle + branchFirstDelta, physicalMaximum);
            }

            var reachAmount =
                (amount - ReachBranchPlateauEnd) /
                (1f - ReachBranchPlateauEnd);
            return (
                startAngle + branchFirstDelta + branchSecondDelta * reachAmount,
                Math.Clamp(
                    MathEx.Lerp(
                        physicalMaximum,
                        targetRadius,
                        SmoothStep(reachAmount)),
                    physicalMinimum,
                    physicalMaximum));
        }

        if (!plan.UsesWaypoint)
        {
            var delta = MathEx.DeltaAngle(startAngle, targetAngle) +
                        plan.FirstWinding * ReachPathTwoPi;
            var radialAmount = targetRadius < startRadius
                ? SmoothStep(MathEx.Clamp01((amount - 0.22f) / 0.78f))
                : amount;
            return (
                startAngle + delta * amount,
                Math.Clamp(
                    MathEx.Lerp(startRadius, targetRadius, radialAmount),
                    physicalMinimum,
                    physicalMaximum));
        }

        var waypointAngle = MathF.Atan2(
            waypointDirection.Y,
            waypointDirection.X);
        var firstDelta = MathEx.DeltaAngle(startAngle, waypointAngle) +
                         plan.FirstWinding * ReachPathTwoPi;
        var unwrappedWaypoint = startAngle + firstDelta;
        var secondDelta = MathEx.DeltaAngle(waypointAngle, targetAngle) +
                          plan.SecondWinding * ReachPathTwoPi;
        var split = Math.Clamp(plan.Split, 0.05f, 0.95f);
        if (amount <= split)
        {
            var local = amount / split;
            return (
                startAngle + firstDelta * local,
                Math.Clamp(
                    MathEx.Lerp(startRadius, plan.WaypointRadius, local),
                    physicalMinimum,
                    physicalMaximum));
        }

        var secondLocal = (amount - split) / (1f - split);
        return (
            unwrappedWaypoint + secondDelta * secondLocal,
            Math.Clamp(
                MathEx.Lerp(
                    plan.WaypointRadius,
                    targetRadius,
                    secondLocal),
                physicalMinimum,
                physicalMaximum));
    }

    private Vector2 ResolveFreeFallReachWaypointDirection(
        ProceduralLizard lizard,
        int legIndex,
        Vector2 shoulder,
        FreeFallReachPathPlan plan)
    {
        if (!plan.UsesWaypoint)
        {
            return Vector2.Zero;
        }
        return MathEx.SafeNormalize(
            plan.WaypointDirection,
            -Vector2.UnitY);
    }

    private static float ReachPathAngularTravel(
        FreeFallReachPathPlan plan,
        Vector2 waypointDirection,
        float startAngle,
        float targetAngle)
    {
        if (!plan.UsesWaypoint)
        {
            return MathF.Abs(
                MathEx.DeltaAngle(startAngle, targetAngle) +
                plan.FirstWinding * ReachPathTwoPi);
        }
        var waypointAngle = MathF.Atan2(
            waypointDirection.Y,
            waypointDirection.X);
        return MathF.Abs(
                   MathEx.DeltaAngle(startAngle, waypointAngle) +
                   plan.FirstWinding * ReachPathTwoPi) +
               MathF.Abs(
                   MathEx.DeltaAngle(waypointAngle, targetAngle) +
                   plan.SecondWinding * ReachPathTwoPi);
    }

    private float ResolveFreeFallReachPathAmount(int legIndex, float amount)
    {
        var plan = _freeFallReachPathPlans[legIndex];
        if (!plan.FlipsBend || _freeFallReachBendTransitioned[legIndex] ||
            amount < ReachBranchSwitchAmount)
        {
            return amount;
        }

        if (!_freeFallReachExtensionReady[legIndex])
        {
            // First commit the captured branch at full extension. A later
            // fixed step may switch only after that exact pose was observable.
            return Math.Min(amount, ReachBranchPlateauEnd);
        }

        _freeFallReachBendTransitioned[legIndex] = true;
        // A discontinuous host progress jump cannot switch an IK branch after
        // the arm has already shortened. Commit one fully extended sample;
        // the next update may safely continue toward the target.
        return Math.Min(amount, ReachBranchPlateauEnd);
    }

    private void UpdateFreeFallReachExtensionReady(
        int legIndex,
        Vector2 shoulder)
    {
        var plan = _freeFallReachPathPlans[legIndex];
        if (!plan.FlipsBend ||
            _freeFallReachBendTransitioned[legIndex] ||
            _freeFallReachExtensionReady[legIndex])
        {
            return;
        }

        var upperLength = _upperLegLengths[legIndex];
        var lowerLength = _lowerLegLengths[legIndex];
        var armLength = upperLength + lowerLength;
        var foot = _feet[legIndex].Position;
        var footOffset = foot - shoulder;
        var straightness = footOffset.Length() / Math.Max(armLength, 0.0001f);
        if (straightness < ReachBranchMinimumStraightness)
        {
            return;
        }

        var physicalMaximum = Math.Max(
            MathF.Abs(upperLength - lowerLength) + 0.001f,
            armLength - 0.001f);
        var fullFoot = shoulder +
                       MathEx.SafeNormalize(footOffset, -Vector2.UnitY) *
                       physicalMaximum;
        var alternateElbow = SolveReachElbow(
            shoulder,
            fullFoot,
            legIndex,
            _freeFallReachBendSigns[legIndex]);
        if (Vector2.Distance(_elbows[legIndex].Position, alternateElbow) <=
            ReachBranchMaximumElbowJump + ReachPathGeometryEpsilon)
        {
            _freeFallReachExtensionReady[legIndex] = true;
        }
    }

    private bool TryProjectFreeFallReachPoseToSafety(
        ProceduralLizard lizard,
        int legIndex,
        int bodyIndex,
        Vector2 shoulder,
        Vector2 desiredElbow,
        Vector2 desiredFoot,
        float bendSign,
        out Vector2 projectedElbow,
        out Vector2 projectedFoot)
    {
        var previousElbow = _freeFallReachCommittedPoseInitialized[legIndex]
            ? shoulder + _freeFallReachCommittedElbowOffsets[legIndex]
            : _elbows[legIndex].Position;
        var previousFoot = _freeFallReachCommittedPoseInitialized[legIndex]
            ? shoulder + _freeFallReachCommittedFootOffsets[legIndex]
            : _feet[legIndex].Position;
        var desiredOffset = desiredFoot - shoulder;
        var desiredRadius = desiredOffset.Length();
        var desiredAngle = MathF.Atan2(desiredOffset.Y, desiredOffset.X);
        var catchDirection = MathEx.SafeNormalize(
            _freeFallReachTargets[legIndex] - previousFoot,
            desiredOffset);
        var desiredAdvance = Vector2.Dot(
            desiredFoot - previousFoot,
            catchDirection);
        var requiredAdvance = Math.Min(desiredAdvance, 0.42f);
        var hasProjection = false;
        var bestScore = float.PositiveInfinity;
        projectedElbow = desiredElbow;
        projectedFoot = desiredFoot;

        var upperLength = _upperLegLengths[legIndex];
        var lowerLength = _lowerLegLengths[legIndex];
        var physicalMinimum = MathF.Abs(upperLength - lowerLength) + 0.001f;
        var physicalMaximum = upperLength + lowerLength - 0.001f;
        var retentionFloor = Math.Max(
            physicalMinimum,
            _freeFallReachStartOffsets[legIndex].Length() * 0.72f);
        Span<float> radii = stackalloc float[6]
        {
            Math.Clamp(desiredRadius, retentionFloor, physicalMaximum),
            Math.Clamp(desiredRadius * 1.04f, retentionFloor, physicalMaximum),
            Math.Clamp(desiredRadius * 0.96f, retentionFloor, physicalMaximum),
            Math.Clamp(
                (desiredRadius + physicalMaximum) * 0.5f,
                retentionFloor,
                physicalMaximum),
            physicalMaximum,
            Math.Clamp(
                (previousFoot - shoulder).Length(),
                retentionFloor,
                physicalMaximum)
        };

        for (var advancePass = 0; advancePass < 2; advancePass++)
        {
            var minimumAdvance = advancePass == 0
                ? requiredAdvance
                : Math.Min(requiredAdvance, 0f);
            var bestAdvance = float.NegativeInfinity;
            for (var radiusIndex = 0; radiusIndex < radii.Length; radiusIndex++)
            {
                var radius = radii[radiusIndex];
                for (var sample = 0;
                     sample <= ReachSafetyProjectionSamples;
                     sample++)
                {
                    var offset = sample * ReachSafetyProjectionStep;
                    var firstSide = sample == 0 ? 1 : -1;
                    for (var side = firstSide; side <= 1; side += 2)
                    {
                        var candidateFoot = shoulder +
                                            MathEx.FromAngle(
                                                desiredAngle + offset * side) *
                                            radius;
                        var candidateAdvance = Vector2.Dot(
                            candidateFoot - previousFoot,
                            catchDirection);
                        if (candidateAdvance < minimumAdvance - 0.0001f)
                        {
                            continue;
                        }
                        var candidateElbow = SolveReachElbow(
                            shoulder,
                            candidateFoot,
                            legIndex,
                            bendSign);
                        if (Vector2.Distance(candidateFoot, previousFoot) >
                                ReachSafetyProjectionMaximumFrameJump ||
                            Vector2.Distance(candidateElbow, previousElbow) >
                                ReachSafetyProjectionMaximumFrameJump)
                        {
                            continue;
                        }
                        if (!IsFreeFallLimbBodySafe(
                                lizard,
                                legIndex,
                                bodyIndex,
                                shoulder,
                                candidateElbow,
                                candidateFoot))
                        {
                            continue;
                        }

                        var score =
                            Vector2.DistanceSquared(candidateFoot, desiredFoot) * 4f +
                            Vector2.DistanceSquared(candidateElbow, desiredElbow) * 2f +
                            Vector2.DistanceSquared(candidateFoot, previousFoot) * 0.1f +
                            Vector2.DistanceSquared(candidateElbow, previousElbow) * 0.05f;
                        var improvesFallbackAdvance =
                            advancePass == 1 &&
                            candidateAdvance > bestAdvance + 0.0001f;
                        var equivalentFallbackAdvance =
                            advancePass == 1 &&
                            MathF.Abs(candidateAdvance - bestAdvance) <= 0.0001f;
                        if (hasProjection &&
                            !improvesFallbackAdvance &&
                            (!equivalentFallbackAdvance || score >= bestScore))
                        {
                            continue;
                        }
                        hasProjection = true;
                        bestAdvance = candidateAdvance;
                        bestScore = score;
                        projectedElbow = candidateElbow;
                        projectedFoot = candidateFoot;
                    }
                }
            }
            if (hasProjection)
            {
                return true;
            }
        }

        if (IsFreeFallLimbBodySafe(
                lizard,
                legIndex,
                bodyIndex,
                shoulder,
                previousElbow,
                previousFoot))
        {
            projectedElbow = previousElbow;
            projectedFoot = previousFoot;
            return true;
        }
        return false;
    }

    private void CommitFreeFallReachSafePose(
        int legIndex,
        Vector2 shoulder,
        Vector2 elbow,
        Vector2 foot)
    {
        _freeFallReachCommittedElbowOffsets[legIndex] = elbow - shoulder;
        _freeFallReachCommittedFootOffsets[legIndex] = foot - shoulder;
        _freeFallReachCommittedPoseInitialized[legIndex] = true;
    }

    private bool ValidateAppliedFreeFallReachSafety(ProceduralLizard lizard)
    {
        if (!_freeFallReachCaptured)
        {
            return false;
        }

        var poseChanged = false;
        for (var legIndex = 0; legIndex < LegCount; legIndex++)
        {
            var leg = lizard.Legs[legIndex];
            var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
            var shoulder = leg.Shoulder;
            var elbow = leg.Elbow;
            var foot = leg.Foot;
            var plan = _freeFallReachPathPlans[legIndex];
            var hasCommittedPose =
                _freeFallReachCommittedPoseInitialized[legIndex];
            var committedElbow = hasCommittedPose
                ? shoulder + _freeFallReachCommittedElbowOffsets[legIndex]
                : elbow;
            var committedFoot = hasCommittedPose
                ? shoulder + _freeFallReachCommittedFootOffsets[legIndex]
                : foot;
            var requiresBoundedContinuation =
                _freeFallReachRequiresProjection[legIndex] &&
                plan.FlipsBend &&
                _freeFallReachBendTransitioned[legIndex] &&
                _freeFallReachResolvedPathAmounts[legIndex] >
                    ReachBranchPlateauEnd;
            var withinFrameJump =
                Vector2.Distance(elbow, committedElbow) <=
                    ReachSafetyProjectionMaximumFrameJump &&
                Vector2.Distance(foot, committedFoot) <=
                    ReachSafetyProjectionMaximumFrameJump;
            var desiredSafe = IsFreeFallLimbBodySafe(
                    lizard,
                    legIndex,
                    bodyIndex,
                    shoulder,
                    elbow,
                    foot);
            if (desiredSafe &&
                (!requiresBoundedContinuation || withinFrameJump))
            {
                CommitFreeFallReachSafePose(
                    legIndex,
                    shoulder,
                    elbow,
                    foot);
                continue;
            }

            var canProject =
                _freeFallReachProjectionEligible[legIndex] &&
                plan.FlipsBend &&
                _freeFallReachBendTransitioned[legIndex] &&
                _freeFallReachResolvedPathAmounts[legIndex] >
                    ReachBranchPlateauEnd &&
                FreeFallReachProgress < 1f;
            if (canProject &&
                TryProjectFreeFallReachPoseToSafety(
                    lizard,
                    legIndex,
                    bodyIndex,
                    shoulder,
                    elbow,
                    foot,
                    _freeFallReachBendSigns[legIndex],
                    out var projectedElbow,
                    out var projectedFoot))
            {
                _elbows[legIndex].Position = projectedElbow;
                _feet[legIndex].Position = projectedFoot;
                CommitFreeFallReachSafePose(
                    legIndex,
                    shoulder,
                    projectedElbow,
                    projectedFoot);
                _freeFallReachRequiresProjection[legIndex] = true;
                FreeFallReachRequiresProjectionLegMask |= 1 << legIndex;
                FreeFallReachProjectedLegMaskThisStep |= 1 << legIndex;
                FreeFallReachProjectionCount++;
                poseChanged = true;
                continue;
            }

            _freeFallReachProjectionEligible[legIndex] = false;
            FreeFallReachContactSafe = false;
            FreeFallReachRollbackLegMaskThisStep |= 1 << legIndex;
            FreeFallReachRollbackCount++;
            if (!hasCommittedPose)
            {
                RestoreLastValidFreeFallPose(lizard);
                return true;
            }
            if (!IsFreeFallLimbBodySafe(
                    lizard,
                    legIndex,
                    bodyIndex,
                    shoulder,
                    committedElbow,
                    committedFoot))
            {
                RestoreLastValidFreeFallPose(lizard);
                return true;
            }
            _elbows[legIndex].Position = committedElbow;
            _feet[legIndex].Position = committedFoot;
            poseChanged = true;
        }
        return poseChanged;
    }

    private float ResolveFreeFallReachBendSign(int legIndex)
    {
        var plan = _freeFallReachPathPlans[legIndex];
        return plan.FlipsBend && !_freeFallReachBendTransitioned[legIndex]
            ? _freeFallReachStartBendSigns[legIndex]
            : _freeFallReachBendSigns[legIndex];
    }

    private void InitializeFreeFallLimbSafety(ProceduralLizard lizard)
    {
        Array.Clear(_freeFallSafeLimbInitialized);
        PreserveFreeFallLimbBodySafety(lizard);
    }

    /// <summary>
    /// Keeps the ordinary falling pose from becoming an already-intersecting
    /// start pose for the catch. Each safe body-local pose is retained before
    /// the next solver proposal; an unsafe proposal is rolled back per limb,
    /// preserving bone lengths and velocity instead of snapping at Seeking.
    /// </summary>
    private void PreserveFreeFallLimbBodySafety(ProceduralLizard lizard)
    {
        if (_freeFallReachCaptured)
        {
            return;
        }

        for (var legIndex = 0; legIndex < LegCount; legIndex++)
        {
            var bodyIndex = DanglingTopology2D.GetBodyIndex(legIndex);
            var shoulder = GetShoulderPosition(lizard, legIndex, bodyIndex);
            var elbow = _elbows[legIndex].Position;
            var foot = _feet[legIndex].Position;
            if (IsFreeFallLimbBodySafe(
                    lizard,
                    legIndex,
                    bodyIndex,
                    shoulder,
                    elbow,
                    foot))
            {
                CaptureFreeFallSafeLimb(
                    lizard,
                    legIndex,
                    bodyIndex,
                    shoulder,
                    elbow,
                    foot);
                continue;
            }

            if (_freeFallSafeLimbInitialized[legIndex])
            {
                var (headward, bodyOutward) = FreeFallLimbFrame(
                    lizard,
                    legIndex,
                    bodyIndex,
                    shoulder);
                var safeElbow = shoulder +
                    headward * _freeFallSafeElbowOffsets[legIndex].X +
                    bodyOutward * _freeFallSafeElbowOffsets[legIndex].Y;
                var safeFoot = shoulder +
                    headward * _freeFallSafeFootOffsets[legIndex].X +
                    bodyOutward * _freeFallSafeFootOffsets[legIndex].Y;
                if (IsFreeFallLimbBodySafe(
                        lizard,
                        legIndex,
                        bodyIndex,
                        shoulder,
                        safeElbow,
                        safeFoot))
                {
                    ParticleSolver2D.Translate(
                        ref _elbows[legIndex],
                        safeElbow - elbow);
                    ParticleSolver2D.Translate(
                        ref _feet[legIndex],
                        safeFoot - foot);
                    continue;
                }
            }

            TryProjectFreeFallLimbToSafePose(
                lizard,
                legIndex,
                bodyIndex,
                shoulder,
                elbow,
                foot);
        }
    }

    private void CaptureFreeFallSafeLimb(
        ProceduralLizard lizard,
        int legIndex,
        int bodyIndex,
        Vector2 shoulder,
        Vector2 elbow,
        Vector2 foot)
    {
        var (headward, bodyOutward) = FreeFallLimbFrame(
            lizard,
            legIndex,
            bodyIndex,
            shoulder);
        var elbowOffset = elbow - shoulder;
        var footOffset = foot - shoulder;
        _freeFallSafeElbowOffsets[legIndex] = new Vector2(
            Vector2.Dot(elbowOffset, headward),
            Vector2.Dot(elbowOffset, bodyOutward));
        _freeFallSafeFootOffsets[legIndex] = new Vector2(
            Vector2.Dot(footOffset, headward),
            Vector2.Dot(footOffset, bodyOutward));
        _freeFallSafeLimbInitialized[legIndex] = true;
    }

    private (
        Vector2 Headward,
        Vector2 BodyOutward) FreeFallLimbFrame(
        ProceduralLizard lizard,
        int legIndex,
        int bodyIndex,
        Vector2 shoulder)
    {
        var headward = MathEx.SafeNormalize(
            _spine[Math.Max(0, bodyIndex - 1)].Position -
            _spine[bodyIndex].Position,
            MathEx.FromAngle(lizard.Spine.Angles[bodyIndex]));
        var bodyOutward = MathEx.SafeNormalize(
            shoulder - _spine[bodyIndex].Position,
            MathEx.Perpendicular(headward) *
            DanglingTopology2D.GetSide(legIndex));
        return (headward, bodyOutward);
    }

    private bool IsFreeFallLimbBodySafe(
        ProceduralLizard lizard,
        int legIndex,
        int bodyIndex,
        Vector2 shoulder,
        Vector2 elbow,
        Vector2 foot)
    {
        const float SafetyMargin = 0f;
        var limbRadius = lizard.Profile.Appearance.LimbWidth * 0.5f;
        Span<Vector2> starts = stackalloc Vector2[2]
        {
            shoulder,
            elbow
        };
        Span<Vector2> ends = stackalloc Vector2[2]
        {
            elbow,
            foot
        };
        for (var limbSegment = 0; limbSegment < 2; limbSegment++)
        {
            for (var spineSegment = 0;
                 spineSegment < _spineCount - 1;
                 spineSegment++)
            {
                if (Math.Abs(spineSegment - bodyIndex) <= 2)
                {
                    continue;
                }
                var bodyRadius = Math.Max(
                    lizard.GetBodyWidth(spineSegment),
                    lizard.GetBodyWidth(spineSegment + 1));
                var clearance = ReachPathSegmentDistance(
                    starts[limbSegment],
                    ends[limbSegment],
                    _spine[spineSegment].Position,
                    _spine[spineSegment + 1].Position) -
                    bodyRadius - limbRadius;
                if (clearance < SafetyMargin)
                {
                    return false;
                }
            }
            if (ReachPathReentersOwnedEnvelope(
                    lizard,
                    bodyIndex,
                    starts[limbSegment],
                    ends[limbSegment],
                    limbRadius))
            {
                return false;
            }
        }
        return true;
    }

    private bool TryProjectFreeFallLimbToSafePose(
        ProceduralLizard lizard,
        int legIndex,
        int bodyIndex,
        Vector2 shoulder,
        Vector2 capturedElbow,
        Vector2 capturedFoot)
    {
        const int DirectionSamples = 72;
        var upperLength = _upperLegLengths[legIndex];
        var lowerLength = _lowerLegLengths[legIndex];
        var maximumRadius = upperLength + lowerLength - 0.001f;
        var minimumRadius = MathF.Abs(upperLength - lowerLength) + 0.001f;
        var capturedOffset = capturedFoot - shoulder;
        var capturedRadius = Math.Clamp(
            capturedOffset.Length(),
            minimumRadius,
            maximumRadius);
        var capturedDirection = MathEx.SafeNormalize(
            capturedOffset,
            Vector2.UnitY);
        var capturedBend = Vector2.Dot(
            capturedElbow - shoulder,
            MathEx.Perpendicular(capturedDirection));
        var capturedSign = MathF.Abs(capturedBend) > 0.0001f
            ? MathF.Sign(capturedBend)
            : DanglingTopology2D.GetSide(legIndex);
        Span<float> radii = stackalloc float[3]
        {
            capturedRadius,
            Math.Clamp(maximumRadius * 0.82f, minimumRadius, maximumRadius),
            Math.Clamp(maximumRadius * 0.96f, minimumRadius, maximumRadius)
        };

        var found = false;
        var bestScore = float.PositiveInfinity;
        var bestElbow = capturedElbow;
        var bestFoot = capturedFoot;
        foreach (var radius in radii)
        {
            for (var sample = 0; sample < DirectionSamples; sample++)
            {
                var direction = MathEx.FromAngle(
                    -MathF.PI + ReachPathTwoPi * sample / DirectionSamples);
                var candidateFoot = shoulder + direction * radius;
                for (var branch = 0; branch < 2; branch++)
                {
                    var bendSign = branch == 0
                        ? capturedSign
                        : -capturedSign;
                    var candidateElbow = SolveReachElbow(
                        shoulder,
                        candidateFoot,
                        legIndex,
                        bendSign);
                    if (!IsFreeFallLimbBodySafe(
                            lizard,
                            legIndex,
                            bodyIndex,
                            shoulder,
                            candidateElbow,
                            candidateFoot))
                    {
                        continue;
                    }
                    var score =
                        Vector2.DistanceSquared(candidateElbow, capturedElbow) +
                        Vector2.DistanceSquared(candidateFoot, capturedFoot) * 2f +
                        branch * 4f;
                    if (score >= bestScore)
                    {
                        continue;
                    }
                    found = true;
                    bestScore = score;
                    bestElbow = candidateElbow;
                    bestFoot = candidateFoot;
                }
            }
        }

        if (!found)
        {
            return false;
        }
        ParticleSolver2D.Translate(
            ref _elbows[legIndex],
            bestElbow - capturedElbow);
        ParticleSolver2D.Translate(
            ref _feet[legIndex],
            bestFoot - capturedFoot);
        CaptureFreeFallSafeLimb(
            lizard,
            legIndex,
            bodyIndex,
            shoulder,
            bestElbow,
            bestFoot);
        return true;
    }

    private bool ReachPathReentersOwnedEnvelope(
        ProceduralLizard lizard,
        int bodyIndex,
        Vector2 limbStart,
        Vector2 limbEnd,
        float limbRadius,
        int sampleCount = ReachPathEnvelopeSamples)
    {
        var hasLeftEnvelope = false;
        for (var sample = 0; sample <= sampleCount; sample++)
        {
            var point = Vector2.Lerp(
                limbStart,
                limbEnd,
                sample / (float)sampleCount);
            var inside = false;
            for (var spineSegment = 0;
                 spineSegment < _spineCount - 1;
                 spineSegment++)
            {
                if (Math.Abs(spineSegment - bodyIndex) > 2)
                {
                    continue;
                }
                var bodyRadius = Math.Max(
                    lizard.GetBodyWidth(spineSegment),
                    lizard.GetBodyWidth(spineSegment + 1));
                inside |= ReachPathPointSegmentDistance(
                    point,
                    _spine[spineSegment].Position,
                    _spine[spineSegment + 1].Position) <=
                    bodyRadius + limbRadius;
            }
            if (!inside)
            {
                hasLeftEnvelope = true;
            }
            else if (hasLeftEnvelope)
            {
                return true;
            }
        }
        return false;
    }

    private static int ResolveWinding(float shortestDelta, int directionMode) =>
        directionMode switch
        {
            < 0 => shortestDelta > 0f ? -1 : 0,
            > 0 => shortestDelta < 0f ? 1 : 0,
            _ => 0
        };

    private static float ReachPathSegmentDistance(
        Vector2 firstStart,
        Vector2 firstEnd,
        Vector2 secondStart,
        Vector2 secondEnd)
    {
        var firstDirection = firstEnd - firstStart;
        var secondDirection = secondEnd - secondStart;
        var denominator = ReachPathCross(firstDirection, secondDirection);
        if (MathF.Abs(denominator) > ReachPathGeometryEpsilon)
        {
            var between = secondStart - firstStart;
            var firstAmount =
                ReachPathCross(between, secondDirection) / denominator;
            var secondAmount =
                ReachPathCross(between, firstDirection) / denominator;
            if (firstAmount is >= 0f and <= 1f &&
                secondAmount is >= 0f and <= 1f)
            {
                return 0f;
            }
        }
        return Math.Min(
            Math.Min(
                ReachPathPointSegmentDistance(
                    firstStart,
                    secondStart,
                    secondEnd),
                ReachPathPointSegmentDistance(
                    firstEnd,
                    secondStart,
                    secondEnd)),
            Math.Min(
                ReachPathPointSegmentDistance(
                    secondStart,
                    firstStart,
                    firstEnd),
                ReachPathPointSegmentDistance(
                    secondEnd,
                    firstStart,
                    firstEnd)));
    }

    private static float ReachPathPointSegmentDistance(
        Vector2 point,
        Vector2 segmentStart,
        Vector2 segmentEnd)
    {
        var direction = segmentEnd - segmentStart;
        var lengthSquared = direction.LengthSquared();
        if (lengthSquared <=
            ReachPathGeometryEpsilon * ReachPathGeometryEpsilon)
        {
            return Vector2.Distance(point, segmentStart);
        }
        var amount = MathEx.Clamp01(
            Vector2.Dot(point - segmentStart, direction) / lengthSquared);
        return Vector2.Distance(
            point,
            segmentStart + direction * amount);
    }

    private static float ReachPathCross(Vector2 first, Vector2 second) =>
        first.X * second.Y - first.Y * second.X;
}
