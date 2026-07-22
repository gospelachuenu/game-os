using System.Text.Json;
using System.Text.Json.Serialization;

namespace UI;

/// <summary>
/// One thing the console can show and play: a playlist, album, or mix.
/// </summary>
public sealed class MusicItem
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string ArtworkUrl { get; init; }

    /// <summary>
    /// What to play. YouTube Music addresses content two ways and both are needed: a
    /// browse id for a playlist or album, a video id for a single track.
    /// </summary>
    public string? PlaylistId { get; init; }
    public string? VideoId { get; init; }

    private bool _isFocused;

    public bool IsFocused
    {
        get => _isFocused;
        set
        {
            if (_isFocused != value)
            {
                _isFocused = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsFocused)));
            }
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>A titled row of items, as the music home screen presents them.</summary>
public sealed class MusicShelf
{
    public required string Title { get; init; }
    public required IReadOnlyList<MusicItem> Items { get; init; }
}

/// <summary>
/// Reads YouTube Music's catalogue by calling its own internal API from inside the
/// already-signed-in page.
///
/// WHY FROM INSIDE THE PAGE RATHER THAN AN HTTP CLIENT
///
/// The internal API needs the session cookies, a visitor id, and a set of client headers
/// that the page already has. Calling `fetch` from within the page inherits all of it, so
/// there is no second sign-in, no cookie handling, and no token to keep fresh — if the
/// user is signed in, this works.
///
/// This is an UNOFFICIAL API. It is what every YouTube Music client library uses and has
/// been stable for years, but Google owes it no compatibility, so everything here is
/// written to degrade to an empty list rather than throw.
/// </summary>
public static class MusicCatalogue
{
    /// <summary>
    /// Fetches the home shelves — recently played, your mixes, moods.
    ///
    /// The response is deeply nested and its exact shape varies by account and rollout,
    /// so the script walks it defensively rather than indexing fixed paths: anything it
    /// cannot understand is skipped instead of breaking the whole screen.
    /// </summary>
    public const string HomeScript = """
        (async function () {
          try {
            const ctx = window.yt && window.yt.config_ ? window.yt.config_ : {};
            const key = ctx.INNERTUBE_API_KEY;
            const client = ctx.INNERTUBE_CONTEXT ? ctx.INNERTUBE_CONTEXT.client : null;
            if (!key || !client) return JSON.stringify({ error: 'no-context' });

            const res = await fetch('/youtubei/v1/browse?key=' + key + '&prettyPrint=false', {
              method: 'POST',
              credentials: 'include',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({
                context: { client: client },
                browseId: 'FEmusic_home'
              })
            });

            const data = await res.json();

            // Pull every "shelf" of items out of the response wherever it appears.
            const shelves = [];

            function thumb(t) {
              try {
                const list = t.thumbnail.thumbnails;
                return list[list.length - 1].url;
              } catch (e) { return ''; }
            }

            function runsText(o) {
              try {
                return o.runs.map(r => r.text).join('');
              } catch (e) { return ''; }
            }

            function readItem(node) {
              const r = node.musicTwoRowItemRenderer || node.musicResponsiveListItemRenderer;
              if (!r) return null;

              const title = runsText(r.title) || '';
              if (!title) return null;

              let playlistId = null, videoId = null;
              try {
                const nav = r.navigationEndpoint || r.title.runs[0].navigationEndpoint;
                if (nav.watchEndpoint) {
                  videoId = nav.watchEndpoint.videoId || null;
                  playlistId = nav.watchEndpoint.playlistId || null;
                } else if (nav.browseEndpoint) {
                  const id = nav.browseEndpoint.browseId || '';
                  // Album/playlist browse ids carry a VL/MPRE prefix; strip VL to get
                  // the playlist id the player accepts.
                  playlistId = id.startsWith('VL') ? id.slice(2) : id;
                }
              } catch (e) { /* item without a target — skipped below */ }

              if (!playlistId && !videoId) return null;

              return {
                title: title,
                subtitle: runsText(r.subtitle) || '',
                artwork: thumb(r.thumbnailRenderer
                  ? r.thumbnailRenderer.musicThumbnailRenderer
                  : r.thumbnail.musicThumbnailRenderer),
                playlistId: playlistId,
                videoId: videoId
              };
            }

            function walk(node) {
              if (!node || typeof node !== 'object') return;

              if (node.musicCarouselShelfRenderer) {
                const shelf = node.musicCarouselShelfRenderer;
                let name = '';
                try {
                  name = runsText(shelf.header.musicCarouselShelfBasicHeaderRenderer.title);
                } catch (e) { name = ''; }

                const items = (shelf.contents || [])
                  .map(readItem)
                  .filter(x => x !== null);

                if (items.length) {
                  shelves.push({ title: name || 'For you', items: items });
                }
                return;
              }

              for (const k in node) walk(node[k]);
            }

            walk(data);
            return JSON.stringify({ shelves: shelves.slice(0, 6) });
          } catch (e) {
            return JSON.stringify({ error: String(e) });
          }
        })();
        """;

