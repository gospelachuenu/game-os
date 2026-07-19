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

    public void Show()
    {
        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)));
        Opened?.Invoke(this, EventArgs.Empty);
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
