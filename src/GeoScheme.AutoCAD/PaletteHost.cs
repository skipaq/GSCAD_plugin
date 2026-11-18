using System;
using System.Drawing;
using System.Windows.Forms.Integration;
using Autodesk.AutoCAD.Windows;

namespace GeoScheme.AutoCAD
{
    /// <summary>Палитра «Листы PDF» — модельнее окно внутри AutoCAD.</summary>
    public static class PaletteHost
    {
        private static PaletteSet _palette;

        public static void Show()
        {
            if (_palette == null)
            {
                _palette = new PaletteSet("ГЕО Схемы", new Guid("6A1C7E52-3B0D-4C1A-9C1E-7F2B4D8A9E31"))
                {
                    Style = PaletteSetStyles.ShowPropertiesMenu
                          | PaletteSetStyles.ShowAutoHideButton
                          | PaletteSetStyles.ShowCloseButton,
                    MinimumSize = new Size(340, 420)
                };

                var host = new ElementHost
                {
                    Dock = System.Windows.Forms.DockStyle.Fill,
                    Child = new SheetPickerControl()
                };

                _palette.Add("Листы PDF", host);
            }

            _palette.Visible = true;
            _palette.Activate(0);
        }

        public static void Hide()
        {
            if (_palette != null)
                _palette.Visible = false;
        }
    }
}
