using System;
using System.Drawing;
using System.Windows.Forms;

namespace GeoScheme.AutoCAD
{
    /// <summary>Ввод многострочного текста блока.</summary>
    public static class TextInputDialog
    {
        public static string Ask(string title, string value)
        {
            using (var form = new Form())
            {
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(560, 420);

                var box = new TextBox
                {
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    AcceptsReturn = true,
                    Location = new Point(15, 15),
                    Size = new Size(530, 340),
                    Text = value ?? string.Empty
                };

                var ok = new Button
                {
                    Text = "ОК",
                    DialogResult = DialogResult.OK,
                    Location = new Point(380, 370),
                    Width = 75
                };

                var cancel = new Button
                {
                    Text = "Отмена",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(470, 370),
                    Width = 75
                };

                form.Controls.Add(box);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = null;
                form.CancelButton = cancel;

                if (form.ShowDialog() != DialogResult.OK)
                    return null;

                var text = box.Text.TrimEnd();
                return text.Length == 0 ? null : text;
            }
        }
    }
}
