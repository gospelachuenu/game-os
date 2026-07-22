using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MaintenanceHub;

namespace UI;

/// <summary>
/// "What's new" after a software update. Built from a release's ordered PatchNoteBlock
/// list, choosing its layout from the content:
///
///   - Blocks WITH images each get a full Spotlight page (benefit-led headline + the
///     screenshot), paged through one at a time.
///   - Everything without an image collapses onto a final Grouped-list page ("Also in
///     this update"), two columns by kind.
///   - A release with no images at all skips Spotlight and shows only the grouped
///     list.
///
/// This is why the layout is chosen from the data rather than a flag: a release can
/// never end up in a presentation its content cannot fill — Spotlight with nothing to
/// illustrate, or a grouped list padded with a lone fix.
///
/// The ambient video keeps playing behind it (transparent background + scrim), same as
/// the other overlay screens. Fully controller-navigable.
/// </summary>
public partial class PatchNotesScreen : UserControl
{
    /// <summary>Raised when Show() runs — MainWindow hides the dashboard content behind it.</summary>
    public event EventHandler? Opened;

    /// <summary>Raised when the user is done — MainWindow restores the dashboard.</summary>
    public event EventHandler? CloseRequested;

    private sealed record SpotlightItem(string Headline, string Body, string? MediaPath, bool IsVideo, string? Caption);

    private readonly List<SpotlightItem> _spotlight = new();
    private readonly ObservableCollection<string> _fixed = new();
    private readonly ObservableCollection<string> _improved = new();

    /// <summary>Unillustrated "New" items — shown in the recap's notes column.</summary>
    private readonly ObservableCollection<string> _recapNew = new();

    private string _version = string.Empty;
    private string? _packageDirectory;
    private bool _hasGroupedPage;

    /// <summary>
    /// Most features the recap will show. Past three the clips shrink to the point of
    /// being decoration rather than something you can actually see, which defeats the
    /// purpose of a recap. Extra illustrated features still appear in the notes column
    /// as text — they are not lost, just not given a clip.
    /// </summary>
    private const int MaxRecapFeatures = 3;

    // Page layout: 0..N-1 are Spotlight pages, then the grouped page (if any), then the
    // recap always last.
    private int _page;
    private int PageCount => _spotlight.Count + (_hasGroupedPage ? 1 : 0) + 1;
    private bool OnGroupedPage => _hasGroupedPage && _page == _spotlight.Count;
    private bool OnRecapPage => _page == PageCount - 1;

    public PatchNotesScreen()
    {
        InitializeComponent();
        FixedItems.ItemsSource = _fixed;
        ImprovedItems.ItemsSource = _improved;

        FixedItems.ItemTemplate = BuildNoteTemplate();
        ImprovedItems.ItemTemplate = BuildNoteTemplate();
    }

    /// <summary>
    /// Shows the notes for a release.
    /// </summary>
    /// <param name="packageDirectory">
    /// Root the notes' image paths resolve against. Paths are resolved STRICTLY inside
    /// this directory — a note is untrusted content arriving with an update, so a path
    /// escaping the package (a URL, an absolute path, "..\..") is refused rather than
    /// followed.
    /// </param>
    public void Show(string version, IReadOnlyList<PatchNoteBlock> notes, string? packageDirectory = null)
    {
        _version = version;
        _packageDirectory = packageDirectory;
        _page = 0;

        _spotlight.Clear();
        _fixed.Clear();
        _improved.Clear();
        _recapNew.Clear();

        BuildPages(notes);

        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
        Opened?.Invoke(this, EventArgs.Empty);

        RenderPage();
    }

