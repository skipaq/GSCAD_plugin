using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GeoScheme.AutoCAD
{
    /// <summary>
    /// Немодальное окно подгона шрифта: видно результат в чертеже,
    /// можно применять несколько раз, «Отмена» возвращает как было.
    /// </summary>
    public sealed class TextFitDialog : Form
    {
        private readonly NumericUpDown _height;
        private readonly Action<double> _apply;
        private readonly Action _restore;
        private bool _applied;

        public TextFitDialog(double stampHeight, double currentHeight,
            Action<double> apply, Action restore)
        {
            _apply = apply;
            _restore = restore;

            Text = "Подгон шрифта";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(420, 200);
            TopMost = true;

            var stampLabel = new Label
            {
                Text = "Высота текста в штампе:",
                Location = new Point(15, 18),
                Width = 220,
                Height = 18
            };

            var stampValue = new TextBox
            {
                Text = stampHeight.ToString("0.###"),
                ReadOnly = true,
                Location = new Point(240, 15),
                Width = 150,
                BackColor = SystemColors.Control
            };

            var currentLabel = new Label
            {
                Text = "Высота текста в чертеже:",
                Location = new Point(15, 55),
                Width = 220,
                Height = 18
            };

            _height = new NumericUpDown
            {
                Minimum = 0.001M,
                Maximum = 100000M,
                DecimalPlaces = 3,
                Increment = 0.01M,
                Value = (decimal)Math.Max(0.001, currentHeight),
                Location = new Point(240, 52),
                Width = 150
            };

            var applyButton = new Button
            {
                Text = "Применить",
                Location = new Point(100, 120),
                Width = 95,
                Height = 30
            };

            var applyCloseButton = new Button
            {
                Text = "Применить и закрыть",
                Location = new Point(200, 120),
                Width = 140,
                Height = 30
            };

            var cancelButton = new Button
            {
                Text = "Отмена",
                Location = new Point(345, 120),
                Width = 60,
                Height = 30
            };

            applyButton.Click += (s, e) =>
            {
                _applied = true;
                _apply((double)_height.Value);
            };

            applyCloseButton.Click += (s, e) =>
            {
                _applied = true;
                _apply((double)_height.Value);
                Close();
            };

            cancelButton.Click += (s, e) => Close();

            FormClosing += (s, e) =>
            {
                if (!_applied)
                    _restore();
            };

            Controls.Add(stampLabel);
            Controls.Add(stampValue);
            Controls.Add(currentLabel);
            Controls.Add(_height);
            Controls.Add(applyButton);
            Controls.Add(applyCloseButton);
            Controls.Add(cancelButton);
        }
    }
}
