using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using InputDaemon;

namespace UI;

/// <summary>
/// Windowed test build only: a normal desktop window, no shell replacement, no
/// focus-stealing hooks, no fullscreen/kiosk behavior. Safe to run repeatedly on a
/// personal dev machine. The real console UI.exe (fullscreen, controller-navigable,
/// integrated with ConsoleSupervisor's FocusGuardian) is a separate future effort.
///
/// SPOTLIGHT navigation model: one flat row of games. Left/Right moves focus along
/// it and the hero above swaps to match (MainWindowViewModel.FocusedGame drives both
/// the hero and the background tint). Up/Down moves between the hero's action buttons
/// (Play / Details) and the game row. There is no multi-row grid — that was the
/// previous design.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly GamepadInputPoller _gamepadPoller;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        // Keep the dashboard content hidden until the ambient video's first frame has
        // actually decoded, so tiles never flash in over a plain dark background
        // while the MediaElement is still starting up.
        Ambient.MediaReady += Ambient_MediaReady;

        _viewModel.GameLaunchRequested += ViewModel_GameLaunchRequested;
        _viewModel.GameDetailRequested += ViewModel_GameDetailRequested;
        _viewModel.FocusedTileChanged += ViewModel_FocusedTileChanged;
        LaunchTakeover.Dismissed += (_, _) =>
        {
            // The launch swallow is entered from the game detail screen, which
            // collapsed the dashboard; restore it when the swallow is dismissed.
            ContentGrid.Visibility = Visibility.Visible;
            Focus();
        };

        Guide.BatteryIcon = _viewModel.BatteryIcon;
        Guide.ControllerSetupRequested += (_, _) => ControllerSetup.Show();
        ControllerSetup.CloseRequested += (_, _) => Focus();

        // Selecting a game opens the detail screen (keeps ambient video, hides
        // dashboard content). Its Play button fires the real launch swallow; backing
        // out restores the dashboard.
        GameDetail.Opened += (_, _) => ContentGrid.Visibility = Visibility.Collapsed;
        GameDetail.CloseRequested += (_, _) =>
        {
            ContentGrid.Visibility = Visibility.Visible;
            Focus();
        };
        GameDetail.PlayRequested += (detail, thumbRect) =>
        {
            _pendingLaunchRect = thumbRect;

            // Prefer the real cover image for the launch swallow (with the overlay's
            // own darkening scrim keeping the text readable); fall back to the gradient
            // art when the game has no cover image.
            Brush launchArt = detail.IconSource is not null
                ? new ImageBrush(detail.IconSource) { Stretch = Stretch.UniformToFill }
                : detail.ArtBrush;

            _viewModel.RaiseLaunch(detail.Entry, launchArt, detail.GameName);
        };

        // Updates is its OWN screen, not an overlay layered on top of the dashboard —
        // hide the ambient video and dashboard content entirely while it's open
        // instead of leaving them rendering/playing underneath a translucent panel.
        // Updates takes over the home screen's CONTENT (game rows + rail), but keeps
        // the SAME ambient video playing behind it — so the screen swaps what's shown
        // without the background ever cutting to flat black. Only ContentGrid is
        // hidden; Ambient keeps running underneath, and UpdatesScreen's own background
        // is transparent so the video shows through.
        Updates.Opened += (_, _) => ContentGrid.Visibility = Visibility.Collapsed;
        Updates.CloseRequested += (_, _) =>
        {
            ContentGrid.Visibility = Visibility.Visible;
            Focus();
        };

        // Settings gets the same treatment — same ambient video behind it.
        Settings.Opened += (_, _) => ContentGrid.Visibility = Visibility.Collapsed;
        Settings.CloseRequested += (_, _) =>
        {
            ContentGrid.Visibility = Visibility.Visible;
            Focus();
        };

        // Real physical controller support (Xbox natively via XInput; PlayStation
        // controllers work too as long as something on the system — Steam, DS4Windows
        // — translates them into an XInput device, which Windows does not do on its
        // own). Drives the exact same navigation methods the keyboard handler below
        // calls, so there is one single source of truth for what each action does.
        _gamepadPoller = new GamepadInputPoller(new XInputGamepadReader());
        _gamepadPoller.MoveUp += () =>
        {
            if (Updates.Visibility == Visibility.Visible) Updates.MoveSelection(-1);
            else if (Guide.IsOpen) Guide.MoveSelection(-1);
            else if (_isRailFocused) MoveRailSelection(-1);
            else if (!IsAnyOverlayOpen) MoveZone(-1);
        };
        _gamepadPoller.MoveDown += () =>
        {
            if (Updates.Visibility == Visibility.Visible) Updates.MoveSelection(1);
            else if (Guide.IsOpen) Guide.MoveSelection(1);
            else if (_isRailFocused) MoveRailSelection(1);
            else if (!IsAnyOverlayOpen) MoveZone(1);
        };
        // On the Updates screen, Left/Right moves between the detail panel's two
        // action buttons. In the Guide Menu (single-column) Left/Right is meaningless
        // and swallowed. On the dashboard it moves along the game row, or between the
        // hero's Play/Details buttons when the hero zone has focus.
        _gamepadPoller.MoveLeft += () =>
        {
            if (Updates.Visibility == Visibility.Visible) Updates.MoveButtonSelection(-1);
            else if (!IsAnyOverlayOpen) MoveHorizontal(-1);
        };
        _gamepadPoller.MoveRight += () =>
        {
            if (Updates.Visibility == Visibility.Visible) Updates.MoveButtonSelection(1);
            else if (!IsAnyOverlayOpen) MoveHorizontal(1);
        };
        _gamepadPoller.Confirm += () =>
        {
            if (Updates.Visibility == Visibility.Visible) Updates.ActivateSelected();
            else if (GameDetail.Visibility == Visibility.Visible) GameDetail.ActivateSelected();
            else if (Guide.IsOpen) Guide.ActivateSelected();
            else if (_isRailFocused) ActivateRailSelection();
            else if (!IsAnyOverlayOpen) ActivateDashboardSelection();
        };
        _gamepadPoller.Back += () =>
        {
            if (ControllerSetup.Visibility == Visibility.Visible)
            {
                ControllerSetup.Hide();
            }
            else if (Updates.Visibility == Visibility.Visible)
            {
                Updates.Hide();
            }
            else if (Settings.Visibility == Visibility.Visible)
            {
                Settings.Hide();
            }
            else if (GameDetail.Visibility == Visibility.Visible)
            {
                GameDetail.Hide();
            }
            else if (Guide.IsOpen)
            {
                Guide.Close();
            }
            else if (_isRailFocused)
            {
                SetRailFocused(false);
            }
        };
        _gamepadPoller.ToggleGuideMenu += () => Guide.Toggle();
        _gamepadPoller.Start();

        // Live clock in the top-right status line.
        _clockTimer.Tick += (_, _) => _viewModel.RefreshClock();
        _clockTimer.Start();

        Loaded += (_, _) => UpdateZoneHighlight();
        Closed += (_, _) =>
        {
            _gamepadPoller.Stop();
            _clockTimer.Stop();
        };
    }

    private readonly System.Windows.Threading.DispatcherTimer _clockTimer =
        new() { Interval = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// True while any full-screen/modal overlay is showing (Guide Menu, Controller
    /// Setup wizard, Updates screen, or the launch takeover). Dashboard tile
    /// navigation must be suppressed whenever this is true, otherwise stray gamepad
    /// input meant for whichever overlay is open silently moves focus on the
    /// dashboard underneath, which only becomes visible once the overlay closes.
    /// </summary>
    private bool IsAnyOverlayOpen =>
        Guide.IsOpen
        || ControllerSetup.Visibility == Visibility.Visible
        || Updates.Visibility == Visibility.Visible
        || Settings.Visibility == Visibility.Visible
        || GameDetail.Visibility == Visibility.Visible
        || LaunchTakeover.Visibility == Visibility.Visible;

    /// <summary>
    /// True while gamepad/keyboard focus has moved off the dashboard onto the side
    /// rail's Updates/Settings buttons. Left at the first game in the row hands focus
    /// here; Right, or Confirm activating a button, hands it back.
    /// </summary>
    private bool _isRailFocused;
    private int _railFocusIndex;
    private static readonly string[] RailButtonNames = { "UpdatesRailBorder", "SettingsRailBorder" };

    /// <summary>
    /// Which zone of the Spotlight dashboard has focus. The hero's action buttons and
    /// the game row are the two navigable zones; Up/Down moves between them, Left/Right
    /// means "previous/next button" in the hero and "previous/next game" in the row.
    /// </summary>
    private enum DashboardZone { Hero, GameRow }

    private DashboardZone _zone = DashboardZone.GameRow;

    /// <summary>0 = Play, 1 = Details. Only meaningful while _zone == Hero.</summary>
    private int _heroButtonIndex;

    /// <summary>The thumbnail rect (window-relative) the last Play was launched from — the swallow grows out of it.</summary>
    private Rect _pendingLaunchRect;

    /// <summary>Up/Down: move between the hero's action buttons and the game row.</summary>
    private void MoveZone(int delta)
    {
        if (_isRailFocused)
        {
            return;
        }

        var next = delta < 0 ? DashboardZone.Hero : DashboardZone.GameRow;
        if (next == _zone)
        {
            return;
        }

        _zone = next;
        _heroButtonIndex = 0;
        UpdateZoneHighlight();
    }

    /// <summary>Left/Right: along the game row, or between the hero's Play/Details buttons.</summary>
    private void MoveHorizontal(int delta)
    {
        if (_isRailFocused)
        {
            if (delta > 0)
            {
                SetRailFocused(false);
            }

            return;
        }

        if (_zone == DashboardZone.Hero)
        {
            _heroButtonIndex = Math.Clamp(_heroButtonIndex + delta, 0, 1);
            UpdateZoneHighlight();
            return;
        }

        // In the game row: moving left off the first game hands focus to the side rail.
        var atFirst = _viewModel.Games.Count > 0 && ReferenceEquals(_viewModel.FocusedGame, _viewModel.Games[0]);
        if (delta < 0 && atFirst)
        {
            SetRailFocused(true);
            return;
        }

        _viewModel.MoveFocus(delta);
    }

    /// <summary>Confirm on the dashboard: hero buttons act, game row opens the focused game's details.</summary>
    private void ActivateDashboardSelection()
    {
        if (_zone == DashboardZone.Hero)
        {
            if (_heroButtonIndex == 0)
            {
                PlayFocusedGame();
            }
            else
            {
                _viewModel.OpenFocusedDetails();
            }

            return;
        }

        _viewModel.OpenFocusedDetails();
    }

    /// <summary>Paints the accent ring on whichever dashboard element currently has focus.</summary>
    private void UpdateZoneHighlight()
    {
        var accent = (Brush)FindResource("Theme.AccentPrimaryBrush");
        var heroActive = _zone == DashboardZone.Hero && !_isRailFocused;

        PlayButtonRing.BorderBrush = heroActive && _heroButtonIndex == 0 ? accent : Brushes.Transparent;
        DetailsButtonRing.BorderBrush = heroActive && _heroButtonIndex == 1 ? accent : Brushes.Transparent;
    }

    private void PlayFocusedGame()
    {
        _pendingLaunchRect = GetFocusedTileScreenRect();
        _viewModel.PlayFocusedGame();
    }

    private void SetRailFocused(bool focused)
    {
        _isRailFocused = focused;
        _railFocusIndex = 0;
        UpdateRailHighlight();
        UpdateZoneHighlight();
    }

    private void MoveRailSelection(int delta)
    {
        _railFocusIndex = Math.Clamp(_railFocusIndex + delta, 0, RailButtonNames.Length - 1);
        UpdateRailHighlight();
    }

    private void ActivateRailSelection()
    {
        if (_railFocusIndex == 0)
        {
            Updates.Show();
        }
        else
        {
            Settings.Show();
        }
    }

    private void UpdateRailHighlight()
    {
        var accentBrush = (Brush)FindResource("Theme.AccentPrimaryBrush");
        for (var i = 0; i < RailButtonNames.Length; i++)
        {
            var button = i == 0 ? UpdatesRailButton : SettingsRailButton;
            button.ApplyTemplate();
            if (button.Template.FindName(RailButtonNames[i], button) is Border border)
            {
                border.BorderBrush = _isRailFocused && _railFocusIndex == i ? accentBrush : Brushes.Transparent;
            }
        }
    }

    private void ViewModel_GameDetailRequested(GameTileViewModel tile)
    {
        GameDetail.Show(tile);
    }

    private void ViewModel_GameLaunchRequested(Brush art, string gameName, string uri)
    {
        System.Diagnostics.Debug.WriteLine($"[LaunchOverlay] Would launch: {uri}");

        // Grow the swallow out of the detail screen's thumbnail (captured when Play was
        // pressed), falling back to the full window if we somehow have no rect.
        var startRect = _pendingLaunchRect.Width > 0
            ? _pendingLaunchRect
            : new Rect(0, 0, ActualWidth, ActualHeight);
        LaunchTakeover.Show(startRect, art, gameName);
    }

    /// <summary>
    /// Keeps the newly-focused game centred-ish in the horizontal row, and cross-fades
    /// the hero so the title/art swap reads as a deliberate transition rather than a
    /// hard cut. Spotlight's row scrolls sideways, so this works on HorizontalOffset.
    /// </summary>
    private void ViewModel_FocusedTileChanged(GameTileViewModel tile)
    {
        AnimateHeroSwap();

        var tileElement = FindTileElement(tile);
        if (tileElement is null)
        {
            return;
        }

        var transform = tileElement.TransformToVisual(GameRowScroller);
        var tileLeft = transform.Transform(new Point(0, 0)).X;
        var tileRight = tileLeft + tileElement.ActualWidth;

        double? targetOffset = null;
        if (tileLeft < 0)
        {
            targetOffset = GameRowScroller.HorizontalOffset + tileLeft - 40;
        }
        else if (tileRight > GameRowScroller.ViewportWidth)
        {
            targetOffset = GameRowScroller.HorizontalOffset + (tileRight - GameRowScroller.ViewportWidth) + 40;
        }

        if (targetOffset is null)
        {
            return;
        }

        AnimateScrollTo(Math.Max(0, targetOffset.Value));
    }

    /// <summary>
    /// Fades and lifts the hero slightly whenever the focused game changes, so the
    /// whole-screen swap has motion behind it instead of the text just popping.
    /// </summary>
    private void AnimateHeroSwap()
    {
        var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(0.34) };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });

        var lift = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(0.34) };
        lift.KeyFrames.Add(new LinearDoubleKeyFrame(14, KeyTime.FromPercent(0)));
        lift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });

        HeroPanel.BeginAnimation(OpacityProperty, fade);
        HeroSlide.BeginAnimation(TranslateTransform.YProperty, lift);
    }

    /// <summary>
    /// ScrollViewer has no dependency property for HorizontalOffset that
    /// BeginAnimation can target directly, so the scroll is driven manually via a
    /// DispatcherTimer stepping through eased values instead of a real Storyboard.
    /// </summary>
    private void AnimateScrollTo(double targetOffset)
    {
        var startOffset = GameRowScroller.HorizontalOffset;
        const int steps = 16;
        var duration = TimeSpan.FromSeconds(0.28);
        var stepInterval = TimeSpan.FromTicks(duration.Ticks / steps);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = stepInterval };
        var currentStep = 0;
        timer.Tick += (_, _) =>
        {
            currentStep++;
            var progress = Math.Min(1.0, currentStep / (double)steps);
            var eased = easing.Ease(progress);
            GameRowScroller.ScrollToHorizontalOffset(startOffset + (targetOffset - startOffset) * eased);

            if (currentStep >= steps)
            {
                timer.Stop();
            }
        };
        timer.Start();
    }

    /// <summary>
    /// Walks the visual tree under SectionsScrollViewer looking for the FrameworkElement
    /// whose DataContext is the given tile — works across however many nested rows/
    /// ItemsControls the dashboard has, unlike indexing a single named ItemsControl.
    /// </summary>
    private FrameworkElement? FindTileElement(GameTileViewModel tile)
    {
        return FindByDataContext(RootGrid, tile);
    }

    private static FrameworkElement? FindByDataContext(DependencyObject root, object dataContext)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { } element && ReferenceEquals(element.DataContext, dataContext))
            {
                return element;
            }

            var found = FindByDataContext(child, dataContext);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the currently-focused tile's realized visual element and returns its
    /// bounds relative to the root Grid (the same coordinate space LaunchOverlay's
    /// Panel is positioned in), so the launch takeover can start exactly over the
    /// tile the user selected and grow from there — a "swallow" transition rather
    /// than a separate screen appearing on top.
    /// </summary>
    private Rect GetFocusedTileScreenRect()
    {
        var focusedTile = _viewModel.FocusedGame;
        if (focusedTile is null)
        {
            return new Rect(0, 0, ActualWidth, ActualHeight);
        }

        var tileElement = FindTileElement(focusedTile);
        if (tileElement is null)
        {
            return new Rect(0, 0, ActualWidth, ActualHeight);
        }

        var transform = tileElement.TransformToVisual(RootGrid);
        var topLeft = transform.Transform(new Point(0, 0));

        return new Rect(topLeft.X, topLeft.Y, tileElement.ActualWidth, tileElement.ActualHeight);
    }

    private void Ambient_MediaReady(object? sender, EventArgs e)
    {
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        ContentGrid.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (ControllerSetup.Visibility == Visibility.Visible)
        {
            if (e.Key is Key.Tab or Key.B or Key.Escape)
            {
                ControllerSetup.Hide();
            }

            return;
        }

        if (Updates.Visibility == Visibility.Visible)
        {
            switch (e.Key)
            {
                case Key.Up:
                    Updates.MoveSelection(-1);
                    break;
                case Key.Down:
                    Updates.MoveSelection(1);
                    break;
                case Key.Left:
                    Updates.MoveButtonSelection(-1);
                    break;
                case Key.Right:
                    Updates.MoveButtonSelection(1);
                    break;
                case Key.Enter:
                    Updates.ActivateSelected();
                    break;
                case Key.Tab or Key.B or Key.Escape:
                    Updates.Hide();
                    break;
            }

            return;
        }

        if (Settings.Visibility == Visibility.Visible)
        {
            if (e.Key is Key.Tab or Key.B or Key.Escape)
            {
                Settings.Hide();
            }

            return;
        }

        if (GameDetail.Visibility == Visibility.Visible)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    GameDetail.ActivateSelected();
                    break;
                case Key.B or Key.Escape or Key.Tab:
                    GameDetail.Hide();
                    break;
            }

            return;
        }

        if (Guide.IsOpen)
        {
            switch (e.Key)
            {
                case Key.Up:
                    Guide.MoveSelection(-1);
                    break;
                case Key.Down:
                    Guide.MoveSelection(1);
                    break;
                case Key.Enter:
                    Guide.ActivateSelected();
                    break;
                case Key.Tab or Key.Escape:
                    Guide.Close();
                    break;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                MoveHorizontal(-1);
                break;
            case Key.Right:
                MoveHorizontal(1);
                break;
            case Key.Up:
                if (_isRailFocused) MoveRailSelection(-1);
                else MoveZone(-1);
                break;
            case Key.Down:
                if (_isRailFocused) MoveRailSelection(1);
                else MoveZone(1);
                break;
            case Key.Enter:
                if (_isRailFocused) ActivateRailSelection();
                else ActivateDashboardSelection();
                break;
            case Key.B when _isRailFocused:
                SetRailFocused(false);
                break;
            case Key.Tab:
                Guide.Toggle();
                break;
            case Key.O when Guide.IsOpen:
                Guide.ToggleOverlay();
                break;
        }
    }

    /// <summary>Clicking a cover tile focuses that game (the hero swaps to it) and opens its details.</summary>
    private void GameTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameTileViewModel game })
        {
            return;
        }

        _viewModel.FocusGame(game);
        _viewModel.OpenFocusedDetails();
    }

    private void HeroPlayButton_Click(object sender, RoutedEventArgs e) => PlayFocusedGame();

    private void HeroDetailsButton_Click(object sender, RoutedEventArgs e) => _viewModel.OpenFocusedDetails();

    private void UpdatesRailButton_Click(object sender, RoutedEventArgs e)
    {
        Updates.Show();
    }

    private void SettingsRailButton_Click(object sender, RoutedEventArgs e)
    {
        Settings.Show();
    }

}
