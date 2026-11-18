using System;
using System.Drawing;
using System.Windows.Forms;

namespace GeoScheme.AutoCAD
{
    /// <summary>Выбор способа простановки размеров.</summary>
    public static class DimensionChoiceDialog
    {
        public const int Manual = 1;
        public const int Auto = 2;

        public static int Ask()
        {
            using (var form = new Form())
            {
                form.Text = "Размеры";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(320, 120);

                var manual = new Button
                {
                    Text = "Проставить вручную",
                    DialogResult = DialogResult.Yes,
                    Location = new Point(15, 20),
                    Width = 290,
                    Height = 32
                };

                var auto = new Button
                {
                    Text = "Проставить автоматически",
                    DialogResult = DialogResult.No,
                    Location = new Point(15, 62),
                    Width = 290,
                    Height = 32
                };

                form.Controls.Add(manual);
                form.Controls.Add(auto);
                form.AcceptButton = manual;
                form.CancelButton = manual;

                switch (form.ShowDialog())
                {
                    case DialogResult.Yes:
                        return Manual;
                    case DialogResult.No:
                        return Auto;
                    default:
                        return 0;
                }
            }
        }
    }
}
