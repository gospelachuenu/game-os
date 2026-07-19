using System.Windows;

namespace UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Apply the theme BEFORE calling base.OnStartup: StartupUri causes WPF to
        // construct MainWindow (and its view model, which generates theme-tinted tile
        // art) as part of base.OnStartup, so the theme must already be set by then.
        ThemeManager.Apply(DashboardTheme.MeadowGreen);
        base.OnStartup(e);
    }
}
