using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace UI;

/// <summary>
/// Live, looping vector animations for the "what's new" screen — built in WPF rather than
/// recorded, so each feature is illustrated by smooth 60fps motion in the console's own
/// style with no video file to ship, capture, or keep in sync with the real UI.
///
/// A patch note asks for one by putting <c>anim:name</c> in its image field (e.g.
/// <c>"image": "anim:browser"</c>). The renderer maps the name to a builder here. These
/// are DELIBERATELY illustrative, not literal screenshots: they show what a feature is
/// about — a browser opening, music starting — the way a product-launch animation does,
/// not a frame-accurate capture.
/// </summary>
public static class PatchNoteAnimations
{
    /// <summary>The prefix that marks a patch-note "image" as a built animation.</summary>
    public const string Prefix = "anim:";

    /// <summary>True if this media reference names a built animation rather than a file.</summary>
    public static bool IsAnimation(string? mediaPath) =>
        mediaPath is not null && mediaPath.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the animation for a reference, or null if the name is unknown — in which
    /// case the caller shows its normal placeholder rather than a blank.
    /// </summary>
    public static FrameworkElement? Build(string mediaPath)
    {
        var name = mediaPath[Prefix.Length..].Trim().ToLowerInvariant();

        return name switch
        {
            "browser" => BuildBrowser(),
            "youtube" => BuildYouTube(),
            "music" => BuildMusic(),
            "keyboard" => BuildKeyboard(),
            _ => null,
        };
    }

    private static readonly Color Accent = Color.FromRgb(0xA8, 0xE0, 0x3B);
    private static readonly Color Ink = Color.FromRgb(0xF2, 0xF4, 0xF0);
    private static readonly Color Dim = Color.FromRgb(0x5A, 0x60, 0x6A);

    /// <summary>A dark rounded stage every animation sits on, so they share one look.</summary>
    private static Grid Stage(out Grid content)
    {
        var root = new Grid { ClipToBounds = true };

        root.Children.Add(new Border
        {
            Background = new LinearGradientBrush(
                Color.FromRgb(0x10, 0x14, 0x1A),
                Color.FromRgb(0x08, 0x0A, 0x0F),
                new Point(0, 0), new Point(1, 1)),
        });

        content = new Grid { Margin = new Thickness(0) };
        root.Children.Add(content);
        return root;
    }

    private static SolidColorBrush Brush(Color c) => new(c);

