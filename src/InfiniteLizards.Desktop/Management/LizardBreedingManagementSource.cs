using System.Collections.Immutable;
using System.Diagnostics;
using InfiniteLizards.Desktop.Persistence;
using InfiniteLizards.Gameplay.Application;
using InfiniteLizards.Gameplay.Breeding;
using InfiniteLizards.Gameplay.Genetics;
using InfiniteLizards.Gameplay.Phenotypes;

namespace InfiniteLizards.Desktop.Management;

/// <summary>
/// Serializes UI commands, converts domain DTOs into player-facing read models
/// and owns write-through persistence. No Avalonia control can mutate money,
/// age, genomes or habitats directly.
/// </summary>
internal sealed class LizardBreedingManagementSource : IBreedingManagementSource
{
    internal const string BreedingNestHabitatId = "breeding-nest-primary";
    private static readonly TimeSpan TimedSaveInterval = TimeSpan.FromSeconds(30d);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly BreedingWorldStore _store;
    private string? _runningDesktopLizardId;
    private LizardGenome? _runningDesktopLizardGenome;
    private LizardBreedingService _service;
    private volatile BreedingManagementSnapshot _current;
    private string? _nextDesktopLizardId;
    private TimeSpan _elapsedSinceSave;
    private string? _notice;
    private bool _disposed;

    public event EventHandler? Changed;

    public BreedingManagementSnapshot Current => _current;

    internal string SavePath => _store.Path;
    internal string? RunningDesktopLizardId => _runningDesktopLizardId;
    internal LizardGenome? RunningDesktopLizardGenome => _runningDesktopLizardGenome;

    internal void MarkRunningDesktopFallback()
    {
        if (_runningDesktopLizardId is null && _runningDesktopLizardGenome is null)
        {
            return;
        }
        _runningDesktopLizardId = null;
        _runningDesktopLizardGenome = null;
        _notice = "收藏桌宠无法应用当前外观配置，本次会话改用桌面向导；下次桌宠预约仍保留。";
        _current = BuildSnapshot();
    }

