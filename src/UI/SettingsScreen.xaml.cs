using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace UI;

/// <summary>
/// Full-screen Settings overlay, opened from its own dedicated rail button (same
/// pattern as UpdatesScreen — its own screen, not a layer on top of the dashboard).
/// </summary>
public partial class SettingsScreen : UserControl
{
    public SettingsScreenViewModel ViewModel { get; } = new();

    /// <summary>Raised the moment Show() is called — lets MainWindow hide the dashboard/ambient video underneath.</summary>
    public event EventHandler? Opened;

    public event EventHandler? CloseRequested;

    public SettingsScreen()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    /// <summary>
    /// Supplies the browser whose remembered site permissions this screen can clear.
    /// Set by MainWindow rather than constructed here — there is one browser, and
    /// Settings should not own it.
    /// </summary>
    public BrowserScreen? Browser { get; set; }

    /// <summary>True while the Revoke button has controller focus.</summary>
    private bool _resetButtonFocused;

    public void Show()
    {
        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)));

        // Reset each time: the screen always opens with nothing focused, so the first
        // D-pad press moves onto the button rather than silently arming it.
        _resetButtonFocused = false;
        UpdateResetButtonFocus();
        RefreshPermissionSummary();

        Opened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves controller focus onto or off the Revoke button.
    ///
    /// Settings has exactly one actionable control, so this is deliberately a toggle
    /// rather than a general focus system — a full navigation model can come when there
    /// is more than one thing to move between.
    /// </summary>
    public void MoveSelection(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        _resetButtonFocused = delta > 0;
        UpdateResetButtonFocus();
    }

    /// <summary>Activates the focused control. True if something was actually pressed.</summary>
    public bool ActivateSelected()
    {
        if (!_resetButtonFocused)
        {
            return false;
        }

        _ = RevokePermissionsAsync();
        return true;
    }

    private void UpdateResetButtonFocus()
    {
        var accent = TryFindResource("Theme.AccentPrimaryBrush") as System.Windows.Media.Brush;

        ResetPermissionsButton.BorderBrush = _resetButtonFocused
            ? accent ?? ResetPermissionsButton.BorderBrush
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2A, 0x2F, 0x3A));

        ResetPermissionsButton.Background = new System.Windows.Media.SolidColorBrush(
            _resetButtonFocused
                ? System.Windows.Media.Color.FromArgb(0x1F, 0xA8, 0xE0, 0x3B)
                : System.Windows.Media.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    }

    private void ResetPermissions_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => _ = RevokePermissionsAsync();

    /// <summary>
    /// Clears every remembered site permission, then reports the result.
    ///
    /// Confirmed in the label rather than a dialog: the action is easily repeated and
    /// costs nothing if pressed by accident, so a confirmation step would be friction
    /// for no safety gain.
    /// </summary>
    private async Task RevokePermissionsAsync()
    {
        if (Browser is null)
        {
            return;
        }

        ResetPermissionsLabel.Text = "Revoking…";

        await Browser.ClearRememberedPermissionsAsync();

        ResetPermissionsLabel.Text = "Revoked";
        RefreshPermissionSummary();

        // Back to the normal label, so the button reads as pressable again.
        await Task.Delay(1600);
        ResetPermissionsLabel.Text = "Revoke all permissions";
    }

    private async void RefreshPermissionSummary()
    {
        if (Browser is null)
        {
            return;
        }

        var granted = await Browser.GetGrantedPermissionOriginsAsync();

        PermissionSummary.Text = granted.Count == 0
            ? "No website has been given access to the microphone."
            : "Allowed to use the microphone: " + string.Join(", ", granted);
    }

    public void Hide()
    {
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromSeconds(0.2));
        fadeOut.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void ThemeSwatch_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ThemeOptionViewModel option })
        {
            ViewModel.SelectTheme(option);
        }
    }
}
