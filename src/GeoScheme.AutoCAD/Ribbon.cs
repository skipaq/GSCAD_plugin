using System;
using System.Windows.Input;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.Windows;

namespace GeoScheme.AutoCAD
{
    /// <summary>
    /// Сборка вкладки «ГЕО Схемы» в ленте AutoCAD.
    /// </summary>
    internal static class RibbonBuilder
    {
        public static void EnsureTab()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null)
                return;

            if (ribbon.FindTab(Plugin.TabId) != null)
                return;

            var tab = new RibbonTab
            {
                Id = Plugin.TabId,
                Title = "ГЕО Схемы",
                Name = "ГЕО Схемы"
            };

            tab.Panels.Add(MakePanel("Импорт",
                Button("Импорт PDF", "GSPALETTE", "Палитра листов: выбрать и вставить в чертёж"),
                Button("Калибровка", "GSCALIBR", "Масштаб по известному размеру"),
                Button("Выделить подложку", "GSSELBASE", "Выделить весь импортированный лист")));

            tab.Panels.Add(MakePanel("Схема",
                Button("Объекты", "GSOBJECT", "Обвести и выделить объекты схемы"),
                Button("Оси", "GSAXIS", "Ось по обрывкам линий и маркеру"),
                Button("Размеры", "GSDIM", "Проектные размеры по объекту")));

            tab.Panels.Add(MakePanel("Отклонения",
                Button("Выбрать тип отклонений", "GSFACT", "Фактические размеры, высотные отклонения, отклонения В/Н")));

            tab.Panels.Add(MakePanel("Оформление",
                Button("Рамка", "GSFRAME", "Вставить рамку из библиотеки"),
                Button("Текстовые блоки", "GSTEXT", "Библиотека текстовых блоков"),
                Button("Блоки", "GSBLOCKS", "Библиотека DXF-блоков: легенды, условные обозначения"),
                Button("Подгон шрифта", "GSFITTEXT", "Подогнать высоту текста под штамп рамки")));

            tab.Panels.Add(MakePanel("Вывод",
                Button("PDF", "GSPDF", "Экспорт листа в PDF")));

            ribbon.Tabs.Add(tab);
        }

        private static RibbonPanel MakePanel(string title, params RibbonButton[] buttons)
        {
            var source = new RibbonPanelSource { Title = title };
            foreach (var b in buttons)
                source.Items.Add(b);

            return new RibbonPanel { Source = source };
        }

        private static RibbonButton Button(string text, string command, string tooltip)
        {
            return new RibbonButton
            {
                Text = text,
                Name = text,
                ShowText = true,
                ShowImage = false,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                ToolTip = tooltip,
                CommandHandler = new SendCommandHandler(command),
                CommandParameter = command
            };
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
                doc?.SendStringToExecute(_command + " ", true, false, false);
            }
        }
    }
}
