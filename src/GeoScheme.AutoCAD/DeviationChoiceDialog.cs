using System;
using System.Drawing;
using System.Windows.Forms;

namespace GeoScheme.AutoCAD
{
    /// <summary>Выбор типа отклонений — кнопками.</summary>
    public static class DeviationChoiceDialog
    {
        public const string Sizes = "фактические размеры";
        public const string Heights = "высотные отклонения";
        public const string Vn = "отклонения В/Н";
        public const string Anchors = "анкера";

        public static string Ask()
        {
            using (var form = new Form())
            {
                form.Text = "Тип отклонений";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(340, 250);

                var sizes = MakeButton("Фактические размеры", 15);
                var heights = MakeButton("Высотные отклонения", 57);
                var vn = MakeButton("Отклонения В/Н", 99);
                var anchors = MakeButton("Анкера", 141);

                var cancel = new Button
                {
                    Text = "Отмена",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(240, 155),
                    Width = 85
                };

                var choice = (string)null;

                sizes.Click += (s, e) => { choice = Sizes; form.Close(); };
                heights.Click += (s, e) => { choice = Heights; form.Close(); };
                vn.Click += (s, e) => { choice = Vn; form.Close(); };
                anchors.Click += (s, e) => { choice = Anchors; form.Close(); };

                form.Controls.Add(sizes);
                form.Controls.Add(heights);
                form.Controls.Add(vn);
                form.Controls.Add(anchors);
                form.Controls.Add(cancel);
                form.CancelButton = cancel;

                form.ShowDialog();
                return choice;
            }
        }

        private static Button MakeButton(string text, int top)
        {
            return new Button
            {
                Text = text,
                Location = new Point(15, top),
                Width = 310,
                Height = 34
            };
        }
    }
}
