using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace UI;

/// <summary>
/// Quick-access side menu (plan.md §9.1): slides in from the right over whatever is
/// currently on screen. Toggled by Tab in this test build. Volume/backlight sliders
/// and the overlay toggle are visual-only here (IsHitTestVisible="False" on the
/// controls themselves) since there's no real audio/DDC-CI/overlay backend wired to
/// this test build yet — they exist to prove the panel layout, not to control
/// anything real.
/// </summary>
public partial class GuideMenu : UserControl
{
    public static readonly DependencyProperty BatteryIconProperty =
        DependencyProperty.Register(nameof(BatteryIcon), typeof(BatteryIconViewModel), typeof(GuideMenu), new PropertyMetadata(null));

    public BatteryIconViewModel? BatteryIcon
    {
        get => (BatteryIconViewModel?)GetValue(BatteryIconProperty);
        set => SetValue(BatteryIconProperty, value);
    }

    public static readonly DependencyProperty SelectedActionIndexProperty =
        DependencyProperty.Register(nameof(SelectedActionIndex), typeof(int), typeof(GuideMenu), new PropertyMetadata(0));

    /// <summary>
    /// Kept at a constant 0 — Set Up Controller is the only gamepad-activatable
    /// action in this menu now that Updates moved to its own dedicated rail button.
    /// Still exposed (rather than deleted outright) so the existing highlight
    /// DataTrigger in GuideMenu.xaml and MainWindow's MoveSelection/ActivateSelected
    /// calls keep working unchanged if a second action item is ever added back here.
    /// </summary>
    public int SelectedActionIndex
    {
        get => (int)GetValue(SelectedActionIndexProperty);
        set => SetValue(SelectedActionIndexProperty, value);
    }

    public static readonly DependencyProperty PermissionDetailProperty =
        DependencyProperty.Register(nameof(PermissionDetail), typeof(string), typeof(GuideMenu),
            new PropertyMetadata(string.Empty));

    /// <summary>Sub-label naming which sites hold permissions, e.g. "youtube.com".</summary>
    public string PermissionDetail
    {
        get => (string)GetValue(PermissionDetailProperty);
        set => SetValue(PermissionDetailProperty, value);
    }

    /// <summary>
    /// The actions currently on offer, in display order.
    ///
    /// Built from what is actually visible rather than hard-coded indices: two of the
    /// three entries appear only while the browser is open, so fixed numbering would
    /// silently point the highlight at the wrong row as they come and go.
    /// </summary>
    private List<FrameworkElement> VisibleActions
    {
        get
        {
            var actions = new List<FrameworkElement>();

            // Music first: while a game is running this is the only reason to open the
            // guide, so it should be what the highlight lands on.
            if (NowPlayingCard.Visibility == Visibility.Visible)
            {
                actions.Add(NowPlayingCard);
                actions.Add(VolumeRow);
            }

            actions.Add(ControllerSetupButton);

            if (RevokePermissionsButton.Visibility == Visibility.Visible)
            {
                actions.Add(RevokePermissionsButton);
            }

            if (CloseBrowserButton.Visibility == Visibility.Visible)
            {
                actions.Add(CloseBrowserButton);
            }

            return actions;
        }
    }

    private int ActionCount => VisibleActions.Count;

    public static readonly DependencyProperty CloseBrowserIndexProperty =
        DependencyProperty.Register(nameof(CloseBrowserIndex), typeof(bool), typeof(GuideMenu),
            new PropertyMetadata(false));

    /// <summary>
    /// True when the highlight is on Close Browser. A dependency property rather than a
    /// computed one because the XAML trigger binds to it — a plain getter would never
    /// raise a change notification and the highlight would not move.
    /// </summary>
    public bool CloseBrowserIndex
    {
        get => (bool)GetValue(CloseBrowserIndexProperty);
        private set => SetValue(CloseBrowserIndexProperty, value);
    }

    public static readonly DependencyProperty RevokePermissionsSelectedProperty =
        DependencyProperty.Register(nameof(RevokePermissionsSelected), typeof(bool), typeof(GuideMenu),
            new PropertyMetadata(false));

    /// <summary>True when the highlight is on Revoke Site Permissions.</summary>
    public bool RevokePermissionsSelected
    {
        get => (bool)GetValue(RevokePermissionsSelectedProperty);
        private set => SetValue(RevokePermissionsSelectedProperty, value);
    }

