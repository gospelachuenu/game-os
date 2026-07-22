using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace UI;

/// <summary>
/// Game detail screen shown when a tile is selected — replaces the old immediate
/// swallow-to-launch. Keeps the ambient video playing behind a frosted scrim (same
/// treatment as UpdatesScreen). Its Play button raises PlayRequested, which MainWindow
/// answers by running the existing swallow launch overlay for the same game. Fully
/// controller-navigable: the only focusable action here is Play, so it's always the
/// highlighted target; A/Enter plays, B/Tab/Escape backs out.
/// </summary>
public partial class GameDetailScreen : UserControl
{
    private GameDetailViewModel? _viewModel;
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    /// <summary>Raised the moment Show() runs — MainWindow hides the dashboard content underneath (ambient video keeps playing).</summary>
    public event EventHandler? Opened;

    /// <summary>Raised when the user backs out without playing.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Raised when Play is pressed — carries the game whose launch swallow should run,
    /// plus the thumbnail's bounds (relative to the window root) so the swallow grows
    /// out of the thumbnail rather than just cutting to fullscreen.
    /// </summary>
    public event Action<GameDetailViewModel, Rect>? PlayRequested;

    public GameDetailScreen()
    {
        InitializeComponent();

        // Simulated update progress so the pending-update bar visibly moves, standing
        // in for real MaintenanceHub download-progress callbacks.
        _updateTimer.Tick += (_, _) =>
        {
            if (_viewModel is { HasUpdatePending: true })
            {
                _viewModel.UpdateProgressPercent = Math.Min(100, _viewModel.UpdateProgressPercent + 2);
                if (_viewModel.UpdateProgressPercent >= 100)
                {
                    _viewModel.HasUpdatePending = false;
                }
            }
        };
    }

    public void Show(GameTileViewModel tile)
    {
        _viewModel = new GameDetailViewModel(tile);
        DataContext = _viewModel;

        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)));
        Opened?.Invoke(this, EventArgs.Empty);

        // Play is the only action, so it always carries the highlight ring.
        PlayButtonHighlight.BorderBrush = (Brush)FindResource("Theme.AccentPrimaryBrush");

        _updateTimer.Start();
    }

    public void Hide()
    {
        _updateTimer.Stop();

        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromSeconds(0.2));
        fadeOut.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>Gamepad/keyboard Confirm — the detail screen's only action is Play.</summary>
    public void ActivateSelected() => Play();

    private void PlayButton_Click(object sender, RoutedEventArgs e) => Play();

    private void Play()
    {
        if (_viewModel is null)
        {
            return;
        }

        // Capture the PLAY BUTTON's bounds (relative to the window root) BEFORE
        // collapsing this screen, so the launch swallow grows out of the thing the
        // user actually pressed. Growing it from the cover art instead made the
        // animation appear to sweep in from the side rather than from the point of
        // interaction.
        var root = Window.GetWindow(this);
        var originRect = new Rect(0, 0, ActualWidth, ActualHeight);
        if (root is not null && PlayButton.ActualWidth > 0)
        {
            originRect = PlayButton.TransformToVisual(root)
                                   .TransformBounds(new Rect(0, 0, PlayButton.ActualWidth, PlayButton.ActualHeight));
        }

        // Collapse without the fade-out/CloseRequested path — MainWindow restores the
        // dashboard as part of the launch overlay's own lifecycle, and firing
        // CloseRequested here would double-restore it.
        _updateTimer.Stop();
        Visibility = Visibility.Collapsed;
        PlayRequested?.Invoke(_viewModel, originRect);
    }
}
