using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GeoScheme.AutoCAD
{
    /// <summary>Простой ввод имени.</summary>
    public static class NamePrompt
    {
        public static string Ask(string title, string label, string value)
        {
            using (var form = new Form())
            {
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(360, 120);

                var caption = new Label { Text = label, Location = new Point(15, 12), Width = 330, Height = 18 };

                var box = new TextBox { Text = value, Location = new Point(15, 35), Width = 330 };

                var ok = new Button
                {
                    Text = "ОК",
                    DialogResult = DialogResult.OK,
                    Location = new Point(185, 75),
                    Width = 75
                };

                var cancel = new Button
                {
                    Text = "Отмена",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(270, 75),
                    Width = 75
                };

                form.Controls.Add(caption);
                form.Controls.Add(box);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                if (form.ShowDialog() != DialogResult.OK)
                    return null;

                var result = box.Text.Trim();
                return result.Length == 0 ? null : result;
            }
        }
    }

    /// <summary>Что выбрал пользователь в окне рамок.</summary>
    public sealed class FrameSelection
    {
        public const int Insert = 1;
        public const int Edit = 2;

        public int Action;
        public FrameLibrary.FrameInfo Frame;
    }

    /// <summary>Окно выбора рамки: список, превью с текстом и зумом, управление библиотекой.</summary>
    public static class FrameDialog
    {
        public static FrameSelection Ask(List<FrameLibrary.FrameInfo> frames)
        {
            if (frames == null)
                frames = new List<FrameLibrary.FrameInfo>();

            using (var form = new Form())
            {
                form.Text = "Рамка";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(820, 500);

                var list = new ListBox
                {
                    Location = new Point(15, 15),
                    Size = new Size(230, 370),
                    IntegralHeight = false
                };

                var preview = new Panel
                {
                    Location = new Point(260, 15),
                    Size = new Size(545, 370),
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.FromArgb(43, 47, 54)
                };

                var sizeLabel = new Label
                {
                    Location = new Point(260, 390),
                    Width = 545,
                    Height = 18,
                    ForeColor = Color.Gray
                };

                var zoom = 1.0;
                var panX = 0.0;
                var panY = 0.0;

                void Refresh()
                {
                    list.Items.Clear();

                    foreach (var frame in frames)
                        list.Items.Add(frame.Name);

                    if (list.Items.Count > 0)
                        list.SelectedIndex = 0;

                    preview.Invalidate();
                }

                FrameLibrary.FrameInfo Current() =>
                    list.SelectedIndex >= 0 && list.SelectedIndex < frames.Count
                        ? frames[list.SelectedIndex]
                        : null;

                preview.Paint += (s, e) =>
                {
                    var frame = Current();
                    if (frame == null)
                        return;

                    FramePreview.Draw(e.Graphics, preview.ClientSize, frame, zoom, panX, panY);
                };

                preview.MouseWheel += (s, e) =>
                {
                    var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
                    var next = Math.Max(0.2, Math.Min(20, zoom * factor));
                    factor = next / zoom;

                    // Точка под курсором должна остаться на месте.
                    var mx = e.X;
                    var my = e.Y;

                    panX = mx - (mx - (preview.ClientSize.Width / 2.0 + panX)) * factor
                           - preview.ClientSize.Width / 2.0;
                    panY = my - (my - (preview.ClientSize.Height / 2.0 + panY)) * factor
                           - preview.ClientSize.Height / 2.0;

                    zoom = next;
                    preview.Invalidate();
                };

                list.SelectedIndexChanged += (s, e) =>
                {
                    var frame = Current();
                    sizeLabel.Text = frame == null ? string.Empty : $"{frame.Width:F0} × {frame.Height:F0}";
                    zoom = 1.0;
                    panX = 0;
                    panY = 0;
                    preview.Invalidate();
                };

                var based = new Button
                {
                    Text = "Создать на базе данной…",
                    Location = new Point(15, 395),
                    Width = 230,
                    Height = 28,
                    Enabled = false
                };

                var load = new Button
                {
                    Text = "Загрузить рамку из DXF",
                    Location = new Point(15, 428),
                    Width = 230,
                    Height = 28
                };

                var copy = new Button { Text = "Копировать", Location = new Point(255, 428), Width = 125, Height = 28 };
                var rename = new Button { Text = "Переименовать", Location = new Point(385, 428), Width = 135, Height = 28 };
                var edit = new Button { Text = "Редактировать", Location = new Point(525, 428), Width = 135, Height = 28 };
                var delete = new Button { Text = "Удалить", Location = new Point(665, 428), Width = 140, Height = 28 };

                foreach (var button in new[] { copy, rename, edit, delete })
                    button.Enabled = false;

                void UpdateButtons()
                {
                    var has = Current() != null;

                    foreach (var button in new[] { copy, rename, edit, delete })
                        button.Enabled = has;

                    based.Enabled = has;
                }

                list.SelectedIndexChanged += (s, e) => UpdateButtons();

                load.Click += (s, e) =>
                {
                    using (var dlg = new OpenFileDialog())
                    {
                        dlg.Filter = "DXF (*.dxf)|*.dxf";
                        dlg.Title = "Загрузить рамку";

                        if (dlg.ShowDialog() != DialogResult.OK)
                            return;

                        if (!FrameLibrary.AddCustom(dlg.FileName, out var error))
                        {
                            MessageBox.Show("Не удалось добавить: " + error, "Ошибка");
                            return;
                        }

                        frames.Clear();
                        frames.AddRange(FrameLibrary.Load());
                        Refresh();
                        UpdateButtons();
                    }
                };

                based.Click += (s, e) => CopyFrame("Создать на базе данной");
                copy.Click += (s, e) => CopyFrame("Копировать");

                void CopyFrame(string title)
                {
                    var frame = Current();
                    if (frame == null)
                        return;

                    var name = NamePrompt.Ask(title, "Имя новой рамки:", frame.Name + "_копия");
                    if (name == null)
                        return;

                    if (!FrameLibrary.Copy(frame, name, out var error))
                    {
                        MessageBox.Show("Не удалось скопировать: " + error, "Ошибка");
                        return;
                    }

                    frames.Clear();
                    frames.AddRange(FrameLibrary.Load());
                    Refresh();
                    SelectByName(name);
                }

                rename.Click += (s, e) =>
                {
                    var frame = Current();
                    if (frame == null)
                        return;

                    var name = NamePrompt.Ask("Переименовать", "Новое имя рамки:", frame.Name);
                    if (name == null || name == frame.Name)
                        return;

                    if (!FrameLibrary.Rename(frame, name, out var error))
                    {
                        MessageBox.Show("Не удалось переименовать: " + error, "Ошибка");
                        return;
                    }

                    frames.Clear();
                    frames.AddRange(FrameLibrary.Load());
                    Refresh();
                    SelectByName(name);
                };

                delete.Click += (s, e) =>
                {
                    var frame = Current();
                    if (frame == null)
                        return;

                    if (MessageBox.Show($"Удалить рамку «{frame.Name}»?", "Удаление",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;

                    if (!FrameLibrary.Delete(frame, out var error))
                    {
                        MessageBox.Show("Не удалось удалить: " + error, "Ошибка");
                        return;
                    }

                    frames.Clear();
                    frames.AddRange(FrameLibrary.Load());
                    Refresh();
                    UpdateButtons();
                };

                FrameSelection result = null;

                var insert = new Button
                {
                    Text = "Вставить",
                    DialogResult = DialogResult.OK,
                    Location = new Point(575, 462),
                    Width = 130,
                    Height = 28
                };

                var cancelButton = new Button
                {
                    Text = "Отмена",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(710, 462),
                    Width = 95,
                    Height = 28
                };

                edit.Click += (s, e) =>
                {
                    var frame = Current();
                    if (frame == null)
                        return;

                    result = new FrameSelection { Action = FrameSelection.Edit, Frame = frame };
                    form.Close();
                };

                insert.Click += (s, e) =>
                {
                    var frame = Current();
                    if (frame == null)
                        return;

                    result = new FrameSelection { Action = FrameSelection.Insert, Frame = frame };
                    form.Close();
                };

                form.Controls.Add(list);
                form.Controls.Add(preview);
                form.Controls.Add(sizeLabel);
                form.Controls.Add(based);
                form.Controls.Add(load);
                form.Controls.Add(copy);
                form.Controls.Add(rename);
                form.Controls.Add(edit);
                form.Controls.Add(delete);
                form.Controls.Add(insert);
                form.Controls.Add(cancelButton);
                form.AcceptButton = insert;
                form.CancelButton = cancelButton;

                void SelectByName(string name)
                {
                    for (var i = 0; i < list.Items.Count; i++)
                        if (string.Equals(list.Items[i]?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                        {
                            list.SelectedIndex = i;
                            return;
                        }
                }

                Refresh();
                UpdateButtons();

                form.ShowDialog();

                return result;
            }
        }
    }
}
