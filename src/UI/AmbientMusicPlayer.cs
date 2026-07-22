using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

namespace UI;

/// <summary>
/// Subtle looping background music for the dashboard, played independently of the
/// ambient video (whose two crossfading MediaElements are deliberately muted — having
/// either carry audio would double up and stutter at every crossfade).
///
/// GAPLESS LOOPING — why this is more involved than "rewind on MediaEnded":
///
/// 1. A single MediaPlayer looping via MediaEnded -> Position=0 -> Play() leaves an
///    audible hole. MediaEnded only fires AFTER the track has finished, and the seek
///    plus restart costs tens of milliseconds. You hear a gap every loop.
/// 2. MP3 makes it worse: the format bakes in encoder delay (this source measured
///    ~25 ms of it via ffprobe start_time), so playback resumes late every single
///    time. The asset is therefore WAV, which has no such delay.
///
/// The fix here mirrors the one used for the ambient video: TWO players alternating.
/// While one plays, the other sits pre-loaded and pre-seeked to zero, primed. A timer
/// starts the standby player a hair BEFORE the active one ends, so the two overlap
/// briefly rather than leaving a hole. The source file's own tail is already
/// crossfaded into its head (ffmpeg, constant-power qsin), so that overlap lands on
/// matching audio and is inaudible.
///
/// If no music file is present the whole thing stays silent and inert: Start() returns
/// without doing anything, so a missing asset can never throw or block startup.
/// </summary>
public sealed class AmbientMusicPlayer
{
    /// <summary>
    /// Filenames tried in order, relative to Assets/. WAV first — it is the only one
    /// of these formats without encoder delay, which matters for a seamless loop.
    /// </summary>
    private static readonly string[] CandidateFileNames =
    {
        "ambient_music.wav",
        "ambient_music.mp3",
        "ambient_music.m4a",
    };

    /// <summary>
    /// Resting volume. Deliberately very low — this is ambience sitting under a UI,
    /// not a music player, and the source track is mastered loud (peaks near 0 dB).
    /// </summary>
    public const double DefaultVolume = 0.06;

    /// <summary>
    /// How early the standby player starts relative to the active one ending. Long
    /// enough to absorb timer jitter and MediaPlayer's start latency, short enough
    /// that the overlap sits inside the file's own crossfaded tail.
    /// </summary>
    private static readonly TimeSpan HandoffLead = TimeSpan.FromMilliseconds(450);

    private readonly MediaPlayer _playerA = new();
    private readonly MediaPlayer _playerB = new();
    private readonly DispatcherTimer _handoffTimer = new();

    private readonly bool _hasTrack;
    private bool _isAPlaying = true;
    private TimeSpan _trackDuration = TimeSpan.Zero;
    private double _targetVolume = DefaultVolume;
    private DispatcherTimer? _fadeTimer;

    /// <summary>True while paused for a game launch — see FadeOut/FadeIn.</summary>
    private bool _isSuspended;

    /// <summary>Where playback was paused, so returning to the dashboard resumes from that exact point.</summary>
    private TimeSpan _resumePosition = TimeSpan.Zero;

    public bool IsPlaying { get; private set; }

    private MediaPlayer Active => _isAPlaying ? _playerA : _playerB;
    private MediaPlayer Standby => _isAPlaying ? _playerB : _playerA;

    public AmbientMusicPlayer()
    {
        var path = ResolveTrackPath();
        if (path is null)
        {
            return;
        }

        _hasTrack = true;
        var uri = new Uri(path, UriKind.Absolute);

        _playerA.Open(uri);
        _playerB.Open(uri);
        _playerA.Volume = 0;
        _playerB.Volume = 0;

        // Duration is only known once the media has been opened; capture it from
        // whichever player reports first so the handoff timer can be scheduled.
        _playerA.MediaOpened += (_, _) =>
        {
            if (_playerA.NaturalDuration.HasTimeSpan)
            {
                _trackDuration = _playerA.NaturalDuration.TimeSpan;
            }
        };

        _handoffTimer.Tick += (_, _) => Handoff();
    }

