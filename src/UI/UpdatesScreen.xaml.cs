using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace UI;

/// <summary>
/// Full-screen Updates overlay, opened from its own dedicated rail button (separate
/// from the Library/Foundry/Friends/Settings items). Mock data only — see
/// UpdatesScreenViewModel for how this maps to the real MaintenanceHub/Windows Update
/// Agent API design once there's real hardware to run it on.
/// </summary>
public partial class UpdatesScreen : UserControl
{
    public UpdatesScreenViewModel ViewModel { get; } = new();

    /// <summary>Raised the moment Show() is called — lets MainWindow hide the dashboard/ambient video underneath, since this is its own screen, not a layer on top of them.</summary>
    public event EventHandler? Opened;

    public event EventHandler? CloseRequested;

    public static readonly DependencyProperty InstallButtonHighlightProperty =
        DependencyProperty.Register(nameof(InstallButtonHighlight), typeof(Brush), typeof(UpdatesScreen), new PropertyMetadata(Brushes.Transparent));

    public static readonly DependencyProperty RemindLaterButtonHighlightProperty =
        DependencyProperty.Register(nameof(RemindLaterButtonHighlight), typeof(Brush), typeof(UpdatesScreen), new PropertyMetadata(Brushes.Transparent));

    /// <summary>Accent ring shown on the primary action button while it's the gamepad-highlighted one (index 0).</summary>
    public Brush InstallButtonHighlight
    {
        get => (Brush)GetValue(InstallButtonHighlightProperty);
        set => SetValue(InstallButtonHighlightProperty, value);
    }

    /// <summary>Accent ring shown on the "Remind me later" button while it's the gamepad-highlighted one (index 1).</summary>
    public Brush RemindLaterButtonHighlight
    {
        get => (Brush)GetValue(RemindLaterButtonHighlightProperty);
        set => SetValue(RemindLaterButtonHighlightProperty, value);
    }

    /// <summary>0 = primary (install) button, 1 = "Remind me later". Moved by Left/Right on the controller, activated by A/Enter.</summary>
    private int _selectedButtonIndex;

    private readonly DispatcherTimer _downloadTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public UpdatesScreen()
    {
        InitializeComponent();
        DataContext = ViewModel;

        foreach (var update in ViewModel.Updates)
        {
            update.IsSelected = ReferenceEquals(update, ViewModel.SelectedUpdate);
        }

        // Simulated download progress so the screen feels alive rather than static
        // mock data — ticks any Downloading item's percent up and flips it to
        // ReadyToInstall at 100%, standing in for real Windows Update Agent API
        // download-progress callbacks.
        _downloadTimer.Tick += (_, _) => TickDownloads();
    }

    private void TickDownloads()
    {
        foreach (var update in ViewModel.Updates.Where(u => u.State == UpdateItemState.Downloading))
        {
            update.DownloadProgressPercent = Math.Min(100, update.DownloadProgressPercent + 3);
            if (update.DownloadProgressPercent >= 100)
            {
                update.State = UpdateItemState.ReadyToInstall;
            }
        }
    }

