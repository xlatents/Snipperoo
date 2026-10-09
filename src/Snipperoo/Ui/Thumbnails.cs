using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using Snipperoo.Encoding;

namespace Snipperoo.Ui;

/// <summary>Small preview images for toasts.</summary>
internal static class Thumbnails
{
    private const int Width = 240;

    /// <summary>Scaled-down copy of a GDI+ bitmap as a frozen WPF image.</summary>
    public static BitmapSource FromBitmap(System.Drawing.Bitmap bitmap)
    {
        int height = Math.Max(1, bitmap.Height * Width / Math.Max(1, bitmap.Width));
        using var small = new System.Drawing.Bitmap(bitmap, Width, height);
        using var stream = new MemoryStream();
        small.Save(stream, ImageFormat.Png);
        return Load(stream);
    }

    /// <summary>A frame from early in the clip, or null if it could not be extracted.</summary>
    public static async Task<BitmapSource?> FromVideoAsync(Ffmpeg ffmpeg, string video, TimeSpan duration)
    {
        string file = Path.Combine(Path.GetTempPath(), $"snipperoo_thumb_{Guid.NewGuid():N}.jpg");
        try
        {
            await ffmpeg.ExtractThumbnailAsync(video, file, Math.Min(0.5, duration.TotalSeconds / 2), Width);
            using var stream = new MemoryStream(await File.ReadAllBytesAsync(file));
            return Load(stream);
        }
        catch (Exception ex) when (ex is FfmpegException or IOException)
        {
            Log.Error("Could not create clip thumbnail", ex);
            return null;
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static BitmapSource Load(Stream stream)
    {
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
