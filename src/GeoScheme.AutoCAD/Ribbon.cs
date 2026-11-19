using System;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.Windows;

namespace GeoScheme.AutoCAD
{
    /// <summary>Сборка вкладки «ГЕО Схемы» в ленте AutoCAD.</summary>
    internal static class RibbonBuilder
    {
        public static void EnsureTab()
        {
            var ribbon = ComponentManager.Ribbon;

            if (ribbon == null || ribbon.FindTab(Plugin.TabId) != null)
                return;

            var tab = new RibbonTab
            {
                Id = Plugin.TabId,
                Title = "ГЕО Схемы",
                Name = "ГЕО Схемы"
            };

            tab.Panels.Add(MakePanel("Импорт",
                Big("Импорт PDF", "GSPALETTE", "Палитра листов PDF", "import"),
                Big("Калибровка", "GSCALIBR", "Масштаб по известному размеру", "calib")));

            tab.Panels.Add(MakePanel("Схема",
                Big("Объекты", "GSOBJECT", "Выделить объекты схемы", "object"),
                Big("Оси", "GSAXIS", "Ось по обрывкам и маркеру", "axis"),
                Big("Размеры", "GSDIM", "Размеры: вручную или автоматически", "dims"),
                Big("Всё без подложки", "GSSELALL", "Выделить всё, кроме подложки", "select")));

            tab.Panels.Add(MakePanel("Отклонения",
                Big("Выбрать тип отклонений", "GSFACT", "Фактические размеры, высотные, В/Н, анкера", "fact")));

            tab.Panels.Add(MakePanel("Оформление",
                Big("Рамка", "GSFRAME", "Вставить рамку из библиотеки", "frame"),
                Big("Блоки", "GSBLOCKS", "Библиотека DXF-блоков", "blocks"),
                Big("Подгон шрифта", "GSFITTEXT", "Подогнать высоту текста под штамп", "fontfit")));

            tab.Panels.Add(MakePanel("Рисование",
                Small("Отрезок", "_.LINE ", "line"),
                Small("Полилиния", "_.PLINE ", "pline"),
                Small("Круг", "_.CIRCLE ", "circle"),
                Small("Прямоугольник", "_.RECTANG ", "rect"),
                Small("Штриховка", "_.HATCH ", "hatch"),
                TextSplit("text", "mtext")));

            tab.Panels.Add(MakePanel("Редактирование",
                Small("Переместить", "_.MOVE ", "move"),
                Small("Копировать", "_.COPY ", "copy"),
                Small("Повернуть", "_.ROTATE ", "rotate"),
                Small("Отразить", "_.MIRROR ", "mirror"),
                Small("Расчленить", "_.EXPLODE ", "explode"),
                Small("Сместить", "_.OFFSET ", "offset"),
                Small("Соединить", "_.JOIN ", "join"),
                Small("Копирование свойств", "_.MATCHPROP ", "matchprop")));

            tab.Panels.Add(MakePanel("Вывод",
                Big("PDF", "GSPDF", "Экспорт листа в PDF", "import")));

            ribbon.Tabs.Add(tab);
        }

        private static RibbonPanel MakePanel(string title, params RibbonItem[] items)
        {
            var source = new RibbonPanelSource { Title = title };

            foreach (var item in items)
                source.Items.Add(item);

            return new RibbonPanel { Source = source };
        }

        private static RibbonButton Big(string text, string command, string tooltip, string icon)
        {
            var image = LoadIcon(icon, false);

            return new RibbonButton
            {
                Text = text,
                Name = text,
                ShowText = true,
                ShowImage = image != null,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                ToolTip = tooltip,
                Image = image,
                LargeImage = image,
                CommandHandler = new SendCommandHandler(command),
                CommandParameter = command
            };
        }

        private static RibbonButton Small(string text, string command, string icon)
        {
            // Сначала пробуем взять родную иконку AutoCAD из его ленты.
            var builtIn = FindBuiltInImage(command);

            var image = builtIn ?? LoadIcon(icon, true);

            return new RibbonButton
            {
                Text = text,
                Name = text,
                ShowText = false,
                ShowImage = image != null,
                Size = RibbonItemSize.Standard,
                ToolTip = text,
                Image = image,
                CommandHandler = new SendCommandHandler(command),
                CommandParameter = command
            };
        }

        private static RibbonItem TextSplit(string textIcon, string mtextIcon)
        {
            var split = new RibbonSplitButton
            {
                Text = "Текст",
                Name = "Текст",
                ShowText = false,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                IsSplit = true,
                ToolTip = "Текст: однострочный или многострочный",
                Image = LoadIcon(textIcon, true),
                CommandHandler = new SendCommandHandler("_.TEXT "),
                CommandParameter = "_.TEXT "
            };

            split.Items.Add(Small("Однострочный текст", "_.TEXT ", textIcon));
            split.Items.Add(Small("Многострочный текст", "_.MTEXT ", mtextIcon));

            return split;
        }

        /// <summary>Ищет иконку стандартной команды в родной ленте AutoCAD.</summary>
        private static System.Windows.Media.ImageSource FindBuiltInImage(string command)
        {
            try
            {
                var id = command.Replace("_.", string.Empty).Trim().ToUpperInvariant();
                id = "ID_" + id.Split(' ')[0];

                var ribbon = ComponentManager.Ribbon;
                if (ribbon == null)
                    return null;

                foreach (var tab in ribbon.Tabs)
                {
                    if (tab.Id == Plugin.TabId)
                        continue;

                    foreach (var panel in tab.Panels)
                    {
                        var source = panel.Source;
                        if (source == null)
                            continue;

                        foreach (var item in source.Items)
                        {
                            var found = FindInItem(item, id, true);
                            if (found != null)
                                return found;
                        }
                    }
                }
            }
            catch
            {
                // лента ещё не построена — берём свою иконку
            }

            return null;
        }

        private static System.Windows.Media.ImageSource FindInItem(RibbonItem item, string id, bool large)
        {
            var button = item as RibbonButton;

            if (button == null)
                return null;

            if (string.Equals(button.Id, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(button.Name, id, StringComparison.OrdinalIgnoreCase))
            {
                return large
                    ? (System.Windows.Media.ImageSource)(button.LargeImage ?? button.Image)
                    : (button.Image ?? button.LargeImage);
            }

            if (item is RibbonSplitButton split && split.Items != null)
            {
                foreach (var child in split.Items)
                {
                    var found = FindInItem(child, id, large);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        private static System.Windows.Media.ImageSource LoadIcon(string name, bool small)
        {
            try
            {
                var folder = Path.Combine(
                    Path.GetDirectoryName(typeof(RibbonBuilder).Assembly.Location) ?? string.Empty,
                    "Icons");

                var path = Path.Combine(folder, name + (small ? "_16.png" : "_32.png"));

                if (!File.Exists(path))
                    return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private sealed class SendCommandHandler : ICommand
        {
            private readonly string _command;

            public SendCommandHandler(string command)
            {
                _command = command;
            }

            public event EventHandler CanExecuteChanged
            {
                add { }
                remove { }
            }

            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter)
            {
                var doc = Application.DocumentManager?.MdiActiveDocument;

                if (doc == null)
                    return;

                var command = _command.EndsWith(" ") ? _command : _command + " ";
                doc.SendStringToExecute(command, true, false, false);
            }
        }
    }
}
