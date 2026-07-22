using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace UI;

/// <summary>
/// Playback controls for whatever the BROWSER is playing: the console's mini-player, the
/// guide's transport buttons, the volume slider, and the now-playing toasts.
///
/// There is no separate music app. YouTube is the one player — put music on in it, leave,
/// and it keeps going. This class owns no web view and never navigates; it attaches to the
/// browser's, reports what is playing, and relays the console's buttons back to the page.
///
/// Background playback works because the browser is parked OFF-SCREEN rather than
/// collapsed when you leave it. Measured, not assumed: moved off-screen the page still
/// reports `visibilityState: "visible"`, so Chromium has no reason to suspend its audio;
/// collapsed, it reports `hidden`, which is exactly the state browsers use to pause media.
/// </summary>
public sealed class MusicPlayer
{
    /// <summary>
    /// The web view doing the playing — the BROWSER's, not one of ours.
    ///
    /// There is one YouTube on this console, not two. This class does not create or own a
    /// web view; it attaches to the browser's and reports what is playing so the console's
    /// own controls (mini-player, guide transport, volume, toasts) can drive it.
    /// </summary>
    private readonly WebView2 _web;

    private bool _initialised;

    /// <summary>Raised when the track, artist or playing state changes.</summary>
    public event EventHandler<MusicState>? StateChanged;

    /// <summary>Raised when a NEW track starts — for the on-screen toast.</summary>
    public event EventHandler<MusicState>? TrackChanged;

    /// <summary>The last state reported by the page.</summary>
    public MusicState Current { get; private set; } = MusicState.Idle;


    /// <summary>True once music has been started at least once this session.</summary>
    public bool HasSession { get; private set; }

    public MusicPlayer(WebView2 web)
    {
        _web = web;
    }

    /// <summary>
    /// Starts YouTube Music if it is not already running. Safe to call repeatedly.
    /// </summary>
    /// <summary>
    /// Starts watching the browser's web view. Call once its CoreWebView2 exists.
    ///
    /// Only observes and controls — it never navigates. What is on screen is the user's
    /// business; this reports what is playing and relays the console's transport buttons.
    /// </summary>
    public async Task AttachAsync()
    {
        if (_initialised || _web.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            var core = _web.CoreWebView2;
            core.WebMessageReceived += OnWebMessage;

            // Injected on every document so it survives YouTube's internal page changes,
            // which do not reload the document.
            await core.AddScriptToExecuteOnDocumentCreatedAsync(ReporterScript);

            // The current page was loaded before this was registered, so run it once
            // directly — otherwise nothing is reported until the next navigation.
            await core.ExecuteScriptAsync(ReporterScript);

            _initialised = true;
            HasSession = true;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException
                                   or InvalidOperationException)
        {
            // Watching is a convenience; the browser still works without it.
        }
    }

    /// <summary>Gives the page keyboard focus, so controller keys reach it while it is on screen.</summary>
    public void FocusPage() => _web.Focus();