    /// <summary>
    /// Keeps the highlight flags in step with the selection.
    ///
    /// Driven off identity rather than a fixed index: the browser-only entries come and
    /// go, so position alone does not say which action is which.
    /// </summary>
    private void RefreshActionHighlights()
    {
        var actions = VisibleActions;
        var selected = SelectedActionIndex >= 0 && SelectedActionIndex < actions.Count
            ? actions[SelectedActionIndex]
            : null;

        CloseBrowserIndex = ReferenceEquals(selected, CloseBrowserButton);
        RevokePermissionsSelected = ReferenceEquals(selected, RevokePermissionsButton);
        ControllerSetupSelected = ReferenceEquals(selected, ControllerSetupButton);

        var accent = TryFindResource("Theme.AccentPrimaryBrush") as System.Windows.Media.Brush;
        var clear = System.Windows.Media.Brushes.Transparent;

        NowPlayingCard.BorderBrush = ReferenceEquals(selected, NowPlayingCard) ? accent ?? clear : clear;
        VolumeRow.BorderBrush = ReferenceEquals(selected, VolumeRow) ? accent ?? clear : clear;

        // Within the now-playing row, one transport button carries the highlight.
        var onTransport = ReferenceEquals(selected, NowPlayingCard);
        PrevButton.BorderBrush = onTransport && _transportIndex == 0 ? accent ?? clear : clear;
        PlayPauseButton.BorderBrush = onTransport && _transportIndex == 1 ? accent ?? clear : clear;
        NextButton.BorderBrush = onTransport && _transportIndex == 2 ? accent ?? clear : clear;
    }

    public static readonly DependencyProperty ControllerSetupSelectedProperty =
        DependencyProperty.Register(nameof(ControllerSetupSelected), typeof(bool), typeof(GuideMenu),
            new PropertyMetadata(true));

    /// <summary>
    /// True when the highlight is on Set Up Controller. A property rather than the old
    /// fixed index-0 trigger, since music rows can now sit above it.
    /// </summary>
    public bool ControllerSetupSelected
    {
        get => (bool)GetValue(ControllerSetupSelectedProperty);
        private set => SetValue(ControllerSetupSelectedProperty, value);
    }

