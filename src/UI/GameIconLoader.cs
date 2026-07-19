using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UI;

/// <summary>
/// Resolves a GameEntry.ImageUrl into a loadable ImageSource. Treats ImageUrl as a
/// filename relative to Assets/Icons/ (drop real icon image files there and point
/// ImageUrl at the filename) — returns null if the field is empty or the file
/// doesn't exist yet, so callers can fall back to the generated gradient tile art
/// gracefully instead of the whole tile breaking.
/// </summary>
public static class GameIconLoader
{
    public static ImageSource? TryLoad(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", imageUrl);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            // Corrupt/unreadable image file — fall back to generated art rather than crash.
            return null;
        }
    }
}
