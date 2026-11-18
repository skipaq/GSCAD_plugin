using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GeoScheme.AutoCAD
{
    /// <summary>Окно библиотеки текстовых блоков.</summary>
    public static class TextBlockDialog
    {
        public static TextBlockItem Ask(List<TextBlockItem> blocks, Action<List<TextBlockItem>> save)
        {
            using (var form = new Form())
            {
                form.Text = "Текстовые блоки";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(740, 500);

                var list = new ListBox
                {
                    Location = new Point(15, 15),
                    Size = new Size(230, 400),
                    IntegralHeight = false
                };

                var preview = new Panel
                {
                    Location = new Point(260, 15),
                    Size = new Size(465, 400),
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.FromArgb(43, 47, 54)
                };

                var tip = new ToolTip { AutoPopDelay = 15000, InitialDelay = 500 };

                TextBlockItem Current() =>
                    list.SelectedIndex >= 0 && list.SelectedIndex < blocks.Count
                        ? blocks[list.SelectedIndex]
                        : null;

                void RefreshList(string select = null)
                {
                    list.Items.Clear();

                    foreach (var block in blocks)
                        list.Items.Add(block.Name);

                    if (select != null)
                    {
                        for (var i = 0; i < list.Items.Count; i++)
                            if (string.Equals(list.Items[i]?.ToString(), select, StringComparison.OrdinalIgnoreCase))
                            {
                                list.SelectedIndex = i;
                                break;
                            }
                    }

                    if (list.SelectedIndex < 0 && list.Items.Count > 0)
                        list.SelectedIndex = 0;

                    preview.Invalidate();
                }

                preview.Paint += (s, e) =>
                {
                    var block = Current();
                    if (block == null)
                        return;

                    using (var font = new Font("Arial", 11f))
                    using (var brush = new SolidBrush(Color.WhiteSmoke))
                    {
                        var rect = new RectangleF(10, 10, preview.ClientSize.Width - 20, preview.ClientSize.Height - 20);
                        e.Graphics.DrawString(block.Text, font, brush, rect);
                    }
                };

                list.SelectedIndexChanged += (s, e) => preview.Invalidate();

                list.MouseMove += (s, e) =>
                {
                    var index = list.IndexFromPoint(e.Location);

                    if (index < 0 || index >= blocks.Count)
                    {
                        tip.SetToolTip(list, string.Empty);
                        return;
                    }

                    var text = blocks[index].Text ?? string.Empty;
                    if (text.Length > 600)
                        text = text.Substring(0, 600) + "…";

                    tip.SetToolTip(list, text);
                };

                TextBlockItem result = null;

                var create = new Button { Text = "Создать блок", Location = new Point(15, 425), Width = 120, Height = 28 };
                var edit = new Button { Text = "Редактировать", Location = new Point(145, 425), Width = 120, Height = 28 };
                var copy = new Button { Text = "Копировать", Location = new Point(15, 460), Width = 120, Height = 28 };
                var rename = new Button { Text = "Переименовать", Location = new Point(145, 460), Width = 120, Height = 28 };
                var delete = new Button { Text = "Удалить", Location = new Point(275, 460), Width = 100, Height = 28 };

                var insert = new Button
                {
                    Text = "Вставить",
                    Location = new Point(555, 460),
                    Width = 80,
                    Height = 28
                };

                var cancel = new Button
                {
                    Text = "Отмена",
                    Location = new Point(645, 460),
                    Width = 80,
                    Height = 28
                };

                edit.Enabled = copy.Enabled = rename.Enabled = delete.Enabled = insert.Enabled = false;

                list.SelectedIndexChanged += (s, e) =>
                {
                    var has = Current() != null;
                    edit.Enabled = copy.Enabled = rename.Enabled = delete.Enabled = insert.Enabled = has;
                };

                create.Click += (s, e) =>
                {
                    var name = NamePrompt.Ask("Новый текстовый блок", "Имя блока:", "Блок " + (blocks.Count + 1));
                    if (name == null)
                        return;

                    var text = TextInputDialog.Ask("Текст блока", string.Empty);
                    if (text == null)
                        return;

                    blocks.Add(new TextBlockItem { Name = name, Text = text });
                    save(blocks);
                    RefreshList(name);
                };

                edit.Click += (s, e) =>
                {
                    var block = Current();
                    if (block == null)
                        return;

                    var text = TextInputDialog.Ask("Редактировать текст", block.Text);
                    if (text == null)
                        return;

                    block.Text = text;
                    save(blocks);
                    RefreshList(block.Name);
                };

                copy.Click += (s, e) =>
                {
                    var block = Current();
                    if (block == null)
                        return;

                    var name = NamePrompt.Ask("Копировать", "Имя нового блока:", block.Name + "_копия");
                    if (name == null)
                        return;

                    blocks.Add(new TextBlockItem { Name = name, Text = block.Text });
                    save(blocks);
                    RefreshList(name);
                };

                rename.Click += (s, e) =>
                {
                    var block = Current();
                    if (block == null)
                        return;

                    var name = NamePrompt.Ask("Переименовать", "Новое имя:", block.Name);
                    if (name == null)
                        return;

                    block.Name = name;
                    save(blocks);
                    RefreshList(name);
                };

                delete.Click += (s, e) =>
                {
                    var block = Current();
                    if (block == null)
                        return;

                    if (MessageBox.Show($"Удалить блок «{block.Name}»?", "Удаление",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;

                    blocks.Remove(block);
                    save(blocks);
                    RefreshList();
                };

                insert.Click += (s, e) =>
                {
                    var block = Current();
                    if (block == null)
                        return;

                    result = block;
                    form.Close();
                };

                list.DoubleClick += (s, e) =>
                {
                    var block = Current();
                    if (block == null)
                        return;

                    result = block;
                    form.Close();
                };

                cancel.Click += (s, e) => form.Close();

                form.Controls.Add(list);
                form.Controls.Add(preview);
                form.Controls.Add(create);
                form.Controls.Add(edit);
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
