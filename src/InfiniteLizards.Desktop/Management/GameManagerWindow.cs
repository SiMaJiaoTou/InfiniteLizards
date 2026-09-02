using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace InfiniteLizards.Desktop.Management;

internal enum GameManagerPage
{
    Collection,
    Breeding,
    Hatchery,
    Market
}

internal enum CuteButtonTone
{
    Neutral,
    Mint,
    Lavender,
    Peach,
    Danger
}

/// <summary>
/// Focusable game UI kept completely separate from the transparent desktop
/// surface. It is intentionally non-modal, non-topmost and has no PetWindow
/// owner, preserving the native overlay's click-through ownership contract.
/// </summary>
internal sealed class GameManagerWindow : Window, IDisposable
{
    private static readonly IBrush WindowBrush = CuteGameUiTheme.Canvas;
    private static readonly IBrush PanelBrush = CuteGameUiTheme.Surface;
    private static readonly IBrush CardBrush = CuteGameUiTheme.SurfaceRaised;
    private static readonly IBrush SoftGoldBrush = CuteGameUiTheme.HoneyDeep;
    private static readonly IBrush JadeBrush = CuteGameUiTheme.MintDeep;
    private static readonly IBrush TextBrush = CuteGameUiTheme.Ink;
    private static readonly IBrush MutedTextBrush = CuteGameUiTheme.Muted;
    private static readonly IBrush DangerBrush = CuteGameUiTheme.DangerDeep;
    private static readonly FontFamily GameFont = CuteGameUiTheme.Font;
    private static readonly Lazy<Bitmap> HeaderArtwork = new(LoadHeaderArtwork);

    private readonly IBreedingManagementSource _source;
    private readonly ContentControl _pageHost = new();
    private readonly Border _detailHost = new();
    private readonly TextBlock _coinsText = Label("0", 21d, FontWeight.Bold, SoftGoldBrush);
    private readonly TextBlock _populationText = Label(string.Empty, 13d, FontWeight.Medium, MutedTextBrush);
    private readonly TextBlock _persistenceText = Label(string.Empty, 11d, FontWeight.Bold, DangerBrush);
    private readonly Border _persistencePill = new();
    private readonly TextBlock _noticeText = Label(string.Empty, 12d, FontWeight.Medium, MutedTextBrush);
    private readonly Dictionary<GameManagerPage, Button> _navigationButtons = [];
    private readonly Dictionary<string, List<TextBlock>> _ageLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<TextBlock>> _cooldownLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<TextBlock>> _detailIdentityLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<TextBlock>> _eggProgressLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ProgressBar>> _eggProgressBars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<EggPortraitView>> _eggPortraits = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _clockTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource _lifetime = new();
    private TimeSpan _lastClockSample;
    private GameManagerPage _page = GameManagerPage.Collection;
    private string? _selectedLizardId;
    private string? _firstParentId;
    private string? _secondParentId;
    private string? _pendingSellId;
    private bool _showingLegacyMascot;
    private bool _actionInFlight;
    private bool _tickInFlight;
    private bool _allowClose;
    private bool _disposed;

