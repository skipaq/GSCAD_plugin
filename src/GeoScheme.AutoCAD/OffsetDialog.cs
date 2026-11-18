using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GeoScheme.AutoCAD
{
    /// <summary>Окошко автоматической простановки: отступ и размерный стиль.</summary>
    public static class OffsetDialog
    {
        public sealed class Result
        {
            public bool Ok;
            public double OffsetMm;
            public int StyleIndex;
        }

        public static Result Ask(double defaultMm, IList<string> styleNames, int defaultStyleIndex)
        {
            var result = new Result();

            using (var form = new Form())
            {
                form.Text = "Размеры автоматически";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(380, 190);

                var styleLabel = new Label
                {
                    Text = "Размерный стиль:",
                    AutoSize = false,
                    Width = 340,
                    Height = 18,
                    Location = new Point(15, 12)
                };

                var styleBox = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Location = new Point(15, 32),
                    Width = 345
                };

                if (styleNames != null && styleNames.Count > 0)
                {
                    foreach (var name in styleNames)
                        styleBox.Items.Add(name);

                    styleBox.SelectedIndex = Math.Max(0, Math.Min(defaultStyleIndex, styleNames.Count - 1));
                }
                else
                {
                    styleBox.Enabled = false;
                }

                var offsetLabel = new Label
                {
                    Text = "Отступ размерной линии от объекта, мм:",
                    AutoSize = false,
                    Width = 340,
                    Height = 18,
                    Location = new Point(15, 68)
                };

                var input = new NumericUpDown
                {
                    Minimum = 1,
                    Maximum = 1000000,
                    DecimalPlaces = 0,
                    Value = (decimal)Math.Max(1, Math.Round(defaultMm)),
                    Location = new Point(15, 88),
                    Width = 150
                };

                var hint = new Label
                {
                    Text = "по умолчанию — 2.5 высоты текста",
                    AutoSize = false,
                    Width = 200,
                    Height = 18,
                    ForeColor = Color.Gray,
                    Location = new Point(180, 91)
                };

                var ok = new Button
                {
                    Text = "Проставить",
                    DialogResult = DialogResult.OK,
                    Location = new Point(195, 135),
                    Width = 80
                };

                var cancel = new Button
                {
                    Text = "Отмена",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(280, 135),
                    Width = 80
                };

                form.Controls.Add(styleLabel);
                form.Controls.Add(styleBox);
                form.Controls.Add(offsetLabel);
                form.Controls.Add(input);
                form.Controls.Add(hint);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                if (form.ShowDialog() != DialogResult.OK)
                    return result;

                result.Ok = true;
                result.OffsetMm = (double)input.Value;
                result.StyleIndex = styleBox.SelectedIndex;
                return result;
            }
        }
    }
}
