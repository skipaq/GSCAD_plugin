using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfiumViewer;
using WinForms = System.Windows.Forms;

namespace GeoScheme.AutoCAD
{
    /// <summary>Что палитра передаёт команде импорта.</summary>
    public static class ImportRequest
    {
        public static string PdfPath;
        public static int Page = 1;
        public static bool HasValue => !string.IsNullOrEmpty(PdfPath) && Page > 0;
        public static void Clear() { PdfPath = null; Page = 1; }
    }

    /// <summary>
    /// Палитра выбора листа PDF: список страниц с миниатюрами.
    /// Двойной клик по листу — импорт в модель.
    /// </summary>
    public sealed class SheetPickerControl : UserControl
    {
        private const int ThumbWidth = 150;
        private const int MaxPages = 150;

        /// <summary>Готовый кадр: без WPF-типов, чтобы рендерить в фоне.</summary>
        private sealed class PageData
        {
            public int Number;
            public double WidthMm;
            public double HeightMm;
            public int PixelHeight;
            public byte[] Png;
        }

        private readonly ListBox _pages = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x36)),
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White
        };

        private readonly TextBlock _status = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xB0, 0xBC)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 6)
        };

        private readonly TextBlock _fileLabel = new TextBlock
        {
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 4)
        };

        private string _path;

        public SheetPickerControl()
        {
            NativePdfium.EnsureLoaded();

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var top = new StackPanel { Margin = new Thickness(8, 8, 8, 0) };
            var open = new Button { Content = "Импорт PDF…", Padding = new Thickness(10, 6, 10, 6) };
            open.Click += (s, e) => OpenFile();
            top.Children.Add(open);
            top.Children.Add(_fileLabel);
            top.Children.Add(_status);
            Grid.SetRow(top, 0);

            _pages.Margin = new Thickness(8, 4, 8, 0);
            _pages.MouseDoubleClick += (s, e) => ImportSelected();
            Grid.SetRow(_pages, 1);

            var hint = new TextBlock
            {
                Text = "Двойной клик по листу — вставить в чертёж",
                Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x89, 0x96)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 4, 8, 8)
            };
            Grid.SetRow(hint, 2);

            root.Children.Add(top);
            root.Children.Add(_pages);
            root.Children.Add(hint);
            Content = root;
        }

        private void OpenFile()
        {
            using (var dlg = new WinForms.OpenFileDialog())
            {
                dlg.Filter = "PDF (*.pdf)|*.pdf";
                dlg.Title = "Лист проекта";
                if (dlg.ShowDialog() != WinForms.DialogResult.OK)
                    return;

                _path = dlg.FileName;
                _fileLabel.Text = Path.GetFileName(_path);
                LoadPages(_path);
            }
        }

        private async void LoadPages(string path)
        {
            _pages.Items.Clear();
            _status.Text = "Читаю листы…";

            try
            {
                // Рендер — в фоне и без WPF-объектов: создавать их можно только в STA-потоке.
                var pages = await Task.Run(() => RenderPages(path));

                foreach (var page in pages)
                    _pages.Items.Add(BuildItem(page));

                _status.Text = pages.Count == 0
                    ? "В файле не найдено страниц."
                    : $"Листов: {pages.Count}.";

                if (_pages.Items.Count > 0)
                    _pages.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                _status.Text = "Не удалось прочитать PDF: " + ex.GetBaseException().Message;
            }
        }

        private static List<PageData> RenderPages(string path)
        {
            var result = new List<PageData>();

            using (var pdf = PdfDocument.Load(path))
            {
                int count = Math.Min(pdf.PageCount, MaxPages);

                for (int i = 0; i < count; i++)
                {
                    var size = pdf.PageSizes[i];
                    int height = Math.Max(40, (int)Math.Round(ThumbWidth * size.Height / size.Width));

                    var page = new PageData
                    {
                        Number = i + 1,
                        WidthMm = Math.Round(size.Width / 72.0 * 25.4),
                        HeightMm = Math.Round(size.Height / 72.0 * 25.4),
                        PixelHeight = height
                    };

                    // Если конкретная страница не отрисовалась — лист всё равно должен
                    // быть в списке и импортироваться. Миниатюра просто будет пустой.
                    try
                    {
                        using (var image = pdf.Render(i, ThumbWidth, height, 96, 96, PdfRenderFlags.Annotations))
                        using (var ms = new MemoryStream())
                        {
                            image.Save(ms, ImageFormat.Png);
                            page.Png = ms.ToArray();
                        }
                    }
                    catch
                    {
                        page.Png = null;
                    }

                    result.Add(page);
                }
            }

            return result;
        }

        private static ListBoxItem BuildItem(PageData page)
        {
            var text = new StackPanel { Margin = new Thickness(8, 4, 0, 0) };
            text.Children.Add(new TextBlock
            {
                Text = $"Лист {page.Number}",
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            text.Children.Add(new TextBlock
            {
                Text = $"{page.WidthMm}×{page.HeightMm} мм",
                Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xB0, 0xBC))
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(page.Png == null
                ? (FrameworkElement)new Border
                {
                    Width = ThumbWidth,
                    Height = page.PixelHeight,
                    Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x47))
                }
                : new Image
                {
                    Source = ToBitmapSource(page.Png),
                    Width = ThumbWidth,
                    Height = page.PixelHeight,
                    Stretch = Stretch.Uniform
                });
            row.Children.Add(text);

            return new ListBoxItem
            {
                Content = row,
                Tag = page.Number,
                Padding = new Thickness(4),
                Background = Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
        }

        private static ImageSource ToBitmapSource(byte[] png)
        {
            using (var ms = new MemoryStream(png))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
        }

        private void ImportSelected()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            if (doc == null || _path == null)
                return;

            var item = _pages.SelectedItem as ListBoxItem;
            if (item?.Tag == null)
                return;

            ImportRequest.PdfPath = _path;
            ImportRequest.Page = (int)item.Tag;

            // Диагностика: если этой строки не видно — обработчик клика не сработал.
            try { doc.Editor.WriteMessage("\nВыбран лист {0}, запускаю импорт…", ImportRequest.Page); }
            catch { }

            doc.SendStringToExecute("GSIMPORT ", true, false, false);
        }
    }
}