    private void BuildPages(IReadOnlyList<PatchNoteBlock> notes)
    {
        // An image block illustrates the TEXT block immediately before it (that is how
        // the notes are authored — sentence, then its picture). Any text block not
        // followed by an image goes to the grouped list.
        for (var i = 0; i < notes.Count; i++)
        {
            var block = notes[i];

            if (IsMedia(block.Kind))
            {
                // A leading media block with no preceding text still gets a page, captioned.
                if (_spotlight.Count == 0 || i == 0)
                {
                    _spotlight.Add(new SpotlightItem(
                        block.Text ?? "What's new", string.Empty,
                        block.ImagePath, block.Kind == PatchNoteKind.Video, block.Text));
                }
                continue;
            }

            var hasMediaNext = i + 1 < notes.Count && IsMedia(notes[i + 1].Kind);

            if (hasMediaNext)
            {
                var media = notes[i + 1];
                _spotlight.Add(new SpotlightItem(
                    block.Text ?? string.Empty, string.Empty,
                    media.ImagePath, media.Kind == PatchNoteKind.Video, media.Text));
                i++; // consume the media block
            }
            else if (!string.IsNullOrWhiteSpace(block.Text))
            {
                // New / Improved / Fixed kept apart so the recap can group them
                // properly. The grouped page folds New in with Improved.
                var target = block.Kind switch
                {
                    PatchNoteKind.Fixed => _fixed,
                    PatchNoteKind.New => _recapNew,
                    _ => _improved,
                };
                target.Add(block.Text!);
            }
        }

        // The grouped page folds unillustrated New items in with Improved, so nothing
        // is dropped there; the recap keeps them separate for its own grouping.
        var groupedImproved = _recapNew.Concat(_improved).ToList();

        _hasGroupedPage = _fixed.Count > 0 || groupedImproved.Count > 0;

        FixedItems.ItemsSource = _fixed;
        ImprovedItems.ItemsSource = groupedImproved;

        FixedCount.Text = _fixed.Count.ToString();
        ImprovedCount.Text = groupedImproved.Count.ToString();
        FixedColumn.Visibility = _fixed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ImprovedColumn.Visibility = groupedImproved.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderPage()
    {
        // The recap is always the final page — the consolidated view of everything.
        if (OnRecapPage)
        {
            SpotlightPage.Visibility = Visibility.Collapsed;
            GroupedPage.Visibility = Visibility.Collapsed;
            RecapPage.Visibility = Visibility.Visible;

            // Stop any Spotlight clip; the recap plays its own.
            SpotlightVideo.Stop();

            BuildRecap();
            AnimateIn(RecapPage);
            NavHint.Text = PageCount > 1 ? "◀  Back        A  Done" : "A  Done";
            return;
        }

        RecapPage.Visibility = Visibility.Collapsed;
        StopRecapMedia();

        var showGrouped = OnGroupedPage;

        GroupedPage.Visibility = showGrouped ? Visibility.Visible : Visibility.Collapsed;
        SpotlightPage.Visibility = showGrouped ? Visibility.Collapsed : Visibility.Visible;

        if (showGrouped)
        {
            AnimateIn(GroupedPage);
            NavHint.Text = "▶  Next        A  Done        B  Back";
            return;
        }

        var item = _spotlight[_page];
        SpotlightKicker.Text = $"{_version} · WHAT'S NEW";
        SpotlightHeadline.Text = item.Headline;
        SpotlightBody.Text = item.Body;
        SpotlightBody.Visibility = string.IsNullOrWhiteSpace(item.Body) ? Visibility.Collapsed : Visibility.Visible;
        SpotlightCounter.Text = $"{_page + 1} OF {PageCount}";
        SpotlightCaption.Text = item.Caption ?? string.Empty;
        SpotlightCaption.Visibility = string.IsNullOrWhiteSpace(item.Caption) ? Visibility.Collapsed : Visibility.Visible;

        LoadSpotlightMedia(item);
        BuildDots();
        NavHint.Text = PageCount > 1 ? "▶  Next        A  Done        B  Back" : "A  Done";

        AnimateIn(SpotlightPage);
    }

    private readonly List<MediaElement> _recapVideos = new();

    /// <summary>
    /// Builds the recap: features (with their clips) on the left, remaining notes on
    /// the right. Capped at three features — see MaxRecapFeatures.
    /// </summary>
    private void BuildRecap()
    {
        RecapVersion.Text = $"Version {_version}";
        RecapFeatures.Children.Clear();
        StopRecapMedia();

        var shown = _spotlight.Take(MaxRecapFeatures).ToList();

        // A LONE feature lays out media-on-top, text-beneath. Side-by-side only works
        // when tiles constrain each other's height; with one tile the media stretches
        // to the full column and squashes the text against it.
        var single = shown.Count == 1;

        foreach (var item in shown)
        {
            RecapFeatures.Children.Add(BuildFeatureTile(item, single, shown.Count));
        }

        // Anything beyond the cap is not lost — it joins the notes column as text.
        var overflow = _spotlight.Skip(MaxRecapFeatures).Select(s => s.Headline).ToList();

        var newItems = new List<string>(overflow);
        newItems.AddRange(_recapNew);

        SetGroup(RecapNewGroup, RecapNewItems, newItems);
        SetGroup(RecapFasterGroup, RecapFasterItems, _improved);
        SetGroup(RecapFixedGroup, RecapFixedItems, _fixed);

        RecapNotes.Visibility =
            newItems.Count + _improved.Count + _fixed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetGroup(UIElement group, ItemsControl items, IEnumerable<string> source)
    {
        var list = source.ToList();
        items.ItemsSource = list;
        items.ItemTemplate ??= BuildNoteTemplate();
        group.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One recap feature: its clip/image plus a short title and description.</summary>
    private FrameworkElement BuildFeatureTile(SpotlightItem item, bool single, int count)
    {
        var media = BuildRecapMedia(item);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = "NEW",
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("Theme.AccentPrimaryBrush"),
        });
        text.Children.Add(new TextBlock
        {
            Text = item.Headline,
            // Text shrinks as the count grows so three still fit without clipping.
            FontSize = single ? 20 : count == 2 ? 15 : 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF0)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });

        if (!string.IsNullOrWhiteSpace(item.Caption))
        {
            text.Children.Add(new TextBlock
            {
                Text = item.Caption,
                FontSize = single ? 13 : 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x90, 0x99)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }

        if (single)
        {
            // Media on top, text beneath.
            var stack = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(media, 0);
            Grid.SetRow(text, 1);
            text.Margin = new Thickness(0, 14, 0, 0);
            stack.Children.Add(media);
            stack.Children.Add(text);
            return stack;
        }

        // Side by side, sharing the column height.
        var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        media.Width = count == 2 ? 240 : 190;
        media.Height = count == 2 ? 150 : 119;
        Grid.SetColumn(media, 0);
        Grid.SetColumn(text, 1);
        text.Margin = new Thickness(18, 0, 0, 0);
        row.Children.Add(media);
        row.Children.Add(text);
        return row;
    }

    /// <summary>
    /// The media surface for a recap feature — a looping clip, a still, or the gradient
    /// placeholder when neither loads.
    /// </summary>
    private FrameworkElement BuildRecapMedia(SpotlightItem item)
    {
        var host = new Border
        {
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3A)),
            ClipToBounds = true,
            MinHeight = 100,
        };

