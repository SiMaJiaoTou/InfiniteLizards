using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using InfiniteLizards.Gameplay.Breeding;

namespace InfiniteLizards.Desktop.Persistence;

internal sealed record BreedingWorldSaveEnvelope(
    int SchemaVersion,
    DateTimeOffset SavedAtUtc,
    BreedingWorldSnapshot Snapshot,
    string? ActiveLizardId = null)
{
    internal const int CurrentSchemaVersion = 1;
}

internal sealed record BreedingWorldLoadResult(
    BreedingSimulation Simulation,
    DateTimeOffset SavedAtUtc,
    TimeSpan OfflineElapsed,
    string? Warning,
    string? ActiveLizardId);

/// <summary>
/// Independent, atomic world persistence. Species tuning remains in
/// lizard-settings.json; player economy, eggs and bloodlines live here.
/// </summary>
internal sealed class BreedingWorldStore : IDisposable
{
    internal const string FileName = "breeding-world.json";
    internal static readonly TimeSpan MaximumOfflineAdvance = TimeSpan.FromDays(30d);
    private static readonly TimeSpan FutureClockTolerance = TimeSpan.FromMinutes(5d);
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    private readonly bool _deleteOnDispose;
    private bool _disposed;

    internal string Path { get; }
    internal string BackupPath => Path + ".bak";
    internal bool IsEphemeral => _deleteOnDispose;

