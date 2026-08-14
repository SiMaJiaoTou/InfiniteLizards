using System.Numerics;
using DesktopLizard.Diagnostics.Framework;
using DesktopLizard.Windows;

namespace DesktopLizard.Diagnostics;

/// <summary>
/// Platform-free geometry checks for debug-sidecar placement. These scenarios
/// deliberately exercise every side without creating a WPF window.
/// </summary>
internal static class DebugPanelPlacementSelfTest
{
    private const float PanelWidth = 360f;
    private const float PanelHeight = 132f;
    private const float PetRadius = 160f;
    private const float Gap = 12f;
    private const float Tolerance = 0.001f;

    private readonly record struct PlacementCase(
        string Name,
        Vector2 PetCenter,
        DebugPanelPlacementRect WorkArea,
        DebugPanelSide CurrentSide,
        bool ExpectedVisible,
        DebugPanelSide ExpectedSide);

    public static DiagnosticReport RunReport()
    {
        var normalWorkArea = new DebugPanelPlacementRect(0f, 0f, 1920f, 1080f);
        var narrowWorkArea = new DebugPanelPlacementRect(0f, 0f, 900f, 700f);
        var cases = new[]
        {
            new PlacementCase(
                "center-default",
                new Vector2(960f, 540f),
                normalWorkArea,
                DebugPanelSide.None,
                true,
                DebugPanelSide.Right),
            new PlacementCase(
                "center-retain-left",
                new Vector2(960f, 540f),
                normalWorkArea,
                DebugPanelSide.Left,
                true,
                DebugPanelSide.Left),
            new PlacementCase(
                "left-edge",
                new Vector2(120f, 540f),
                normalWorkArea,
                DebugPanelSide.Left,
                true,
                DebugPanelSide.Right),
            new PlacementCase(
                "right-edge",
                new Vector2(1800f, 540f),
                normalWorkArea,
                DebugPanelSide.Right,
                true,
                DebugPanelSide.Left),
            new PlacementCase(
                "top-edge",
                new Vector2(450f, 120f),
                narrowWorkArea,
                DebugPanelSide.None,
                true,
                DebugPanelSide.Below),
            new PlacementCase(
                "bottom-edge",
                new Vector2(450f, 580f),
                narrowWorkArea,
                DebugPanelSide.None,
                true,
                DebugPanelSide.Above),
            new PlacementCase(
                "negative-monitor",
                new Vector2(-960f, -540f),
                new DebugPanelPlacementRect(-1920f, -1080f, 0f, 0f),
                DebugPanelSide.Above,
                true,
                DebugPanelSide.Above),
            new PlacementCase(
                "panel-larger-than-work-area",
                new Vector2(160f, 60f),
                new DebugPanelPlacementRect(0f, 0f, 320f, 120f),
                DebugPanelSide.None,
                false,
                DebugPanelSide.None),
            new PlacementCase(
                "no-side-space",
                new Vector2(300f, 150f),
                new DebugPanelPlacementRect(0f, 0f, 600f, 300f),
                DebugPanelSide.None,
                false,
                DebugPanelSide.None)
        };

        var decisionFailures = 0;
        var containmentFailures = 0;
        var overlapFailures = 0;
        var gapFailures = 0;
        var visiblePlacements = 0;
        var hiddenPlacements = 0;
        var minimumClearance = float.PositiveInfinity;
        var coveredSides = new HashSet<DebugPanelSide>();
        var failureDetails = new List<string>();

        foreach (var testCase in cases)
        {
            var placement = DebugPanelPlacementCalculator.Calculate(
                testCase.PetCenter,
                testCase.WorkArea,
                PanelWidth,
                PanelHeight,
                PetRadius,
                Gap,
                testCase.CurrentSide);
            if (placement.IsVisible != testCase.ExpectedVisible ||
                placement.Side != testCase.ExpectedSide)
            {
                decisionFailures++;
                failureDetails.Add(
                    $"{testCase.Name}: expected {testCase.ExpectedVisible}/{testCase.ExpectedSide}, " +
                    $"actual {placement.IsVisible}/{placement.Side}");
            }

            if (!placement.IsVisible)
            {
                hiddenPlacements++;
                continue;
            }

            visiblePlacements++;
            coveredSides.Add(placement.Side);
            var panelRect = DebugPanelPlacementRect.FromCenter(
                placement.Center,
                PanelWidth,
                PanelHeight);
            var petRect = DebugPanelPlacementRect.FromCenter(
                testCase.PetCenter,
                PetRadius * 2f,
                PetRadius * 2f);
            if (!testCase.WorkArea.Contains(panelRect))
            {
                containmentFailures++;
                failureDetails.Add($"{testCase.Name}: panel escaped work area");
            }
            if (panelRect.Intersects(petRect))
            {
                overlapFailures++;
                failureDetails.Add($"{testCase.Name}: panel overlaps pet render rectangle");
            }

            var clearance = GetAxisClearance(panelRect, petRect);
            minimumClearance = Math.Min(minimumClearance, clearance);
            if (clearance + Tolerance < Gap)
            {
                gapFailures++;
                failureDetails.Add(
                    $"{testCase.Name}: clearance {clearance:F3} < gap {Gap:F3}");
            }
        }

        var allSidesCovered =
            coveredSides.Contains(DebugPanelSide.Right) &&
            coveredSides.Contains(DebugPanelSide.Left) &&
            coveredSides.Contains(DebugPanelSide.Below) &&
            coveredSides.Contains(DebugPanelSide.Above);
        var currentSideRetained = RunCurrentSideRetentionCheck(normalWorkArea);
        var negativeCoordinatesPassed = cases
            .Where(value => value.Name == "negative-monitor")
            .All(value =>
            {
                var result = DebugPanelPlacementCalculator.Calculate(
                    value.PetCenter,
                    value.WorkArea,
                    PanelWidth,
                    PanelHeight,
                    PetRadius,
                    Gap,
                    value.CurrentSide);
                return result.IsVisible && result.Side == value.ExpectedSide;
            });
        var tinyAreasHidden = cases
            .Where(value => !value.ExpectedVisible)
            .All(value =>
                !DebugPanelPlacementCalculator.Calculate(
                    value.PetCenter,
                    value.WorkArea,
                    PanelWidth,
                    PanelHeight,
                    PetRadius,
                    Gap,
                    value.CurrentSide).IsVisible);

        return new DiagnosticReportBuilder(nameof(DebugPanelPlacementSelfTest))
            .AddMetric("cases", cases.Length)
            .AddMetric("visible_placements", visiblePlacements)
            .AddMetric("hidden_placements", hiddenPlacements)
            .AddMetric("minimum_clearance", minimumClearance, "px")
            .AddMetric("decision_failures", decisionFailures)
            .AddMetric("containment_failures", containmentFailures)
            .AddMetric("overlap_failures", overlapFailures)
            .AddMetric("gap_failures", gapFailures)
            .AddCheck(
                "expected decisions",
                decisionFailures == 0,
                "all scenarios choose the expected side or hide",
                decisionFailures == 0 ? "matched" : string.Join("; ", failureDetails))
            .AddCheck(
                "all four sides",
                allSidesCovered,
                "right, left, below and above covered",
                string.Join(", ", coveredSides.OrderBy(side => side)))
            .AddCheck(
                "retain current side",
                currentSideRetained,
                "a still-valid current side remains selected",
                currentSideRetained ? "retained" : "changed")
            .AddCheck(
                "work-area containment",
                containmentFailures == 0,
                "every visible panel rectangle is fully inside the work area",
                containmentFailures.ToString())
            .AddCheck(
                "pet rectangle separation",
                overlapFailures == 0 && gapFailures == 0,
                $"no overlap and >= {Gap:F1} px gap",
                $"overlap {overlapFailures}, gap {gapFailures}, min {minimumClearance:F3} px")
            .AddCheck(
                "negative coordinates",
                negativeCoordinatesPassed,
                "negative-coordinate monitor places normally",
                negativeCoordinatesPassed ? "placed" : "failed")
            .AddCheck(
                "no-space hiding",
                tinyAreasHidden,
                "panel-too-large and no-side-space cases hide",
                tinyAreasHidden ? "hidden" : "visible")
            .Build();
    }

    private static bool RunCurrentSideRetentionCheck(
        DebugPanelPlacementRect workArea)
    {
        var petCenter = new Vector2(960f, 540f);
        var result = DebugPanelPlacementCalculator.Calculate(
            petCenter,
            workArea,
            PanelWidth,
            PanelHeight,
            PetRadius,
            Gap,
            DebugPanelSide.Above);
        return result.IsVisible && result.Side == DebugPanelSide.Above;
    }

    private static float GetAxisClearance(
        DebugPanelPlacementRect panel,
        DebugPanelPlacementRect pet)
    {
        var horizontal = Math.Max(
            pet.Left - panel.Right,
            panel.Left - pet.Right);
        var vertical = Math.Max(
            pet.Top - panel.Bottom,
            panel.Top - pet.Bottom);
        return Math.Max(horizontal, vertical);
    }
}