    /// <summary>
    /// Starts playback of a playlist or a single track.
    ///
    /// Navigates the hidden page rather than calling a player API: YouTube Music's player
    /// is bound to its own routing, and driving it any other way leaves the queue and the
    /// now-playing metadata out of step with what is actually sounding.
    /// </summary>
    public static string PlayScript(string? playlistId, string? videoId)
    {
        var target = !string.IsNullOrEmpty(videoId)
            ? $"/watch?v={Escape(videoId)}" + (string.IsNullOrEmpty(playlistId) ? "" : $"&list={Escape(playlistId)}")
            : $"/playlist?list={Escape(playlistId)}";

        // The site is a single-page app: pushing through its own router keeps playback
        // alive, where a full navigation would reload everything and stop the music.
        return $$"""
            (function () {
              try {
                const app = document.querySelector('ytmusic-app');
                if (app && app.navigate) { app.navigate('{{target}}'); return 'router'; }
                window.location.href = '{{target}}';
                return 'location';
              } catch (e) { return 'error:' + e; }
            })();
            """;
    }

    /// <summary>
    /// Presses play once a playlist page has loaded. Needed because navigating to a
    /// playlist shows it rather than starting it.
    /// </summary>
    public const string PressPlayScript = """
        (function () {
          try {
            const btn = document.querySelector('ytmusic-play-button-renderer, .play-button');
            if (btn) { btn.click(); return 'clicked'; }
            const v = document.querySelector('video');
            if (v && v.paused) { v.play(); return 'video'; }
            return 'none';
          } catch (e) { return 'error:' + e; }
        })();
        """;

    private static string Escape(string? value) =>
        Uri.EscapeDataString(value ?? string.Empty);

    /// <summary>
    /// Parses what <see cref="HomeScript"/> returned. Never throws — a catalogue that
    /// cannot be read shows as empty, which the screen reports honestly.
    /// </summary>
    public static IReadOnlyList<MusicShelf> ParseHome(string json)
    {
        try
        {
            // ExecuteScriptAsync returns the value JSON-encoded, so a string result
            // arrives as a quoted string that has to be unwrapped first.
            var unwrapped = JsonSerializer.Deserialize<string>(json);
            if (string.IsNullOrWhiteSpace(unwrapped))
            {
                return Array.Empty<MusicShelf>();
            }

            var payload = JsonSerializer.Deserialize<HomePayload>(unwrapped);
            if (payload?.Shelves is null)
            {
                return Array.Empty<MusicShelf>();
            }

            return payload.Shelves
                .Select(s => new MusicShelf
                {
                    Title = s.Title ?? "For you",
                    Items = (s.Items ?? new List<ItemPayload>())
                        .Select(i => new MusicItem
                        {
                            Title = i.Title ?? string.Empty,
                            Subtitle = i.Subtitle ?? string.Empty,
                            ArtworkUrl = i.Artwork ?? string.Empty,
                            PlaylistId = i.PlaylistId,
                            VideoId = i.VideoId,
                        })
                        .Where(i => !string.IsNullOrWhiteSpace(i.Title))
                        .ToList(),
                })
                .Where(s => s.Items.Count > 0)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<MusicShelf>();
        }
    }

    private sealed class HomePayload
    {
        [JsonPropertyName("shelves")] public List<ShelfPayload>? Shelves { get; set; }
    }

    private sealed class ShelfPayload
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("items")] public List<ItemPayload>? Items { get; set; }
    }

    private sealed class ItemPayload
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("subtitle")] public string? Subtitle { get; set; }
        [JsonPropertyName("artwork")] public string? Artwork { get; set; }
        [JsonPropertyName("playlistId")] public string? PlaylistId { get; set; }
        [JsonPropertyName("videoId")] public string? VideoId { get; set; }
    }
}