    /// <summary>
    /// Sends a navigation key to the TV interface, which is driven by arrow keys exactly
    /// as a TV remote would.
    ///
    /// Uses the DevTools protocol rather than SendInput: WebView2 exposes no keyboard-send
    /// API, and an OS-level key only reaches the renderer if its child window holds focus,
    /// which it does not reliably do inside a WPF host. CDP arrives as a trusted event
    /// with no focus dependency — see NativeKeyboard for the full story.
    /// </summary>
    public async Task SendKeyAsync(NativeKeyboard.VirtualKey key)
    {
        var core = _web.CoreWebView2;
        if (core is null)
        {
            return;
        }

        var (domKey, code, virtualKey) = key switch
        {
            NativeKeyboard.VirtualKey.Left => ("ArrowLeft", "ArrowLeft", 37),
            NativeKeyboard.VirtualKey.Up => ("ArrowUp", "ArrowUp", 38),
            NativeKeyboard.VirtualKey.Right => ("ArrowRight", "ArrowRight", 39),
            NativeKeyboard.VirtualKey.Down => ("ArrowDown", "ArrowDown", 40),
            NativeKeyboard.VirtualKey.Return => ("Enter", "Enter", 13),
            NativeKeyboard.VirtualKey.Escape => ("Escape", "Escape", 27),
            _ => (string.Empty, string.Empty, 0),
        };

        if (virtualKey == 0)
        {
            return;
        }

        try
        {
            foreach (var type in new[] { "rawKeyDown", "keyUp" })
            {
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                    $"{{\"type\":\"{type}\",\"key\":\"{domKey}\",\"code\":\"{code}\"," +
                    $"\"windowsVirtualKeyCode\":{virtualKey},\"nativeVirtualKeyCode\":{virtualKey}}}");
            }
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException
                                   or InvalidOperationException)
        {
            // A dropped navigation key is not worth failing over.
        }
    }

    /// <summary>The underlying view, for sending controller keys while browsing music.</summary>
    public WebView2 View => _web;

    // ---------------- transport ----------------

    /// <summary>
    /// Toggles playback.
    ///
    /// Picks the video element that is actually PLAYING rather than the first one on the
    /// page: YouTube keeps several around (previews, thumbnails), and the first is
    /// frequently not the one making sound.
    /// </summary>
    public Task PlayPauseAsync() => RunAsync(
        """
        (function () {
          const vids = [...document.querySelectorAll('video')]
            .filter(v => v.readyState > 0 && v.duration > 0);
          if (!vids.length) return 'no-video';

          // The one that is playing, or failing that the longest — the real content
          // rather than a silent preview loop.
          const v = vids.find(x => !x.paused && !x.ended)
                 || vids.sort((a, b) => b.duration - a.duration)[0];

          if (v.paused) { v.play(); return 'played'; }
          v.pause();
          return 'paused';
        })();
        """);

    /// <summary>
    /// Stops a VIDEO that the user has just backed out of.
    ///
    /// Checks inside the page rather than trusting the console's music/video guess: if
    /// this is wrong it silences someone's music, so the decision is made where the real
    /// metadata is. Anything with an album, or short enough to be a track, is left alone.
    /// </summary>
    public Task StopIfVideoAsync() => RunAsync(
        """
        (function () {
          try {
            const vids = [...document.querySelectorAll('video')]
              .filter(x => x.readyState > 0 && x.duration > 0);
            const v = vids.find(x => !x.paused && !x.ended);
            if (!v) return 'nothing-playing';

            const md = navigator.mediaSession && navigator.mediaSession.metadata;
            const album = md && md.album ? String(md.album).trim() : '';

            // ONLY an album counts as music. Duration was tried as a secondary signal and
            // it does not work: a five-minute video is indistinguishable from a
            // five-minute song by length alone, so anything short was being kept alive.
            //
            // Backing out of a video should stop it — that is the ordinary expectation.
            // Deliberate background listening comes from HOLDING B, which is a different
            // gesture with its own check.
            if (album) return 'music-album';

            v.pause();
            return 'stopped';
          } catch (e) { return 'error'; }
        })();
        """);

    /// <summary>
    /// Pauses because something ELSE wants the speakers — a video, a game — remembering
    /// that it was playing so it can be brought back afterwards.
    ///
    /// Separate from PlayPauseAsync because a user-initiated pause and an automatic duck
    /// mean different things: only the automatic one should resume by itself. Pausing
    /// rather than lowering the volume, since a quiet song under a video is still two
    /// things competing for attention.
    /// </summary>
    public async Task DuckAsync()
    {
        if (!_initialised || !Current.IsPlaying)
        {
            return;
        }

        _resumeWhenFree = true;
        await RunAsync("const v=document.querySelector('video');if(v&&!v.paused){v.pause();}");
    }

    /// <summary>
    /// Resumes music that <see cref="DuckAsync"/> paused. Does nothing if the user had
    /// already paused it themselves — their choice outranks the console's.
    /// </summary>
    public async Task UnduckAsync()
    {
        if (!_initialised || !_resumeWhenFree)
        {
            return;
        }

        _resumeWhenFree = false;
        await RunAsync("const v=document.querySelector('video');if(v&&v.paused){v.play();}");
    }

    /// <summary>True when the console paused the music and owes it a resume.</summary>
    private bool _resumeWhenFree;

    public Task NextAsync() => ClickPlayerButtonAsync("next");

    public Task PreviousAsync() => ClickPlayerButtonAsync("previous");

    /// <summary>Seeks by a number of seconds, positive or negative. Clamped to the track.</summary>
    public Task SeekAsync(double seconds) => RunAsync(
        "const v=document.querySelector('video');" +
        $"if(v){{v.currentTime=Math.max(0,Math.min(v.duration||0,v.currentTime+({seconds})));}}");

    /// <summary>Current music volume, 0.0–1.0.</summary>
    public double Volume { get; private set; } = 0.65;

    /// <summary>
    /// Sets the music volume, 0.0–1.0.
    ///
    /// Remembered here as well as pushed to the page, because YouTube Music recreates its
    /// media element on every track change — without re-applying it, the volume would
    /// silently jump back to full on the next song.
    /// </summary>
    public Task SetVolumeAsync(double volume)
    {
        Volume = Math.Clamp(volume, 0, 1);
        return ApplyVolumeAsync();
    }

    private Task ApplyVolumeAsync() => RunAsync(
        "const v=document.querySelector('video');" +
        $"if(v){{v.volume={Volume.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)};}}");

    /// <summary>
    /// Skips a track.
    ///
    /// Goes through the MEDIA SESSION rather than clicking a button, because skipping is
    /// a playlist operation: an HTML video element has no "next", only the site knows
    /// what comes after this track. The Media Session API is how a TV remote's transport
    /// keys reach a page, so it is exactly the channel these buttons should use — and
    /// unlike a CSS selector it does not break when the site's markup changes.
    ///
    /// Falls back to the TV app's own keyboard shortcuts, which it handles natively.
    /// </summary>
    private Task ClickPlayerButtonAsync(string which) => RunAsync(BuildSkipScript(which));

    /// <summary>
    /// Builds the skip script, trying several routes in turn.
    ///
    /// Deliberately not a single CSS selector: the first version used
    /// `.next-button.ytmusic-player-bar`, which belongs to YouTube Music and matches
    /// nothing on the TV app, so every skip silently did nothing. Each route below is
    /// tried until one reports success, and the whole thing returns which one worked so a
    /// failure is visible rather than silent.
    /// </summary>
    private static string BuildSkipScript(string which)
    {
        var forward = which == "next";
        var mediaKey = forward ? "MediaTrackNext" : "MediaTrackPrevious";
        var keyCode = forward ? 176 : 177;

        // The TV app's own remote-control shortcuts, as a TV remote would send them.
        var arrowKey = forward ? "MediaFastForward" : "MediaRewind";

        return $$"""
            (function () {
              const results = [];

              // 1. A real media key. This is the channel a TV remote uses, and the TV app
              //    listens for it natively.
              try {
                for (const target of [document, document.body, window]) {
                  target.dispatchEvent(new KeyboardEvent('keydown', {
                    key: '{{mediaKey}}', code: '{{mediaKey}}',
                    keyCode: {{keyCode}}, which: {{keyCode}},
                    bubbles: true, cancelable: true, composed: true
                  }));
                }
                results.push('mediakey');
              } catch (e) { results.push('mediakey-failed'); }

              // 2. The page's own player API, which the TV app exposes on its player
              //    element. Direct and exact when it is there.
              try {
                const p = document.querySelector('#movie_player, .html5-video-player');
                if (p && typeof p.{{(forward ? "nextVideo" : "previousVideo")}} === 'function') {
                  p.{{(forward ? "nextVideo" : "previousVideo")}}();
                  results.push('playerapi');
                  return results.join(',');
                }
              } catch (e) { results.push('playerapi-failed'); }

              // 3. Any visible next/previous control, however it is marked up.
              try {
                const sel = '{{(forward ? ".ytp-next-button, .next-button, [aria-label*=\\\"Next\\\"]" : ".ytp-prev-button, .previous-button, [aria-label*=\\\"Previous\\\"]")}}';
                const b = document.querySelector(sel);
                if (b) { b.click(); results.push('clicked'); return results.join(','); }
              } catch (e) { results.push('click-failed'); }

              // 4. Nothing else worked: jump the media element itself. Not a true skip,
              //    but on a playlist the site advances when a track ends.
              try {
                const v = document.querySelector('video');
                if (v && {{forward.ToString().ToLowerInvariant()}} && v.duration) {
                  v.currentTime = v.duration;
                  results.push('seek-end');
                } else if (v) {
                  v.currentTime = 0;
                  results.push('restart');
                }
              } catch (e) { results.push('seek-failed'); }

              return results.join(',');
            })();
            """;
    }

    /// <summary>
    /// The result of the last transport command, for diagnosing why a button did nothing.
    /// The scripts return which route worked, so this says whether the page was reached
    /// at all rather than leaving a dead button unexplained.
    /// </summary>
    public string LastCommandResult { get; private set; } = string.Empty;

    private async Task RunAsync(string script)
    {
        // Only the view matters, not _initialised: the reporter may have failed to
        // install while the page itself is perfectly able to take commands. Requiring
        // both meant a failed attach silently disabled every transport button.
        if (_web.CoreWebView2 is null)
        {
            LastCommandResult = "no web view";
            return;
        }

        try
        {
            LastCommandResult = await _web.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException
                                   or InvalidOperationException)
        {
            LastCommandResult = e.GetType().Name;
            // A dropped command is not worth tearing the console down for.
        }
    }

    // ---------------- state reporting ----------------

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        MusicState state;

        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            state = new MusicState(
                Title: Text(root, "title"),
                Artist: Text(root, "artist"),
                ArtworkUrl: Text(root, "artwork"),
                IsPlaying: root.TryGetProperty("playing", out var p) && p.GetBoolean(),
                IsMusic: root.TryGetProperty("music", out var m) && m.GetBoolean(),
                IsAd: root.TryGetProperty("ad", out var a) && a.GetBoolean());

        }
        catch (JsonException)
        {
            return;
        }

        var trackChanged = state.Title != Current.Title || state.IsAd != Current.IsAd;
        Current = state;

        StateChanged?.Invoke(this, state);

        if (trackChanged)
        {
            // The site builds a fresh media element per track, which starts at full
            // volume — re-apply the console's setting or every song begins at 100%.
            _ = ApplyVolumeAsync();
        }

        // Only announce something worth announcing — a pause is not a new track.
        if (trackChanged && !string.IsNullOrWhiteSpace(state.Title))
        {
            TrackChanged?.Invoke(this, state);
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// Runs inside the page and reports what is playing.
    ///
    /// Track details come from the Media Session API, which YouTube Music populates for
    /// the OS media overlay — far more stable than scraping the player's markup, which
    /// changes with their redesigns. Ad detection has no such API and does read the DOM,
    /// so it is the fragile part: it is written to fail quietly and simply report no ad
    /// rather than break playback if the markup moves.
    ///
    /// Polls rather than using events because Media Session exposes no change
    /// notification — metadata is a property the page writes whenever it likes.
    /// </summary>
    private const string ReporterScript = """
        (function () {
          if (window.__consoleMusicReporter) return;
          window.__consoleMusicReporter = true;

          let last = '';

          function read() {
            // The video that is actually PLAYING, not merely the first on the page:
            // YouTube keeps silent previews and thumbnails around, and the first element
            // is frequently not the one making sound.
            const vids = [...document.querySelectorAll('video')]
              .filter(x => x.readyState > 0 && x.duration > 0);
            const v = vids.find(x => !x.paused && !x.ended)
                   || vids.sort((a, b) => b.duration - a.duration)[0]
                   || null;

            const md = navigator.mediaSession && navigator.mediaSession.metadata;

            // Ads: the TV app marks its player while one runs. Wrapped so a markup change
            // costs us the ad flag rather than the whole reporter.
            let ad = false;
            try {
              ad = !!document.querySelector('.ad-showing, .ad-interrupting')
                || !!document.querySelector('.ytp-ad-player-overlay, ytmusic-ad-slot-renderer');
            } catch (e) { ad = false; }

            // Artwork, in order of preference. Media Session is the cleanest source but
            // the TV app does not always populate it, so fall back to deriving the
            // thumbnail from the video id — YouTube serves those at a fixed URL, which
            // works for anything playing.
            let artwork = '';

            try {
              if (md && md.artwork && md.artwork.length) {
                artwork = md.artwork[md.artwork.length - 1].src;
              }
            } catch (e) { artwork = ''; }

            if (!artwork) {
              try {
                // The video id is in the URL on a watch page, or on the player element.
                let id = new URLSearchParams(location.search).get('v');

                if (!id) {
                  const p = document.querySelector('#movie_player, .html5-video-player');
                  if (p && typeof p.getVideoData === 'function') {
                    const d = p.getVideoData();
                    id = d && d.video_id ? d.video_id : null;
                  }
                }

                if (!id) {
                  // The TV app keeps it on the player's own data attributes.
                  const el = document.querySelector('[video-id]');
                  if (el) id = el.getAttribute('video-id');
                }

                if (id) {
                  artwork = 'https://i.ytimg.com/vi/' + id + '/hqdefault.jpg';
                }
              } catch (e) { /* leave blank rather than break the report */ }
            }

            if (!artwork) {
              try {
                // Last resort: the poster frame the player itself is showing.
                const poster = v && v.poster ? v.poster : '';
                if (poster && poster.indexOf('data:') !== 0) artwork = poster;
              } catch (e) { /* blank */ }
            }

            // IS THIS MUSIC, OR A VIDEO?
            //
            // Background playback is for music only: a video whose picture is gone but
            // whose audio carries on is just a nuisance. YouTube does not label content
            // as "music" directly, so this reads the signals it does give:
            //
            //  - Media Session ALBUM is populated for music tracks and empty for ordinary
            //    videos. This is the strongest signal by far.
            //  - An ARTIST distinct from the channel name points at a music release.
            //  - Duration: music is minutes, not the half-hour-plus of long-form video.
            //    Only used as a tiebreak, never on its own — plenty of videos are short.
            // ALBUM ONLY. Duration was tried as a secondary signal and does not work: a
            // five-minute video and a five-minute song are the same length, so anything
            // short was being treated as music and kept playing after the user left.
            //
            // An album is the one thing YouTube populates for music and leaves empty for
            // ordinary video, so it is the only signal worth trusting. Being strict here
            // is the right way round: the cost of a false negative is that music stops
            // when you leave, which is merely disappointing. The cost of a false positive
            // is a video playing to nobody, which is the bug being fixed.
            let music = false;
            try {
              const album = md && md.album ? String(md.album).trim() : '';
              music = !!album;
            } catch (e) { music = false; }

            return {
              title: md ? (md.title || '') : '',
              artist: md ? (md.artist || '') : '',
              artwork: artwork,
              playing: !!(v && !v.paused && !v.ended),
              music: music,
              ad: ad
            };
          }

          setInterval(function () {
            try {
              const state = read();
              const json = JSON.stringify(state);
              if (json !== last) {
                last = json;
                window.chrome.webview.postMessage(json);
              }
            } catch (e) { /* never let reporting break playback */ }
          }, 900);
        })();
        """;
}

/// <summary>What is playing right now.</summary>
public readonly record struct MusicState(
    string Title,
    string Artist,
    string ArtworkUrl,
    bool IsPlaying,
    bool IsMusic,
    bool IsAd)
{
    public static MusicState Idle =>
        new(string.Empty, string.Empty, string.Empty, false, false, false);

    /// <summary>True when there is something worth showing in the mini-player.</summary>
    public bool HasTrack => !string.IsNullOrWhiteSpace(Title) || IsAd;

    /// <summary>
    /// True when this may keep playing after the user leaves.
    ///
    /// Music only. A video's audio continuing with the picture gone is a nuisance rather
    /// than a feature, so anything not recognised as music stops when you leave it.
    /// </summary>
    public bool CanPlayInBackground => IsPlaying && IsMusic;
}