    /// <summary>
    /// Browser: a window frame draws in, an address bar fills, a page settles below it.
    /// Reads as "a browser opened", which is the feature.
    /// </summary>
    private static FrameworkElement BuildBrowser()
    {
        var root = Stage(out var content);

        var window = new Border
        {
            Width = 240,
            Height = 150,
            CornerRadius = new CornerRadius(10),
            Background = Brush(Color.FromRgb(0x0E, 0x10, 0x16)),
            BorderBrush = Brush(Color.FromRgb(0x2A, 0x2F, 0x3A)),
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };

        var scale = new ScaleTransform(0.9, 0.9);
        window.RenderTransform = scale;

        var inner = new StackPanel { Margin = new Thickness(14) };
        window.Child = inner;

        // A toolbar row with an address bar that "types".
        var bar = new Border
        {
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = Brush(Color.FromRgb(0x1A, 0x1E, 0x26)),
            Margin = new Thickness(0, 0, 0, 12),
        };
        var addr = new Rectangle
        {
            Height = 6,
            RadiusX = 3,
            RadiusY = 3,
            Fill = Brush(Accent),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(10, 0, 10, 0),
            Width = 0,
        };
        bar.Child = new Grid { Children = { addr } };
        inner.Children.Add(bar);

        // Page content lines that fade in.
        var page = new StackPanel { Opacity = 0 };
        foreach (var w in new[] { 200.0, 170, 190, 120 })
        {
            page.Children.Add(new Rectangle
            {
                Height = 8,
                RadiusX = 4,
                RadiusY = 4,
                Width = w,
                Fill = Brush(Color.FromRgb(0x2A, 0x2F, 0x3A)),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }
        inner.Children.Add(page);

        content.Children.Add(window);

        // The loop: window pops in, address bar fills, page fades, hold, reset.
        root.Loaded += (_, _) =>
        {
            var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

            AddDouble(sb, scale, "(ScaleTransform.ScaleX)", 0.9, 1, 0.0, 0.4, new BackEase { EasingMode = EasingMode.EaseOut });
            AddDouble(sb, scale, "(ScaleTransform.ScaleY)", 0.9, 1, 0.0, 0.4, new BackEase { EasingMode = EasingMode.EaseOut });
            AddDouble(sb, addr, "(FrameworkElement.Width)", 0, 200, 0.5, 0.6);
            AddDouble(sb, page, "(UIElement.Opacity)", 0, 1, 1.1, 0.5);

            // Hold, then fade the whole thing out and let RepeatBehavior restart it.
            AddDouble(sb, window, "(UIElement.Opacity)", 1, 0, 3.4, 0.4);

            sb.Duration = TimeSpan.FromSeconds(4);
            sb.Begin();
        };

        return root;
    }

    /// <summary>
    /// YouTube: the red tile grows, a play triangle pulses. "YouTube is here".
    /// </summary>
    private static FrameworkElement BuildYouTube()
    {
        var root = Stage(out var content);

        var tile = new Border
        {
            Width = 150,
            Height = 96,
            CornerRadius = new CornerRadius(14),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Background = new LinearGradientBrush(
                Color.FromRgb(0xE6, 0x2C, 0x2C),
                Color.FromRgb(0x9B, 0x12, 0x12),
                new Point(0, 0), new Point(1, 1)),
        };

        var scale = new ScaleTransform(0.6, 0.6);
        tile.RenderTransform = scale;

        // A play triangle centred on the tile.
        var play = new Polygon
        {
            Points = new PointCollection { new(0, 0), new(0, 26), new(22, 13) },
            Fill = Brush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var playScale = new ScaleTransform(1, 1);
        play.RenderTransform = playScale;
        tile.Child = play;

        content.Children.Add(tile);

        root.Loaded += (_, _) =>
        {
            var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

            AddDouble(sb, scale, "(ScaleTransform.ScaleX)", 0.6, 1, 0.0, 0.5, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 });
            AddDouble(sb, scale, "(ScaleTransform.ScaleY)", 0.6, 1, 0.0, 0.5, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 });

            // Play button gives a little heartbeat pulse.
            AddDouble(sb, playScale, "(ScaleTransform.ScaleX)", 1, 1.2, 1.0, 0.3, autoReverse: true);
            AddDouble(sb, playScale, "(ScaleTransform.ScaleY)", 1, 1.2, 1.0, 0.3, autoReverse: true);

            AddDouble(sb, tile, "(UIElement.Opacity)", 1, 0, 3.4, 0.4);

            sb.Duration = TimeSpan.FromSeconds(4);
            sb.Begin();
        };

        return root;
    }

    /// <summary>
    /// Music: a now-playing bar slides up, and equaliser bars bounce. "Music keeps playing".
    /// </summary>
    private static FrameworkElement BuildMusic()
    {
        var root = Stage(out var content);

        // A proper now-playing player: album art, a title/artist, live equaliser, and a
        // transport row. Centred and fully visible at rest — the motion is a gentle
        // entrance plus the ever-running equaliser, not something that starts off-screen.
        var bar = new Border
        {
            Width = 250,
            Height = 108,
            CornerRadius = new CornerRadius(14),
            Background = Brush(Color.FromRgb(0x0E, 0x10, 0x16)),
            BorderBrush = Brush(Color.FromRgb(0x2A, 0x2F, 0x3A)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Padding = new Thickness(14),
        };
        var scale = new ScaleTransform(0.94, 0.94);
        bar.RenderTransform = scale;

        var layout = new StackPanel();
        bar.Child = layout;

        // Top: album art + title/artist + equaliser.
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        layout.Children.Add(top);

        top.Children.Add(new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(7),
            Background = new LinearGradientBrush(Accent, Color.FromRgb(0x39, 0x11, 0x5C), 45),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 96 };
        meta.Children.Add(new Rectangle
        {
            Height = 7, RadiusX = 3, RadiusY = 3, Width = 84,
            Fill = Brush(Ink), HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
        });
        meta.Children.Add(new Rectangle
        {
            Height = 6, RadiusX = 3, RadiusY = 3, Width = 56,
            Fill = Brush(Dim), HorizontalAlignment = HorizontalAlignment.Left,
        });
        top.Children.Add(meta);

        // Equaliser, right of the metadata.
        var eq = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        var bars = new List<Rectangle>();
        foreach (var _ in Enumerable.Range(0, 4))
        {
            var r = new Rectangle
            {
                Width = 4, Height = 10, RadiusX = 2, RadiusY = 2,
                Fill = Brush(Accent),
                Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            bars.Add(r);
            eq.Children.Add(r);
        }
        top.Children.Add(eq);

        // Transport row: previous / play-pause / next.
        var transport = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0),
        };
        layout.Children.Add(transport);

        Border TransportKey(string glyph, bool primary)
        {
            var b = new Border
            {
                Width = primary ? 40 : 34,
                Height = 26,
                CornerRadius = new CornerRadius(7),
                Background = Brush(primary ? Color.FromArgb(0x26, 0xA8, 0xE0, 0x3B)
                                           : Color.FromRgb(0x1A, 0x1E, 0x26)),
                BorderThickness = new Thickness(1.5),
                BorderBrush = Brush(primary ? Accent : Colors.Transparent),
                Margin = new Thickness(5, 0, 5, 0),
            };
            b.Child = new TextBlock
            {
                Text = glyph,
                FontSize = 12,
                Foreground = Brush(primary ? Accent : Ink),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return b;
        }

        transport.Children.Add(TransportKey("⏮", false));  // previous
        var playKey = TransportKey("⏸", true);             // pause (playing)
        transport.Children.Add(playKey);
        transport.Children.Add(TransportKey("⏭", false));  // next

        content.Children.Add(bar);

        root.Loaded += (_, _) =>
        {
            // A gentle settle-in, so the player is present immediately and simply eases
            // to full size rather than flying in from off-screen (the old blank cause).
            var intro = new Storyboard();
            AddDouble(intro, scale, "(ScaleTransform.ScaleX)", 0.94, 1, 0.0, 0.45, new BackEase { EasingMode = EasingMode.EaseOut });
            AddDouble(intro, scale, "(ScaleTransform.ScaleY)", 0.94, 1, 0.0, 0.45, new BackEase { EasingMode = EasingMode.EaseOut });
            AddDouble(intro, bar, "(UIElement.Opacity)", 0.4, 1, 0.0, 0.35);
            intro.Begin();

            // Equaliser runs forever — this is what says "playing".
            var eqSb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            var heights = new[] { 20.0, 12, 24, 14 };
            for (var i = 0; i < bars.Count; i++)
            {
                var a = new DoubleAnimation
                {
                    From = 10,
                    To = heights[i],
                    Duration = TimeSpan.FromSeconds(0.42),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromSeconds(i * 0.14),
                    EasingFunction = new SineEase(),
                };
                Storyboard.SetTarget(a, bars[i]);
                Storyboard.SetTargetProperty(a, new PropertyPath("(FrameworkElement.Height)"));
                eqSb.Children.Add(a);
            }

            // The play button gives a soft pulse, tying the transport to the motion.
            var pulse = new DoubleAnimation
            {
                From = 1, To = 0.55,
                Duration = TimeSpan.FromSeconds(0.9),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase(),
            };
            Storyboard.SetTarget(pulse, playKey);
            Storyboard.SetTargetProperty(pulse, new PropertyPath("(UIElement.Opacity)"));
            eqSb.Children.Add(pulse);

            eqSb.Begin();
        };

        return root;
    }

    /// <summary>
    /// Keyboard: a key grid fades in and a highlight hops across it. "Type with the controller".
    /// </summary>
    private static FrameworkElement BuildKeyboard()
    {
        var root = Stage(out var content);

        var grid = new UniformGrid
        {
            Columns = 6,
            Rows = 3,
            Width = 240,
            Height = 120,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var keys = new List<Border>();
        for (var i = 0; i < 18; i++)
        {
            var k = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = Brush(Color.FromRgb(0x1A, 0x1E, 0x26)),
                BorderThickness = new Thickness(2),
                BorderBrush = Brush(Colors.Transparent),
                Margin = new Thickness(3),
            };
            keys.Add(k);
            grid.Children.Add(k);
        }

        content.Children.Add(grid);

        root.Loaded += (_, _) =>
        {
            // Fade the whole grid in.
            var intro = new Storyboard();
            AddDouble(intro, grid, "(UIElement.Opacity)", 0, 1, 0.0, 0.4);
            intro.Begin();

            // A highlight hops key to key forever, lighting each accent then releasing it.
            var hop = new[] { 0, 1, 7, 8, 14, 15, 9, 3 };
            var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(hop.Length * 0.35) };

            for (var step = 0; step < hop.Length; step++)
            {
                var key = keys[hop[step]];
                var at = TimeSpan.FromSeconds(0.4 + step * 0.35);

                var on = new ColorAnimation
                {
                    To = Accent,
                    Duration = TimeSpan.FromSeconds(0.12),
                    BeginTime = at,
                };
                Storyboard.SetTarget(on, key);
                Storyboard.SetTargetProperty(on, new PropertyPath("(Border.BorderBrush).(SolidColorBrush.Color)"));

                var off = new ColorAnimation
                {
                    To = Colors.Transparent,
                    Duration = TimeSpan.FromSeconds(0.18),
                    BeginTime = at + TimeSpan.FromSeconds(0.22),
                };
                Storyboard.SetTarget(off, key);
                Storyboard.SetTargetProperty(off, new PropertyPath("(Border.BorderBrush).(SolidColorBrush.Color)"));

                // Each key needs its own mutable brush for the colour animation to target.
                key.BorderBrush = new SolidColorBrush(Colors.Transparent);

                sb.Children.Add(on);
                sb.Children.Add(off);
            }

            sb.Begin();
        };

        return root;
    }

    /// <summary>Adds a double animation to a storyboard with the usual boilerplate.</summary>
    private static void AddDouble(Storyboard sb, DependencyObject target, string property,
        double from, double to, double beginSeconds, double durationSeconds,
        IEasingFunction? easing = null, bool autoReverse = false)
    {
        var a = new DoubleAnimation
        {
            From = from,
            To = to,
            BeginTime = TimeSpan.FromSeconds(beginSeconds),
            Duration = TimeSpan.FromSeconds(durationSeconds),
            EasingFunction = easing,
            AutoReverse = autoReverse,
        };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, new PropertyPath(property));
        sb.Children.Add(a);
    }
}
