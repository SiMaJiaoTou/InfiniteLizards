using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopLizard.Core;
using DesktopLizard.Diagnostics.Framework;

namespace DesktopLizard.Diagnostics;

internal readonly record struct ConfigurationTestResult(
    bool Passed,
    bool DefaultValidationPassed,
    bool RoundTripPassed,
    bool PersistentSeedPassed,
    bool SchemaMigrationPassed,
    bool InvalidFallbackPassed,
    bool InvalidFilePreserved,
    bool DeterministicProfilePassed,
    bool DistinctIndividualsPassed,
    bool VariationApplied,
    bool TransitionNormalizationPassed,
    bool CustomMatrixControlPassed,
    bool UnsafeMatrixRejected,
    bool UnsafeNumericConfigurationRejected,
    bool LostGripConfigurationPassed,
    int WarningCount,
    string FailureDetails);

/// <summary>
/// Verifies the editable JSON boundary independently from the desktop window.
/// The test intentionally exercises persistence, validation, fallback and the
/// resolved per-individual profile so configuration regressions cannot hide
/// behind the default in-memory values.
/// </summary>
internal static class ConfigurationSelfTest
{
    private readonly record struct Measurements(
        bool DefaultValidationPassed,
        bool RoundTripPassed,
        bool PersistentSeedPassed,
        bool SchemaMigrationPassed,
        bool PersistenceFailurePreservesValidConfig,
        bool InvalidFallbackPassed,
        bool InvalidFilePreserved,
        bool DeterministicProfilePassed,
        bool DistinctIndividualsPassed,
        bool VariationApplied,
        bool TransitionNormalizationPassed,
        bool CustomMatrixControlPassed,
        bool UnsafeMatrixRejected,
        bool UnsafeNumericConfigurationRejected,
        bool HardLimitsPassed,
        bool UnknownPropertyRejected,
        bool TimeStepDeterminismPassed,
        bool FrameCatchUpBoundsPassed,
        bool GeometryEnvelopePassed,
        bool DerivedGeometryBoundsPassed,
        bool IndividualVariationControlsPassed,
        bool LostGripConfigurationPassed,
        int WarningCount,
        string FailureDetails);

    public static ConfigurationTestResult Run()
    {
        var measurements = Measure();
        var report = BuildReport(measurements);
        return new ConfigurationTestResult(
            report.Passed,
            measurements.DefaultValidationPassed,
            measurements.RoundTripPassed,
            measurements.PersistentSeedPassed,
            measurements.SchemaMigrationPassed,
            measurements.InvalidFallbackPassed,
            measurements.InvalidFilePreserved,
            measurements.DeterministicProfilePassed,
            measurements.DistinctIndividualsPassed,
            measurements.VariationApplied,
            measurements.TransitionNormalizationPassed,
            measurements.CustomMatrixControlPassed,
            measurements.UnsafeMatrixRejected,
            measurements.UnsafeNumericConfigurationRejected,
            measurements.LostGripConfigurationPassed,
            measurements.WarningCount,
            measurements.FailureDetails);
    }

    public static DiagnosticReport RunReport() => BuildReport(Measure());

