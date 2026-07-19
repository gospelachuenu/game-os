using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace UI;

/// <summary>
/// Ambient video background with a crossfaded loop: two MediaElements
/// (Assets/ambient_loop.mp4, muted) both play continuously and permanently offset
/// from each other by half the clip's duration. A poll timer watches whichever is
/// currently "active" (opacity 1); shortly before it reaches the end, opacity
/// crossfades to the other player — which is already mid-playback, already primed,
/// never needs a fresh Play()/Position=0 call — then the roles swap and the poll
/// continues against the new active player. This avoids the hard stutter of
/// MediaElement's Position=0+Play() restart, which causes a visible decoder re-seek
/// hitch even though the video content itself already loops seamlessly (baked-in
/// crossfade at the file's own tail/head).
/// </summary>
public partial class AmbientBackground : UserControl
{
    private const double CrossfadeLeadSeconds = 0.6;
    private const double CrossfadeDurationSeconds = 0.5;

    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private MediaElement? _activePlayer;
    private MediaElement? _standbyPlayer;
    private TimeSpan _mediaDuration;
    private bool _crossfadeInProgress;
    private bool _standbyOffsetApplied;

    /// <summary>
    /// Raised once the first video frame has actually decoded (MediaOpened on the
    /// first-loaded player), not just when Play() was called. MainWindow uses this to
    /// hold the dashboard content hidden until the background is genuinely ready.
    /// </summary>
    public event EventHandler? MediaReady;

    public AmbientBackground()
    {
        InitializeComponent();
        _pollTimer.Tick += PollTimer_Tick;
    }

    private void PlayerA_Loaded(object sender, RoutedEventArgs e)
    {
        var videoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ambient_loop.mp4");
        if (!File.Exists(videoPath))
        {
            // No video asset present — don't block the UI waiting for a MediaOpened
            // event that will never fire.
            MediaReady?.Invoke(this, EventArgs.Empty);
            return;
        }

        _activePlayer = PlayerA;
        _standbyPlayer = PlayerB;

        PlayerA.MediaOpened += FirstPlayer_MediaOpened;
        PlayerA.MediaEnded += Player_MediaEnded;
        PlayerA.Source = new Uri(videoPath, UriKind.Absolute);
        PlayerA.Play();

        PlayerB.MediaOpened += StandbyPlayer_MediaOpened;
        PlayerB.MediaEnded += Player_MediaEnded;
        PlayerB.Source = new Uri(videoPath, UriKind.Absolute);
        PlayerB.Play();
    }

    /// <summary>
    /// Whichever player reaches the actual end of its own timeline restarts silently
    /// from zero — this only ever happens to whichever player is currently the
    /// invisible standby (opacity 0), since the visible/active one always crossfades
    /// away before it gets that far, so the restart hitch is never on screen.
    /// </summary>
    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (sender is MediaElement player)
        {
            player.Position = TimeSpan.Zero;
            player.Play();
        }
    }

    private void FirstPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        PlayerA.MediaOpened -= FirstPlayer_MediaOpened;
        _mediaDuration = PlayerA.NaturalDuration.HasTimeSpan ? PlayerA.NaturalDuration.TimeSpan : TimeSpan.Zero;
        _pollTimer.Start();
        MediaReady?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Offsets the standby player to start halfway through the clip, so at any given
    /// moment the two players are never near their own loop point at the same time —
    /// whichever one is about to end always has a partner that's mid-playback and
    /// ready to crossfade into immediately.
    /// </summary>
    private void StandbyPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        PlayerB.MediaOpened -= StandbyPlayer_MediaOpened;

        if (!_standbyOffsetApplied && PlayerB.NaturalDuration.HasTimeSpan)
        {
            _standbyOffsetApplied = true;
            PlayerB.Position = TimeSpan.FromSeconds(PlayerB.NaturalDuration.TimeSpan.TotalSeconds / 2.0);
        }
    }

    private void PollTimer_Tick(object? sender, EventArgs e)
    {
        if (_crossfadeInProgress || _activePlayer is null || _standbyPlayer is null || _mediaDuration == TimeSpan.Zero)
        {
            return;
        }

        var remaining = _mediaDuration - _activePlayer.Position;
        if (remaining.TotalSeconds <= CrossfadeLeadSeconds && remaining.TotalSeconds >= 0)
        {
            BeginCrossfade();
        }
    }

    private void BeginCrossfade()
    {
        if (_activePlayer is null || _standbyPlayer is null)
        {
            return;
        }

        _crossfadeInProgress = true;

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(CrossfadeDurationSeconds));
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(CrossfadeDurationSeconds));

        var outgoing = _activePlayer;
        var incoming = _standbyPlayer;

        fadeIn.Completed += (_, _) =>
        {
            (_activePlayer, _standbyPlayer) = (incoming, outgoing);
            _crossfadeInProgress = false;
        };

        outgoing.BeginAnimation(OpacityProperty, fadeOut);
        incoming.BeginAnimation(OpacityProperty, fadeIn);
    }
}
