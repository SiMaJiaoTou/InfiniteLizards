using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopLizard.Core;

internal static class PointerSchemaCompatibilitySelfTest
{
    public static PointerSchemaCompatibilitySelfTestResult Run()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "InfiniteLizards.PointerSchemaCompatibilitySelfTest",
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "schema8-before-pointer-mode.json");
            var document = JsonSerializer.SerializeToNode(
                LizardConfiguration.Default with { IndividualSeed = 24681 })!
                .AsObject();
            var pointer = document["Behavior"]!["Pointer"]!.AsObject();
            pointer.Remove("ResponseMode");
            pointer.Remove("AvoidanceDistance");
            pointer["TriggerMinimumDistance"] = 20f;
            pointer["TriggerMaximumDistance"] = 100f;
            pointer["RearmDistance"] = 120f;
            pointer["LostDistance"] = 140f;
            File.WriteAllText(path, document.ToJsonString());

            var loaded = LizardConfigurationStore.LoadOrCreate(path);
            var resolved = loaded.Configuration.Behavior.Pointer;
            var passed =
                loaded.LoadedFromDisk &&
                loaded.Warnings.Count == 0 &&
                loaded.Configuration.SchemaVersion == 8 &&
                resolved.ResponseMode == PointerResponseMode.Chase &&
                MathF.Abs(resolved.TriggerMinimumDistance - 20f) <= 0.0001f &&
                MathF.Abs(resolved.TriggerMaximumDistance - 100f) <= 0.0001f &&
                MathF.Abs(resolved.RearmDistance - 120f) <= 0.0001f &&
                MathF.Abs(resolved.LostDistance - 140f) <= 0.0001f &&
                MathF.Abs(resolved.AvoidanceDistance - 160f) <= 0.0001f &&
                loaded.Configuration.Validate().Count == 0;
            return new PointerSchemaCompatibilitySelfTestResult(
                passed,
                passed
                    ? "A schema-8 Chase document without new pointer fields preserved 20/100/120/140 exactly."
                    : "The schema-8 Chase document fell back or rewrote its legacy pointer thresholds.");
        }
        catch (Exception exception)
        {
            return new PointerSchemaCompatibilitySelfTestResult(false, exception.Message);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

internal readonly record struct PointerSchemaCompatibilitySelfTestResult(
    bool Passed,
    string Detail);
