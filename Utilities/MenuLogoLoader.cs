using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace TaskFolder.Utilities
{
    /// <summary>
    /// Loads a user-selected menu logo without keeping the source file locked.
    /// </summary>
    public static class MenuLogoLoader
    {
        public static Bitmap LoadScaledBitmap(string path, int size)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || size <= 0)
                return null;

            try
            {
                using Image source = LoadSource(path);
                if (source == null) return null;

                var result = new Bitmap(size, size);
                using Graphics graphics = Graphics.FromImage(result);
                graphics.Clear(Color.Transparent);
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;

                float scale = Math.Min((float)size / source.Width, (float)size / source.Height);
                int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                int x = (size - width) / 2;
                int y = (size - height) / 2;
                graphics.DrawImage(source, new Rectangle(x, y, width, height));
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load menu logo: {ex.Message}");
                return null;
            }
        }

        private static Image LoadSource(string path)
        {
            if (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var icon = new Icon(path);
                return icon.ToBitmap();
            }

            using var fileImage = Image.FromFile(path);
            return new Bitmap(fileImage);
        }
    }
}