        var grid = new Grid();

        var fallback = new Border
        {
            Background = new LinearGradientBrush(
                Color.FromRgb(0x12, 0x16, 0x1C),
                Color.FromRgb(0x0B, 0x0D, 0x12),
                45),
        };
        grid.Children.Add(fallback);

        var full = ResolveInPackage(item.MediaPath);
        if (full is not null)
        {
            try
            {
                if (item.IsVideo)
                {
                    var video = new MediaElement
                    {
                        Source = new Uri(full, UriKind.Absolute),
                        LoadedBehavior = MediaState.Manual,
                        UnloadedBehavior = MediaState.Stop,
                        Stretch = Stretch.UniformToFill,
                        Volume = 0,
                        IsMuted = true,
                    };

                    // Loop each recap clip independently.
                    video.MediaEnded += (_, _) =>
                    {
                        video.Position = TimeSpan.Zero;
                        video.Play();
                    };

                    grid.Children.Add(video);
                    _recapVideos.Add(video);
                    video.Play();
                    fallback.Visibility = Visibility.Collapsed;
                }
                else
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(full, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    grid.Children.Add(new Image { Source = bitmap, Stretch = Stretch.UniformToFill });
                    fallback.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception e) when (e is IOException or NotSupportedException or UriFormatException or ArgumentException)
            {
                // Placeholder stays.
            }
        }

        host.Child = grid;
        return host;
    }

    /// <summary>Stops every recap clip, so nothing decodes once the page is left.</summary>
    private void StopRecapMedia()
    {
        foreach (var video in _recapVideos)
        {
            video.Stop();
            video.Source = null;
        }

        _recapVideos.Clear();
    }

    private static bool IsMedia(PatchNoteKind kind) =>
        kind is PatchNoteKind.Image or PatchNoteKind.Video;