    internal GameManagerWindow(IBreedingManagementSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Title = "Infinite Lizards · 蜥蜴家园";
        Width = 1120d;
        Height = 720d;
        MinWidth = 980d;
        MinHeight = 640d;
        CanResize = true;
        CanMinimize = true;
        CanMaximize = true;
        SystemDecorations = SystemDecorations.Full;
        Background = WindowBrush;
        Topmost = false;
        ShowActivated = true;
        ShowInTaskbar = true;
        Focusable = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = BuildShell();

        _source.Changed += OnSourceChanged;
        Closing += OnClosing;
        Closed += OnClosed;

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1d)
        };
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();
        _lastClockSample = _clock.Elapsed;
        RefreshFromSource();
    }

    internal void ShowHome()
    {
        SelectPage(GameManagerPage.Collection);
        ShowAndActivate();
    }

    internal void ShowDetails(string? lizardId = null)
    {
        var snapshot = _source.Current;
        var candidate = !string.IsNullOrWhiteSpace(lizardId)
            ? snapshot.Lizards.FirstOrDefault(lizard => lizard.Id == lizardId)
            : null;
        candidate ??= snapshot.Lizards.FirstOrDefault();
        _showingLegacyMascot = false;
        _selectedLizardId = candidate?.Id;
        // The legacy on-screen guide exists before the player buys their first
        // breeding individual. A click still opens a useful destination: the
        // market where the first mature founder can be acquired.
        SelectPage(candidate is null ? GameManagerPage.Market : GameManagerPage.Collection);
        ShowAndActivate();
    }

    internal void ShowDesktopPetDetails(string? runningLizardId)
    {
        var snapshot = _source.Current;
        var candidate = string.IsNullOrWhiteSpace(runningLizardId)
            ? null
            : snapshot.Lizards.FirstOrDefault(lizard => lizard.Id == runningLizardId);
        _showingLegacyMascot = candidate is null;
        _selectedLizardId = candidate?.Id;
        SelectPage(snapshot.Lizards.IsEmpty
            ? GameManagerPage.Market
            : GameManagerPage.Collection);
        ShowAndActivate();
    }

    internal void AllowPermanentClose() => _allowClose = true;

    private Control BuildShell()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("104,*"),
            Background = WindowBrush
        };

        // Oversized pastel bubbles sit behind the real controls. They give the
        // window a playful habitat feel without stealing hit testing or space.
        root.Children.Add(BackdropBubble(
            210d,
            CuteGameUiTheme.MintSoft,
            HorizontalAlignment.Right,
            VerticalAlignment.Top,
            new Thickness(0d, -84d, -58d, 0d)));
        root.Children.Add(BackdropBubble(
            156d,
            CuteGameUiTheme.PeachSoft,
            HorizontalAlignment.Left,
            VerticalAlignment.Bottom,
            new Thickness(-72d, 0d, 0d, -64d)));
        root.Children.Add(BuildHeader());

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("148,*"),
            Margin = new Thickness(16d, 0d, 16d, 16d)
        };
        Grid.SetRow(content, 1);
        var nav = BuildNavigation();
        Grid.SetColumn(nav, 0);
        content.Children.Add(nav);

        var pageSurface = new Border
        {
            Background = PanelBrush,
            BorderBrush = CuteGameUiTheme.Line,
            BorderThickness = new Thickness(1d),
            CornerRadius = new CornerRadius(24d),
            Padding = new Thickness(18d),
            Margin = new Thickness(14d, 0d),
            ClipToBounds = true,
            Child = _pageHost
        };
        Grid.SetColumn(pageSurface, 1);
        content.Children.Add(pageSurface);

        _detailHost.Background = PanelBrush;
        _detailHost.BorderBrush = CuteGameUiTheme.Lavender;
        _detailHost.BorderThickness = new Thickness(1d);
        _detailHost.CornerRadius = new CornerRadius(24d);
        _detailHost.ClipToBounds = true;
        _detailHost.Width = 344d;
        _detailHost.HorizontalAlignment = HorizontalAlignment.Right;
        _detailHost.Margin = new Thickness(14d, 0d);
        _detailHost.IsVisible = false;
        Grid.SetColumn(_detailHost, 1);
        content.Children.Add(_detailHost);
        root.Children.Add(content);
        return root;
    }

    private Control BuildHeader()
    {
        var header = new Border
        {
            Background = PanelBrush,
            BorderBrush = CuteGameUiTheme.Line,
            BorderThickness = new Thickness(1d),
            CornerRadius = new CornerRadius(24d),
            Padding = new Thickness(16d, 8d),
            Margin = new Thickness(16d, 12d, 16d, 10d)
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,220,Auto")
        };
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 11d,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleRow.Children.Add(IconTile(
            BreedingIcon.Home,
            48d,
            CuteGameUiTheme.MintSoft,
            CuteGameUiTheme.Mint));
        var titleText = new StackPanel { Spacing = 1d };
        titleText.Children.Add(Label("蜥蜴家园", 22d, FontWeight.Bold, TextBrush));
        var homeMeta = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8d
        };
        homeMeta.Children.Add(_populationText);
        homeMeta.Children.Add(PillSeparator());
        _persistencePill.CornerRadius = new CornerRadius(10d);
        _persistencePill.Padding = new Thickness(7d, 2d);
        _persistencePill.Child = _persistenceText;
        homeMeta.Children.Add(_persistencePill);
        titleText.Children.Add(homeMeta);
        titleRow.Children.Add(titleText);
        grid.Children.Add(titleRow);

        var artwork = new Image
        {
            Source = HeaderArtwork.Value,
            Width = 214d,
            Height = 62d,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        Grid.SetColumn(artwork, 1);
        grid.Children.Add(artwork);

        var economy = new Border
        {
            Background = CuteGameUiTheme.HoneySoft,
            BorderBrush = CuteGameUiTheme.Honey,
            BorderThickness = new Thickness(1d),
            CornerRadius = new CornerRadius(20d),
            Padding = new Thickness(14d, 7d)
        };
        Grid.SetColumn(economy, 2);
        var moneyRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9d,
            VerticalAlignment = VerticalAlignment.Center
        };
        moneyRow.Children.Add(AtlasIcon(BreedingIcon.Coin, 32d));
        var balance = new StackPanel { Spacing = 0d };
        balance.Children.Add(Label("家园余额", 10d, FontWeight.Bold, CuteGameUiTheme.HoneyDeep));
        balance.Children.Add(_coinsText);
        moneyRow.Children.Add(balance);
        economy.Child = moneyRow;
        grid.Children.Add(economy);
        header.Child = grid;
        return header;
    }

    private Control BuildNavigation()
    {
        var border = new Border
        {
            Background = PanelBrush,
            BorderBrush = CuteGameUiTheme.Line,
            BorderThickness = new Thickness(1d),
            CornerRadius = new CornerRadius(24d),
            Padding = new Thickness(10d)
        };
        var stack = new StackPanel { Spacing = 8d };
        var littleTitle = Label("我的小屋", 11d, FontWeight.Bold, CuteGameUiTheme.InkSoft);
        littleTitle.Margin = new Thickness(8d, 3d, 8d, 2d);
        stack.Children.Add(littleTitle);
        AddNav(stack, GameManagerPage.Collection, BreedingIcon.Collection, "收藏");
        AddNav(stack, GameManagerPage.Breeding, BreedingIcon.Breeding, "繁育场");
        AddNav(stack, GameManagerPage.Hatchery, BreedingIcon.Egg, "孵化室");
        AddNav(stack, GameManagerPage.Market, BreedingIcon.MarketStall, "市场");
        stack.Children.Add(new Border
        {
            Height = 1d,
            Background = CuteGameUiTheme.Line,
            Margin = new Thickness(7d, 7d)
        });
        _noticeText.TextWrapping = TextWrapping.Wrap;
        _noticeText.LineHeight = 17d;
        var noticeCard = new Border
        {
            Background = CuteGameUiTheme.PeachSoft,
            BorderBrush = CuteGameUiTheme.Peach,
            BorderThickness = new Thickness(1d),
            CornerRadius = new CornerRadius(14d),
            Padding = new Thickness(9d),
            Child = _noticeText
        };
        stack.Children.Add(noticeCard);
        border.Child = stack;
        return border;
    }

    private void AddNav(
        Panel panel,
        GameManagerPage page,
        BreedingIcon icon,
        string label)
    {
        var button = IconButton(
            icon,
            label,
            30d,
            CuteButtonTone.Neutral,
            animateHover: false);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Click += (_, _) => SelectPage(page);
        _navigationButtons.Add(page, button);
        panel.Children.Add(button);
    }

    private void SelectPage(GameManagerPage page)
    {
        _page = page;
        _pendingSellId = null;
        RefreshFromSource();
    }

    private void RefreshFromSource()
    {
        if (_disposed)
        {
            return;
        }
        var snapshot = _source.Current;
        var ids = snapshot.Lizards.Select(lizard => lizard.Id).ToHashSet(StringComparer.Ordinal);
        if (_firstParentId is not null && !ids.Contains(_firstParentId)) _firstParentId = null;
        if (_secondParentId is not null && !ids.Contains(_secondParentId)) _secondParentId = null;
        if (_selectedLizardId is not null && !ids.Contains(_selectedLizardId)) _selectedLizardId = null;
        if (_pendingSellId is not null && !ids.Contains(_pendingSellId)) _pendingSellId = null;

        _coinsText.Text = $"{snapshot.Coins} 枚";
        _populationText.Text = $"{snapshot.Lizards.Length} 只蜥蜴 · {snapshot.Eggs.Length} 枚蛋";
        _persistenceText.Text = snapshot.PersistenceModeLabel;
        _persistenceText.Foreground = snapshot.IsPersistent ? JadeBrush : DangerBrush;
        _persistencePill.Background = snapshot.IsPersistent
            ? CuteGameUiTheme.MintSoft
            : CuteGameUiTheme.DangerSoft;
        _persistencePill.BorderBrush = snapshot.IsPersistent
            ? CuteGameUiTheme.Mint
            : CuteGameUiTheme.Danger;
        _persistencePill.BorderThickness = new Thickness(1d);
        var pendingSale = Find(snapshot, _pendingSellId);
        var notice = pendingSale is not null
            ? $"再次点击确认出售 {pendingSale.Name}。出售后无法撤销。"
            : string.IsNullOrWhiteSpace(snapshot.Notice)
                ? "点击桌面蜥蜴可直接查看详情。"
                : snapshot.Notice;
        _noticeText.Text = snapshot.IsPersistent
            ? notice
            : $"⚠ 临时家园：退出后不会保存。\n{notice}";
        foreach (var pair in _navigationButtons)
        {
            pair.Value.Background = pair.Key == _page
                ? CuteGameUiTheme.MintSoft
                : Brushes.Transparent;
            pair.Value.BorderBrush = pair.Key == _page
                ? CuteGameUiTheme.Mint
                : Brushes.Transparent;
        }

        ClearTimedControlRegistrations();
        _pageHost.Content = _page switch
        {
            GameManagerPage.Collection => BuildCollectionPage(snapshot),
            GameManagerPage.Breeding => BuildBreedingPage(snapshot),
            GameManagerPage.Hatchery => BuildHatcheryPage(snapshot),
            GameManagerPage.Market => BuildMarketPage(snapshot),
            _ => BuildCollectionPage(snapshot)
        };
        RefreshDetail(snapshot);
    }

    private Control BuildCollectionPage(BreedingManagementSnapshot snapshot)
    {
        var stack = PageStack(
            "蜥蜴收藏",
            "每个个体都有独立基因、可见外形和行为倾向。点击卡片查看完整参数。",
            BreedingIcon.Collection);
        if (snapshot.Lizards.IsEmpty)
        {
            stack.Children.Add(EmptyState(
                BreedingIcon.Collection,
                "家园里还没有蜥蜴",
                $"前往市场，用 {snapshot.BuyPrice} 枚金币购买一只成熟创始蜥蜴。"));
        }
        else
        {
            var cards = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemWidth = 228d,
                ItemHeight = 334d
            };
            foreach (var lizard in snapshot.Lizards)
            {
                cards.Children.Add(BuildLizardCard(lizard));
            }
            stack.Children.Add(cards);
        }
        return Scroll(stack);
    }

    private Control BuildLizardCard(BreedingLizardCard lizard)
    {
        var card = new Border
        {
            Width = 216d,
            Height = 320d,
            Margin = new Thickness(5d),
            Background = CardBrush,
            BorderBrush = _selectedLizardId == lizard.Id
                ? CuteGameUiTheme.LavenderStrong
                : CuteGameUiTheme.Line,
            BorderThickness = new Thickness(_selectedLizardId == lizard.Id ? 2d : 1d),
            CornerRadius = new CornerRadius(20d),
            Padding = new Thickness(10d)
        };
        var stack = new StackPanel { Spacing = 5d };
        var portrait = new LizardPortraitView
        {
            Model = lizard.Portrait,
            Width = 176d,
            Height = 176d,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(portrait);
        stack.Children.Add(Label(lizard.Name, 17d, FontWeight.Bold, TextBrush));
        stack.Children.Add(StatusPill(
            $"{lizard.StageLabel} · 第 {lizard.Generation} 代{DesktopBadge(lizard)}",
            lizard.CanBreed ? CuteGameUiTheme.MintSoft : CuteGameUiTheme.LavenderSoft,
            lizard.CanBreed ? CuteGameUiTheme.MintDeep : CuteGameUiTheme.LavenderStrong));
        var ageLabel = Label(lizard.AgeLabel, 11d, FontWeight.Normal, MutedTextBrush);
        Track(_ageLabels, lizard.Id, ageLabel);
        stack.Children.Add(ageLabel);
        var detailButton = IconButton(
            BreedingIcon.Details,
            "查看详情",
            23d,
            CuteButtonTone.Lavender);
        detailButton.Click += (_, _) =>
        {
            _showingLegacyMascot = false;
            _selectedLizardId = lizard.Id;
            RefreshFromSource();
        };
        stack.Children.Add(detailButton);
        card.Child = stack;
        return card;
    }

    private Control BuildBreedingPage(BreedingManagementSnapshot snapshot)
    {
        var stack = PageStack(
            "繁育场",
            "将两只成熟且冷却完成的蜥蜴放入同一个繁育巢，后代会继承双亲基因并可能突变。",
            BreedingIcon.Breeding);
        var slots = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,72,*"),
            Margin = new Thickness(0d, 6d, 0d, 12d)
        };
        var first = Find(snapshot, _firstParentId);
        var second = Find(snapshot, _secondParentId);
        var firstSlot = BuildParentSlot("亲本 A", first, true);
        var secondSlot = BuildParentSlot("亲本 B", second, false);
        Grid.SetColumn(firstSlot, 0);
        Grid.SetColumn(secondSlot, 2);
        slots.Children.Add(firstSlot);
        slots.Children.Add(secondSlot);
        var heart = AtlasIcon(BreedingIcon.Breeding, 58d);
        heart.HorizontalAlignment = HorizontalAlignment.Center;
        heart.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(heart, 1);
        slots.Children.Add(heart);
        stack.Children.Add(slots);

        var availability = _source.GetPairAvailability(_firstParentId, _secondParentId);
        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12d,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var breed = IconButton(
            BreedingIcon.Breeding,
            "开始交配并产下一枚蛋",
            30d,
            CuteButtonTone.Mint);
        breed.IsEnabled = availability.IsAllowed && !_actionInFlight;
        ApplyAvailabilityVisual(breed);
        ToolTip.SetTip(breed, availability.IsAllowed ? "双亲会进入繁育冷却" : availability.Reason);
        breed.Click += async (_, _) =>
        {
            if (_firstParentId is null || _secondParentId is null) return;
            await RunActionAsync(token => _source.BreedAsync(_firstParentId, _secondParentId, token));
        };
        actionRow.Children.Add(breed);
        stack.Children.Add(actionRow);
        if (!availability.IsAllowed && !string.IsNullOrWhiteSpace(availability.Reason))
        {
            var reason = Label(availability.Reason, 12d, FontWeight.Medium, MutedTextBrush);
            reason.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(reason);
        }

        stack.Children.Add(SectionTitle("选择成熟亲本", BreedingIcon.Genetics));
        var candidates = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var lizard in snapshot.Lizards)
        {
            var candidate = new Border
            {
                Width = 246d,
                Margin = new Thickness(4d),
                Padding = new Thickness(10d),
                Background = CardBrush,
                BorderBrush = CuteGameUiTheme.Line,
                BorderThickness = new Thickness(1d),
                CornerRadius = new CornerRadius(18d)
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("52,*,Auto") };
            var mini = new LizardPortraitView { Width = 48d, Height = 48d, Model = lizard.Portrait };
            row.Children.Add(mini);
            var identity = new StackPanel { Spacing = 2d, Margin = new Thickness(7d, 0d) };
            identity.Children.Add(Label(lizard.Name, 13d, FontWeight.Bold, TextBrush));
            var cooldownLabel = Label(
                lizard.CanBreed ? "可繁育" : lizard.CooldownLabel,
                10d,
                FontWeight.Medium,
                lizard.CanBreed ? JadeBrush : MutedTextBrush);
            Track(_cooldownLabels, lizard.Id, cooldownLabel);
            identity.Children.Add(cooldownLabel);
            Grid.SetColumn(identity, 1);
            row.Children.Add(identity);
            var choices = new StackPanel { Spacing = 4d };
            var toA = SmallButton(BreedingIcon.Breeding, "放入 A", CuteButtonTone.Lavender);
            var toB = SmallButton(BreedingIcon.Breeding, "放入 B", CuteButtonTone.Peach);
            toA.IsEnabled = lizard.CanBreed && !_actionInFlight;
            toB.IsEnabled = lizard.CanBreed && !_actionInFlight;
            ApplyAvailabilityVisual(toA);
            ApplyAvailabilityVisual(toB);
            toA.Click += (_, _) => { _firstParentId = lizard.Id; RefreshFromSource(); };
            toB.Click += (_, _) => { _secondParentId = lizard.Id; RefreshFromSource(); };
            choices.Children.Add(toA);
            choices.Children.Add(toB);
            Grid.SetColumn(choices, 2);
            row.Children.Add(choices);
            candidate.Child = row;
            candidates.Children.Add(candidate);
        }
        stack.Children.Add(candidates);
        return Scroll(stack);
    }

    private Control BuildParentSlot(
        string label,
        BreedingLizardCard? lizard,
        bool first)
    {
        var border = new Border
        {
            MinHeight = 194d,
            Background = lizard is null
                ? CuteGameUiTheme.SurfaceMuted
                : CardBrush,
            BorderBrush = lizard is null
                ? CuteGameUiTheme.LineStrong
                : CuteGameUiTheme.Lavender,
            BorderThickness = new Thickness(lizard is null ? 1d : 2d),
            CornerRadius = new CornerRadius(22d),
            Padding = new Thickness(13d)
        };
        var stack = new StackPanel
        {
            Spacing = 7d,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(Label(label, 12d, FontWeight.Bold, SoftGoldBrush));
        if (lizard is null)
        {
            stack.Children.Add(AtlasIcon(BreedingIcon.Genetics, 76d));
            stack.Children.Add(Label("等待放入繁育巢", 13d, FontWeight.Medium, MutedTextBrush));
        }
        else
        {
            stack.Children.Add(new LizardPortraitView { Model = lizard.Portrait, Width = 104d, Height = 104d });
            stack.Children.Add(Label(lizard.Name, 16d, FontWeight.Bold, TextBrush));
            var remove = SmallButton(
                BreedingIcon.Remove,
                "移出繁育巢",
                CuteButtonTone.Neutral);
            remove.Click += (_, _) =>
            {
                if (first) _firstParentId = null;
                else _secondParentId = null;
                RefreshFromSource();
            };
            stack.Children.Add(remove);
        }
        border.Child = stack;
        return border;
    }

    private Control BuildHatcheryPage(BreedingManagementSnapshot snapshot)
    {
        var stack = PageStack(
            "孵化室",
            "蛋会按自身孵化速度成长；离线时间也会结算。破壳后幼体还需要一段时间才能成熟。",
            BreedingIcon.Egg);
        if (snapshot.Eggs.IsEmpty)
        {
            stack.Children.Add(EmptyState(
                BreedingIcon.Egg,
                "孵化室目前是空的",
                "把两只成熟蜥蜴放入繁育巢，就能获得一枚带有双亲基因的蛋。"));
        }
        else
        {
            foreach (var egg in snapshot.Eggs)
            {
                var card = new Border
                {
                    Margin = new Thickness(0d, 5d),
                    Padding = new Thickness(14d),
                    Background = CardBrush,
                    BorderBrush = CuteGameUiTheme.Lavender,
                    BorderThickness = new Thickness(1d),
                    CornerRadius = new CornerRadius(20d)
                };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("112,*,160") };
                var eggPortrait = new EggPortraitView
                {
                    Model = egg.Shell,
                    Width = 104d,
                    Height = 104d
                };
                Track(_eggPortraits, egg.Id, eggPortrait);
                row.Children.Add(eggPortrait);
                var information = new StackPanel { Spacing = 6d, Margin = new Thickness(12d, 4d) };
                information.Children.Add(Label(
                    $"第 {egg.Generation} 代基因蛋",
                    17d,
                    FontWeight.Bold,
                    TextBrush));
                information.Children.Add(Label(
                    $"亲本：{egg.FirstParentName} × {egg.SecondParentName}",
                    12d,
                    FontWeight.Medium,
                    MutedTextBrush));
                var progress = new ProgressBar
                {
                    Minimum = 0d,
                    Maximum = 1d,
                    Value = Math.Clamp(egg.Progress, 0d, 1d),
                    Height = 9d,
                    Foreground = JadeBrush,
                    Background = CuteGameUiTheme.MintSoft,
                    CornerRadius = new CornerRadius(6d)
                };
                Track(_eggProgressBars, egg.Id, progress);
                information.Children.Add(progress);
                var progressPill = StatusPill(
                    egg.ProgressLabel,
                    CuteGameUiTheme.LavenderSoft,
                    CuteGameUiTheme.LavenderStrong);
                if (progressPill.Child is TextBlock progressLabel)
                {
                    Track(_eggProgressLabels, egg.Id, progressLabel);
                }
                information.Children.Add(progressPill);
                Grid.SetColumn(information, 1);
                row.Children.Add(information);
                var preview = new StackPanel
                {
                    Spacing = 3d,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                preview.Children.Add(Label(
                    "遗传预览",
                    10d,
                    FontWeight.Bold,
                    MutedTextBrush));
                preview.Children.Add(new LizardPortraitView
                {
                    Model = egg.Preview,
                    Width = 104d,
                    Height = 104d
                });
                Grid.SetColumn(preview, 2);
                row.Children.Add(preview);
                card.Child = row;
                stack.Children.Add(card);
            }
        }
        return Scroll(stack);
    }

    private Control BuildMarketPage(BreedingManagementSnapshot snapshot)
    {
        var stack = PageStack(
            "蜥蜴市场",
            $"市场创始蜥蜴均已成熟，可立即参与繁育。买入 {snapshot.BuyPrice} 枚金币，卖出返还 {snapshot.SellPrice} 枚金币。",
            BreedingIcon.MarketStall);
        var buyCard = new Border
        {
            Padding = new Thickness(16d),
            Margin = new Thickness(0d, 4d, 0d, 15d),
            Background = CuteGameUiTheme.MintSoft,
            BorderBrush = CuteGameUiTheme.Mint,
            BorderThickness = new Thickness(1d),
            CornerRadius = new CornerRadius(22d)
        };
        var buyGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("84,*,Auto") };
        buyGrid.Children.Add(AtlasIcon(BreedingIcon.Buy, 76d));
        var offer = new StackPanel { Spacing = 3d, Margin = new Thickness(9d, 5d) };
        offer.Children.Add(Label("购买成熟随机蜥蜴", 17d, FontWeight.Bold, TextBrush));
        offer.Children.Add(Label(
            "随机生成完整双等位基因，外观、体型、附肢和性格都可能不同。",
            12d,
            FontWeight.Normal,
            MutedTextBrush));
        Grid.SetColumn(offer, 1);
        buyGrid.Children.Add(offer);
        var buy = IconButton(
            BreedingIcon.Buy,
            $"买入 · {snapshot.BuyPrice} 枚",
            28d,
            CuteButtonTone.Mint);
        buy.IsEnabled = snapshot.CanBuy && !_actionInFlight;
        ApplyAvailabilityVisual(buy);
        ToolTip.SetTip(
            buy,
            snapshot.CanBuy
                ? "购买一只成熟创始蜥蜴"
                : snapshot.BuyDisabledReason);
        buy.Click += async (_, _) => await RunActionAsync(_source.BuyAsync);
        Grid.SetColumn(buy, 2);
        buyGrid.Children.Add(buy);
        buyCard.Child = buyGrid;
        stack.Children.Add(buyCard);

        stack.Children.Add(SectionTitle(
            $"出售已有蜥蜴 · 每只 {snapshot.SellPrice} 枚",
            BreedingIcon.Sell));
        if (snapshot.Lizards.IsEmpty)
        {
            stack.Children.Add(Label("当前没有可出售的蜥蜴。", 13d, FontWeight.Medium, MutedTextBrush));
        }
        foreach (var lizard in snapshot.Lizards)
        {
            var card = new Border
            {
                Margin = new Thickness(0d, 4d),
                Padding = new Thickness(11d),
                Background = CardBrush,
                BorderBrush = _pendingSellId == lizard.Id
                    ? DangerBrush
                    : CuteGameUiTheme.Line,
                BorderThickness = new Thickness(1d),
                CornerRadius = new CornerRadius(18d)
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("58,*,Auto") };
            row.Children.Add(new LizardPortraitView { Model = lizard.Portrait, Width = 54d, Height = 54d });
            var identity = new StackPanel { Spacing = 2d, Margin = new Thickness(9d, 3d) };
            identity.Children.Add(Label(lizard.Name, 14d, FontWeight.Bold, TextBrush));
            identity.Children.Add(Label(
                $"{lizard.StageLabel} · 第 {lizard.Generation} 代{DesktopBadge(lizard)}",
                11d,
                FontWeight.Medium,
                MutedTextBrush));
            Grid.SetColumn(identity, 1);
            row.Children.Add(identity);
            var isConfirming = _pendingSellId == lizard.Id;
            var sell = IconButton(
                BreedingIcon.Sell,
                isConfirming
                    ? $"确认出售 · +{snapshot.SellPrice}"
                    : $"卖出 · +{snapshot.SellPrice}",
                24d,
                isConfirming
                    ? CuteButtonTone.Danger
                    : CuteButtonTone.Peach);
            sell.IsEnabled = lizard.CanSell && !_actionInFlight;
            ApplyAvailabilityVisual(sell);
            ToolTip.SetTip(sell, lizard.CanSell ? "再次确认后永久出售" : lizard.SellDisabledReason);
            sell.Click += async (_, _) =>
            {
                if (_pendingSellId != lizard.Id)
                {
                    _pendingSellId = lizard.Id;
                    RefreshFromSource();
                    return;
                }
                await RunActionAsync(token => _source.SellAsync(lizard.Id, token));
                _pendingSellId = null;
            };
            Grid.SetColumn(sell, 2);
            row.Children.Add(sell);
            card.Child = row;
            stack.Children.Add(card);
        }
        return Scroll(stack);
    }

    private void RefreshDetail(BreedingManagementSnapshot snapshot)
    {
        if (_showingLegacyMascot)
        {
            RefreshLegacyMascotDetail(snapshot);
            return;
        }

        var lizard = Find(snapshot, _selectedLizardId);
        if (lizard is null)
        {
            _detailHost.IsVisible = false;
            _detailHost.Child = null;
            return;
        }

        _detailHost.IsVisible = true;
        var stack = new StackPanel { Spacing = 10d, Margin = new Thickness(16d) };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var heading = new StackPanel { Spacing = 1d };
        heading.Children.Add(Label(lizard.Name, 22d, FontWeight.Bold, TextBrush));
        var detailIdentityLabel = Label(
            $"{lizard.StageLabel} · 第 {lizard.Generation} 代 · {lizard.AgeLabel}",
            11d,
            FontWeight.Medium,
            JadeBrush);
        Track(_detailIdentityLabels, lizard.Id, detailIdentityLabel);
        heading.Children.Add(detailIdentityLabel);
        header.Children.Add(heading);
        var close = SmallButton(BreedingIcon.Close, "关闭", CuteButtonTone.Lavender);
        close.Click += (_, _) => { _selectedLizardId = null; RefreshFromSource(); };
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        stack.Children.Add(header);
        stack.Children.Add(new LizardPortraitView
        {
            Model = lizard.Portrait,
            Width = 284d,
            Height = 284d,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var lineage = new Border
        {
            Background = CuteGameUiTheme.PeachSoft,
            BorderBrush = CuteGameUiTheme.Peach,
            BorderThickness = new Thickness(1d),
            CornerRadius = new CornerRadius(16d),
            Padding = new Thickness(10d)
        };
        lineage.Child = Label(
            lizard.Generation == 0
                ? lizard.WasMarketPurchased ? "谱系：市场创始个体" : "谱系：创始个体"
                : $"谱系：{ShortId(lizard.FirstParentId)} × {ShortId(lizard.SecondParentId)}",
            11d,
            FontWeight.Medium,
            MutedTextBrush);
        stack.Children.Add(lineage);
        stack.Children.Add(BuildDesktopIdentityPanel(lizard));

        foreach (var group in lizard.Traits.GroupBy(trait => trait.Group))
        {
            stack.Children.Add(BuildTraitGroup(group.Key, group));
        }
        _detailHost.Child = Scroll(stack);
    }

    private Control BuildDesktopIdentityPanel(BreedingLizardCard lizard)
    {
        var panel = new Border
        {
            Background = CardBrush,
            BorderBrush = lizard.IsRunningDesktopPet || lizard.IsNextDesktopPet
                ? CuteGameUiTheme.Mint
                : CuteGameUiTheme.Line,
            BorderThickness = new Thickness(1d),
            CornerRadius = new CornerRadius(16d),
            Padding = new Thickness(10d)
        };
        var stack = new StackPanel { Spacing = 7d };
        stack.Children.Add(SectionTitle("桌面身份", BreedingIcon.Desktop, 24d));
        var status = lizard switch
        {
            { IsRunningDesktopPet: true, IsNextDesktopPet: true } =>
                "当前桌宠 · 下次启动仍使用这只",
            { IsRunningDesktopPet: true } =>
                "当前桌宠 · 已预约其他个体在下次启动接替",
            { IsNextDesktopPet: true } =>
                "下次桌宠 · 重启应用后生效",
            _ => "收藏个体 · 尚未设为桌宠"
        };
        var statusLabel = Label(
            status,
            11d,
            FontWeight.Medium,
            lizard.IsRunningDesktopPet || lizard.IsNextDesktopPet
                ? JadeBrush
                : MutedTextBrush);
        statusLabel.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(statusLabel);

        var setDesktop = IconButton(
            BreedingIcon.Desktop,
            lizard.IsNextDesktopPet
                ? "已设为下次桌宠"
                : "设为桌宠 · 下次启动生效",
            23d,
            CuteButtonTone.Mint);
        setDesktop.IsEnabled = !lizard.IsNextDesktopPet && !_actionInFlight;
        setDesktop.IsEnabled &= _source.Current.IsPersistent;
        ApplyAvailabilityVisual(setDesktop);
        ToolTip.SetTip(
            setDesktop,
            !_source.Current.IsPersistent
                ? "临时家园无法预约下次启动的桌宠"
                : lizard.IsNextDesktopPet
                ? "当前选择已写入存档"
                : "不会热换正在运行的桌宠；重启后使用这只蜥蜴");
        setDesktop.Click += async (_, _) => await RunActionAsync(
            token => _source.SetDesktopLizardAsync(lizard.Id, token));
        stack.Children.Add(setDesktop);
        panel.Child = stack;
        return panel;
    }

    private void RefreshLegacyMascotDetail(BreedingManagementSnapshot snapshot)
    {
        _detailHost.IsVisible = true;
        var stack = new StackPanel { Spacing = 10d, Margin = new Thickness(16d) };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var heading = new StackPanel { Spacing = 1d };
        heading.Children.Add(Label("桌面向导", 22d, FontWeight.Bold, TextBrush));
        heading.Children.Add(Label(
            "本次会话使用旧配置外观",
            11d,
            FontWeight.Medium,
            JadeBrush));
        header.Children.Add(heading);
        var close = SmallButton(BreedingIcon.Close, "关闭", CuteButtonTone.Lavender);
        close.Click += (_, _) =>
        {
            _showingLegacyMascot = false;
            RefreshFromSource();
        };
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        stack.Children.Add(header);
        stack.Children.Add(new LizardPortraitView
        {
            Model = LizardPortraitModel.Default,
            Width = 284d,
            Height = 284d,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var next = Find(snapshot, snapshot.NextDesktopLizardId);
        var copy = next is null
            ? "当前桌面蜥蜴是家园向导，不对应收藏中的基因个体。购买第一只蜥蜴后，它会自动预约为下次启动的桌宠。"
            : $"当前桌面蜥蜴是家园向导，不对应收藏中的基因个体。{next.Name} 已预约为下次启动的桌宠。";
        var explanation = new Border
        {
            Background = CuteGameUiTheme.PeachSoft,
            BorderBrush = CuteGameUiTheme.Peach,
            BorderThickness = new Thickness(1d),
            CornerRadius = new CornerRadius(16d),
            Padding = new Thickness(11d)
        };
        var label = Label(copy, 12d, FontWeight.Medium, MutedTextBrush);
        label.TextWrapping = TextWrapping.Wrap;
        explanation.Child = label;
        stack.Children.Add(explanation);
        _detailHost.Child = Scroll(stack);
    }

    private Control BuildTraitGroup(
        string group,
        IEnumerable<BreedingTraitReadout> traits)
    {
        var traitArray = traits.ToArray();
        var box = new Border
        {
            Background = CuteGameUiTheme.SurfaceMuted,
            BorderBrush = CuteGameUiTheme.Line,
            BorderThickness = new Thickness(1d),
            CornerRadius = new CornerRadius(16d),
            Padding = new Thickness(10d)
        };
        var stack = new StackPanel { Spacing = 7d };
        stack.Children.Add(SectionTitle(group, traitArray.FirstOrDefault()?.Icon ?? BreedingIcon.Genetics, 24d));
        foreach (var trait in traitArray)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("24,*,Auto")
            };
            ToolTip.SetTip(row, trait.Description);
            row.Children.Add(AtlasIcon(trait.Icon, 21d));
            var name = Label(
                trait.Name,
                11d,
                FontWeight.Medium,
                trait.IsExpressed ? TextBrush : MutedTextBrush);
            name.Margin = new Thickness(5d, 0d);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var value = Label(trait.Value, 11d, FontWeight.Bold, SoftGoldBrush);
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            stack.Children.Add(row);
            if (trait.NormalizedValue is >= 0d and <= 1d)
            {
                stack.Children.Add(new ProgressBar
                {
                    Minimum = 0d,
                    Maximum = 1d,
                    Value = trait.NormalizedValue,
                    Height = 4d,
                    Foreground = JadeBrush,
                    Background = CuteGameUiTheme.MintSoft,
                    CornerRadius = new CornerRadius(4d)
                });
            }
        }
        box.Child = stack;
        return box;
    }

    private void ClearTimedControlRegistrations()
    {
        _ageLabels.Clear();
        _cooldownLabels.Clear();
        _detailIdentityLabels.Clear();
        _eggProgressLabels.Clear();
        _eggProgressBars.Clear();
        _eggPortraits.Clear();
    }

    private void RefreshTimedControls(BreedingManagementSnapshot snapshot)
    {
        foreach (var lizard in snapshot.Lizards)
        {
            SetTrackedText(_ageLabels, lizard.Id, lizard.AgeLabel);
            SetTrackedText(
                _cooldownLabels,
                lizard.Id,
                lizard.CanBreed ? "可繁育" : lizard.CooldownLabel);
            SetTrackedText(
                _detailIdentityLabels,
                lizard.Id,
                $"{lizard.StageLabel} · 第 {lizard.Generation} 代 · {lizard.AgeLabel}");
        }

        foreach (var egg in snapshot.Eggs)
        {
            SetTrackedText(_eggProgressLabels, egg.Id, egg.ProgressLabel);
            if (_eggProgressBars.TryGetValue(egg.Id, out var progressBars))
            {
                foreach (var progressBar in progressBars)
                {
                    progressBar.Value = Math.Clamp(egg.Progress, 0d, 1d);
                }
            }
            if (_eggPortraits.TryGetValue(egg.Id, out var portraits))
            {
                foreach (var portrait in portraits)
                {
                    portrait.Model = egg.Shell;
                }
            }
        }
    }

    private static void SetTrackedText(
        Dictionary<string, List<TextBlock>> registry,
        string id,
        string text)
    {
        if (!registry.TryGetValue(id, out var labels))
        {
            return;
        }
        foreach (var label in labels)
        {
            label.Text = text;
        }
    }

    private static void Track<T>(
        Dictionary<string, List<T>> registry,
        string id,
        T value)
    {
        if (!registry.TryGetValue(id, out var values))
        {
            values = [];
            registry.Add(id, values);
        }
        values.Add(value);
    }

    private static bool RequiresStructuralRefresh(
        BreedingManagementSnapshot before,
        BreedingManagementSnapshot after)
    {
        if (before.Coins != after.Coins ||
            before.BuyPrice != after.BuyPrice ||
            before.SellPrice != after.SellPrice ||
            before.CanBuy != after.CanBuy ||
            before.IdentityCapacityAvailable != after.IdentityCapacityAvailable ||
            before.IsPersistent != after.IsPersistent ||
            !string.Equals(before.Notice, after.Notice, StringComparison.Ordinal) ||
            !string.Equals(before.RunningDesktopLizardId, after.RunningDesktopLizardId, StringComparison.Ordinal) ||
            !string.Equals(before.NextDesktopLizardId, after.NextDesktopLizardId, StringComparison.Ordinal) ||
            before.Lizards.Length != after.Lizards.Length ||
            before.Eggs.Length != after.Eggs.Length)
        {
            return true;
        }

        for (var index = 0; index < before.Lizards.Length; index++)
        {
            var previous = before.Lizards[index];
            var current = after.Lizards[index];
            if (!string.Equals(previous.Id, current.Id, StringComparison.Ordinal) ||
                !string.Equals(previous.Name, current.Name, StringComparison.Ordinal) ||
                !string.Equals(previous.StageLabel, current.StageLabel, StringComparison.Ordinal) ||
                previous.CanBreed != current.CanBreed ||
                previous.CanSell != current.CanSell ||
                previous.IsRunningDesktopPet != current.IsRunningDesktopPet ||
                previous.IsNextDesktopPet != current.IsNextDesktopPet)
            {
                return true;
            }
        }

        for (var index = 0; index < before.Eggs.Length; index++)
        {
            if (!string.Equals(
                    before.Eggs[index].Id,
                    after.Eggs[index].Id,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private async void OnClockTick(object? sender, EventArgs e)
    {
        if (_tickInFlight || _disposed)
        {
            return;
        }
        var now = _clock.Elapsed;
        var elapsed = now - _lastClockSample;
        _lastClockSample = now;
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        _tickInFlight = true;
        try
        {
            var before = _source.Current;
            await _source.AdvanceAsync(elapsed, _lifetime.Token);
            if (IsVisible)
            {
                var after = _source.Current;
                if (RequiresStructuralRefresh(before, after))
                {
                    RefreshFromSource();
                }
                else
                {
                    RefreshTimedControls(after);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[InfiniteLizards] breeding clock failed: {exception}");
            _noticeText.Text = "成长计时暂时无法更新，存档不会被覆盖。";
        }
        finally
        {
            _tickInFlight = false;
        }
    }

    private async Task RunActionAsync(
        Func<CancellationToken, ValueTask<BreedingManagementActionResult>> action)
    {
        if (_actionInFlight || _disposed)
        {
            return;
        }
        _actionInFlight = true;
        RefreshFromSource();
        try
        {
            var result = await action(_lifetime.Token);
            _noticeText.Text = result.Message;
            if (result.Succeeded)
            {
                _pendingSellId = null;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[InfiniteLizards] breeding action failed: {exception}");
            _noticeText.Text = "操作失败，存档未改变。";
        }
        finally
        {
            _actionInFlight = false;
            RefreshFromSource();
        }
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshFromSource();
        }
        else
        {
            Dispatcher.UIThread.Post(RefreshFromSource, DispatcherPriority.Background);
        }
    }

    private void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }
        e.Cancel = true;
        Hide();
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _clockTimer.Stop();
        _clockTimer.Tick -= OnClockTick;
        _source.Changed -= OnSourceChanged;
        Closing -= OnClosing;
        Closed -= OnClosed;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _source.Dispose();
    }

    private static StackPanel PageStack(
        string title,
        string description,
        BreedingIcon icon)
    {
        var stack = new StackPanel { Spacing = 10d };
        var header = new Border
        {
            Background = AccentSoftFor(icon),
            BorderBrush = AccentLineFor(icon),
            BorderThickness = new Thickness(1d),
            CornerRadius = new CornerRadius(20d),
            Padding = new Thickness(13d, 10d),
            Margin = new Thickness(0d, 0d, 0d, 3d)
        };
        var headerRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("54,*,Auto")
        };
        headerRow.Children.Add(IconTile(
            icon,
            46d,
            CuteGameUiTheme.SurfaceRaised,
            AccentLineFor(icon)));
        var text = new StackPanel
        {
            Spacing = 2d,
            Margin = new Thickness(8d, 0d)
        };
        text.Children.Add(Label(title, 22d, FontWeight.Bold, TextBrush));
        var subtitle = Label(description, 12d, FontWeight.Normal, MutedTextBrush);
        subtitle.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(subtitle);
        Grid.SetColumn(text, 1);
        headerRow.Children.Add(text);
        var sparkles = Label("✦  ·  ✦", 12d, FontWeight.Bold, AccentInkFor(icon));
        sparkles.Margin = new Thickness(8d);
        Grid.SetColumn(sparkles, 2);
        headerRow.Children.Add(sparkles);
        header.Child = headerRow;
        stack.Children.Add(header);
        return stack;
    }

    private static Control EmptyState(BreedingIcon icon, string title, string description)
    {
        var border = new Border
        {
            MinHeight = 300d,
            Background = CuteGameUiTheme.LavenderSoft,
            BorderBrush = CuteGameUiTheme.Lavender,
            BorderThickness = new Thickness(1d),
            CornerRadius = new CornerRadius(22d),
            Padding = new Thickness(24d)
        };
        var stack = new StackPanel
        {
            Spacing = 9d,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(AtlasIcon(icon, 112d));
        stack.Children.Add(Label(title, 18d, FontWeight.Bold, TextBrush));
        var copy = Label(description, 12d, FontWeight.Normal, MutedTextBrush);
        copy.MaxWidth = 420d;
        copy.TextWrapping = TextWrapping.Wrap;
        copy.TextAlignment = TextAlignment.Center;
        stack.Children.Add(copy);
        border.Child = stack;
        return border;
    }

    private static Control SectionTitle(string title, BreedingIcon icon, double iconSize = 28d)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7d,
            Margin = new Thickness(0d, 5d, 0d, 2d)
        };
        row.Children.Add(AtlasIcon(icon, iconSize));
        row.Children.Add(Label(title, 15d, FontWeight.Bold, TextBrush));
        return row;
    }

    private static Button IconButton(
        BreedingIcon icon,
        string text,
        double iconSize,
        CuteButtonTone tone = CuteButtonTone.Neutral,
        bool animateHover = true)
    {
        var (normal, hover, border, foreground) = ButtonPalette(tone);
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7d,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(AtlasIcon(icon, iconSize));
        content.Children.Add(Label(text, 12d, FontWeight.Bold, foreground));
        var button = new Button
        {
            Content = content,
            Background = normal,
            Foreground = foreground,
            BorderBrush = border,
            BorderThickness = new Thickness(1d),
            Padding = new Thickness(10d, 7d),
            CornerRadius = new CornerRadius(16d),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 36d
        };
        // Fluent draws interaction states inside its template presenter. Local
        // resource overrides keep hover/press in the same pastel palette on
        // both macOS and Windows instead of leaking the default blue/gray.
        var interactionBackground = tone == CuteButtonTone.Danger
            ? hover
            : normal;
        button.Resources["ButtonBackgroundPointerOver"] = interactionBackground;
        button.Resources["ButtonBackgroundPressed"] = interactionBackground;
        button.Resources["ButtonBorderBrushPointerOver"] = border;
        button.Resources["ButtonBorderBrushPressed"] = border;
        button.Resources["ButtonForegroundPointerOver"] = foreground;
        button.Resources["ButtonForegroundPressed"] = foreground;
        if (animateHover)
        {
            button.PointerEntered += (_, _) =>
            {
                if (button.IsEnabled) button.Background = hover;
            };
            button.PointerExited += (_, _) =>
            {
                if (button.IsEnabled) button.Background = normal;
            };
        }
        return button;
    }

    private static Button SmallButton(
        BreedingIcon icon,
        string text,
        CuteButtonTone tone = CuteButtonTone.Neutral)
    {
        var button = IconButton(icon, text, 18d, tone);
        button.Padding = new Thickness(7d, 4d);
        button.MinHeight = 34d;
        button.CornerRadius = new CornerRadius(14d);
        return button;
    }

    private static Border IconTile(
        BreedingIcon icon,
        double size,
        IBrush background,
        IBrush border) => new()
    {
        Width = size,
        Height = size,
        Background = background,
        BorderBrush = border,
        BorderThickness = new Thickness(1d),
        CornerRadius = new CornerRadius(Math.Max(12d, size * 0.36d)),
        Padding = new Thickness(Math.Max(3d, size * 0.08d)),
        Child = AtlasIcon(icon, size * 0.78d)
    };

    private static Border BackdropBubble(
        double size,
        IBrush color,
        HorizontalAlignment horizontal,
        VerticalAlignment vertical,
        Thickness margin) => new()
    {
        Width = size,
        Height = size,
        Background = color,
        CornerRadius = new CornerRadius(size * 0.5d),
        HorizontalAlignment = horizontal,
        VerticalAlignment = vertical,
        Margin = margin,
        IsHitTestVisible = false
    };

    private static Border PillSeparator() => new()
    {
        Width = 4d,
        Height = 4d,
        Background = CuteGameUiTheme.PeachStrong,
        CornerRadius = new CornerRadius(2d),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Border StatusPill(
        string text,
        IBrush background,
        IBrush foreground) => new()
    {
        Background = background,
        CornerRadius = new CornerRadius(10d),
        Padding = new Thickness(8d, 3d),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = Label(text, 10d, FontWeight.Bold, foreground)
    };

    private static IBrush AccentSoftFor(BreedingIcon icon) => icon switch
    {
        BreedingIcon.Breeding => CuteGameUiTheme.PeachSoft,
        BreedingIcon.Egg or BreedingIcon.Hatching => CuteGameUiTheme.LavenderSoft,
        BreedingIcon.Buy or BreedingIcon.Sell => CuteGameUiTheme.MintSoft,
        _ => CuteGameUiTheme.SkySoft
    };

    private static IBrush AccentLineFor(BreedingIcon icon) => icon switch
    {
        BreedingIcon.Breeding => CuteGameUiTheme.Peach,
        BreedingIcon.Egg or BreedingIcon.Hatching => CuteGameUiTheme.Lavender,
        BreedingIcon.Buy or BreedingIcon.Sell => CuteGameUiTheme.Mint,
        _ => CuteGameUiTheme.Lavender
    };

    private static IBrush AccentInkFor(BreedingIcon icon) => icon switch
    {
        BreedingIcon.Breeding => CuteGameUiTheme.PeachStrong,
        BreedingIcon.Egg or BreedingIcon.Hatching => CuteGameUiTheme.LavenderStrong,
        BreedingIcon.Buy or BreedingIcon.Sell => CuteGameUiTheme.MintDeep,
        _ => CuteGameUiTheme.LavenderStrong
    };

    private static (IBrush Normal, IBrush Hover, IBrush Border, IBrush Foreground)
        ButtonPalette(CuteButtonTone tone) => tone switch
        {
            CuteButtonTone.Mint => (
                CuteGameUiTheme.MintSoft,
                CuteGameUiTheme.Mint,
                CuteGameUiTheme.Mint,
                CuteGameUiTheme.MintDeep),
            CuteButtonTone.Lavender => (
                CuteGameUiTheme.LavenderSoft,
                CuteGameUiTheme.Lavender,
                CuteGameUiTheme.Lavender,
                CuteGameUiTheme.LavenderStrong),
            CuteButtonTone.Peach => (
                CuteGameUiTheme.PeachSoft,
                CuteGameUiTheme.Peach,
                CuteGameUiTheme.Peach,
                CuteGameUiTheme.PeachStrong),
            CuteButtonTone.Danger => (
                CuteGameUiTheme.Danger,
                CuteGameUiTheme.DangerDeep,
                CuteGameUiTheme.DangerDeep,
                Brushes.White),
            _ => (
                CuteGameUiTheme.SurfaceMuted,
                CuteGameUiTheme.LavenderSoft,
                CuteGameUiTheme.LineStrong,
                CuteGameUiTheme.InkSoft)
        };

    private static void ApplyAvailabilityVisual(Button button)
    {
        button.Opacity = button.IsEnabled ? 1d : 0.48d;
        if (!button.IsEnabled)
        {
            button.Background = CuteGameUiTheme.Disabled;
            button.BorderBrush = CuteGameUiTheme.Line;
        }
    }

    private static BreedingAtlasIcon AtlasIcon(BreedingIcon icon, double size) => new()
    {
        Icon = icon,
        Width = size,
        Height = size
    };

    private static TextBlock Label(
        string text,
        double size,
        FontWeight weight,
        IBrush foreground) => new()
    {
        Text = text,
        FontFamily = GameFont,
        FontSize = size,
        FontWeight = weight,
        Foreground = foreground,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static ScrollViewer Scroll(Control content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };

    private static BreedingLizardCard? Find(
        BreedingManagementSnapshot snapshot,
        string? id) => id is null
        ? null
        : snapshot.Lizards.FirstOrDefault(lizard => lizard.Id == id);

    private static string DesktopBadge(BreedingLizardCard lizard) => lizard switch
    {
        { IsRunningDesktopPet: true } => " · 当前桌宠",
        { IsNextDesktopPet: true } => " · 下次桌宠",
        _ => string.Empty
    };

    private static string ShortId(string? id) => string.IsNullOrWhiteSpace(id)
        ? "未知"
        : id.Length <= 8 ? id : id[..8];

    private static Bitmap LoadHeaderArtwork()
    {
        var uri = new Uri(
            "avares://InfiniteLizards.Desktop/Assets/UI/cute-terrarium-header.png");
        using var stream = AssetLoader.Open(uri);
        return new Bitmap(stream);
    }
}
