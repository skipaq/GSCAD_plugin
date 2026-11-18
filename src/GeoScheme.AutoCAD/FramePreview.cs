using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace GeoScheme.AutoCAD
{
    /// <summary>Отрисовка миниатюры рамки: линии и текст, с зумом и сдвигом.</summary>
    public static class FramePreview
    {
        private static readonly Regex MTextCode = new Regex(@"\\[A-Za-z][^;]*;", RegexOptions.Compiled);

        public static void Draw(Graphics g, Size size, FrameLibrary.FrameInfo frame,
            double zoom, double panX, double panY)
        {
            if (frame == null || frame.Width <= 0 || frame.Height <= 0)
                return;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            const double margin = 14;
            var fit = Math.Min(
                (size.Width - 2 * margin) / frame.Width,
                (size.Height - 2 * margin) / frame.Height);

            var scale = fit * zoom;

            var centerX = size.Width / 2.0 + panX;
            var centerY = size.Height / 2.0 + panY;

            double Px(double x) => centerX + (x - frame.CenterX) * scale;
            double Py(double y) => centerY - (y - frame.CenterY) * scale;

            var stateClip = g.Save();
            g.SetClip(new Rectangle(0, 0, size.Width, size.Height));

            using (var pen = new Pen(Color.FromArgb(225, 225, 225), 1f))
            {
                foreach (var segment in frame.Segments)
                    g.DrawLine(pen, (float)Px(segment.x1), (float)Py(segment.y1),
                        (float)Px(segment.x2), (float)Py(segment.y2));
            }

            var brush = Brushes.WhiteSmoke;

            foreach (var text in frame.Texts)
            {
                var pixelHeight = text.h * scale;

                if (pixelHeight < 3.5)
                    continue;

                var content = Clean(text.text);
                if (content.Length == 0)
                    continue;

                var state = g.Save();

                try
                {
                    g.TranslateTransform((float)Px(text.x), (float)Py(text.y));
                    g.RotateTransform((float)(-text.rot * 180.0 / Math.PI));

                    using (var font = CreateFont(text.font, (float)pixelHeight))
                    {
                        var format = new StringFormat
                        {
                            Alignment = StringAlignment.Near,
                            LineAlignment = StringAlignment.Near
                        };

                        g.DrawString(content, font, brush, 0, 0, format);
                    }
                }
                catch
                {
                    // текст не отрисовался — не критично
                }
                finally
                {
                    g.Restore(state);
                }
            }

            g.Restore(stateClip);
        }

        private static readonly List<PrivateFontCollection> Loaded = new List<PrivateFontCollection>();

        private static Font CreateFont(string name, float pixelHeight)
        {
            var italic = name != null && name.IndexOf("italic", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!string.IsNullOrWhiteSpace(name))
            {
                // Сначала пробуем семейство по имени (GOST Common Italic),
                // затем без слова Italic (GOST Common + курсив).
                var candidates = new List<string> { name };

                var trimmed = name.Replace(" Italic", "").Replace(" italic", "").Trim();
                if (!string.Equals(trimmed, name, StringComparison.OrdinalIgnoreCase))
                    candidates.Add(trimmed);

                foreach (var candidate in candidates)
                {
                    try
                    {
                        var family = new FontFamily(candidate);
                        return new Font(family, pixelHeight,
                            italic ? FontStyle.Italic : FontStyle.Regular, GraphicsUnit.Pixel);
                    }
                    catch
                    {
                        // семейства нет — идём дальше
                    }
                }

                var fromFile = LoadFromFile(name, pixelHeight, italic);
                if (fromFile != null)
                    return fromFile;
            }

            return new Font("Arial", pixelHeight,
                italic ? FontStyle.Italic : FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private static Font LoadFromFile(string name, float pixelHeight, bool italic)
        {
            var fileName = name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                ? name
                : name + ".ttf";

            foreach (var folder in FontFolders())
            {
                var path = Path.Combine(folder, fileName);

                if (!File.Exists(path))
                    continue;

                try
                {
                    var collection = new PrivateFontCollection();
                    collection.AddFontFile(path);
                    Loaded.Add(collection);

                    if (collection.Families.Length == 0)
                        continue;

                    return new Font(collection.Families[0], pixelHeight,
                        italic ? FontStyle.Italic : FontStyle.Regular, GraphicsUnit.Pixel);
                }
                catch
                {
                    // файл не подошёл — пробуем следующую папку
                }
            }

            return null;
        }

        private static IEnumerable<string> FontFolders()
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts));

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            foreach (var autocad in SafeFolders(Path.Combine(programFiles, "Autodesk")))
                yield return Path.Combine(autocad, "Fonts");

            yield return Path.GetDirectoryName(typeof(FramePreview).Assembly.Location) ?? string.Empty;
        }

        private static IEnumerable<string> SafeFolders(string root)
        {
            string[] folders;

            try
            {
                folders = Directory.GetDirectories(root);
            }
            catch
            {
                yield break;
            }

            foreach (var folder in folders)
                yield return folder;
        }

        /// <summary>Чистим служебные коды МТекста, чтобы в превью был обычный текст.</summary>
        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var result = MTextCode.Replace(text, string.Empty);
            result = result.Replace("\\P", " ").Replace("\\p", " ");
            result = result.Replace("\\~", " ");
            result = result.Replace("{", string.Empty).Replace("}", string.Empty);
            result = result.Replace("%%d", "°").Replace("%%D", "°");
            result = result.Replace("%%c", "Ø").Replace("%%C", "Ø");

            return result.Trim();
        }
    }
}
