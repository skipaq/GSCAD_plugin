using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GeoScheme.AutoCAD
{
    /// <summary>
    /// Окно высотных отклонений: диапазон, высота текста и диаметр кружка.
    /// Высоту можно взять из размерного стиля или ввести вручную.
    /// </summary>
    public static class HeightDeviationDialog
    {
        public sealed class Result
        {
            public bool Ok;
            public int From;
            public int To;
            public double Height;
            public double Diameter;
            public int StyleIndex; // -1 = своя высота
        }

        public static Result Ask(IList<string> styleNames, IList<double> styleHeights,
            int defaultStyleIndex, double defaultHeight)
        {
            var result = new Result();

            using (var form = new Form())
            {
                form.Text = "Высотные отклонения";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(380, 300);

                var rangeLabel = new Label
                {
                    Text = "Диапазон отклонений:",
                    Location = new Point(15, 12),
                    Width = 200,
                    Height = 18
                };

                var fromLabel = new Label { Text = "ОТ:", Location = new Point(15, 40), Width = 30 };
                var fromBox = new NumericUpDown
                {
                    Minimum = -100000,
                    Maximum = 100000,
                    DecimalPlaces = 0,
                    Value = -5,
                    Location = new Point(48, 38),
                    Width = 90
                };

                var toLabel = new Label { Text = "ДО:", Location = new Point(160, 40), Width = 30 };
                var toBox = new NumericUpDown
                {
                    Minimum = -100000,
                    Maximum = 100000,
                    DecimalPlaces = 0,
                    Value = 5,
                    Location = new Point(193, 38),
                    Width = 90
                };

                var styleLabel = new Label
                {
                    Text = "Высота текста:",
                    Location = new Point(15, 80),
                    Width = 200,
                    Height = 18
                };

                var styleBox = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Location = new Point(15, 100),
                    Width = 340
                };

                styleBox.Items.Add("— ввести свою высоту —");

                for (var i = 0; i < styleNames.Count; i++)
                    styleBox.Items.Add(styleNames[i]);

                styleBox.SelectedIndex = Math.Max(0,
                    Math.Min(defaultStyleIndex + 1, styleNames.Count));

                var heightBox = new NumericUpDown
                {
                    Minimum = 0.001M,
                    Maximum = 100000M,
                    DecimalPlaces = 3,
                    Increment = 0.01M,
                    Value = (decimal)defaultHeight,
                    Location = new Point(15, 128),
                    Width = 120
                };

                var heightHint = new Label
                {
                    Text = "высота из стиля",
                    Location = new Point(145, 131),
                    Width = 150,
                    Height = 18,
                    ForeColor = Color.Gray
                };

                var diameterLabel = new Label
                {
                    Text = "Диаметр кружка:",
                    Location = new Point(15, 170),
                    Width = 200,
                    Height = 18
                };

                var diameterBox = new NumericUpDown
                {
                    Minimum = 0.001M,
                    Maximum = 100000M,
                    DecimalPlaces = 3,
                    Increment = 0.01M,
                    Value = (decimal)(defaultHeight * 2),
                    Location = new Point(15, 190),
                    Width = 120
                };

                var diameterHint = new Label
                {
                    Text = "по умолчанию — высота × 2",
                    Location = new Point(145, 193),
                    Width = 200,
                    Height = 18,
                    ForeColor = Color.Gray
                };

                var ok = new Button
                {
                    Text = "ОК",
                    DialogResult = DialogResult.OK,
                    Location = new Point(195, 240),
                    Width = 75
                };

                var cancel = new Button
                {
                    Text = "Отмена",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(280, 240),
                    Width = 75
                };

                var synchronizing = false;

                Action updateFromStyle = () =>
                {
                    if (synchronizing)
                        return;

                    synchronizing = true;

                    var index = styleBox.SelectedIndex - 1;

                    if (index < 0)
                    {
                        heightBox.ReadOnly = false;
                        heightBox.Enabled = true;
                    }
                    else
                    {
                        heightBox.ReadOnly = true;
                        heightBox.Value = (decimal)Math.Max(0.001, styleHeights[index]);
                    }

                    diameterBox.Value = Math.Max(diameterBox.Minimum, heightBox.Value * 2);
                    synchronizing = false;
                };

                styleBox.SelectedIndexChanged += (s, e) => updateFromStyle();
                heightBox.ValueChanged += (s, e) =>
                {
                    if (synchronizing)
                        return;

                    synchronizing = true;
                    diameterBox.Value = Math.Max(diameterBox.Minimum, heightBox.Value * 2);
                    synchronizing = false;
                };

                form.Controls.Add(rangeLabel);
                form.Controls.Add(fromLabel);
                form.Controls.Add(fromBox);
                form.Controls.Add(toLabel);
                form.Controls.Add(toBox);
                form.Controls.Add(styleLabel);
                form.Controls.Add(styleBox);
                form.Controls.Add(heightBox);
                form.Controls.Add(heightHint);
                form.Controls.Add(diameterLabel);
                form.Controls.Add(diameterBox);
                form.Controls.Add(diameterHint);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                updateFromStyle();

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

                result.Height = (double)heightBox.Value;
                result.Diameter = (double)diameterBox.Value;
                result.StyleIndex = styleBox.SelectedIndex - 1;

                return result;
            }
        }
    }
}