    internal LizardBreedingManagementSource(
        BreedingWorldStore store,
        ulong seed,
        DateTimeOffset nowUtc)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        var loaded = store.LoadOrCreate(seed, nowUtc);
        _service = new LizardBreedingService(loaded.Simulation);
        var startupWorld = _service.CaptureSnapshot();
        var startupLizard = startupWorld.Lizards.FirstOrDefault(lizard =>
                string.Equals(lizard.Id, loaded.ActiveLizardId, StringComparison.Ordinal)) ??
            startupWorld.Lizards.FirstOrDefault();
        _runningDesktopLizardId = startupLizard?.Id;
        _runningDesktopLizardGenome = startupLizard?.Genome;
        _nextDesktopLizardId = store.IsEphemeral ? null : startupLizard?.Id;
        _notice = loaded.Warning ?? (loaded.OfflineElapsed > TimeSpan.Zero
            ? $"已结算 {FormatDuration(loaded.OfflineElapsed)} 离线成长。"
            : "初始余额 10 枚；可购买两只成熟蜥蜴开始繁育。");
        _current = BuildSnapshot();
        if (loaded.OfflineElapsed > TimeSpan.Zero)
        {
            // Persist the reconciled age immediately so the same offline span
            // cannot be applied again after a crash during the first session.
            _store.Save(
                _service.CaptureSnapshot(),
                nowUtc,
                _nextDesktopLizardId);
        }
    }

    public BreedingActionAvailability GetPairAvailability(
        string? firstParentId,
        string? secondParentId)
    {
        if (string.IsNullOrWhiteSpace(firstParentId) ||
            string.IsNullOrWhiteSpace(secondParentId))
        {
            return new BreedingActionAvailability(false, "请先把两只成熟蜥蜴放入繁育巢。");
        }
        if (string.Equals(firstParentId, secondParentId, StringComparison.Ordinal))
        {
            return new BreedingActionAvailability(false, "繁育需要两只不同的蜥蜴。");
        }
        var first = _current.Lizards.FirstOrDefault(lizard => lizard.Id == firstParentId);
        var second = _current.Lizards.FirstOrDefault(lizard => lizard.Id == secondParentId);
        if (first is null || second is null)
        {
            return new BreedingActionAvailability(false, "至少一只亲本已不在家园中。");
        }
        if (!first.CanBreed || !second.CanBreed)
        {
            return new BreedingActionAvailability(false, "两只蜥蜴都成熟且冷却完成后才能繁育。");
        }
        if (!_current.IdentityCapacityAvailable)
        {
            return new BreedingActionAvailability(
                false,
                "存档的个体编号空间已用尽，无法创建新的蛋。");
        }
        return BreedingActionAvailability.Allowed;
    }

    public ValueTask<BreedingManagementActionResult> BuyAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteWriteThroughAsync(
            service => service.Buy("home-habitat"),
            cancellationToken);

    public ValueTask<BreedingManagementActionResult> SellAsync(
        string lizardId,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(
                lizardId,
                _runningDesktopLizardId,
                StringComparison.Ordinal))
        {
            return ValueTask.FromResult(new BreedingManagementActionResult(
                false,
                "当前桌宠正在桌面运行，不能在本次会话中出售；请先设置另一只桌宠并重启。"));
        }

        return ExecuteWriteThroughAsync(
            service => service.Sell(lizardId),
            cancellationToken);
    }

    public async ValueTask<BreedingManagementActionResult> SetDesktopLizardAsync(
        string lizardId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        BreedingManagementActionResult presentation;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var world = _service.CaptureSnapshot();
            if (_store.IsEphemeral)
            {
                presentation = new BreedingManagementActionResult(
                    false,
                    "临时家园会在退出时删除，不能预约下次启动的桌宠。");
            }
            else if (!world.Lizards.Any(lizard => lizard.Id == lizardId))
            {
                presentation = new BreedingManagementActionResult(
                    false,
                    "这只蜥蜴已不在收藏中。");
            }
            else if (string.Equals(
                         _nextDesktopLizardId,
                         lizardId,
                         StringComparison.Ordinal))
            {
                presentation = new BreedingManagementActionResult(
                    true,
                    string.Equals(_runningDesktopLizardId, lizardId, StringComparison.Ordinal)
                        ? "这只蜥蜴已经是当前桌宠。"
                        : "这只蜥蜴已经预约为下次启动的桌宠。");
            }
            else
            {
                var beforeSelection = _nextDesktopLizardId;
                _nextDesktopLizardId = lizardId;
                if (!TrySaveNow(out var saveFailure))
                {
                    _nextDesktopLizardId = beforeSelection;
                    presentation = new BreedingManagementActionResult(
                        false,
                        $"桌宠选择未提交，存档写入失败：{saveFailure}");
                }
                else
                {
                    presentation = new BreedingManagementActionResult(
                        true,
                        string.Equals(_runningDesktopLizardId, lizardId, StringComparison.Ordinal)
                            ? "已取消桌宠切换；下次启动仍使用当前蜥蜴。"
                            : "已设为下次启动的桌宠；当前桌宠会保持到本次会话结束。");
                }
            }
            _notice = presentation.Message;
            _current = BuildSnapshot();
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return presentation;
    }

    public async ValueTask<BreedingManagementActionResult> BreedAsync(
        string firstParentId,
        string secondParentId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        BreedingManagementActionResult presentation;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var availability = GetPairAvailability(firstParentId, secondParentId);
            if (!availability.IsAllowed)
            {
                return new BreedingManagementActionResult(false, availability.Reason);
            }

            var before = _service.CaptureSnapshot();
            var beforeSelection = _nextDesktopLizardId;
            try
            {
                var firstMove = _service.Move(firstParentId, BreedingNestHabitatId);
                var secondMove = firstMove.Succeeded
                    ? _service.Move(secondParentId, BreedingNestHabitatId)
                    : firstMove;
                var breeding = secondMove.Succeeded
                    ? _service.Breed(firstParentId, secondParentId)
                    : secondMove;
                if (!breeding.Succeeded)
                {
                    Restore(before);
                    _nextDesktopLizardId = beforeSelection;
                    presentation = new BreedingManagementActionResult(false, breeding.Message);
                }
                else if (!TrySaveNow(out var saveFailure))
                {
                    Restore(before);
                    _nextDesktopLizardId = beforeSelection;
                    presentation = new BreedingManagementActionResult(
                        false,
                        $"繁育未提交，存档写入失败：{saveFailure}");
                }
                else
                {
                    presentation = new BreedingManagementActionResult(
                        true,
                        "两只亲本已放入同一个繁育巢，并成功产下一枚蛋。");
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                InvalidDataException or
                InvalidOperationException or
                OverflowException)
            {
                Restore(before);
                _nextDesktopLizardId = beforeSelection;
                presentation = new BreedingManagementActionResult(
                    false,
                    $"繁育未提交，领域规则拒绝了本次操作：{exception.Message}");
            }
            _notice = presentation.Message;
            _current = BuildSnapshot();
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return presentation;
    }

    public async ValueTask AdvanceAsync(
        TimeSpan elapsed,
        CancellationToken cancellationToken = default)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var shouldNotify = false;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var result = _service.Advance(elapsed);
            _elapsedSinceSave += elapsed;
            if (!result.HatchlingLizardIds.IsEmpty)
            {
                _notice = result.HatchlingLizardIds.Length == 1
                    ? "一枚蛋刚刚破壳，家园里多了一只幼年蜥蜴！"
                    : $"{result.HatchlingLizardIds.Length} 枚蛋刚刚破壳！";
                shouldNotify = true;
            }
            else if (!result.NewlyMaturedLizardIds.IsEmpty)
            {
                _notice = result.NewlyMaturedLizardIds.Length == 1
                    ? "一只幼年蜥蜴已经成熟，可以参与繁育。"
                    : $"{result.NewlyMaturedLizardIds.Length} 只蜥蜴已经成熟。";
                shouldNotify = true;
            }

            if (shouldNotify || _elapsedSinceSave >= TimedSaveInterval)
            {
                if (!TrySaveNow(out var failure))
                {
                    _notice = $"成长仍在继续，但暂时无法保存：{failure}";
                    shouldNotify = true;
                }
            }
            _current = BuildSnapshot();
        }
        finally
        {
            _gate.Release();
        }
        if (shouldNotify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async ValueTask<BreedingManagementActionResult> ExecuteWriteThroughAsync(
        Func<LizardBreedingService, BreedingActionResult> command,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        BreedingManagementActionResult presentation;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var before = _service.CaptureSnapshot();
            var beforeSelection = _nextDesktopLizardId;
            var result = command(_service);
            if (!result.Succeeded)
            {
                presentation = new BreedingManagementActionResult(false, result.Message);
            }
            else if (!TrySaveNow(out var saveFailure))
            {
                Restore(before);
                _nextDesktopLizardId = beforeSelection;
                presentation = new BreedingManagementActionResult(
                    false,
                    $"操作未提交，存档写入失败：{saveFailure}");
            }
            else
            {
                presentation = new BreedingManagementActionResult(true, result.Message);
            }
            _notice = presentation.Message;
            _current = BuildSnapshot();
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return presentation;
    }

    private bool TrySaveNow(out string failure)
    {
        try
        {
            var world = _service.CaptureSnapshot();
            NormalizeNextDesktopLizard(world);
            _store.Save(
                world,
                DateTimeOffset.UtcNow,
                _nextDesktopLizardId);
            _elapsedSinceSave = TimeSpan.Zero;
            failure = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            NotSupportedException or
            ArgumentException)
        {
            Trace.WriteLine($"[InfiniteLizards] breeding save failed: {exception}");
            failure = exception.Message;
            return false;
        }
    }

    private void Restore(BreedingWorldSnapshot snapshot) =>
        _service = new LizardBreedingService(BreedingSimulation.Restore(snapshot));

    private void NormalizeNextDesktopLizard(BreedingWorldSnapshot world)
    {
        if (_store.IsEphemeral)
        {
            _nextDesktopLizardId = null;
            return;
        }
        if (_nextDesktopLizardId is not null &&
            world.Lizards.Any(lizard => lizard.Id == _nextDesktopLizardId))
        {
            return;
        }
        _nextDesktopLizardId = world.Lizards.FirstOrDefault()?.Id;
    }

    private BreedingManagementSnapshot BuildSnapshot()
    {
        var collection = _service.CaptureCollection();
        var world = _service.CaptureSnapshot();
        NormalizeNextDesktopLizard(world);
        var worldLizards = world.Lizards.ToDictionary(lizard => lizard.Id, StringComparer.Ordinal);
        var names = collection.Lizards.ToDictionary(lizard => lizard.Id, lizard => lizard.Name, StringComparer.Ordinal);
        var recoverableBreedingUnitsAfterSale =
            Math.Max(0, collection.Lizards.Length - 1) +
            collection.Eggs.Length +
            (collection.Coins + (long)collection.SellPrice) / collection.BuyPrice;
        var sellingWouldSoftLock = recoverableBreedingUnitsAfterSale < 2L;
        var identityCapacityAvailable = world.NextSequence < long.MaxValue;
        var cards = collection.Lizards.Select(details =>
        {
            var raw = worldLizards[details.Id];
            var isRunningDesktopPet = string.Equals(
                details.Id,
                _runningDesktopLizardId,
                StringComparison.Ordinal);
            var isNextDesktopPet = !_store.IsEphemeral &&
                string.Equals(
                    details.Id,
                    _nextDesktopLizardId,
                    StringComparison.Ordinal);
            var canSell = !isRunningDesktopPet && !sellingWouldSoftLock;
            var sellDisabledReason = isRunningDesktopPet
                ? "当前桌宠正在桌面运行；请先设置另一只桌宠并重启后再出售。"
                : sellingWouldSoftLock
                    ? "至少保留一条可恢复血统，或攒够再次买入所需的余额。"
                    : string.Empty;
            return new BreedingLizardCard(
                details.Id,
                details.Name,
                details.LifeStage == BreedingLifeStage.Mature ? "成熟" : "幼年",
                details.LifeStage == BreedingLifeStage.Mature
                    ? $"已成长 {FormatDuration(raw.Age)}"
                    : $"成熟进度 {details.MaturityProgress:P0}",
                details.BreedingCooldown > TimeSpan.Zero
                    ? $"冷却剩余 {FormatDuration(details.BreedingCooldown)}"
                    : details.CanBreed ? "可繁育" : "尚未成熟",
                details.Generation,
                details.CanBreed,
                canSell,
                sellDisabledReason,
                isRunningDesktopPet,
                isNextDesktopPet,
                CreatePortrait(raw.Genome),
                details.Traits
                    .Select(ToReadout)
                    .ToImmutableArray(),
                details.FirstParentId,
                details.SecondParentId,
                raw.WasMarketPurchased);
        }).ToImmutableArray();

        var worldEggs = world.Eggs.ToDictionary(egg => egg.Id, StringComparer.Ordinal);
        var eggs = collection.Eggs.Select(details =>
        {
            var raw = worldEggs[details.Id];
            var phenotype = LizardTraitRegistry.Default.Express(raw.Genome);
            var rows = phenotype.Traits.Select(trait => new LizardTraitDetailRow(
                trait.TraitId,
                trait.DisplayName,
                trait.Description,
                trait.Category,
                trait.DisplayValue,
                trait.NormalizedValue,
                trait.IsExpressed)).ToImmutableArray();
            return new BreedingEggCard(
                details.Id,
                $"孵化进度 {details.IncubationProgress:P0} · 剩余 {FormatDuration(details.IncubationRemaining)}",
                details.IncubationProgress,
                names.GetValueOrDefault(details.FirstParentId, ShortId(details.FirstParentId)),
                names.GetValueOrDefault(details.SecondParentId, ShortId(details.SecondParentId)),
                details.Generation,
                CreateEggPortrait(raw.Genome, details.IncubationProgress),
                CreatePortrait(raw.Genome));
        }).ToImmutableArray();
        return new BreedingManagementSnapshot(
            collection.Coins,
            collection.BuyPrice,
            collection.SellPrice,
            cards,
            eggs,
            _notice)
        {
            CanBuy = collection.Coins >= collection.BuyPrice && identityCapacityAvailable,
            BuyDisabledReason = !identityCapacityAvailable
                ? "存档的个体编号空间已用尽，无法再买入蜥蜴。"
                : collection.Coins < collection.BuyPrice
                    ? $"余额不足 {collection.BuyPrice} 枚金币"
                    : string.Empty,
            IdentityCapacityAvailable = identityCapacityAvailable,
            IsPersistent = !_store.IsEphemeral,
            PersistenceModeLabel = _store.IsEphemeral
                ? "临时家园 · 退出后不保存"
                : "家园进度已启用自动保存",
            RunningDesktopLizardId = _runningDesktopLizardId,
            NextDesktopLizardId = _store.IsEphemeral ? null : _nextDesktopLizardId
        };
    }

    private static BreedingTraitReadout ToReadout(LizardTraitDetailRow trait) => new(
        trait.TraitId,
        GroupName(trait.Category),
        trait.Name,
        trait.Description,
        trait.DisplayValue,
        trait.NormalizedValue,
        IconFor(trait.Category, trait.TraitId),
        trait.IsExpressed);

    internal static LizardPortraitModel CreatePortrait(LizardGenome genome)
    {
        ArgumentNullException.ThrowIfNull(genome);
        var phenotype = LizardTraitRegistry.Default.Express(genome);
        var visual = BreedablePhenotypeCompiler.CompileVisual(phenotype);
        return CreatePortrait(visual, phenotype);
    }

    internal static EggPortraitModel CreateEggPortrait(
        LizardGenome genome,
        double progress)
    {
        ArgumentNullException.ThrowIfNull(genome);
        var phenotype = LizardTraitRegistry.Default.Express(genome);
        return new EggPortraitModel(
            BreedablePhenotypeCompiler.CompileEggAppearance(phenotype),
            double.IsFinite(progress) ? Math.Clamp(progress, 0d, 1d) : 0d);
    }

    private static LizardPortraitModel CreatePortrait(
        BreedableVisualPhenotype visual,
        LizardPhenotype phenotype)
    {
        double Value(string id, double fallback, double minimum, double maximum)
        {
            if (!phenotype.TryGetTrait(id, out var trait) ||
                !trait.IsExpressed ||
                !double.IsFinite(trait.Value))
            {
                return Math.Clamp(fallback, minimum, maximum);
            }
            return Math.Clamp(trait.Value, minimum, maximum);
        }
        bool Has(string id) =>
            phenotype.TryGetTrait(id, out var trait) &&
            trait.IsExpressed &&
            double.IsFinite(trait.Value) &&
            trait.Value >= 0.5d;

        return new LizardPortraitModel(
            visual,
            Has("head.horns.present"),
            Value("head.horns.length", 0d, 0d, 1.8d),
            Value("head.horns.curvature", 0d, 0d, 1d),
            Has("head.crest.present"),
            Value("head.crest.height", 0d, 0d, 1.5d),
            Has("appendage.side-fins.present"),
            (int)Math.Round(
                Value("appendage.side-fins.pairs", 1d, 1d, 4d),
                MidpointRounding.AwayFromZero),
            Value("appendage.side-fins.size", 0d, 0d, 1.6d),
            Has("appendage.gill-tuft.present"),
            Value("appendage.gill-tuft.length", 0d, 0d, 1.7d));
    }

    private static string GroupName(TraitCategory category) => category switch
    {
        TraitCategory.Pigmentation or TraitCategory.Pattern => "颜色与花纹",
        TraitCategory.Body or TraitCategory.Head or TraitCategory.Skin => "体型与头部",
        TraitCategory.Limbs => "腿与足部",
        TraitCategory.Appendages => "鳍、颈饰与触须",
        TraitCategory.Tail => "尾部结构",
        TraitCategory.Locomotion => "速度与动作",
        TraitCategory.Temperament => "性格",
        TraitCategory.PointerResponse => "鼠标反应",
        TraitCategory.BehaviorTransitions => "状态转移倾向",
        TraitCategory.Lifecycle => "成长与变异",
        _ => "其他基因"
    };

    private static BreedingIcon IconFor(TraitCategory category, string id)
    {
        if (id.Contains("whisker", StringComparison.Ordinal)) return BreedingIcon.Whiskers;
        if (id.Contains("fin", StringComparison.Ordinal) || id.Contains("frill", StringComparison.Ordinal)) return BreedingIcon.Fins;
        if (id.StartsWith("tail.", StringComparison.Ordinal)) return BreedingIcon.Tail;
        return category switch
        {
            TraitCategory.Pigmentation or TraitCategory.Pattern or TraitCategory.Skin => BreedingIcon.Color,
            TraitCategory.Limbs => BreedingIcon.Limbs,
            TraitCategory.Appendages => BreedingIcon.Fins,
            TraitCategory.Tail => BreedingIcon.Tail,
            TraitCategory.Locomotion => BreedingIcon.Speed,
            TraitCategory.Temperament or
            TraitCategory.PointerResponse or
            TraitCategory.BehaviorTransitions => BreedingIcon.Temperament,
            _ => BreedingIcon.Genetics
        };
    }

    private static string FormatDuration(TimeSpan value)
    {
        value = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        if (value.TotalDays >= 1d) return $"{Math.Floor(value.TotalDays):0} 天 {value.Hours} 小时";
        if (value.TotalHours >= 1d) return $"{Math.Floor(value.TotalHours):0} 小时 {value.Minutes} 分";
        if (value.TotalMinutes >= 1d) return $"{Math.Floor(value.TotalMinutes):0} 分 {value.Seconds} 秒";
        return $"{Math.Max(0, value.Seconds)} 秒";
    }

    private static string ShortId(string id) => id.Length <= 8 ? id : id[..8];

    public void Dispose()
    {
        if (_disposed) return;
        _gate.Wait();
        try
        {
            if (_disposed) return;
            try
            {
                var world = _service.CaptureSnapshot();
                NormalizeNextDesktopLizard(world);
                _store.Save(
                    world,
                    DateTimeOffset.UtcNow,
                    _nextDesktopLizardId);
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"[InfiniteLizards] final breeding save failed: {exception}");
            }
            _disposed = true;
            _store.Dispose();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
