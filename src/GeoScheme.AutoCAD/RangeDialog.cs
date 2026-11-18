using System;
using System.Drawing;
using System.Windows.Forms;

namespace GeoScheme.AutoCAD
{
    /// <summary>Диапазон отклонений: ОТ и ДО.</summary>
    public static class RangeDialog
    {
        public sealed class Result
        {
            public bool Ok;
            public int From;
            public int To;
        }

        public static Result Ask(int defaultFrom = -5, int defaultTo = 5)
        {
            var result = new Result();

            using (var form = new Form())
            {
                form.Text = "Фактические размеры — диапазон отклонений";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(320, 150);

                var fromLabel = new Label
                {
                    Text = "ОТ:",
                    AutoSize = false,
                    Width = 40,
                    Height = 20,
                    Location = new Point(15, 20)
                };

                var fromBox = new NumericUpDown
                {
                    Minimum = -100000,
                    Maximum = 100000,
                    DecimalPlaces = 0,
                    Value = defaultFrom,
                    Location = new Point(60, 18),
                    Width = 90
                };

                var toLabel = new Label
                {
                    Text = "ДО:",
                    AutoSize = false,
                    Width = 40,
                    Height = 20,
                    Location = new Point(165, 20)
                };

                var toBox = new NumericUpDown
                {
                    Minimum = -100000,
                    Maximum = 100000,
                    DecimalPlaces = 0,
                    Value = defaultTo,
                    Location = new Point(210, 18),
                    Width = 90
                };

                var hint = new Label
                {
                    Text = "например −5 и +5: размер 100 даст 95…105",
                    AutoSize = false,
                    Width = 290,
                    Height = 18,
                    ForeColor = Color.Gray,
                    Location = new Point(15, 50)
                };

                var ok = new Button
                {
                    Text = "ОК",
                    DialogResult = DialogResult.OK,
                    Location = new Point(135, 90),
                    Width = 80
                };

                var cancel = new Button
                {
                    Text = "Отмена",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(222, 90),
                    Width = 80
                };

                form.Controls.Add(fromLabel);
                form.Controls.Add(fromBox);
                form.Controls.Add(toLabel);
                form.Controls.Add(toBox);
                form.Controls.Add(hint);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                if (form.ShowDialog() != DialogResult.OK)
                    return result;

                result.Ok = true;
                result.From = (int)fromBox.Value;
                result.To = (int)toBox.Value;

                if (result.From > result.To)
                {
                    var tmp = result.From;
                    result.From = result.To;
                    result.To = tmp;
                }

                return result;
            }
        }
    }
}