    /// <summary>
    /// Loads the page's screenshot OR video, resolved strictly inside the package
    /// directory. A missing, unreadable, or out-of-bounds file falls back to the
    /// gradient placeholder rather than leaving an empty frame or throwing — the notes
    /// are cosmetic and must never take out the post-update experience.
    /// </summary>
    private void LoadSpotlightMedia(SpotlightItem item)
    {
        // Reset both surfaces; the placeholder shows unless a file loads.
        SpotlightImage.Source = null;
        SpotlightImage.Visibility = Visibility.Collapsed;
        SpotlightVideo.Stop();
        SpotlightVideo.Source = null;
        SpotlightVideo.Visibility = Visibility.Collapsed;
        SpotlightImageFallback.Visibility = Visibility.Visible;

        var full = ResolveInPackage(item.MediaPath);
        if (full is null)
        {
            return;
        }

        try
        {
            if (item.IsVideo)
            {
                SpotlightVideo.Source = new Uri(full, UriKind.Absolute);
                SpotlightVideo.Visibility = Visibility.Visible;
                SpotlightImageFallback.Visibility = Visibility.Collapsed;
                SpotlightVideo.Position = TimeSpan.Zero;
                SpotlightVideo.Play(); // muted, loops via MediaEnded
            }
            else
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(full, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                SpotlightImage.Source = bitmap;
                SpotlightImage.Visibility = Visibility.Visible;
                SpotlightImageFallback.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UriFormatException or ArgumentException)
        {
            // Placeholder stays. A broken screenshot or clip is never worth a crash.
        }
    }

    /// <summary>
    /// Resolves a package-relative media path to an absolute one, or null if it is
    /// missing, empty, or escapes the package directory.
    ///
    /// Patch notes are untrusted content arriving with an update, so a path that is a
    /// URL, absolute, or climbs out with "..\.." is refused rather than followed.
    /// </summary>
    private string? ResolveInPackage(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || _packageDirectory is null)
        {
            return null;
        }

        try
        {
            var root = Path.GetFullPath(_packageDirectory);
            var full = Path.GetFullPath(Path.Combine(root, relativePath));

            var withinPackage = full.StartsWith(
                root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

            return withinPackage && File.Exists(full) ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Loops the spotlight clip: rewind and replay when it ends.</summary>
    private void SpotlightVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        SpotlightVideo.Position = TimeSpan.Zero;
        SpotlightVideo.Play();
    }

    private void BuildDots()
    {
        SpotlightDots.Children.Clear();
        if (PageCount <= 1)
        {
            return;
        }

        var accent = (Brush)FindResource("Theme.AccentPrimaryBrush");
        var idle = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3A));

        for (var i = 0; i < PageCount; i++)
        {
            SpotlightDots.Children.Add(new Border
            {
                Width = i == _page ? 22 : 8,
                Height = 3,
                Margin = new Thickness(0, 0, 6, 0),
                CornerRadius = new CornerRadius(2),
                Background = i == _page ? accent : idle,
            });
        }
    }

    private static void AnimateIn(UIElement page)
    {
        page.BeginAnimation(OpacityProperty, null);
        page.Opacity = 0;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)));
    }

    // ---------------- navigation ----------------

    /// <summary>Advance a page, or finish when past the last one.</summary>
    public void Next()
    {
        if (_page < PageCount - 1)
        {
            _page++;
            RenderPage();
        }
        else
        {
            Hide();
        }
    }

    public void Back()
    {
        if (_page > 0)
        {
            _page--;
            RenderPage();
        }
        else
        {
            // Backing out of the first page leaves the notes entirely.
            Hide();
        }
    }

    /// <summary>A/Enter — "Done" on the last page, otherwise advance.</summary>
    public void Confirm() => Next();

    public void Hide()
    {
        // Stop any playing clip so nothing decodes behind the dashboard once this is
        // gone — the Spotlight one and every recap tile.
        SpotlightVideo.Stop();
        SpotlightVideo.Source = null;
        StopRecapMedia();

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromSeconds(0.25));
        fade.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void Done_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => Hide();

    // ---------------- note-item template ----------------

    private static DataTemplate BuildNoteTemplate()
    {
        // A bordered line per note: cheaper and clearer than a bullet list, and matches
        // the grouped-list mock's row treatment.
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.PaddingProperty, new Thickness(0, 0, 0, 12));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x80, 0x2A, 0x2F, 0x3A)));
        border.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 12));

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        text.SetValue(TextBlock.FontSizeProperty, 14.0);
        text.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xD6, 0xD8, 0xD2)));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(TextBlock.LineHeightProperty, 20.0);

        border.AppendChild(text);

        return new DataTemplate { VisualTree = border };
    }
}
