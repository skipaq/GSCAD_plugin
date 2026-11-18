using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GeoScheme.AutoCAD
{
    /// <summary>Выбор рамки с миниатюрой.</summary>
    public static class FrameDialog
    {
        public static FrameLibrary.FrameInfo Ask(List<FrameLibrary.FrameInfo> frames)
        {
            if (frames == null || frames.Count == 0)
                return null;

            using (var form = new Form())
            {
                form.Text = "Рамка";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(620, 430);

                var list = new ListBox
                {
                    Location = new Point(15, 15),
                    Size = new Size(220, 340),
                    IntegralHeight = false
                };

                foreach (var frame in frames)
                    list.Items.Add(frame.Name);

                if (list.Items.Count > 0)
                    list.SelectedIndex = 0;

                var preview = new Panel
                {
                    Location = new Point(250, 15),
                    Size = new Size(350, 340),
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.FromArgb(43, 47, 54)
                };

                var sizeLabel = new Label
                {
                    Location = new Point(250, 362),
                    Width = 350,
                    Height = 18,
                    ForeColor = Color.Gray
                };

                preview.Paint += (s, e) =>
                {
                    if (list.SelectedIndex < 0)
                        return;

                    var frame = frames[list.SelectedIndex];
                    DrawFrame(e.Graphics, preview.ClientSize, frame);
                };

                list.SelectedIndexChanged += (s, e) =>
                {
                    preview.Invalidate();

                    if (list.SelectedIndex >= 0)
                    {
                        var frame = frames[list.SelectedIndex];
                        sizeLabel.Text = $"{frame.Width:F0} × {frame.Height:F0}";
                    }
                };

                var add = new Button
                {
                    Text = "＋ Добавить свою рамку",
                    Location = new Point(15, 362),
                    Width = 220,
                    Height = 30
                };

                add.Click += (s, e) =>
                {
                    using (var dlg = new OpenFileDialog())
                    {
                        dlg.Filter = "DXF (*.dxf)|*.dxf";
                        dlg.Title = "Своя рамка";

                        if (dlg.ShowDialog() != DialogResult.OK)
                            return;

                        if (!FrameLibrary.AddCustom(dlg.FileName, out var error))
                        {
                            MessageBox.Show("Не удалось добавить рамку: " + error, "Ошибка");
                            return;
                        }

                        var info = FrameLibrary.Read(dlg.FileName);
                        if (info == null)
                        {
                            MessageBox.Show("Не удалось прочитать DXF.", "Ошибка");
                            return;
                        }

                        frames.Add(info);
                        list.Items.Add(info.Name);
                        list.SelectedIndex = list.Items.Count - 1;
                    }
                };

                var insert = new Button
                {
                    Text = "Вставить",
                    DialogResult = DialogResult.OK,
                    Location = new Point(440, 390),
                    Width = 80
                };

                var cancel = new Button
                {
                    Text = "Отмена",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(525, 390),
                    Width = 80
                };

                form.Controls.Add(list);
                form.Controls.Add(preview);
                form.Controls.Add(sizeLabel);
                form.Controls.Add(add);
                form.Controls.Add(insert);
                form.Controls.Add(cancel);
                form.AcceptButton = insert;
                form.CancelButton = cancel;

                if (form.ShowDialog() != DialogResult.OK || list.SelectedIndex < 0)
                    return null;

                return frames[list.SelectedIndex];
            }
        }

        private static void DrawFrame(Graphics g, Size size, FrameLibrary.FrameInfo frame)
        {
            if (frame.Segments.Count == 0 || frame.Width <= 0 || frame.Height <= 0)
                return;

            const float margin = 12;
            var scale = Math.Min(
                (size.Width - 2 * margin) / (float)frame.Width,
                (size.Height - 2 * margin) / (float)frame.Height);

            var centerX = size.Width / 2f;
            var centerY = size.Height / 2f;

            using (var pen = new Pen(Color.FromArgb(230, 230, 230), 1f))
            {
                foreach (var segment in frame.Segments)
                {
                    var x1 = centerX + (float)(segment.x1 - frame.CenterX) * scale;
                    var y1 = centerY - (float)(segment.y1 - frame.CenterY) * scale;
                    var x2 = centerX + (float)(segment.x2 - frame.CenterX) * scale;
                    var y2 = centerY - (float)(segment.y2 - frame.CenterY) * scale;

                    g.DrawLine(pen, x1, y1, x2, y2);
                }
            }
        }
    }
}