    private static Measurements Measure()
    {
        var failures = new List<string>();
        var defaultValidationPassed = LizardConfiguration.Default.Validate().Count == 0;
        var roundTripPassed = false;
        var persistentSeedPassed = false;
        var schemaMigrationPassed = false;
        var persistenceFailurePreservesValidConfig = false;
        var invalidFallbackPassed = false;
        var invalidFilePreserved = false;
        var derivedGeometryFallbackPassed = false;
        var unknownPropertyRejected = false;
        var warningCount = 0;
        var root = Path.Combine(
            Path.GetTempPath(),
            "DesktopLizard.ConfigurationSelfTest",
            Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            var customPath = Path.Combine(root, "custom.json");
            var custom = CreateCustomConfiguration();
            LizardConfigurationStore.Save(custom, customPath);
            var customDocument = JsonNode.Parse(File.ReadAllText(customPath))!.AsObject();
            var customRest = customDocument["Behavior"]!["Rest"]!.AsObject();
            var customLostGrip = customDocument["Behavior"]!["LostGripFall"]!.AsObject();
            var customPhysics = customDocument["Physics"]!.AsObject();
            var customVariationPhysics =
                customDocument["IndividualVariation"]!["Physics"]!.AsObject();
            var derivedPropertiesIgnored =
                !customRest.ContainsKey("MinimumDuration") &&
                !customRest.ContainsKey("MaximumDuration");
            var lostGripFieldsEmitted = new[]
            {
                "MinimumDistance",
                "ReachLeadDistance",
                "MinimumInitialVelocity",
                "MaximumInitialVelocity",
                "Gravity",
                "MaximumFallVelocity",
                "BottomSafetyInset",
                "RegripDuration",
                "ResumeIdleDuration",
                "PointerSuppressionDuration"
            }.All(customLostGrip.ContainsKey) &&
            !customLostGrip.ContainsKey("MaximumDistance");
            var regripPhysicsFieldsEmitted = new[]
            {
                "RegripFrontReachLengthFactor",
                "RegripFrontReachOutwardWeight",
                "RegripRearReachLengthFactor",
                "RegripRearReachOutwardWeight",
                "RegripContactHoldFraction"
            }.All(customPhysics.ContainsKey) &&
            new[]
            {
                "RegripReachAgility",
                "RegripOutwardCuriosity",
                "RegripHoldCalmness"
            }.All(customVariationPhysics.ContainsKey);
            var loaded = LizardConfigurationStore.LoadOrCreate(customPath);
            roundTripPassed =
                loaded.LoadedFromDisk &&
                loaded.Warnings.Count == 0 &&
                derivedPropertiesIgnored &&
                lostGripFieldsEmitted &&
                regripPhysicsFieldsEmitted &&
                loaded.Configuration.IndividualSeed == custom.IndividualSeed &&
                MathF.Abs(
                    loaded.Configuration.Behavior.Speed.ReferenceMinimumCrawl -
                    custom.Behavior.Speed.ReferenceMinimumCrawl) <= 0.0001f &&
                loaded.Configuration.Runtime.DebugPathCapacity == custom.Runtime.DebugPathCapacity &&
                MathF.Abs(
                    loaded.Configuration.Behavior.LostGripFall.Gravity -
                    custom.Behavior.LostGripFall.Gravity) <= 0.0001f &&
                MathF.Abs(
                    loaded.Configuration.Behavior.LostGripFall.ReachLeadDistance -
                    custom.Behavior.LostGripFall.ReachLeadDistance) <= 0.0001f &&
                MathF.Abs(
                    loaded.Configuration.Physics.RegripFrontReachLengthFactor -
                    custom.Physics.RegripFrontReachLengthFactor) <= 0.0001f &&
                MathF.Abs(
                    loaded.Configuration.Physics.RegripFrontReachOutwardWeight -
                    custom.Physics.RegripFrontReachOutwardWeight) <= 0.0001f &&
                MathF.Abs(
                    loaded.Configuration.Physics.RegripRearReachLengthFactor -
                    custom.Physics.RegripRearReachLengthFactor) <= 0.0001f &&
                MathF.Abs(
                    loaded.Configuration.Physics.RegripRearReachOutwardWeight -
                    custom.Physics.RegripRearReachOutwardWeight) <= 0.0001f &&
                MathF.Abs(
                    loaded.Configuration.Physics.RegripContactHoldFraction -
                    custom.Physics.RegripContactHoldFraction) <= 0.0001f &&
                MathF.Abs(
                    loaded.Configuration.IndividualVariation.Physics.RegripReachAgility -
                    custom.IndividualVariation.Physics.RegripReachAgility) <= 0.0001f &&
                MathF.Abs(
                    loaded.Configuration.IndividualVariation.Physics.RegripOutwardCuriosity -
                    custom.IndividualVariation.Physics.RegripOutwardCuriosity) <= 0.0001f &&
                MathF.Abs(
                    loaded.Configuration.IndividualVariation.Physics.RegripHoldCalmness -
                    custom.IndividualVariation.Physics.RegripHoldCalmness) <= 0.0001f &&
                loaded.Configuration.Behavior.TransitionMatrix.AfterForward.Entries.Length == 1 &&
                loaded.Configuration.Behavior.TransitionMatrix.AfterForward.Entries[0].Action ==
                AutonomousAction.FastForward;

            var legacyDerivedPath = Path.Combine(root, "legacy-derived-v2.json");
            customRest["MinimumDuration"] = custom.Behavior.Rest.MinimumDuration;
            customRest["MaximumDuration"] = custom.Behavior.Rest.MaximumDuration;
            File.WriteAllText(legacyDerivedPath, customDocument.ToJsonString());
            var loadedLegacyDerived = LizardConfigurationStore.LoadOrCreate(legacyDerivedPath);
            roundTripPassed =
                roundTripPassed &&
                loadedLegacyDerived.LoadedFromDisk &&
                loadedLegacyDerived.Warnings.Count == 0 &&
                loadedLegacyDerived.Configuration.IndividualSeed == custom.IndividualSeed;

            var generatedPath = Path.Combine(root, "generated.json");
            var generated = LizardConfigurationStore.LoadOrCreate(generatedPath);
            var generatedAgain = LizardConfigurationStore.LoadOrCreate(generatedPath);
            persistentSeedPassed =
                !generated.LoadedFromDisk &&
                generated.Configuration.IndividualSeed is > 0 &&
                generatedAgain.LoadedFromDisk &&
                generatedAgain.Configuration.IndividualSeed == generated.Configuration.IndividualSeed;

            // Make the canonical temporary file path unwritable without
            // relying on platform-specific file permission bits. A directory
            // at that exact path makes File.WriteAllText fail on Windows and
            // Unix while the source document remains readable and valid.
            var readOnlySavePath = Path.Combine(root, "valid-read-only-save.json");
            File.WriteAllText(
                readOnlySavePath,
                $"{{\"SchemaVersion\":{LizardConfiguration.CurrentSchemaVersion}," +
                "\"Behavior\":{\"Speed\":{\"ReferenceMinimumCrawl\":23}}}");
            Directory.CreateDirectory(readOnlySavePath + ".tmp");
            var loadedWithoutWriteBack = LizardConfigurationStore.LoadOrCreate(
                readOnlySavePath);
            persistenceFailurePreservesValidConfig =
                loadedWithoutWriteBack.LoadedFromDisk &&
                loadedWithoutWriteBack.Configuration.IndividualSeed is > 0 &&
                MathF.Abs(
                    loadedWithoutWriteBack.Configuration.Behavior.Speed.ReferenceMinimumCrawl -
                    23f) <= 0.0001f &&
                loadedWithoutWriteBack.Warnings.Count == 1 &&
                loadedWithoutWriteBack.Warnings[0].Contains(
                    "无法写回",
                    StringComparison.Ordinal) &&
                string.Equals(
                    File.ReadAllText(readOnlySavePath),
                    $"{{\"SchemaVersion\":{LizardConfiguration.CurrentSchemaVersion}," +
                    "\"Behavior\":{\"Speed\":{\"ReferenceMinimumCrawl\":23}}}",
                    StringComparison.Ordinal);
            warningCount += loadedWithoutWriteBack.Warnings.Count;

            var legacyPath = Path.Combine(root, "legacy-v1.json");
            File.WriteAllText(legacyPath, "{\"SchemaVersion\":1,\"IndividualSeed\":12345}");
            var migrated = LizardConfigurationStore.LoadOrCreate(legacyPath);
            schemaMigrationPassed =
                migrated.LoadedFromDisk &&
                migrated.Configuration.SchemaVersion == LizardConfiguration.CurrentSchemaVersion &&
                migrated.Configuration.IndividualSeed == 12345 &&
                migrated.Warnings.Count == 1 &&
                File.ReadAllText(legacyPath).Contains("\"SecondaryMotion\"", StringComparison.Ordinal) &&
                File.ReadAllText(legacyPath).Contains("\"Rendering\"", StringComparison.Ordinal) &&
                File.ReadAllText(legacyPath).Contains("\"Runtime\"", StringComparison.Ordinal);
            warningCount += migrated.Warnings.Count;

            var legacyV2Path = Path.Combine(root, "legacy-v2.json");
            File.WriteAllText(
                legacyV2Path,
                "{\"SchemaVersion\":2,\"IndividualSeed\":24680," +
                "\"Behavior\":{\"Speed\":{\"BaseSpeedMultiplier\":1.47}}," +
                "\"Physics\":{\"LimbExtensionDuration\":0.28}," +
                "\"IndividualVariation\":{\"Transitions\":{\"FallbackPersonality\":0.5}}}");
            var migratedV2 = LizardConfigurationStore.LoadOrCreate(legacyV2Path);
            var migratedV2Text = File.ReadAllText(legacyV2Path);
            schemaMigrationPassed =
                schemaMigrationPassed &&
                migratedV2.LoadedFromDisk &&
                migratedV2.Configuration.SchemaVersion == LizardConfiguration.CurrentSchemaVersion &&
                migratedV2.Configuration.IndividualSeed == 24680 &&
                MathF.Abs(
                    migratedV2.Configuration.Behavior.Speed.BoundaryRecoverySpeedMultiplier -
                    1.47f) <= 0.0001f &&
                migratedV2.Warnings.Count == 1 &&
                migratedV2Text.Contains("\"RestWeightVariation\"", StringComparison.Ordinal) &&
                migratedV2Text.Contains("\"Multipliers\"", StringComparison.Ordinal) &&
                migratedV2Text.Contains(
                    "\"BoundaryRecoverySpeedMultiplier\"",
                    StringComparison.Ordinal) &&
                !migratedV2Text.Contains("\"BaseSpeedMultiplier\"", StringComparison.Ordinal) &&
                !migratedV2Text.Contains("\"LimbExtensionDuration\"", StringComparison.Ordinal) &&
                !migratedV2Text.Contains("\"FallbackPersonality\"", StringComparison.Ordinal);
            warningCount += migratedV2.Warnings.Count;

            var legacyMatrixPath = Path.Combine(root, "legacy-full-matrix-v3.json");
            const string legacyMatrixDocument =
                "{\"SchemaVersion\":3,\"IndividualSeed\":97531," +
                "\"Behavior\":{\"Speed\":{\"ReferenceMinimumCrawl\":31," +
                "\"ReferenceMaximumCrawl\":44},\"TransitionMatrix\":{" +
                "\"AfterForward\":{\"Entries\":[" +
                "{\"Action\":\"SCurve\",\"Weight\":0.40}," +
                "{\"Action\":\"Curve\",\"Weight\":0.20}," +
                "{\"Action\":\"TurnAround\",\"Weight\":0.08}," +
                "{\"Action\":\"FastForward\",\"Weight\":0.06}," +
                "{\"Action\":\"FastSCurve\",\"Weight\":0.04}," +
                "{\"Action\":\"ForwardExtension\",\"Weight\":0.22}]}," +
                "\"AfterCurve\":{\"Entries\":[" +
                "{\"Action\":\"Forward\",\"Weight\":0.55}," +
                "{\"Action\":\"SCurve\",\"Weight\":0.27}," +
                "{\"Action\":\"FastForward\",\"Weight\":0.06}," +
                "{\"Action\":\"FastSCurve\",\"Weight\":0.04}," +
                "{\"Action\":\"TurnAround\",\"Weight\":0.08}]}," +
                "\"AfterSCurve\":{\"Entries\":[" +
                "{\"Action\":\"Forward\",\"Weight\":0.63}," +
                "{\"Action\":\"Curve\",\"Weight\":0.19}," +
                "{\"Action\":\"FastForward\",\"Weight\":0.06}," +
                "{\"Action\":\"FastSCurve\",\"Weight\":0.04}," +
                "{\"Action\":\"TurnAround\",\"Weight\":0.08}]}," +
                "\"AfterFast\":{\"Entries\":[" +
                "{\"Action\":\"Forward\",\"Weight\":0.68}," +
                "{\"Action\":\"SCurve\",\"Weight\":0.22}," +
                "{\"Action\":\"Curve\",\"Weight\":0.10}]}}}}";
            File.WriteAllText(legacyMatrixPath, legacyMatrixDocument);
            var migratedMatrix = LizardConfigurationStore.LoadOrCreate(legacyMatrixPath);
            var migratedRows = new[]
            {
                migratedMatrix.Configuration.Behavior.TransitionMatrix.AfterForward,
                migratedMatrix.Configuration.Behavior.TransitionMatrix.AfterCurve,
                migratedMatrix.Configuration.Behavior.TransitionMatrix.AfterSCurve,
                migratedMatrix.Configuration.Behavior.TransitionMatrix.AfterFast
            };
            schemaMigrationPassed =
                schemaMigrationPassed &&
                migratedMatrix.LoadedFromDisk &&
                migratedMatrix.Warnings.Count == 1 &&
                migratedMatrix.Configuration.SchemaVersion ==
                LizardConfiguration.CurrentSchemaVersion &&
                migratedMatrix.Configuration.IndividualSeed == 97531 &&
                MathF.Abs(
                    migratedMatrix.Configuration.Behavior.Speed.ReferenceMinimumCrawl - 31f) <=
                0.0001f &&
                migratedRows.All(IsNormalized) &&
                migratedRows.All(row =>
                    row.Entries.Count(entry =>
                        entry.Action == AutonomousAction.LostGripFall &&
                        MathF.Abs(entry.Weight - 0.02f) <= 0.0001f) == 1);
            warningCount += migratedMatrix.Warnings.Count;

            var legacyLargeCanvasPath = Path.Combine(root, "legacy-large-canvas-v3.json");
            const string legacyLargeCanvasDocument =
                "{\"SchemaVersion\":3,\"IndividualSeed\":13579," +
                "\"Appearance\":{\"RenderCanvasSize\":820,\"VisualScale\":0.7}," +
                "\"Runtime\":{\"NavigationMarginModel\":4," +
                "\"ReleaseRenderMarginPixels\":8}}";
            File.WriteAllText(legacyLargeCanvasPath, legacyLargeCanvasDocument);
            var migratedLargeCanvas = LizardConfigurationStore.LoadOrCreate(
                legacyLargeCanvasPath);
            var migratedLargeEnvelope = LizardGeometryEnvelope.Calculate(
                migratedLargeCanvas.Configuration.Appearance,
                migratedLargeCanvas.Configuration.Gait,
                migratedLargeCanvas.Configuration.SecondaryMotion,
                migratedLargeCanvas.Configuration.Rendering);
            var migratedLargeRequiredInset = migratedLargeEnvelope
                .RequiredFallBottomSafetyInset(
                    migratedLargeCanvas.Configuration.Appearance,
                    migratedLargeCanvas.Configuration.Runtime);
            schemaMigrationPassed =
                schemaMigrationPassed &&
                migratedLargeCanvas.LoadedFromDisk &&
                migratedLargeCanvas.Warnings.Count == 1 &&
                migratedLargeCanvas.Configuration.SchemaVersion ==
                LizardConfiguration.CurrentSchemaVersion &&
                migratedLargeCanvas.Configuration.IndividualSeed == 13579 &&
                MathF.Abs(
                    migratedLargeCanvas.Configuration.Appearance.RenderCanvasSize - 820f) <=
                0.0001f &&
                MathF.Abs(migratedLargeCanvas.Configuration.Appearance.VisualScale - 0.7f) <=
                0.0001f &&
                MathF.Abs(
                    migratedLargeCanvas.Configuration.Runtime.NavigationMarginModel - 4f) <=
                0.0001f &&
                MathF.Abs(
                    migratedLargeCanvas.Configuration.Runtime.ReleaseRenderMarginPixels - 8f) <=
                0.0001f &&
                migratedLargeCanvas.Configuration.Behavior.LostGripFall.BottomSafetyInset >=
                migratedLargeRequiredInset &&
                migratedLargeCanvas.Configuration.Behavior.LostGripFall.BottomSafetyInset > 70f;
            warningCount += migratedLargeCanvas.Warnings.Count;

            var legacyDefaultFallPath = Path.Combine(root, "legacy-default-fall-v4.json");
            File.WriteAllText(
                legacyDefaultFallPath,
                "{\"SchemaVersion\":4,\"IndividualSeed\":86420," +
                "\"Behavior\":{\"LostGripFall\":{" +
                "\"MinimumDistance\":78,\"MaximumDistance\":132}}}");
            var migratedDefaultFall = LizardConfigurationStore.LoadOrCreate(
                legacyDefaultFallPath);
            var migratedDefaultFallText = File.ReadAllText(legacyDefaultFallPath);
            var migratedDefaultFallFields = JsonNode.Parse(migratedDefaultFallText)!
                ["Behavior"]!["LostGripFall"]!.AsObject();

            var legacyCustomFallPath = Path.Combine(root, "legacy-custom-fall-v4.json");
            File.WriteAllText(
                legacyCustomFallPath,
                "{\"SchemaVersion\":4,\"IndividualSeed\":86421," +
                "\"Behavior\":{\"LostGripFall\":{" +
                "\"MinimumDistance\":96,\"MaximumDistance\":240," +
                "\"Gravity\":444}}}");
            var migratedCustomFall = LizardConfigurationStore.LoadOrCreate(
                legacyCustomFallPath);
            var migratedCustomFallText = File.ReadAllText(legacyCustomFallPath);
            var migratedCustomFallFields = JsonNode.Parse(migratedCustomFallText)!
                ["Behavior"]!["LostGripFall"]!.AsObject();
            schemaMigrationPassed =
                schemaMigrationPassed &&
                migratedDefaultFall.LoadedFromDisk &&
                migratedDefaultFall.Warnings.Count == 1 &&
                migratedDefaultFall.Configuration.SchemaVersion ==
                LizardConfiguration.CurrentSchemaVersion &&
                MathF.Abs(
                    migratedDefaultFall.Configuration.Behavior.LostGripFall.MinimumDistance -
                    LostGripFallConfiguration.DefaultMinimumDistance) <= 0.0001f &&
                !migratedDefaultFallFields.ContainsKey("MaximumDistance") &&
                migratedCustomFall.LoadedFromDisk &&
                migratedCustomFall.Warnings.Count == 1 &&
                migratedCustomFall.Configuration.SchemaVersion ==
                LizardConfiguration.CurrentSchemaVersion &&
                MathF.Abs(
                    migratedCustomFall.Configuration.Behavior.LostGripFall.MinimumDistance -
                    96f) <= 0.0001f &&
                MathF.Abs(
                    migratedCustomFall.Configuration.Behavior.LostGripFall.Gravity - 444f) <=
                0.0001f &&
                !migratedCustomFallFields.ContainsKey("MaximumDistance");
            warningCount += migratedDefaultFall.Warnings.Count;
            warningCount += migratedCustomFall.Warnings.Count;

            var legacyReachPath = Path.Combine(root, "legacy-reach-v5.json");
            const string legacyReachDocument =
                "{\"SchemaVersion\":5,\"IndividualSeed\":86422," +
                "\"Behavior\":{\"LostGripFall\":{" +
                "\"MinimumDistance\":173,\"Gravity\":477}," +
                "\"TransitionMatrix\":{\"AfterForward\":{\"Entries\":[" +
                "{\"Action\":\"SCurve\",\"Weight\":0.70}," +
                "{\"Action\":\"LostGripFall\",\"Weight\":0.30}]}," +
                "\"AfterCurve\":{\"Entries\":[" +
                "{\"Action\":\"Forward\",\"Weight\":1.0}]}," +
                "\"AfterSCurve\":{\"Entries\":[" +
                "{\"Action\":\"Forward\",\"Weight\":1.0}]}," +
                "\"AfterFast\":{\"Entries\":[" +
                "{\"Action\":\"Forward\",\"Weight\":1.0}]}}}}";
            File.WriteAllText(legacyReachPath, legacyReachDocument);
            var migratedReach = LizardConfigurationStore.LoadOrCreate(legacyReachPath);
            var migratedReachText = File.ReadAllText(legacyReachPath);
            var migratedReachFields = JsonNode.Parse(migratedReachText)!
                ["Behavior"]!["LostGripFall"]!.AsObject();
            var migratedReachRow =
                migratedReach.Configuration.Behavior.TransitionMatrix.AfterForward;
            var schema5ReachMigrationPassed =
                migratedReach.LoadedFromDisk &&
                migratedReach.Warnings.Count == 1 &&
                migratedReach.Configuration.SchemaVersion ==
                    LizardConfiguration.CurrentSchemaVersion &&
                migratedReach.Configuration.IndividualSeed == 86422 &&
                MathF.Abs(
                    migratedReach.Configuration.Behavior.LostGripFall.MinimumDistance -
                    173f) <= 0.0001f &&
                MathF.Abs(
                    migratedReach.Configuration.Behavior.LostGripFall.Gravity - 477f) <=
                    0.0001f &&
                MathF.Abs(
                    migratedReach.Configuration.Behavior.LostGripFall.ReachLeadDistance -
                    LizardConfiguration.Default.Behavior.LostGripFall.ReachLeadDistance) <=
                    0.0001f &&
                migratedReachFields.ContainsKey("ReachLeadDistance") &&
                migratedReachRow.Entries.Length == 2 &&
                migratedReachRow.Entries[0].Action == AutonomousAction.SCurve &&
                MathF.Abs(migratedReachRow.Entries[0].Weight - 0.70f) <= 0.0001f &&
                migratedReachRow.Entries[1].Action == AutonomousAction.LostGripFall &&
                MathF.Abs(migratedReachRow.Entries[1].Weight - 0.30f) <= 0.0001f;
            schemaMigrationPassed = schemaMigrationPassed && schema5ReachMigrationPassed;
            if (!schema5ReachMigrationPassed)
            {
                failures.Add(
                    "schema5-reach-migration(" +
                    $"loaded={migratedReach.LoadedFromDisk}," +
                    $"warnings={migratedReach.Warnings.Count}," +
                    $"warningText={string.Join('|', migratedReach.Warnings)}," +
                    $"version={migratedReach.Configuration.SchemaVersion}," +
                    $"seed={migratedReach.Configuration.IndividualSeed}," +
                    $"minimum={migratedReach.Configuration.Behavior.LostGripFall.MinimumDistance:R}," +
                    $"gravity={migratedReach.Configuration.Behavior.LostGripFall.Gravity:R}," +
                    $"lead={migratedReach.Configuration.Behavior.LostGripFall.ReachLeadDistance:R}," +
                    $"leadField={migratedReachFields.ContainsKey("ReachLeadDistance")}," +
                    $"row={string.Join('/', migratedReachRow.Entries.Select(entry => $"{entry.Action}:{entry.Weight:R}"))})");
            }
            warningCount += migratedReach.Warnings.Count;

            var legacyFourPawPath = Path.Combine(root, "legacy-four-paw-v7.json");
            const string legacyFourPawDocument =
                "{\"SchemaVersion\":7,\"IndividualSeed\":86423," +
                "\"Behavior\":{\"LostGripFall\":{" +
                "\"ReachLeadDistance\":61,\"Gravity\":477," +
                "\"AccelerationRampDuration\":0.31," +
                "\"InitialAccelerationRatio\":0.29}}," +
                "\"Physics\":{\"Gravity\":777," +
                "\"RegripFrontReachLengthFactor\":0.63," +
                "\"RegripFrontReachOutwardWeight\":0.21," +
                "\"RegripContactHoldFraction\":0.37}}";
            File.WriteAllText(legacyFourPawPath, legacyFourPawDocument);
            var migratedFourPaw = LizardConfigurationStore.LoadOrCreate(
                legacyFourPawPath);
            var migratedFourPawPhysics = JsonNode.Parse(
                File.ReadAllText(legacyFourPawPath))!["Physics"]!.AsObject();
            var schema7FourPawMigrationPassed =
                migratedFourPaw.LoadedFromDisk &&
                migratedFourPaw.Warnings.Count == 1 &&
                migratedFourPaw.Configuration.SchemaVersion ==
                    LizardConfiguration.CurrentSchemaVersion &&
                migratedFourPaw.Configuration.IndividualSeed == 86423 &&
                MathF.Abs(
                    migratedFourPaw.Configuration.Behavior.LostGripFall
                        .ReachLeadDistance - 61f) <= 0.0001f &&
                MathF.Abs(
                    migratedFourPaw.Configuration.Behavior.LostGripFall.Gravity -
                    477f) <= 0.0001f &&
                MathF.Abs(
                    migratedFourPaw.Configuration.Behavior.LostGripFall
                        .AccelerationRampDuration - 0.31f) <= 0.0001f &&
                MathF.Abs(
                    migratedFourPaw.Configuration.Behavior.LostGripFall
                        .InitialAccelerationRatio - 0.29f) <= 0.0001f &&
                MathF.Abs(migratedFourPaw.Configuration.Physics.Gravity - 777f) <=
                    0.0001f &&
                MathF.Abs(
                    migratedFourPaw.Configuration.Physics
                        .RegripFrontReachLengthFactor - 0.63f) <= 0.0001f &&
                MathF.Abs(
                    migratedFourPaw.Configuration.Physics
                        .RegripFrontReachOutwardWeight - 0.21f) <= 0.0001f &&
                MathF.Abs(
                    migratedFourPaw.Configuration.Physics
                        .RegripContactHoldFraction - 0.37f) <= 0.0001f &&
                MathF.Abs(
                    migratedFourPaw.Configuration.Physics
                        .RegripRearReachLengthFactor -
                    LizardConfiguration.Default.Physics
                        .RegripRearReachLengthFactor) <= 0.0001f &&
                MathF.Abs(
                    migratedFourPaw.Configuration.Physics
                        .RegripRearReachOutwardWeight -
                    LizardConfiguration.Default.Physics
                        .RegripRearReachOutwardWeight) <= 0.0001f &&
                migratedFourPawPhysics.ContainsKey(
                    "RegripRearReachLengthFactor") &&
                migratedFourPawPhysics.ContainsKey(
                    "RegripRearReachOutwardWeight");
            schemaMigrationPassed =
                schemaMigrationPassed && schema7FourPawMigrationPassed;
            if (!schema7FourPawMigrationPassed)
            {
                failures.Add(
                    "schema7-four-paw-migration(" +
                    $"loaded={migratedFourPaw.LoadedFromDisk}," +
                    $"warnings={migratedFourPaw.Warnings.Count}," +
                    $"version={migratedFourPaw.Configuration.SchemaVersion}," +
                    $"front={migratedFourPaw.Configuration.Physics.RegripFrontReachLengthFactor:R}/" +
                    $"{migratedFourPaw.Configuration.Physics.RegripFrontReachOutwardWeight:R}," +
                    $"rear={migratedFourPaw.Configuration.Physics.RegripRearReachLengthFactor:R}/" +
                    $"{migratedFourPaw.Configuration.Physics.RegripRearReachOutwardWeight:R})");
            }
            warningCount += migratedFourPaw.Warnings.Count;

            var fullSchema7Path = Path.Combine(
                root,
                "legacy-four-paw-full-v7.json");
            var fullSchema7 = LizardConfiguration.Default with
            {
                SchemaVersion = 7,
                IndividualSeed = 86424,
                Runtime = LizardConfiguration.Default.Runtime with
                {
                    SimulationRate = 144f,
                    DebugPanelWidthPixels = 372
                },
                Behavior = LizardConfiguration.Default.Behavior with
                {
                    LostGripFall =
                        LizardConfiguration.Default.Behavior.LostGripFall with
                        {
                            ReachLeadDistance = 63f,
                            Gravity = 489f,
                            AccelerationRampDuration = 0.33f,
                            InitialAccelerationRatio = 0.27f
                        }
                },
                Physics = LizardConfiguration.Default.Physics with
                {
                    Gravity = 812f,
                    RegripFrontReachLengthFactor = 0.67f,
                    RegripFrontReachOutwardWeight = 0.23f,
                    RegripContactHoldFraction = 0.35f,
                    // Deliberately different: schema 7 never owned these
                    // values, and the JSON members below are removed.
                    RegripRearReachLengthFactor = 0.11f,
                    RegripRearReachOutwardWeight = 0.89f
                }
            };
            var fullSchema7Node = JsonSerializer
                .SerializeToNode(fullSchema7)!
                .AsObject();
            var fullSchema7Physics = fullSchema7Node["Physics"]!.AsObject();
            fullSchema7Physics.Remove("RegripRearReachLengthFactor");
            fullSchema7Physics.Remove("RegripRearReachOutwardWeight");
            File.WriteAllText(fullSchema7Path, fullSchema7Node.ToJsonString());
            var migratedFullSchema7 = LizardConfigurationStore.LoadOrCreate(
                fullSchema7Path);
            var expectedFullSchema8 = fullSchema7 with
            {
                SchemaVersion = LizardConfiguration.CurrentSchemaVersion,
                Physics = fullSchema7.Physics with
                {
                    RegripRearReachLengthFactor =
                        PhysicsConfiguration.DefaultRegripRearReachLengthFactor,
                    RegripRearReachOutwardWeight =
                        PhysicsConfiguration.DefaultRegripRearReachOutwardWeight
                }
            };
            var fullSchema7Preserved =
                migratedFullSchema7.LoadedFromDisk &&
                migratedFullSchema7.Warnings.Count == 1 &&
                JsonNode.DeepEquals(
                    JsonSerializer.SerializeToNode(
                        migratedFullSchema7.Configuration),
                    JsonSerializer.SerializeToNode(expectedFullSchema8));
            schemaMigrationPassed &= fullSchema7Preserved;
            if (!fullSchema7Preserved)
            {
                failures.Add(
                    "schema7-full-value-preservation(" +
                    $"loaded={migratedFullSchema7.LoadedFromDisk}," +
                    $"warnings={migratedFullSchema7.Warnings.Count}," +
                    $"version={migratedFullSchema7.Configuration.SchemaVersion}," +
                    $"seed={migratedFullSchema7.Configuration.IndividualSeed})");
            }
            warningCount += migratedFullSchema7.Warnings.Count;

            var invalidPath = Path.Combine(root, "invalid.json");
            const string invalidDocument = "{\"SchemaVersion\":999,\"IndividualSeed\":17}";
            File.WriteAllText(invalidPath, invalidDocument);
            var invalid = LizardConfigurationStore.LoadOrCreate(invalidPath);
            warningCount += invalid.Warnings.Count;
            invalidFallbackPassed =
                !invalid.LoadedFromDisk &&
                invalid.Warnings.Count == 1 &&
                invalid.Configuration.SchemaVersion == LizardConfiguration.CurrentSchemaVersion &&
                invalid.Configuration.IndividualSeed is > 0;
            invalidFilePreserved = string.Equals(
                File.ReadAllText(invalidPath),
                invalidDocument,
                StringComparison.Ordinal);

            var nullPath = Path.Combine(root, "invalid-null.json");
            const string nullDocument = "{\"SchemaVersion\":2,\"Behavior\":null}";
            File.WriteAllText(nullPath, nullDocument);
            var invalidNull = LizardConfigurationStore.LoadOrCreate(nullPath);
            warningCount += invalidNull.Warnings.Count;
            invalidFallbackPassed =
                invalidFallbackPassed &&
                !invalidNull.LoadedFromDisk &&
                invalidNull.Warnings.Count == 1 &&
                invalidNull.Configuration.SchemaVersion == LizardConfiguration.CurrentSchemaVersion;
            invalidFilePreserved =
                invalidFilePreserved &&
                string.Equals(File.ReadAllText(nullPath), nullDocument, StringComparison.Ordinal);

            var overflowScalePath = Path.Combine(root, "invalid-overflow-scale.json");
            var overflowScaleDocument =
                $"{{\"SchemaVersion\":{LizardConfiguration.CurrentSchemaVersion}," +
                "\"IndividualSeed\":19001," +
                "\"Appearance\":{\"VisualScale\":1e38}}";
            File.WriteAllText(overflowScalePath, overflowScaleDocument);
            var overflowScale = LizardConfigurationStore.LoadOrCreate(overflowScalePath);
            warningCount += overflowScale.Warnings.Count;

            var oversizedLogicalSurfacePath = Path.Combine(
                root,
                "invalid-oversized-logical-surface.json");
            var oversizedLogicalSurfaceDocument =
                $"{{\"SchemaVersion\":{LizardConfiguration.CurrentSchemaVersion}," +
                "\"IndividualSeed\":19002," +
                "\"Appearance\":{\"VisualScale\":30}}";
            File.WriteAllText(
                oversizedLogicalSurfacePath,
                oversizedLogicalSurfaceDocument);
            var oversizedLogicalSurface = LizardConfigurationStore.LoadOrCreate(
                oversizedLogicalSurfacePath);
            warningCount += oversizedLogicalSurface.Warnings.Count;

            var oversizedModelSurfacePath = Path.Combine(
                root,
                "invalid-oversized-model-surface.json");
            var oversizedModelSurfaceDocument =
                $"{{\"SchemaVersion\":{LizardConfiguration.CurrentSchemaVersion}," +
                "\"IndividualSeed\":19003," +
                "\"Appearance\":{\"RenderCanvasSize\":20000,\"VisualScale\":0.1}}";
            File.WriteAllText(oversizedModelSurfacePath, oversizedModelSurfaceDocument);
            var oversizedModelSurface = LizardConfigurationStore.LoadOrCreate(
                oversizedModelSurfacePath);
            warningCount += oversizedModelSurface.Warnings.Count;

            // Resolving the fallback profile is the last configuration-side
            // boundary before application composition. It must be safe for
            // every rejected extreme document, not merely deserialize.
            var fallbackProfiles = new[]
            {
                LizardConfigurationStore.ResolveProfile(overflowScale),
                LizardConfigurationStore.ResolveProfile(oversizedLogicalSurface),
                LizardConfigurationStore.ResolveProfile(oversizedModelSurface)
            };
            derivedGeometryFallbackPassed =
                new[]
                {
                    (overflowScale, overflowScalePath, overflowScaleDocument),
                    (oversizedLogicalSurface,
                        oversizedLogicalSurfacePath,
                        oversizedLogicalSurfaceDocument),
                    (oversizedModelSurface,
                        oversizedModelSurfacePath,
                        oversizedModelSurfaceDocument)
                }.All(item =>
                    !item.Item1.LoadedFromDisk &&
                    item.Item1.Warnings.Count == 1 &&
                    string.Equals(
                        File.ReadAllText(item.Item2),
                        item.Item3,
                        StringComparison.Ordinal)) &&
                fallbackProfiles.All(profile =>
                    profile.Source.Validate().Count == 0 &&
                    float.IsFinite(
                        profile.Appearance.RenderCanvasSize *
                        profile.Appearance.VisualScale));
            invalidFallbackPassed = invalidFallbackPassed && derivedGeometryFallbackPassed;
            invalidFilePreserved = invalidFilePreserved && derivedGeometryFallbackPassed;

            var undersizedCatchUpPath = Path.Combine(
                root,
                "invalid-undersized-frame-catch-up.json");
            var undersizedCatchUpDocument =
                $"{{\"SchemaVersion\":{LizardConfiguration.CurrentSchemaVersion}," +
                "\"IndividualSeed\":19004," +
                "\"Runtime\":{\"MaximumFrameCatchUp\":0.005}}";
            File.WriteAllText(undersizedCatchUpPath, undersizedCatchUpDocument);
            var undersizedCatchUp = LizardConfigurationStore.LoadOrCreate(
                undersizedCatchUpPath);
            warningCount += undersizedCatchUp.Warnings.Count;
            invalidFallbackPassed =
                invalidFallbackPassed &&
                !undersizedCatchUp.LoadedFromDisk &&
                undersizedCatchUp.Warnings.Count == 1 &&
                undersizedCatchUp.Configuration.Validate().Count == 0;
            invalidFilePreserved =
                invalidFilePreserved &&
                string.Equals(
                    File.ReadAllText(undersizedCatchUpPath),
                    undersizedCatchUpDocument,
                    StringComparison.Ordinal);

            var typoPath = Path.Combine(root, "invalid-typo.json");
            const string typoDocument =
                "{\"SchemaVersion\":2,\"IndividualSeed\":17,\"Runtime\":{\"SimulatonRate\":120}}";
            File.WriteAllText(typoPath, typoDocument);
            var typo = LizardConfigurationStore.LoadOrCreate(typoPath);
            warningCount += typo.Warnings.Count;
            unknownPropertyRejected =
                !typo.LoadedFromDisk &&
                typo.Warnings.Count == 1 &&
                typo.Configuration.SchemaVersion == LizardConfiguration.CurrentSchemaVersion &&
                string.Equals(File.ReadAllText(typoPath), typoDocument, StringComparison.Ordinal);

            var obsoleteCurrentFallPath = Path.Combine(
                root,
                "invalid-current-maximum-distance.json");
            var obsoleteCurrentFallDocument =
                "{\"SchemaVersion\":" + LizardConfiguration.CurrentSchemaVersion +
                ",\"IndividualSeed\":18,\"Behavior\":{\"LostGripFall\":{" +
                "\"MaximumDistance\":240}}}";
            File.WriteAllText(obsoleteCurrentFallPath, obsoleteCurrentFallDocument);
            var obsoleteCurrentFall = LizardConfigurationStore.LoadOrCreate(
                obsoleteCurrentFallPath);
            warningCount += obsoleteCurrentFall.Warnings.Count;
            unknownPropertyRejected =
                unknownPropertyRejected &&
                !obsoleteCurrentFall.LoadedFromDisk &&
                obsoleteCurrentFall.Warnings.Count == 1 &&
                obsoleteCurrentFall.Configuration.SchemaVersion ==
                LizardConfiguration.CurrentSchemaVersion &&
                string.Equals(
                    File.ReadAllText(obsoleteCurrentFallPath),
                    obsoleteCurrentFallDocument,
                    StringComparison.Ordinal);
        }
        catch (Exception exception)
        {
            failures.Add($"persistence: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception exception)
            {
                failures.Add($"cleanup: {exception.GetType().Name}: {exception.Message}");
            }
        }

        var variationConfiguration = LizardConfiguration.Default with
        {
            IndividualVariation = LizardConfiguration.Default.IndividualVariation with
            {
                Enabled = true
            }
        };
        var first = IndividualProfileFactory.Create(variationConfiguration, 0x24519);
        var repeated = IndividualProfileFactory.Create(variationConfiguration, 0x24519);
        var deterministicProfilePassed = SameProfileSignature(first, repeated);

        var profiles = Enumerable.Range(0, 12)
            .Select(index => IndividualProfileFactory.Create(
                variationConfiguration,
                0x24519 + index * 7919))
            .ToArray();
        var distinctIndividualsPassed =
            profiles.Select(profile => profile.Traits.Id).Distinct(StringComparer.Ordinal).Count() == profiles.Length &&
            profiles.Select(ProfileSignature).Distinct(StringComparer.Ordinal).Count() >= 8;
        var variationApplied =
            profiles.Any(profile => MathF.Abs(
                profile.Behavior.Speed.ReferenceMinimumCrawl -
                variationConfiguration.Behavior.Speed.ReferenceMinimumCrawl) > 0.0001f) &&
            profiles.Any(profile => MathF.Abs(
                profile.Gait.FrontLegLinkLength -
                variationConfiguration.Gait.FrontLegLinkLength) > 0.0001f) &&
            profiles.Any(profile => MathF.Abs(
                profile.Physics.Gravity - variationConfiguration.Physics.Gravity) > 0.0001f) &&
            profiles.Any(profile => MathF.Abs(
                profile.Physics.RegripFrontReachLengthFactor -
                variationConfiguration.Physics.RegripFrontReachLengthFactor) > 0.0001f) &&
            profiles.Any(profile => MathF.Abs(
                profile.Physics.RegripFrontReachOutwardWeight -
                variationConfiguration.Physics.RegripFrontReachOutwardWeight) > 0.0001f) &&
            profiles.Any(profile => MathF.Abs(
                profile.Physics.RegripRearReachLengthFactor -
                variationConfiguration.Physics.RegripRearReachLengthFactor) > 0.0001f) &&
            profiles.Any(profile => MathF.Abs(
                profile.Physics.RegripRearReachOutwardWeight -
                variationConfiguration.Physics.RegripRearReachOutwardWeight) > 0.0001f) &&
            profiles.Any(profile => MathF.Abs(
                profile.Physics.RegripContactHoldFraction -
                variationConfiguration.Physics.RegripContactHoldFraction) > 0.0001f) &&
            profiles.Any(profile => MathF.Abs(
                profile.Appearance.SpineLinkLength -
                variationConfiguration.Appearance.SpineLinkLength) > 0.0001f) &&
            profiles.Any(profile => MathF.Abs(
                profile.SecondaryMotion.IdleTailAmplitude -
                variationConfiguration.SecondaryMotion.IdleTailAmplitude) > 0.0001f) &&
            profiles.Any(profile => MathF.Abs(
                profile.Rendering.FrontFootRadius -
                variationConfiguration.Rendering.FrontFootRadius) > 0.0001f);
        var transitionNormalizationPassed = profiles.All(profile =>
            IsNormalized(profile.Behavior.TransitionMatrix.AfterForward) &&
            IsNormalized(profile.Behavior.TransitionMatrix.AfterCurve) &&
            IsNormalized(profile.Behavior.TransitionMatrix.AfterSCurve) &&
            IsNormalized(profile.Behavior.TransitionMatrix.AfterFast) &&
            MathF.Abs(profile.Behavior.Rest.Bands.Sum(band => band.Weight) - 1f) <= 0.0001f);

        var zeroWeightConfiguration = variationConfiguration with
        {
            Behavior = variationConfiguration.Behavior with
            {
                TransitionMatrix = variationConfiguration.Behavior.TransitionMatrix with
                {
                    AfterForward = variationConfiguration.Behavior.TransitionMatrix.AfterForward with
                    {
                        Entries = variationConfiguration.Behavior.TransitionMatrix.AfterForward.Entries
                            .Select((entry, index) => entry with
                            {
                                Weight = index switch
                                {
                                    0 => 0f,
                                    1 => entry.Weight +
                                         variationConfiguration.Behavior.TransitionMatrix.AfterForward.Entries[0].Weight,
                                    _ => entry.Weight
                                }
                            })
                            .ToArray()
                    }
                },
                Rest = variationConfiguration.Behavior.Rest with
                {
                    Bands = variationConfiguration.Behavior.Rest.Bands
                        .Select((band, index) => band with
                        {
                            Weight = index switch
                            {
                                0 => 0f,
                                1 => band.Weight + variationConfiguration.Behavior.Rest.Bands[0].Weight,
                                _ => band.Weight
                            }
                        })
                        .ToArray()
                }
            }
        };
        var zeroWeightProfile = IndividualProfileFactory.Create(zeroWeightConfiguration, 0x24519);
        var zeroWeightsPreserved =
            zeroWeightProfile.Behavior.TransitionMatrix.AfterForward.Entries[0].Weight == 0f &&
            zeroWeightProfile.Behavior.Rest.Bands[0].Weight == 0f &&
            IsNormalized(zeroWeightProfile.Behavior.TransitionMatrix.AfterForward) &&
            MathF.Abs(zeroWeightProfile.Behavior.Rest.Bands.Sum(band => band.Weight) - 1f) <= 0.0001f;

        var controlled = CreateCustomConfiguration();
        var controlledProfile = IndividualProfileFactory.Create(
            controlled,
            controlled.IndividualSeed ?? 0);
        var customMatrixControlPassed =
            ReferenceEquals(controlledProfile.Behavior, controlled.Behavior) &&
            Enumerable.Range(0, 101).All(index =>
                AutonomousTransitionPolicy.ChooseAfterForward(
                    index / 100f,
                    forwardExtended: false,
                    controlledProfile.Behavior.TransitionMatrix) == AutonomousAction.FastForward);
        var unsafeMatrixRejected =
            CreateConfigurationWithAfterFastAction(AutonomousAction.TurnAround).Validate().Count > 0 &&
            CreateConfigurationWithAfterForwardAction((AutonomousAction)999).Validate().Count > 0;
        var unsafeNumericConfigurationRejected =
            (LizardConfiguration.Default with
            {
                Runtime = LizardConfiguration.Default.Runtime with { SimulationRate = 100000f }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Behavior = LizardConfiguration.Default.Behavior with
                {
                    Speed = LizardConfiguration.Default.Behavior.Speed with
                    {
                        ReferenceMinimumCrawl = 96.2f,
                        ReferenceMaximumCrawl = 96.2f,
                        MaximumCrawl = 96.2f
                    }
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                IndividualVariation = LizardConfiguration.Default.IndividualVariation with
                {
                    SpeedVariation = 0.75f,
                    Multipliers = LizardConfiguration.Default.IndividualVariation.Multipliers with
                    {
                        SpeedActivity = 1f,
                        SpeedAgility = 1f
                    }
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                IndividualVariation = LizardConfiguration.Default.IndividualVariation with
                {
                    DurationVariation = 0.75f,
                    Behavior = LizardConfiguration.Default.IndividualVariation.Behavior with
                    {
                        PointerAttentionCuriosity = 1f,
                        PointerAttentionBoldness = 1f
                    }
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                IndividualVariation = LizardConfiguration.Default.IndividualVariation with
                {
                    Transitions = LizardConfiguration.Default.IndividualVariation.Transitions with
                    {
                        FastSCurveActivity = 1f,
                        FastSCurveAgility = 1f,
                        FastSCurvePlayfulness = 1f
                    }
                }
            }).Validate().Count > 0;
        var defaultFall = LizardConfiguration.Default.Behavior.LostGripFall;
        var defaultPhysics = LizardConfiguration.Default.Physics;
        var minimumCatchPreparationDistance =
            3f * defaultFall.MaximumFallVelocity /
            LizardConfiguration.Default.Runtime.SimulationRate;
        var catchPreparationBoundaryConfiguration = LizardConfiguration.Default with
        {
            Behavior = LizardConfiguration.Default.Behavior with
            {
                LostGripFall = defaultFall with
                {
                    MinimumDistance = minimumCatchPreparationDistance,
                    ReachLeadDistance = minimumCatchPreparationDistance
                }
            }
        };
        var lostGripConfigurationPassed =
            CreateConfigurationWithAfterForwardAction(AutonomousAction.LostGripFall)
                .Validate().Count == 0 &&
            CreateConfigurationWithAfterFastAction(AutonomousAction.LostGripFall)
                .Validate().Count == 0 &&
            (LizardConfiguration.Default with
            {
                Behavior = LizardConfiguration.Default.Behavior with
                {
                    LostGripFall = defaultFall with { MinimumDistance = 0f }
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Behavior = LizardConfiguration.Default.Behavior with
                {
                    LostGripFall = defaultFall with { ReachLeadDistance = 0f }
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Behavior = LizardConfiguration.Default.Behavior with
                {
                    LostGripFall = defaultFall with { Gravity = 0f }
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Behavior = LizardConfiguration.Default.Behavior with
                {
                    LostGripFall = defaultFall with
                    {
                        MaximumFallVelocity = defaultFall.MaximumInitialVelocity - 1f
                    }
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Behavior = LizardConfiguration.Default.Behavior with
                {
                    LostGripFall = defaultFall with { BottomSafetyInset = 0f }
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Behavior = LizardConfiguration.Default.Behavior with
                {
                    LostGripFall = defaultFall with { RegripDuration = 0f }
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Physics = defaultPhysics with
                {
                    RegripFrontReachLengthFactor = 0f,
                    RegripFrontReachOutwardWeight = 1f,
                    RegripRearReachLengthFactor = 0f,
                    RegripRearReachOutwardWeight = 1f,
                    RegripContactHoldFraction = 0.8f
                }
            }).Validate().Count == 0 &&
            (LizardConfiguration.Default with
            {
                Physics = defaultPhysics with { RegripFrontReachLengthFactor = 1.01f }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Physics = defaultPhysics with { RegripFrontReachOutwardWeight = -0.01f }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Physics = defaultPhysics with { RegripRearReachLengthFactor = 1.01f }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Physics = defaultPhysics with { RegripRearReachOutwardWeight = -0.01f }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Physics = defaultPhysics with { RegripContactHoldFraction = 0.81f }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                IndividualVariation = LizardConfiguration.Default.IndividualVariation with
                {
                    Physics = LizardConfiguration.Default.IndividualVariation.Physics with
                    {
                        RegripReachAgility = 1.01f,
                        RegripOutwardCuriosity = 1.01f,
                        RegripHoldCalmness = 1.01f
                    }
                }
            }).Validate().Count > 0 &&
            catchPreparationBoundaryConfiguration.Validate().Count == 0 &&
            (catchPreparationBoundaryConfiguration with
            {
                Behavior = catchPreparationBoundaryConfiguration.Behavior with
                {
                    LostGripFall = catchPreparationBoundaryConfiguration.Behavior.LostGripFall with
                    {
                        MinimumDistance = minimumCatchPreparationDistance - 0.01f
                    }
                }
            }).Validate().Count > 0 &&
            (catchPreparationBoundaryConfiguration with
            {
                Behavior = catchPreparationBoundaryConfiguration.Behavior with
                {
                    LostGripFall = catchPreparationBoundaryConfiguration.Behavior.LostGripFall with
                    {
                        ReachLeadDistance = minimumCatchPreparationDistance - 0.01f
                    }
                }
            }).Validate().Count > 0;
        var maximumLimitConfiguration = LizardConfiguration.Default with
        {
            Runtime = LizardConfiguration.Default.Runtime with
            {
                DebugPathCapacity = RuntimeConfiguration.MaximumDebugPathCapacity
            },
            Behavior = LizardConfiguration.Default.Behavior with
            {
                EscapeSprint = LizardConfiguration.Default.Behavior.EscapeSprint with
                {
                    DirectionCandidateCount = LizardConfiguration.MaximumEscapeDirectionCandidateCount
                }
            }
        };
        var hardLimitsPassed =
            maximumLimitConfiguration.Validate().Count == 0 &&
            (LizardConfiguration.Default with
            {
                Runtime = LizardConfiguration.Default.Runtime with
                {
                    DebugPathCapacity = int.MaxValue
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Behavior = LizardConfiguration.Default.Behavior with
                {
                    EscapeSprint = LizardConfiguration.Default.Behavior.EscapeSprint with
                    {
                        DirectionCandidateCount = int.MaxValue
                    }
                }
            }).Validate().Count > 0;

        const float deterministicRate = 200f;
        var deterministicStep = 1f / deterministicRate;
        var exactStepConfiguration = LizardConfiguration.Default with
        {
            Runtime = LizardConfiguration.Default.Runtime with
            {
                SimulationRate = deterministicRate
            },
            Physics = LizardConfiguration.Default.Physics with
            {
                MinimumSimulationStep = deterministicStep,
                MaximumSimulationStep = deterministicStep,
                FallbackSimulationStep = deterministicStep
            }
        };
        var defaultStep = 1f / LizardConfiguration.Default.Runtime.SimulationRate;
        var timeStepDeterminismPassed =
            BitConverter.SingleToInt32Bits(defaultStep) ==
            BitConverter.SingleToInt32Bits(LizardConfiguration.Default.Physics.FallbackSimulationStep) &&
            exactStepConfiguration.Validate().Count == 0 &&
            (exactStepConfiguration with
            {
                Physics = exactStepConfiguration.Physics with
                {
                    MinimumSimulationStep = deterministicStep + 0.00001f,
                    MaximumSimulationStep = deterministicStep + 0.001f,
                    FallbackSimulationStep = deterministicStep + 0.00001f
                }
            }).Validate().Count > 0 &&
            (exactStepConfiguration with
            {
                Physics = exactStepConfiguration.Physics with
                {
                    MinimumSimulationStep = deterministicStep - 0.001f,
                    MaximumSimulationStep = deterministicStep - 0.00001f,
                    FallbackSimulationStep = deterministicStep - 0.00001f
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Physics = LizardConfiguration.Default.Physics with
                {
                    FallbackSimulationStep =
                        LizardConfiguration.Default.Physics.MinimumSimulationStep / 2f
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Physics = LizardConfiguration.Default.Physics with
                {
                    FallbackSimulationStep =
                        LizardConfiguration.Default.Physics.MaximumSimulationStep * 2f
                }
            }).Validate().Count > 0;
        var frameCatchUpBoundsPassed =
            (LizardConfiguration.Default with
            {
                Runtime = LizardConfiguration.Default.Runtime with
                {
                    MaximumFrameCatchUp = RuntimeConfiguration.MinimumFrameCatchUpSeconds
                }
            }).Validate().Count == 0 &&
            (LizardConfiguration.Default with
            {
                Runtime = LizardConfiguration.Default.Runtime with
                {
                    MaximumFrameCatchUp = RuntimeConfiguration.MaximumFrameCatchUpSeconds
                }
            }).Validate().Count == 0 &&
            (LizardConfiguration.Default with
            {
                Runtime = LizardConfiguration.Default.Runtime with
                {
                    MaximumFrameCatchUp = 0.005f
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Runtime = LizardConfiguration.Default.Runtime with
                {
                    MaximumFrameCatchUp = MathF.BitDecrement(
                        RuntimeConfiguration.MinimumFrameCatchUpSeconds)
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Runtime = LizardConfiguration.Default.Runtime with
                {
                    MaximumFrameCatchUp = MathF.BitIncrement(
                        RuntimeConfiguration.MaximumFrameCatchUpSeconds)
                }
            }).Validate().Count > 0;

        var defaultEnvelope = LizardGeometryEnvelope.Calculate(
            LizardConfiguration.Default.Appearance,
            LizardConfiguration.Default.Gait,
            LizardConfiguration.Default.SecondaryMotion,
            LizardConfiguration.Default.Rendering);
        var configuredGeometryRejected =
            (LizardConfiguration.Default with
            {
                Appearance = LizardConfiguration.Default.Appearance with
                {
                    HeadAnchorOffset = 400f
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Appearance = LizardConfiguration.Default.Appearance with
                {
                    RenderCanvasSize = 500f
                }
            }).Validate().Count > 0;
        var variedProfilesFit = Enumerable.Range(1, 64).All(seed =>
        {
            var profile = IndividualProfileFactory.Create(
                LizardConfiguration.Default,
                seed,
                applyVariation: true);
            var envelope = LizardGeometryEnvelope.Calculate(
                profile.Appearance,
                profile.Gait,
                profile.SecondaryMotion,
                profile.Rendering);
            return profile.Appearance.CreatureCanvasSize * 0.5f >= envelope.NormalModelRadius &&
                   profile.Appearance.RenderCanvasSize * 0.5f >= envelope.DanglingModelRadius &&
                   profile.Behavior.LostGripFall.BottomSafetyInset + 0.0001f >=
                   envelope.RequiredFallBottomSafetyInset(
                       profile.Appearance,
                       profile.Runtime);
        });
        var geometryEnvelopePassed =
            LizardConfiguration.Default.Appearance.CreatureCanvasSize * 0.5f >=
            defaultEnvelope.NormalModelRadius &&
            LizardConfiguration.Default.Appearance.RenderCanvasSize * 0.5f >=
            defaultEnvelope.DanglingModelRadius &&
            configuredGeometryRejected &&
            variedProfilesFit;
        var derivedGeometryBoundsPassed =
            derivedGeometryFallbackPassed &&
            (LizardConfiguration.Default with
            {
                Appearance = LizardConfiguration.Default.Appearance with
                {
                    VisualScale = 1e38f
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Appearance = LizardConfiguration.Default.Appearance with
                {
                    VisualScale = 30f
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Appearance = LizardConfiguration.Default.Appearance with
                {
                    RenderCanvasSize = 20_000f,
                    VisualScale = 0.1f
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Appearance = LizardConfiguration.Default.Appearance with
                {
                    HeadAnchorOffset = 1e38f
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Runtime = LizardConfiguration.Default.Runtime with
                {
                    NavigationMarginModel = 30_000f
                }
            }).Validate().Count > 0 &&
            (LizardConfiguration.Default with
            {
                Behavior = LizardConfiguration.Default.Behavior with
                {
                    LostGripFall = LizardConfiguration.Default.Behavior.LostGripFall with
                    {
                        BottomSafetyInset = 20_000f
                    }
                }
            }).Validate().Count > 0;

        const int variationControlSeed = 0x4C5A31;
        var exactRestConfiguration = LizardConfiguration.Default with
        {
            IndividualVariation = LizardConfiguration.Default.IndividualVariation with
            {
                RestWeightVariation = 0f
            }
        };
        var exactRestProfile = IndividualProfileFactory.Create(
            exactRestConfiguration,
            variationControlSeed,
            applyVariation: true);
        var restWeightsExact = exactRestConfiguration.Behavior.Rest.Bands
            .Select(band => BitConverter.SingleToInt32Bits(band.Weight))
            .SequenceEqual(exactRestProfile.Behavior.Rest.Bands
                .Select(band => BitConverter.SingleToInt32Bits(band.Weight)));
        var zeroSpeedCoefficientConfiguration = LizardConfiguration.Default with
        {
            IndividualVariation = LizardConfiguration.Default.IndividualVariation with
            {
                Multipliers = LizardConfiguration.Default.IndividualVariation.Multipliers with
                {
                    SpeedActivity = 0f,
                    SpeedAgility = 0f
                }
            }
        };
        var zeroSpeedCoefficientProfile = IndividualProfileFactory.Create(
            zeroSpeedCoefficientConfiguration,
            variationControlSeed,
            applyVariation: true);
        var speedCoefficientsExact =
            BitConverter.SingleToInt32Bits(
                zeroSpeedCoefficientConfiguration.Behavior.Speed.ReferenceMinimumCrawl) ==
            BitConverter.SingleToInt32Bits(
                zeroSpeedCoefficientProfile.Behavior.Speed.ReferenceMinimumCrawl) &&
            BitConverter.SingleToInt32Bits(
                zeroSpeedCoefficientConfiguration.Behavior.Speed.ReferenceMaximumCrawl) ==
            BitConverter.SingleToInt32Bits(
                zeroSpeedCoefficientProfile.Behavior.Speed.ReferenceMaximumCrawl);
        var noTransitionVariationConfiguration = LizardConfiguration.Default with
        {
            IndividualVariation = LizardConfiguration.Default.IndividualVariation with
            {
                TransitionWeightVariation = 0f
            }
        };
        var noTransitionVariationProfile = IndividualProfileFactory.Create(
            noTransitionVariationConfiguration,
            variationControlSeed,
            applyVariation: true);
        var transitionWeightsExact = noTransitionVariationConfiguration.Behavior.TransitionMatrix
            .AfterForward.Entries.Select(entry => BitConverter.SingleToInt32Bits(entry.Weight))
            .SequenceEqual(noTransitionVariationProfile.Behavior.TransitionMatrix.AfterForward.Entries
                .Select(entry => BitConverter.SingleToInt32Bits(entry.Weight)));
        var zeroRegripCoefficientConfiguration = LizardConfiguration.Default with
        {
            IndividualVariation = LizardConfiguration.Default.IndividualVariation with
            {
                Physics = LizardConfiguration.Default.IndividualVariation.Physics with
                {
                    RegripReachAgility = 0f,
                    RegripOutwardCuriosity = 0f,
                    RegripHoldCalmness = 0f
                }
            }
        };
        var zeroRegripCoefficientProfile = IndividualProfileFactory.Create(
            zeroRegripCoefficientConfiguration,
            variationControlSeed,
            applyVariation: true);
        var regripCoefficientsExact =
            BitConverter.SingleToInt32Bits(
                zeroRegripCoefficientConfiguration.Physics.RegripFrontReachLengthFactor) ==
            BitConverter.SingleToInt32Bits(
                zeroRegripCoefficientProfile.Physics.RegripFrontReachLengthFactor) &&
            BitConverter.SingleToInt32Bits(
                zeroRegripCoefficientConfiguration.Physics.RegripFrontReachOutwardWeight) ==
            BitConverter.SingleToInt32Bits(
                zeroRegripCoefficientProfile.Physics.RegripFrontReachOutwardWeight) &&
            BitConverter.SingleToInt32Bits(
                zeroRegripCoefficientConfiguration.Physics.RegripRearReachLengthFactor) ==
            BitConverter.SingleToInt32Bits(
                zeroRegripCoefficientProfile.Physics.RegripRearReachLengthFactor) &&
            BitConverter.SingleToInt32Bits(
                zeroRegripCoefficientConfiguration.Physics.RegripRearReachOutwardWeight) ==
            BitConverter.SingleToInt32Bits(
                zeroRegripCoefficientProfile.Physics.RegripRearReachOutwardWeight) &&
            BitConverter.SingleToInt32Bits(
                zeroRegripCoefficientConfiguration.Physics.RegripContactHoldFraction) ==
            BitConverter.SingleToInt32Bits(
                zeroRegripCoefficientProfile.Physics.RegripContactHoldFraction);
        var individualVariationControlsPassed =
            restWeightsExact &&
            speedCoefficientsExact &&
            transitionWeightsExact &&
            regripCoefficientsExact;

        AddFailure(defaultValidationPassed, "default-validation", failures);
        AddFailure(roundTripPassed, "round-trip", failures);
        AddFailure(persistentSeedPassed, "persistent-seed", failures);
        AddFailure(schemaMigrationPassed, "schema-migration", failures);
        AddFailure(
            persistenceFailurePreservesValidConfig,
            "write-back-failure-preserves-valid-config",
            failures);
        AddFailure(invalidFallbackPassed, "invalid-fallback", failures);
        AddFailure(invalidFilePreserved, "invalid-file-preservation", failures);
        AddFailure(deterministicProfilePassed, "deterministic-profile", failures);
        AddFailure(distinctIndividualsPassed, "distinct-individuals", failures);
        AddFailure(variationApplied, "variation-application", failures);
        AddFailure(transitionNormalizationPassed, "normalized-probabilities", failures);
        AddFailure(zeroWeightsPreserved, "zero-weight-preservation", failures);
        AddFailure(customMatrixControlPassed, "matrix-control", failures);
        AddFailure(unsafeMatrixRejected, "unsafe-matrix-rejection", failures);
        AddFailure(unsafeNumericConfigurationRejected, "unsafe-numeric-rejection", failures);
        AddFailure(hardLimitsPassed, "hard-limit-validation", failures);
        AddFailure(unknownPropertyRejected, "unknown-property-rejection", failures);
        AddFailure(timeStepDeterminismPassed, "time-step-determinism", failures);
        AddFailure(frameCatchUpBoundsPassed, "frame-catch-up-bounds", failures);
        AddFailure(geometryEnvelopePassed, "geometry-envelope", failures);
        AddFailure(derivedGeometryBoundsPassed, "derived-geometry-bounds", failures);
        AddFailure(individualVariationControlsPassed, "individual-variation-controls", failures);
        AddFailure(lostGripConfigurationPassed, "lost-grip-configuration", failures);

        return new Measurements(
            defaultValidationPassed,
            roundTripPassed,
            persistentSeedPassed,
            schemaMigrationPassed,
            persistenceFailurePreservesValidConfig,
            invalidFallbackPassed,
            invalidFilePreserved,
            deterministicProfilePassed,
            distinctIndividualsPassed,
            variationApplied,
            transitionNormalizationPassed,
            customMatrixControlPassed,
            unsafeMatrixRejected,
            unsafeNumericConfigurationRejected,
            hardLimitsPassed,
            unknownPropertyRejected,
            timeStepDeterminismPassed,
            frameCatchUpBoundsPassed,
            geometryEnvelopePassed,
            derivedGeometryBoundsPassed,
            individualVariationControlsPassed,
            lostGripConfigurationPassed,
            warningCount,
            string.Join("; ", failures));
    }

    private static LizardConfiguration CreateCustomConfiguration()
    {
        var defaults = LizardConfiguration.Default;
        var singleFastForward = new TransitionRowConfiguration
        {
            Entries = [new WeightedTransitionConfiguration(AutonomousAction.FastForward, 1f)]
        };
        return defaults with
        {
            IndividualSeed = 424242,
            IndividualVariation = defaults.IndividualVariation with
            {
                Enabled = false,
                Physics = defaults.IndividualVariation.Physics with
                {
                    RegripReachAgility = 0.11f,
                    RegripOutwardCuriosity = 0.13f,
                    RegripHoldCalmness = 0.17f
                }
            },
            Behavior = defaults.Behavior with
            {
                Speed = defaults.Behavior.Speed with
                {
                    ReferenceMinimumCrawl = 31f,
                    ReferenceMaximumCrawl = 44f
                },
                LostGripFall = defaults.Behavior.LostGripFall with
                {
                    ReachLeadDistance = 43f
                },
                TransitionMatrix = defaults.Behavior.TransitionMatrix with
                {
                    AfterForward = singleFastForward
                }
            },
            Physics = defaults.Physics with
            {
                RegripFrontReachLengthFactor = 0.77f,
                RegripFrontReachOutwardWeight = 0.29f,
                RegripRearReachLengthFactor = 0.73f,
                RegripRearReachOutwardWeight = 0.41f,
                RegripContactHoldFraction = 0.24f
            },
            Runtime = defaults.Runtime with { DebugPathCapacity = 77 }
        };
    }

    private static bool IsNormalized(TransitionRowConfiguration row) =>
        row.Entries.Length > 0 &&
        MathF.Abs(row.Entries.Sum(entry => entry.Weight) - 1f) <= 0.0001f;

    private static LizardConfiguration CreateConfigurationWithAfterFastAction(
        AutonomousAction action)
    {
        var defaults = LizardConfiguration.Default;
        return defaults with
        {
            Behavior = defaults.Behavior with
            {
                TransitionMatrix = defaults.Behavior.TransitionMatrix with
                {
                    AfterFast = new TransitionRowConfiguration
                    {
                        Entries = [new WeightedTransitionConfiguration(action, 1f)]
                    }
                }
            }
        };
    }

    private static LizardConfiguration CreateConfigurationWithAfterForwardAction(
        AutonomousAction action)
    {
        var defaults = LizardConfiguration.Default;
        return defaults with
        {
            Behavior = defaults.Behavior with
            {
                TransitionMatrix = defaults.Behavior.TransitionMatrix with
                {
                    AfterForward = new TransitionRowConfiguration
                    {
                        Entries = [new WeightedTransitionConfiguration(action, 1f)]
                    }
                }
            }
        };
    }

    private static bool SameProfileSignature(LizardProfile first, LizardProfile second) =>
        first.Traits == second.Traits &&
        string.Equals(ProfileSignature(first), ProfileSignature(second), StringComparison.Ordinal);

    private static string ProfileSignature(LizardProfile profile) => string.Join(
        '|',
        profile.Traits.Id,
        profile.Behavior.Speed.ReferenceMinimumCrawl.ToString("R"),
        profile.Behavior.Speed.ReferenceMaximumCrawl.ToString("R"),
        profile.Behavior.TransitionMatrix.AfterForward.Entries[0].Weight.ToString("R"),
        profile.Gait.FrontLegLinkLength.ToString("R"),
        profile.Physics.Gravity.ToString("R"),
        profile.Physics.RegripFrontReachLengthFactor.ToString("R"),
        profile.Physics.RegripFrontReachOutwardWeight.ToString("R"),
        profile.Physics.RegripRearReachLengthFactor.ToString("R"),
        profile.Physics.RegripRearReachOutwardWeight.ToString("R"),
        profile.Physics.RegripContactHoldFraction.ToString("R"),
        profile.Appearance.SpineLinkLength.ToString("R"),
        profile.SecondaryMotion.IdleTailAmplitude.ToString("R"),
        profile.Rendering.FrontFootRadius.ToString("R"));

    private static void AddFailure(bool passed, string name, List<string> failures)
    {
        if (!passed)
        {
            failures.Add(name);
        }
    }

    private static DiagnosticReport BuildReport(Measurements result) =>
        new DiagnosticReportBuilder("ConfigurationSelfTest")
            .AddMetric("warnings", result.WarningCount)
            .AddCheck("default configuration validates", result.DefaultValidationPassed, "true", result.DefaultValidationPassed.ToString())
            .AddCheck("JSON round-trip", result.RoundTripPassed, "true", result.RoundTripPassed.ToString())
            .AddCheck("generated seed persists", result.PersistentSeedPassed, "true", result.PersistentSeedPassed.ToString())
            .AddCheck("legacy schema migrates", result.SchemaMigrationPassed, "true", result.SchemaMigrationPassed.ToString())
            .AddCheck("write-back failure preserves valid configuration", result.PersistenceFailurePreservesValidConfig, "true", result.PersistenceFailurePreservesValidConfig.ToString())
            .AddCheck("invalid JSON falls back", result.InvalidFallbackPassed, "true", result.InvalidFallbackPassed.ToString())
            .AddCheck("invalid file is preserved", result.InvalidFilePreserved, "true", result.InvalidFilePreserved.ToString())
            .AddCheck("same seed is deterministic", result.DeterministicProfilePassed, "true", result.DeterministicProfilePassed.ToString())
            .AddCheck("different seeds create individuals", result.DistinctIndividualsPassed, "true", result.DistinctIndividualsPassed.ToString())
            .AddCheck("variation reaches every presentation layer", result.VariationApplied, "true", result.VariationApplied.ToString())
            .AddCheck("resolved probabilities stay normalized", result.TransitionNormalizationPassed, "true", result.TransitionNormalizationPassed.ToString())
            .AddCheck("transition matrix controls selection", result.CustomMatrixControlPassed, "true", result.CustomMatrixControlPassed.ToString())
            .AddCheck("unsafe matrix entries are rejected", result.UnsafeMatrixRejected, "true", result.UnsafeMatrixRejected.ToString())
            .AddCheck("unsafe numeric bounds are rejected", result.UnsafeNumericConfigurationRejected, "true", result.UnsafeNumericConfigurationRejected.ToString())
            .AddCheck("capacity and candidate hard limits", result.HardLimitsPassed, "true", result.HardLimitsPassed.ToString())
            .AddCheck("unknown JSON properties are rejected", result.UnknownPropertyRejected, "true", result.UnknownPropertyRejected.ToString())
            .AddCheck("fixed time-step configuration is deterministic", result.TimeStepDeterminismPassed, "true", result.TimeStepDeterminismPassed.ToString())
            .AddCheck("frame catch-up window covers at least 30 Hz", result.FrameCatchUpBoundsPassed, "true", result.FrameCatchUpBoundsPassed.ToString())
            .AddCheck("configured geometry fits render surfaces", result.GeometryEnvelopePassed, "true", result.GeometryEnvelopePassed.ToString())
            .AddCheck("derived geometry is finite and practically bounded", result.DerivedGeometryBoundsPassed, "true", result.DerivedGeometryBoundsPassed.ToString())
            .AddCheck("individual variation controls can be disabled exactly", result.IndividualVariationControlsPassed, "true", result.IndividualVariationControlsPassed.ToString())
            .AddCheck("lost-grip parameters and matrix entries validate", result.LostGripConfigurationPassed, "true", result.LostGripConfigurationPassed.ToString())
            .AddCheck("no unexpected failures", string.IsNullOrEmpty(result.FailureDetails), "empty", result.FailureDetails)
            .Build();
}