    private void VolumeSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_suppressVolumeEvent)
        {
            VolumeChanged?.Invoke(this, e.NewValue / 100.0);
        }
    }

    /// <summary>
    /// Shows or hides the browser-only actions. Called as the browser opens and closes.
    /// </summary>
    public void SetPermissionActionVisible(bool visible, string detail)
    {
        RevokePermissionsButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PermissionDetail = detail;

        // Never leave the highlight past the end of a shortened list.
        if (SelectedActionIndex >= ActionCount)
        {
            SelectedActionIndex = 0;
        }

        RefreshActionHighlights();
    }

    /// <summary>Shows or hides the Close Browser action.</summary>
    public void SetBrowserActionsVisible(bool visible)
    {
        CloseBrowserButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (SelectedActionIndex >= ActionCount)
        {
            SelectedActionIndex = 0;
        }

        RefreshActionHighlights();
    }

    public bool IsOpen { get; private set; }

    public event EventHandler? ControllerSetupRequested;

    /// <summary>Raised when the user asks to revoke remembered site permissions.</summary>
    public event EventHandler? RevokePermissionsRequested;

    /// <summary>Raised as the menu starts closing, however it was dismissed.</summary>
    public event EventHandler? Closed;

    /// <summary>Raised when the user asks to leave the browser.</summary>
    public event EventHandler? CloseBrowserRequested;

    /// <summary>Music transport, raised from the now-playing card.</summary>
    public event EventHandler? PlayPauseRequested;
    public event EventHandler? NextTrackRequested;
    public event EventHandler? PreviousTrackRequested;

    /// <summary>Raised as the volume slider moves. Value is 0.0–1.0.</summary>
    public event EventHandler<double>? VolumeChanged;

    /// <summary>
    /// Which transport button the highlight is on: 0 previous, 1 play/pause, 2 next.
    /// Only meaningful while the now-playing row holds focus.
    /// </summary>
    private int _transportIndex = 1;

    /// <summary>
    /// Updates the now-playing card, showing or hiding it with the music.
    /// </summary>
    public void SetNowPlaying(bool hasMusic, string title, string artist,
                              System.Windows.Media.ImageSource? artwork, bool isPlaying)
    {
        NowPlayingCard.Visibility = hasMusic ? Visibility.Visible : Visibility.Collapsed;

        if (!hasMusic)
        {
            // Never leave the highlight on a row that has just disappeared.
            if (SelectedActionIndex >= ActionCount)
            {
                SelectedActionIndex = 0;
            }

            RefreshActionHighlights();
            return;
        }

        NowPlayingTitle.Text = title;
        NowPlayingArtist.Text = artist;
        NowPlayingArt.Source = artwork;
        PlayPauseGlyph.Text = isPlaying ? "⏸" : "▶";

        RefreshActionHighlights();
    }

    /// <summary>Sets the volume slider without raising VolumeChanged.</summary>
    public void SetVolumeDisplay(double volume)
    {
        _suppressVolumeEvent = true;
        VolumeSlider.Value = Math.Clamp(volume, 0, 1) * 100;
        _suppressVolumeEvent = false;
    }

    private bool _suppressVolumeEvent;

    /// <summary>Moves the transport highlight, or adjusts volume, depending on the row.</summary>
    public void MoveHorizontal(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        var actions = VisibleActions;
        var selected = SelectedActionIndex >= 0 && SelectedActionIndex < actions.Count
            ? actions[SelectedActionIndex]
            : null;

        if (ReferenceEquals(selected, NowPlayingCard))
        {
            _transportIndex = Math.Clamp(_transportIndex + delta, 0, 2);
            RefreshActionHighlights();
            return;
        }

        if (ReferenceEquals(selected, VolumeRow))
        {
            // 5% a press: fine enough to land on a comfortable level, coarse enough to
            // cross the range without holding the stick for an age.
            var next = Math.Clamp(VolumeSlider.Value + (delta * 5), 0, 100);
            VolumeSlider.Value = next;
            VolumeChanged?.Invoke(this, next / 100.0);
        }
    }

    private void PlayPause_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => PlayPauseRequested?.Invoke(this, EventArgs.Empty);

    private void Next_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => NextTrackRequested?.Invoke(this, EventArgs.Empty);

    private void Prev_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => PreviousTrackRequested?.Invoke(this, EventArgs.Empty);

    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _overlayEnabled = true;

    public GuideMenu()
    {
        InitializeComponent();

        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
        _clockTimer.Start();

        UpdateOverlayToggleVisual(animate: false);
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("h:mm tt");
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        IsOpen = true;
        IsHitTestVisible = true;
        Panel.IsHitTestVisible = true;
        Scrim.IsHitTestVisible = true;
        SelectedActionIndex = 0;
        RefreshActionHighlights();

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        PanelTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(PanelTransform.X, 0, TimeSpan.FromSeconds(0.3)) { EasingFunction = easing });
        Scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
    }

    public void Close()
    {
        IsOpen = false;

        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
        var slideOut = new DoubleAnimation(PanelTransform.X, Panel.Width, TimeSpan.FromSeconds(0.25)) { EasingFunction = easing };
        var fadeOut = new DoubleAnimation(Scrim.Opacity, 0, TimeSpan.FromSeconds(0.25));
        fadeOut.Completed += (_, _) =>
        {
            Panel.IsHitTestVisible = false;
            Scrim.IsHitTestVisible = false;
            IsHitTestVisible = false;
        };

        // Raised immediately rather than after the slide: anything hidden to make room
        // for this menu (the browser's page) should come back as it leaves, not a
        // quarter-second later.
        Closed?.Invoke(this, EventArgs.Empty);

        PanelTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
        Scrim.BeginAnimation(OpacityProperty, fadeOut);
    }

    public void ToggleOverlay()
    {
        _overlayEnabled = !_overlayEnabled;
        UpdateOverlayToggleVisual(animate: true);
    }

    /// <summary>Moves the gamepad highlight between the menu's action buttons. No wraparound, matching dashboard row navigation.</summary>
    public void MoveSelection(int delta)
    {
        SelectedActionIndex = Math.Clamp(SelectedActionIndex + delta, 0, ActionCount - 1);
        RefreshActionHighlights();
    }

    /// <summary>Activates whichever action button currently has the gamepad highlight — the Confirm-button equivalent of clicking it.</summary>
    public void ActivateSelected()
    {
        var actions = VisibleActions;
        if (SelectedActionIndex < 0 || SelectedActionIndex >= actions.Count)
        {
            return;
        }

        var selected = actions[SelectedActionIndex];

        if (ReferenceEquals(selected, NowPlayingCard))
        {
            // Stays open: skipping tracks usually means skipping several, and closing the
            // menu after each one would make that infuriating.
            switch (_transportIndex)
            {
                case 0: PreviousTrackRequested?.Invoke(this, EventArgs.Empty); break;
                case 1: PlayPauseRequested?.Invoke(this, EventArgs.Empty); break;
                default: NextTrackRequested?.Invoke(this, EventArgs.Empty); break;
            }

            return;
        }

        if (ReferenceEquals(selected, VolumeRow))
        {
            // Nothing to activate — volume is adjusted with Left/Right.
            return;
        }

        if (ReferenceEquals(selected, ControllerSetupButton))
        {
            Close();
            ControllerSetupRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (ReferenceEquals(selected, RevokePermissionsButton))
        {
            // Deliberately stays open: the sub-label updates in place to confirm, which
            // is better feedback than the menu vanishing and leaving the user guessing.
            RevokePermissionsRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (ReferenceEquals(selected, CloseBrowserButton))
        {
            Close();
            CloseBrowserRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RevokePermissionsButton_Click(object sender, RoutedEventArgs e)
        => RevokePermissionsRequested?.Invoke(this, EventArgs.Empty);

    private void CloseBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        CloseBrowserRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ControllerSetupButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        ControllerSetupRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateOverlayToggleVisual(bool animate)
    {
        var targetColor = _overlayEnabled
            ? (Color)ColorConverter.ConvertFromString("#33D17A")
            : (Color)ColorConverter.ConvertFromString("#2A2A33");

        OverlayPill.Background = new SolidColorBrush(targetColor);

        var targetMargin = _overlayEnabled ? new Thickness(23, 0, 0, 0) : new Thickness(3, 0, 0, 0);
        if (animate)
        {
            OverlayKnob.BeginAnimation(MarginProperty, new ThicknessAnimation(OverlayKnob.Margin, targetMargin, TimeSpan.FromSeconds(0.18)));
        }
        else
        {
            OverlayKnob.Margin = targetMargin;
        }
    }
}