    private static string? ResolveTrackPath()
    {
        foreach (var fileName in CandidateFileNames)
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Starts playback, fading up from silence so it never punches in abruptly at startup.</summary>
    public void Start()
    {
        if (!_hasTrack || IsPlaying)
        {
            return;
        }

        IsPlaying = true;

        Standby.Position = TimeSpan.Zero;
        Active.Position = TimeSpan.Zero;
        Active.Play();

        FadeTo(_targetVolume, TimeSpan.FromSeconds(3));
        ScheduleHandoff();
    }

    /// <summary>
    /// Arms the timer to fire slightly before the active player reaches the end of the
    /// track. Duration may not be known yet on the very first call (MediaOpened is
    /// async), in which case this retries shortly.
    /// </summary>
    private void ScheduleHandoff()
    {
        // Suspended for a game launch — no point scheduling a loop hand-off for a
        // player that is paused. FadeIn re-arms this on resume.
        if (!IsPlaying || _isSuspended)
        {
            return;
        }

        if (_trackDuration <= TimeSpan.Zero)
        {
            _handoffTimer.Interval = TimeSpan.FromMilliseconds(250);
            _handoffTimer.Start();
            return;
        }

        var remaining = _trackDuration - Active.Position - HandoffLead;
        if (remaining < TimeSpan.FromMilliseconds(50))
        {
            remaining = TimeSpan.FromMilliseconds(50);
        }

        _handoffTimer.Interval = remaining;
        _handoffTimer.Start();
    }

    /// <summary>
    /// Swaps to the standby player. It starts from zero while the outgoing player is
    /// still finishing its (already crossfaded) tail, so there is no silent gap. The
    /// outgoing player is then stopped and re-armed at zero for the next cycle.
    /// </summary>
    private void Handoff()
    {
        _handoffTimer.Stop();

        if (!IsPlaying)
        {
            return;
        }

        // Duration still unknown — keep waiting rather than looping blind.
        if (_trackDuration <= TimeSpan.Zero)
        {
            ScheduleHandoff();
            return;
        }

        var outgoing = Active;
        var incoming = Standby;

        incoming.Volume = outgoing.Volume;
        incoming.Position = TimeSpan.Zero;
        incoming.Play();

        _isAPlaying = !_isAPlaying;

        // Let the outgoing tail finish underneath the new head, then park it ready
        // for its next turn.
        var stopDelay = new DispatcherTimer { Interval = HandoffLead + TimeSpan.FromMilliseconds(200) };
        stopDelay.Tick += (_, _) =>
        {
            stopDelay.Stop();
            outgoing.Stop();
            outgoing.Position = TimeSpan.Zero;
        };
        stopDelay.Start();

        ScheduleHandoff();
    }

    /// <summary>
    /// Fades down to silence and then PAUSES, used when a game launches. Pausing (not
    /// just muting) matters for two reasons: the track resumes from exactly where it
    /// left off when the user returns, and nothing is decoding audio while a game has
    /// the machine — which is the whole point of a console that gets out of the way.
    /// </summary>
    public void FadeOut(TimeSpan? duration = null)
    {
        if (!_hasTrack || _isSuspended)
        {
            return;
        }

        var fade = duration ?? TimeSpan.FromSeconds(0.8);
        FadeTo(0, fade);

        // Pause only once the fade has actually finished, otherwise the audio cuts
        // abruptly instead of easing away.
        var pauseAfterFade = new DispatcherTimer { Interval = fade + TimeSpan.FromMilliseconds(60) };
        pauseAfterFade.Tick += (_, _) =>
        {
            pauseAfterFade.Stop();

            if (!_isSuspended)
            {
                return;
            }

            _resumePosition = Active.Position;
            _playerA.Pause();
            _playerB.Pause();
            _handoffTimer.Stop();
        };

        _isSuspended = true;
        pauseAfterFade.Start();
    }

    /// <summary>
    /// Resumes from the exact position playback was suspended at, fading back up to
    /// the resting volume — used when returning to the dashboard.
    /// </summary>
    public void FadeIn(TimeSpan? duration = null)
    {
        if (!_hasTrack || !_isSuspended)
        {
            return;
        }

        _isSuspended = false;

        Active.Position = _resumePosition;
        Active.Play();

        FadeTo(_targetVolume, duration ?? TimeSpan.FromSeconds(1.5));

        // Re-arm the loop hand-off relative to where playback has resumed.
        ScheduleHandoff();
    }

    /// <summary>Sets the resting volume (0-1) and moves to it.</summary>
    public void SetVolume(double volume)
    {
        _targetVolume = Math.Clamp(volume, 0, 1);
        FadeTo(_targetVolume, TimeSpan.FromSeconds(0.3));
    }

    public void Stop()
    {
        if (!_hasTrack)
        {
            return;
        }

        IsPlaying = false;
        _handoffTimer.Stop();
        _fadeTimer?.Stop();
        _playerA.Stop();
        _playerB.Stop();
    }

    /// <summary>
    /// Steps volume toward a target over time, applied to BOTH players so a fade that
    /// straddles a handoff stays consistent. MediaPlayer.Volume is a plain CLR
    /// property, not a DependencyProperty, so BeginAnimation cannot target it — a
    /// stepped timer is the workaround (same approach used for ScrollViewer offsets
    /// elsewhere in this UI).
    /// </summary>
    private void FadeTo(double target, TimeSpan duration)
    {
        if (!_hasTrack)
        {
            return;
        }

        _fadeTimer?.Stop();

        var start = Active.Volume;
        var steps = Math.Max(1, (int)(duration.TotalMilliseconds / 40));
        var step = 0;

        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _fadeTimer.Tick += (_, _) =>
        {
            step++;
            var progress = Math.Min(1.0, step / (double)steps);
            var volume = start + (target - start) * progress;

            _playerA.Volume = volume;
            _playerB.Volume = volume;

            if (progress >= 1.0)
            {
                _fadeTimer!.Stop();
            }
        };

        _fadeTimer.Start();
    }
}
