using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.IO;

namespace DesktopLizard.Core;

internal sealed record ConfigurationLoadResult(
    LizardConfiguration Configuration,
    string Path,
    bool LoadedFromDisk,
    IReadOnlyList<string> Warnings);

/// <summary>
/// JSON persistence at the process boundary. Core simulation classes receive
/// a validated profile and never perform file I/O themselves.
/// </summary>
internal static class LizardConfigurationStore
{
    private const string DefaultFileName = "lizard-settings.json";
    private const float MigratedLostGripFallWeight = 0.02f;

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopLizard",
        DefaultFileName);

    public static ConfigurationLoadResult LoadOrCreate(string? path = null)
    {
        path = string.IsNullOrWhiteSpace(path) ? DefaultPath : System.IO.Path.GetFullPath(path);
        var warnings = new List<string>();

        if (!File.Exists(path))
        {
            var created = WithPersistentSeed(LizardConfiguration.Default);
            try
            {
                Save(created, path);
                return new ConfigurationLoadResult(created, path, false, warnings);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"无法创建配置文件，将使用内存默认值：{exception.Message}");
                return new ConfigurationLoadResult(created, path, false, warnings);
            }
        }

        try
        {
            var json = NormalizeLegacyProperties(
                File.ReadAllText(path),
                out var normalizedLegacyProperties);
            var configuration = JsonSerializer.Deserialize<LizardConfiguration>(json, SerializerOptions) ??
                                throw new InvalidDataException("configuration document was empty.");
            var migratedFromVersion = configuration.SchemaVersion;
            if (migratedFromVersion is 1 or 2 or 3 or 4 or 5)
            {
                // Record initializers populate every field added after the
                // stored schema. Promoting and writing the canonical document
                // exposes the new typed individual-variation controls without
                // losing any values already chosen by the user.
                configuration = MigrateToCurrentSchema(configuration);
            }
            var failures = configuration.Validate();
            if (failures.Count > 0)
            {
                throw new InvalidDataException(string.Join(Environment.NewLine, failures));
            }

            var requiresSave =
                configuration.IndividualSeed is null ||
                migratedFromVersion != LizardConfiguration.CurrentSchemaVersion ||
                normalizedLegacyProperties;
            if (configuration.IndividualSeed is null)
            {
                configuration = WithPersistentSeed(configuration);
            }
            // Validate the resolved profile for the actual persistent seed,
            // not only the editable species defaults. Unsafe variation now
            // follows the recoverable bad-config path instead of failing
            // later while App creates the window.
            _ = IndividualProfileFactory.Create(
                configuration,
                configuration.IndividualSeed ?? 0);
            if (requiresSave)
            {
                Save(configuration, path);
            }
            if (migratedFromVersion != LizardConfiguration.CurrentSchemaVersion)
            {
                warnings.Add(
                    $"配置已从 schema {migratedFromVersion} 升级到 schema {LizardConfiguration.CurrentSchemaVersion}，并补全所有可编辑参数。");
            }
            return new ConfigurationLoadResult(configuration, path, true, warnings);
        }
        catch (Exception exception) when (
            exception is JsonException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            NullReferenceException)
        {
            warnings.Add($"配置文件无效，将保留原文件并使用内存默认值：{exception.Message}");
            return new ConfigurationLoadResult(
                WithPersistentSeed(LizardConfiguration.Default),
                path,
                false,
                warnings);
        }
    }

    public static void Save(LizardConfiguration configuration, string? path = null)
    {
        configuration.EnsureValid();
        path = string.IsNullOrWhiteSpace(path) ? DefaultPath : System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(configuration, SerializerOptions);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public static LizardProfile ResolveProfile(ConfigurationLoadResult loadResult) =>
        IndividualProfileFactory.Create(
            loadResult.Configuration,
            loadResult.Configuration.IndividualSeed ?? 0);

    private static LizardConfiguration WithPersistentSeed(LizardConfiguration configuration)
    {
        if (configuration.IndividualSeed is not null)
        {
            return configuration;
        }

        return configuration with
        {
            IndividualSeed = RandomNumberGenerator.GetInt32(1, int.MaxValue)
        };
    }

    private static LizardConfiguration MigrateToCurrentSchema(
        LizardConfiguration configuration)
    {
        var migratedFromVersion = configuration.SchemaVersion;
        var behavior = configuration.Behavior;
        if (behavior is null)
        {
            return configuration with
            {
                SchemaVersion = LizardConfiguration.CurrentSchemaVersion
            };
        }

        var upgradedBehavior = behavior;
        var matrix = behavior.TransitionMatrix;
        if (matrix is not null && migratedFromVersion is 1 or 2 or 3)
        {
            // LostGripFall did not exist before schema 4. A schema-4 matrix
            // that omits the action is already an intentional user choice and
            // must not be silently re-enabled while migrating later
            // lost-grip presentation controls.
            upgradedBehavior = upgradedBehavior with
            {
                TransitionMatrix = matrix with
                {
                    AfterForward = AddMigratedLostGripFall(matrix.AfterForward),
                    AfterCurve = AddMigratedLostGripFall(matrix.AfterCurve),
                    AfterSCurve = AddMigratedLostGripFall(matrix.AfterSCurve),
                    AfterFast = AddMigratedLostGripFall(matrix.AfterFast)
                }
            };
        }
        if (migratedFromVersion == 4 &&
            behavior.LostGripFall is { } legacyFall &&
            MathF.Abs(
                legacyFall.MinimumDistance -
                LostGripFallConfiguration.LegacyDefaultMinimumDistance) <= 0.0001f)
        {
            // Schema 4 bounded the fall to 78--132 px. Schema 5+ uses the
            // current screen-safe bottom as the upper endpoint, so promote
            // only the old default minimum. A user-authored minimum remains
            // authoritative even when it is shorter than the new default.
            upgradedBehavior = upgradedBehavior with
            {
                LostGripFall = legacyFall with
                {
                    MinimumDistance = LostGripFallConfiguration.DefaultMinimumDistance
                }
            };
        }
        upgradedBehavior = RaiseMigratedFallSafetyInset(
            upgradedBehavior,
            configuration);
        return configuration with
        {
            SchemaVersion = LizardConfiguration.CurrentSchemaVersion,
            Behavior = upgradedBehavior
        };
    }

    private static BehaviorConfiguration RaiseMigratedFallSafetyInset(
        BehaviorConfiguration behavior,
        LizardConfiguration configuration)
    {
        if (behavior.LostGripFall is null ||
            configuration.Appearance is null ||
            configuration.Gait is null ||
            configuration.SecondaryMotion is null ||
            configuration.Rendering is null ||
            configuration.Runtime is null)
        {
            return behavior;
        }

        var envelope = LizardGeometryEnvelope.Calculate(
            configuration.Appearance,
            configuration.Gait,
            configuration.SecondaryMotion,
            configuration.Rendering);
        var requiredInset = envelope.RequiredFallBottomSafetyInset(
            configuration.Appearance,
            configuration.Runtime);
        if (!float.IsFinite(requiredInset) ||
            !float.IsFinite(behavior.LostGripFall.BottomSafetyInset) ||
            behavior.LostGripFall.BottomSafetyInset + 0.0001f >= requiredInset)
        {
            return behavior;
        }

        return behavior with
        {
            LostGripFall = behavior.LostGripFall with
            {
                BottomSafetyInset = MathF.Ceiling(requiredInset)
            }
        };
    }

    private static TransitionRowConfiguration AddMigratedLostGripFall(
        TransitionRowConfiguration? row)
    {
        if (row is null)
        {
            // Keep the document invalid but representable so the regular
            // configuration validator can produce the recoverable fallback.
            return new TransitionRowConfiguration();
        }
        if (row.Entries is null ||
            row.Entries.Any(entry => entry is null) ||
            row.Entries.Any(entry => entry.Action == AutonomousAction.LostGripFall))
        {
            return row;
        }

        var donorIndex = -1;
        for (var index = row.Entries.Length - 1; index >= 0; index--)
        {
            if (row.Entries[index].Weight >= MigratedLostGripFallWeight)
            {
                donorIndex = index;
                break;
            }
        }
        if (donorIndex < 0)
        {
            // Leave a malformed legacy row malformed so normal validation can
            // report it. Never manufacture a negative migration weight.
            return row;
        }

        var entries = new WeightedTransitionConfiguration[row.Entries.Length + 1];
        Array.Copy(row.Entries, entries, row.Entries.Length);
        entries[donorIndex] = entries[donorIndex] with
        {
            Weight = entries[donorIndex].Weight - MigratedLostGripFallWeight
        };
        entries[^1] = new WeightedTransitionConfiguration(
            AutonomousAction.LostGripFall,
            MigratedLostGripFallWeight);
        return row with { Entries = entries };
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static string NormalizeLegacyProperties(
        string json,
        out bool normalizedLegacyProperties)
    {
        normalizedLegacyProperties = false;
        var documentOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };
        var root = JsonNode.Parse(json, documentOptions: documentOptions) as JsonObject;
        if (root is null)
        {
            return json;
        }

        var changed = false;
        var isLegacyFallRangeSchema =
            FindProperty(root, "SchemaVersion") is JsonValue schemaVersionNode &&
            schemaVersionNode.TryGetValue<int>(out var storedSchemaVersion) &&
            storedSchemaVersion <= 4;
        if (FindProperty(root, "Behavior") is JsonObject behavior)
        {
            if (FindProperty(behavior, "Rest") is JsonObject rest)
            {
                changed |= RemoveProperty(rest, "MinimumDuration");
                changed |= RemoveProperty(rest, "MaximumDuration");
            }

            if (FindProperty(behavior, "Speed") is JsonObject speed)
            {
                changed |= RenameProperty(
                    speed,
                    "BaseSpeedMultiplier",
                    "BoundaryRecoverySpeedMultiplier");
            }

            if (isLegacyFallRangeSchema &&
                FindProperty(behavior, "LostGripFall") is JsonObject lostGripFall)
            {
                // Schema 5 derives the upper endpoint from the current
                // full-render-safe screen bottom. Remove the obsolete fixed
                // ceiling before strict typed deserialization.
                changed |= RemoveProperty(lostGripFall, "MaximumDistance");
            }
        }
        if (FindProperty(root, "Physics") is JsonObject physics)
        {
            // Schema 3 briefly emitted this field after the ragdoll had
            // already switched to preserving the measured grab-frame bone
            // lengths. It never affected motion, so remove it rather than
            // leaving a misleading user-facing knob behind.
            changed |= RemoveProperty(physics, "LimbExtensionDuration");
        }
        if (FindProperty(root, "IndividualVariation") is JsonObject variation &&
            FindProperty(variation, "Transitions") is JsonObject transitions)
        {
            // Every valid AutonomousAction has an explicit personality
            // mapping; unknown enum values are rejected during validation.
            changed |= RemoveProperty(transitions, "FallbackPersonality");
        }
        normalizedLegacyProperties = changed;
        return changed ? root.ToJsonString() : json;
    }

    private static JsonNode? FindProperty(JsonObject instance, string name)
    {
        foreach (var property in instance)
        {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static bool RemoveProperty(JsonObject instance, string name)
    {
        foreach (var property in instance)
        {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return instance.Remove(property.Key);
            }
        }

        return false;
    }

    private static bool RenameProperty(
        JsonObject instance,
        string legacyName,
        string currentName)
    {
        string? legacyKey = null;
        JsonNode? legacyValue = null;
        foreach (var property in instance)
        {
            if (string.Equals(property.Key, legacyName, StringComparison.OrdinalIgnoreCase))
            {
                legacyKey = property.Key;
                legacyValue = property.Value;
                break;
            }
        }
        if (legacyKey is null)
        {
            return false;
        }

        instance.Remove(legacyKey);
        if (FindProperty(instance, currentName) is null)
        {
            instance[currentName] = legacyValue;
        }
        return true;
    }
}