    internal BreedingWorldStore(string path, bool deleteOnDispose)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A breeding-world path is required.", nameof(path));
        }
        Path = System.IO.Path.GetFullPath(path);
        _deleteOnDispose = deleteOnDispose;
    }

    internal static BreedingWorldStore Create(
        string configurationPath,
        bool ephemeral)
    {
        if (ephemeral)
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "InfiniteLizards",
                $"session-{Environment.ProcessId}-{Guid.NewGuid():N}");
            return new BreedingWorldStore(
                System.IO.Path.Combine(directory, FileName),
                deleteOnDispose: true);
        }

        var settingsDirectory = System.IO.Path.GetDirectoryName(configurationPath);
        if (string.IsNullOrWhiteSpace(settingsDirectory))
        {
            settingsDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopLizard");
        }
        return new BreedingWorldStore(
            System.IO.Path.Combine(settingsDirectory, FileName),
            deleteOnDispose: false);
    }

    internal BreedingWorldLoadResult LoadOrCreate(
        ulong seed,
        DateTimeOffset nowUtc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        nowUtc = NormalizeUtc(nowUtc);
        Exception? primaryFailure = null;
        if (File.Exists(Path))
        {
            try
            {
                return ReadWorld(Path, nowUtc);
            }
            catch (Exception exception) when (IsLoadFailure(exception))
            {
                primaryFailure = exception;
            }
        }

        Exception? backupFailure = null;
        if (File.Exists(BackupPath))
        {
            BreedingWorldLoadResult? recovered = null;
            try
            {
                recovered = ReadWorld(BackupPath, nowUtc);
            }
            catch (Exception exception) when (IsLoadFailure(exception))
            {
                backupFailure = exception;
            }

            if (recovered is not null)
            {
                TryPreserveInvalidFile(Path, nowUtc);
                PromoteRecovered(
                    recovered.Simulation.CaptureSnapshot(),
                    nowUtc,
                    recovered.ActiveLizardId);
                var recoveryMessage = primaryFailure is null
                    ? "繁育主存档缺失，已从备份恢复。"
                    : $"繁育主存档损坏，已从备份恢复：{primaryFailure.Message}";
                return recovered with
                {
                    SavedAtUtc = nowUtc,
                    Warning = CombineWarnings(recoveryMessage, recovered.Warning)
                };
            }
        }

        if (primaryFailure is null && backupFailure is null)
        {
            var created = BreedingSimulation.Create(seed, initialCoins: 10);
            Save(created.CaptureSnapshot(), nowUtc, activeLizardId: null);
            return new BreedingWorldLoadResult(
                created,
                nowUtc,
                TimeSpan.Zero,
                null,
                null);
        }

        var fresh = BreedingSimulation.Create(seed, initialCoins: 10);
        var failureMessage = CombineWarnings(
            primaryFailure is null ? null : $"主存档：{primaryFailure.Message}",
            backupFailure is null ? null : $"备份：{backupFailure.Message}");
        var warning = $"繁育存档及备份均无法读取，已使用新的 10 金币家园：{failureMessage}";
        TryPreserveInvalidFile(Path, nowUtc);
        TryPreserveInvalidFile(BackupPath, nowUtc);
        Save(fresh.CaptureSnapshot(), nowUtc, activeLizardId: null);
        return new BreedingWorldLoadResult(
            fresh,
            nowUtc,
            TimeSpan.Zero,
            warning,
            null);
    }

    internal void Save(
        BreedingWorldSnapshot snapshot,
        DateTimeOffset savedAtUtc,
        string? activeLizardId) =>
        SaveCore(snapshot, savedAtUtc, activeLizardId, rotatePrimaryToBackup: true);

    /// <summary>
    /// Promotes a snapshot already validated from <see cref="BackupPath"/>.
    /// The existing primary is known to be missing or invalid, so it must never
    /// be rotated over the last valid backup even when preserving it failed.
    /// </summary>
    internal void PromoteRecovered(
        BreedingWorldSnapshot snapshot,
        DateTimeOffset savedAtUtc,
        string? activeLizardId) =>
        SaveCore(snapshot, savedAtUtc, activeLizardId, rotatePrimaryToBackup: false);

    private void SaveCore(
        BreedingWorldSnapshot snapshot,
        DateTimeOffset savedAtUtc,
        string? activeLizardId,
        bool rotatePrimaryToBackup)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        savedAtUtc = NormalizeUtc(savedAtUtc);
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var envelope = new BreedingWorldSaveEnvelope(
            BreedingWorldSaveEnvelope.CurrentSchemaVersion,
            savedAtUtc,
            snapshot,
            activeLizardId);
        var json = JsonSerializer.Serialize(envelope, SerializerOptions);
        var temporaryPath = Path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            if (rotatePrimaryToBackup && File.Exists(Path))
            {
                File.Copy(Path, BackupPath, overwrite: true);
            }
            File.Move(temporaryPath, Path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private static BreedingWorldLoadResult ReadWorld(
        string path,
        DateTimeOffset nowUtc)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var envelope = JsonSerializer.Deserialize<BreedingWorldSaveEnvelope>(
                json,
                SerializerOptions) ??
            throw new InvalidDataException("Breeding world was empty.");
        if (envelope.SchemaVersion != BreedingWorldSaveEnvelope.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported breeding-world envelope schema {envelope.SchemaVersion}.");
        }
        if (envelope.Snapshot is null)
        {
            throw new InvalidDataException("Breeding world snapshot is missing.");
        }

        var savedAt = NormalizeUtc(envelope.SavedAtUtc);
        TimeSpan offline;
        string? warning = null;
        if (savedAt > nowUtc + FutureClockTolerance)
        {
            offline = TimeSpan.Zero;
            warning = "系统时间早于上次存档，已暂停离线成长以保护存档。";
        }
        else
        {
            offline = nowUtc - savedAt;
            if (offline < TimeSpan.Zero)
            {
                offline = TimeSpan.Zero;
            }
            if (offline > MaximumOfflineAdvance)
            {
                offline = MaximumOfflineAdvance;
                warning = $"离线成长最多结算 {MaximumOfflineAdvance.TotalDays:0} 天。";
            }
        }

        var simulation = BreedingSimulation.Restore(envelope.Snapshot);
        if (offline > TimeSpan.Zero)
        {
            simulation.Advance(offline);
        }

        var activeLizardId = string.IsNullOrWhiteSpace(envelope.ActiveLizardId)
            ? null
            : envelope.ActiveLizardId.Trim();
        if (activeLizardId is not null &&
            !envelope.Snapshot.Lizards.Any(lizard => lizard.Id == activeLizardId))
        {
            warning = CombineWarnings(
                warning,
                "存档中的桌宠已不在收藏，已自动选择另一只蜥蜴。");
            activeLizardId = null;
        }
        activeLizardId ??= envelope.Snapshot.Lizards.FirstOrDefault()?.Id;
        return new BreedingWorldLoadResult(
            simulation,
            savedAt,
            offline,
            warning,
            activeLizardId);
    }

    private static bool IsLoadFailure(Exception exception) => exception is
        JsonException or
        InvalidDataException or
        ArgumentException or
        InvalidOperationException or
        KeyNotFoundException or
        NotSupportedException or
        FormatException or
        OverflowException or
        IOException or
        UnauthorizedAccessException;

    private static string CombineWarnings(params string?[] warnings) =>
        string.Join(" ", warnings.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static void TryPreserveInvalidFile(
        string path,
        DateTimeOffset nowUtc)
    {
        try
        {
            if (!File.Exists(path)) return;
            var suffix = nowUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss");
            File.Move(path, path + $".invalid-{suffix}", overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static DateTimeOffset NormalizeUtc(DateTimeOffset value)
    {
        if (value == default)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        return value.ToUniversalTime();
    }

    private static JsonSerializerOptions CreateOptions()
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_deleteOnDispose) return;
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (string.IsNullOrWhiteSpace(directory)) return;
        try
        {
            if (File.Exists(Path)) File.Delete(Path);
            if (File.Exists(Path + ".bak")) File.Delete(Path + ".bak");
            if (File.Exists(Path + ".tmp")) File.Delete(Path + ".tmp");
            Directory.Delete(directory, recursive: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
