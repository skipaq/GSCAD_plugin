using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GeoScheme.AutoCAD
{
    /// <summary>Что выбрал пользователь в окне блоков.</summary>
    public sealed class BlockSelection
    {
        public const int Insert = 1;
        public const int Save = 2;

        public int Action;
        public FrameLibrary.FrameInfo Block;
    }

    /// <summary>Окно библиотеки DXF-блоков.</summary>
    public static class BlockDialog
    {
        public static BlockSelection Ask(List<FrameLibrary.FrameInfo> blocks)
        {
            using (var form = new Form())
            {
                form.Text = "Блоки";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(760, 520);

                var list = new ListBox
                {
                    Location = new Point(15, 15),
                    Size = new Size(230, 400),
                    IntegralHeight = false
                };

                var preview = new Panel
                {
                    Location = new Point(260, 15),
                    Size = new Size(485, 400),
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.FromArgb(43, 47, 54)
                };

                var zoom = 1.0;
                var panX = 0.0;
                var panY = 0.0;

                FrameLibrary.FrameInfo Current() =>
                    list.SelectedIndex >= 0 && list.SelectedIndex < blocks.Count
                        ? blocks[list.SelectedIndex]
                        : null;

                void RefreshList()
                {
                    list.Items.Clear();

                    foreach (var block in blocks)
                        list.Items.Add(block.Name);

                    if (list.Items.Count > 0)
                        list.SelectedIndex = 0;

                    preview.Invalidate();
                }

                preview.Paint += (s, e) =>
                {
                    var block = Current();
                    if (block != null)
                        FramePreview.Draw(e.Graphics, preview.ClientSize, block, zoom, panX, panY);
                };

                preview.MouseWheel += (s, e) =>
                {
                    var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
                    var next = Math.Max(0.2, Math.Min(20, zoom * factor));
                    factor = next / zoom;

                    panX = e.X - (e.X - (preview.ClientSize.Width / 2.0 + panX)) * factor
                           - preview.ClientSize.Width / 2.0;
                    panY = e.Y - (e.Y - (preview.ClientSize.Height / 2.0 + panY)) * factor
                           - preview.ClientSize.Height / 2.0;

                    zoom = next;
                    preview.Invalidate();
                };

                list.SelectedIndexChanged += (s, e) =>
                {
                    zoom = 1;
                    panX = 0;
                    panY = 0;
                    preview.Invalidate();
                };

                BlockSelection result = null;

                var save = new Button
                {
                    Text = "Сохранить из выделения",
                    Location = new Point(15, 425),
                    Width = 230,
                    Height = 28
                };

                var copy = new Button { Text = "Копировать", Location = new Point(15, 462), Width = 110, Height = 28 };
                var rename = new Button { Text = "Переименовать", Location = new Point(135, 462), Width = 110, Height = 28 };
                var delete = new Button { Text = "Удалить", Location = new Point(255, 462), Width = 110, Height = 28 };
                var insert = new Button { Text = "Вставить", Location = new Point(575, 462), Width = 80, Height = 28 };
                var cancel = new Button { Text = "Отмена", Location = new Point(665, 462), Width = 80, Height = 28 };

                copy.Enabled = rename.Enabled = delete.Enabled = insert.Enabled = false;

                list.SelectedIndexChanged += (s, e) =>
                {
                    var has = Current() != null;
                    copy.Enabled = rename.Enabled = delete.Enabled = insert.Enabled = has;
                };

                save.Click += (s, e) =>
                {
                    result = new BlockSelection { Action = BlockSelection.Save };
                    form.Close();
                };

                copy.Click += (s, e) =>
                {
                    var block = Current();
                    if (block == null)
                        return;

                    var name = NamePrompt.Ask("Копировать", "Имя нового блока:", block.Name + "_копия");
                    if (name == null)
                        return;

                    if (!BlockLibrary.Copy(block, name, out var error))
                    {
                        MessageBox.Show("Не удалось: " + error, "Ошибка");
                        return;
                    }

                    blocks.Clear();
                    blocks.AddRange(BlockLibrary.Load());
                    RefreshList();
                };

                rename.Click += (s, e) =>
                {
                    var block = Current();
                    if (block == null)
                        return;

                    var name = NamePrompt.Ask("Переименовать", "Новое имя:", block.Name);
                    if (name == null)
                        return;

                    if (!BlockLibrary.Rename(block, name, out var error))
                    {
                        MessageBox.Show("Не удалось: " + error, "Ошибка");
                        return;
                    }

                    blocks.Clear();
                    blocks.AddRange(BlockLibrary.Load());
                    RefreshList();
                };

                delete.Click += (s, e) =>
                {
                    var block = Current();
                    if (block == null)
                        return;

                    if (MessageBox.Show($"Удалить блок «{block.Name}»?", "Удаление",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;

                    if (!BlockLibrary.Delete(block, out var error))
                    {
                        MessageBox.Show("Не удалось: " + error, "Ошибка");
                        return;
                    }

                    blocks.Clear();
                    blocks.AddRange(BlockLibrary.Load());
                    RefreshList();
                };

                insert.Click += (s, e) =>
                {
                    var block = Current();
                    if (block == null)
                        return;

                    result = new BlockSelection { Action = BlockSelection.Insert, Block = block };
                    form.Close();
                };

                list.DoubleClick += (s, e) =>
                {
                    var block = Current();
                    if (block == null)
                        return;

                    result = new BlockSelection { Action = BlockSelection.Insert, Block = block };
                    form.Close();
                };

                cancel.Click += (s, e) => form.Close();

                form.Controls.Add(list);
                form.Controls.Add(preview);
                form.Controls.Add(save);
                form.Controls.Add(copy);
                form.Controls.Add(rename);
                form.Controls.Add(delete);
                form.Controls.Add(insert);
                form.Controls.Add(cancel);
                form.CancelButton = cancel;

                RefreshList();
                form.ShowDialog();

                return result;
            }
        }
    }
}