    public void Show()
    {
        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)));
        Opened?.Invoke(this, EventArgs.Empty);
        _downloadTimer.Start();

        _selectedButtonIndex = 0;
        UpdateButtonHighlight();
    }

    public void Hide()
    {
        _downloadTimer.Stop();

        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromSeconds(0.2));
        fadeOut.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void UpdateCard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: UpdateItemViewModel clicked })
        {
            return;
        }

        SelectUpdate(clicked);
    }

    private void SelectUpdate(UpdateItemViewModel target)
    {
        foreach (var update in ViewModel.Updates)
        {
            update.IsSelected = ReferenceEquals(update, target);
        }

        ViewModel.SelectedUpdate = target;

        // Landing on a new card puts the highlight back on its primary action button.
        _selectedButtonIndex = 0;
        UpdateButtonHighlight();

        BringSelectedCardIntoView(target);
    }

    /// <summary>
    /// Scrolls the list so the newly-selected card is fully visible — the same
    /// camera-follows-focus behaviour the dashboard uses, so navigating with the
    /// controller never leaves the highlighted card clipped or off-screen.
    /// </summary>
    private void BringSelectedCardIntoView(UpdateItemViewModel target)
    {
        // Defer until the container for the item is realized/laid out, otherwise
        // BringIntoView on a not-yet-measured element does nothing.
        Dispatcher.BeginInvoke(() =>
        {
            if (UpdatesItemsControl.ItemContainerGenerator.ContainerFromItem(target) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Moves the highlighted card up/down the list — the gamepad/keyboard equivalent
    /// of clicking a different card, so this screen is fully navigable without a
    /// mouse just like the rest of the dashboard. No wraparound, matching how row
    /// navigation works elsewhere.
    /// </summary>
    public void MoveSelection(int delta)
    {
        if (ViewModel.Updates.Count == 0)
        {
            return;
        }

        var currentIndex = ViewModel.SelectedUpdate is null ? -1 : ViewModel.Updates.IndexOf(ViewModel.SelectedUpdate);
        var nextIndex = Math.Clamp(currentIndex + delta, 0, ViewModel.Updates.Count - 1);
        SelectUpdate(ViewModel.Updates[nextIndex]);
    }

    /// <summary>
    /// Left/Right on the controller moves the highlight between the detail panel's two
    /// action buttons (0 = install, 1 = remind later), so the second button is
    /// reachable without a mouse. No wraparound.
    /// </summary>
    public void MoveButtonSelection(int delta)
    {
        _selectedButtonIndex = Math.Clamp(_selectedButtonIndex + delta, 0, 1);
        UpdateButtonHighlight();
    }

    private void UpdateButtonHighlight()
    {
        var accent = (Brush)FindResource("Theme.AccentPrimaryBrush");
        InstallButtonHighlight = _selectedButtonIndex == 0 ? accent : Brushes.Transparent;
        RemindLaterButtonHighlight = _selectedButtonIndex == 1 ? accent : Brushes.Transparent;
    }

    /// <summary>
    /// Gamepad/keyboard Confirm — activates whichever detail-panel button is currently
    /// highlighted (install action, or "Remind me later").
    /// </summary>
    public void ActivateSelected()
    {
        if (_selectedButtonIndex == 0)
        {
            AdvanceSelectedUpdateState();
        }
        else
        {
            RemindLater();
        }
    }

    /// <summary>
    /// Simulates the update lifecycle (Available -> Downloading -> ReadyToInstall ->
    /// Installed) since there is no real Windows Update Agent API call behind this
    /// test screen. Advances by exactly one stage per activation so the state machine
    /// and its visuals (pill color, progress bar, button label) can all be exercised.
    /// </summary>
    private void InstallButton_Click(object sender, RoutedEventArgs e) => AdvanceSelectedUpdateState();

    private void AdvanceSelectedUpdateState()
    {
        var selected = ViewModel.SelectedUpdate;
        if (selected is null)
        {
            return;
        }

        // Installing a ready update completes it — an installed update is no longer
        // pending, so it drops off the list entirely and focus moves to the neighbour
        // that slides into its place, rather than lingering as a dead "Installed" row.
        if (selected.State == UpdateItemState.ReadyToInstall)
        {
            var next = ViewModel.RemoveCompleted(selected);
            if (next is not null)
            {
                SelectUpdate(next);
            }

            return;
        }

        if (selected.State == UpdateItemState.Available)
        {
            selected.DownloadProgressPercent = 0;
        }

        selected.State = selected.State switch
        {
            UpdateItemState.Available => UpdateItemState.Downloading,
            UpdateItemState.Downloading => UpdateItemState.ReadyToInstall,
            _ => selected.State,
        };
    }

    private void RemindLaterButton_Click(object sender, RoutedEventArgs e) => RemindLater();

    /// <summary>
    /// "Remind me later" dismisses the selected update from the list for now (it's
    /// deferred, not installed), moving focus to the neighbour that slides into its
    /// place — the same list behaviour as completing an install.
    /// </summary>
    private void RemindLater()
    {
        var selected = ViewModel.SelectedUpdate;
        if (selected is null)
        {
            return;
        }

        var next = ViewModel.RemoveCompleted(selected);
        if (next is not null)
        {
            SelectUpdate(next);
        }
    }
}
