using System;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace GeoScheme.AutoCAD
{
    /// <summary>
    /// Команды плагина.
    /// </summary>
    public static class Commands
    {
        [CommandMethod("GSINFO", CommandFlags.Modal)]
        public static void Info()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var ed = doc.Editor;
            ed.WriteMessage("\n=== GeoScheme для AutoCAD v{0} ===", Plugin.Version);
            ed.WriteMessage("\nЧертёж: {0}", doc.Name);
            ed.WriteMessage("\nЕдиницы чертежа: {0}",
                DrawingSettings.GetUnits(doc.Database) == "mm" ? "миллиметры" : "метры");

            try
            {
                ed.WriteMessage("\nСборка: {0}",
                    System.IO.File.GetLastWriteTime(typeof(Plugin).Assembly.Location));
            }
            catch
            {
                // путь к сборке может быть недоступен — не критично
            }
        }

        [CommandMethod("GSPALETTE", CommandFlags.Modal)]
        public static void Palette()
        {
            try
            {
                PaletteHost.Show();
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSIMPORT", CommandFlags.Modal)]
        public static void ImportPdf()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var ed = doc.Editor;
            var db = doc.Database;

            PaletteHost.Hide();

            try
            {
                string path = ImportRequest.PdfPath;
                int page = ImportRequest.Page;

                if (!ImportRequest.HasValue)
                {
                    using (var dlg = new System.Windows.Forms.OpenFileDialog())
                    {
                        dlg.Filter = "PDF (*.pdf)|*.pdf";
                        dlg.Title = "Лист проекта";
                        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                            return;
                        path = dlg.FileName;
                    }

                    var pageOpt = new PromptIntegerOptions("\nНомер листа: ") { LowerLimit = 1 };
                    var pageRes = ed.GetInteger(pageOpt);
                    if (pageRes.Status != PromptStatus.OK)
                        return;
                    page = pageRes.Value;
                }

                ed.WriteMessage("\n1. Импорт листа {0}: {1}", page, System.IO.Path.GetFileName(path));

                var pointRes = ed.GetPoint("\n2. Точка вставки листа (например 0,0): ");
                if (pointRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nИмпорт отменён.");
                    return;
                }

                ed.WriteMessage("\n3. Читаю PDF и вставляю на слой {0}…", PdfImporter.UnderlayLayer);

                if (!PdfImporter.Import(path, page, pointRes.Value, out var error, out var added))
                {
                    ed.WriteMessage("\nНе удалось импортировать: {0}", error);
                    return;
                }

                ed.WriteMessage("\n4. Импортировано объектов: {0}.", added.Count);

                if (BlockBuilder.ZoomTo(ed, db, added))
                    ed.WriteMessage("\n5. Показал лист на экране.");
                else
                    ed.WriteMessage("\n5. Габариты не определились — ищи лист глазами.");

                if (!Calibrator.Run(ed, db, out var selected))
                {
                    ed.WriteMessage("\nЛист вставлен, но без калибровки.");
                    return;
                }

                ed.WriteMessage("\n6. Готово. Лист лежит отдельными объектами на слое {0}.", PdfImporter.UnderlayLayer);
                ed.WriteMessage("\n   Выделить весь лист — GSSELBASE, всё кроме него — GSSELALL.");
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSCALIBR", CommandFlags.Modal)]
        public static void Calibrate()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                Calibrator.Run(doc.Editor, doc.Database, out _);
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSUNITS", CommandFlags.Modal)]
        public static void Units()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                var ed = doc.Editor;
                var opts = new PromptKeywordOptions("\nЕдиницы чертежа [Метры/Милиметры] <Метры>: ", "Метры Милиметры");
                opts.Keywords.Default = "Метры";
                var res = ed.GetKeywords(opts);

                var units = res.Status == PromptStatus.OK && res.StringResult == "Милиметры" ? "mm" : "m";
                DrawingSettings.SetUnits(doc.Database, units);
                ed.WriteMessage("\nЕдиницы чертежа: {0}", units == "mm" ? "миллиметры" : "метры");
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSOBJECT", CommandFlags.Modal)]
        public static void Objects()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                var ed = doc.Editor;
                var selOpts = new PromptSelectionOptions
                {
                    MessageForAdding = "\nВыберите объекты схемы: "
                };

                var sel = ed.GetSelection(selOpts);
                if (sel.Status != PromptStatus.OK)
                    return;

                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var layerId = Layers.Ensure(doc.Database, tr, Layers.Objects, Layers.ObjectsColor, true);
                    var count = 0;

                    foreach (var id in sel.Value.GetObjectIds())
                    {
                        var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent == null)
                            continue;

                        ent.LayerId = layerId;
                        ent.ColorIndex = 256; // поСлою
                        count++;
                    }

                    tr.Commit();
                    ed.WriteMessage("\nПеренесено на слой {0}: {1} объектов.", Layers.Objects, count);
                }
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSSETBASE", CommandFlags.Modal)]
        public static void MarkAsUnderlay()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                var ed = doc.Editor;
                var db = doc.Database;

                var sel = ed.GetSelection(new PromptSelectionOptions
                {
                    MessageForAdding = "\nВыберите объекты, которые считаются подложкой: "
                });

                if (sel.Status != PromptStatus.OK)
                    return;

                var underlayLayer = PdfImporter.GetOrCreateUnderlayLayer(db);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var count = 0;
                    foreach (var id in sel.Value.GetObjectIds())
                    {
                        var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent == null)
                            continue;

                        ent.LayerId = underlayLayer;
                        count++;
                    }

                    tr.Commit();
                    ed.WriteMessage("\nПеренесено на слой {0}: {1} объектов.", PdfImporter.UnderlayLayer, count);
                }
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSSELBASE", CommandFlags.Modal)]
        public static void SelectUnderlay()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                var db = doc.Database;
                var ids = new System.Collections.Generic.List<ObjectId>();

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var msp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                    foreach (var id in msp)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent != null && ent.Layer == Layers.Underlay)
                            ids.Add(id);
                    }

                    tr.Commit();
                }

                doc.Editor.SetImpliedSelection(ids.ToArray());
                doc.Editor.WriteMessage("\nВыделено объектов подложки: {0}.", ids.Count);
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSSELALL", CommandFlags.Modal)]
        public static void SelectAllButUnderlay()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                var db = doc.Database;
                var ids = new System.Collections.Generic.List<ObjectId>();

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var msp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                    foreach (var id in msp)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null)
                            continue;

                        if (ent.Layer != Layers.Underlay)
                            ids.Add(id);
                    }

                    tr.Commit();
                }

                doc.Editor.SetImpliedSelection(ids.ToArray());
                doc.Editor.WriteMessage("\nВыбрано объектов: {0}. Подложка пропущена.", ids.Count);
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSAXIS", CommandFlags.Modal)]
        public static void Axis()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                AxisBuilder.Run(doc.Editor, doc.Database);
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSDIMMAN", CommandFlags.Modal)]
        public static void DimensionsManual()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                DimensionService.Manual(doc.Editor, doc.Database);
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSDIM", CommandFlags.Modal)]
        public static void DimensionsMenu()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                var choice = DimensionChoiceDialog.Ask();

                if (choice == DimensionChoiceDialog.Manual)
                    DimensionService.Manual(doc.Editor, doc.Database);
                else if (choice == DimensionChoiceDialog.Auto)
                    DimensionService.Auto(doc.Editor, doc.Database);
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSDIMTEST", CommandFlags.Modal)]
        public static void DimensionsTest()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                DimensionService.Test(doc.Editor, doc.Database);
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSDIMAUTO", CommandFlags.Modal)]
        public static void DimensionsAuto()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                DimensionService.Auto(doc.Editor, doc.Database);
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSFACT", CommandFlags.Modal)]
        public static void Fact()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                var type = DeviationChoiceDialog.Ask();
                if (type == null)
                    return;

                if (type == DeviationChoiceDialog.Sizes)
                {
                    DeviationService.Sizes(doc.Editor, doc.Database);
                    return;
                }

                if (type == DeviationChoiceDialog.Heights)
                {
                    DeviationService.Heights(doc.Editor, doc.Database);
                    return;
                }

                doc.Editor.WriteMessage("\nВыбраны {0}. Модуль пока не реализован.", type);
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSFACTTEST", CommandFlags.Modal)]
        public static void FactTest()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                DeviationService.Test(doc.Editor, doc.Database);
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSFRAME", CommandFlags.Modal)]
        public static void Frame()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                var frames = FrameLibrary.Load();

                if (frames.Count == 0)
                {
                    doc.Editor.WriteMessage("\nРамки не найдены. Папка: {0}", FrameLibrary.BuiltInFolder);
                    return;
                }

                var choice = FrameDialog.Ask(frames);
                if (choice == null)
                    return;

                if (choice.Action == FrameSelection.Edit)
                {
                    FrameService.Edit(doc.Editor, doc.Database, choice.Frame);
                    return;
                }

                FrameService.Insert(doc.Editor, doc.Database, choice.Frame);
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSFRAMETEST", CommandFlags.Modal)]
        public static void FrameTest()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                FrameService.Test(doc.Editor, doc.Database);
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSFRAMEEXPLODE", CommandFlags.Modal)]
        public static void FrameExplode()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                FrameService.ExplodeSelected(doc.Editor, doc.Database);
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSFITTEXT", CommandFlags.Modal)]
        public static void FitText()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                TextFitService.Run(doc.Editor, doc.Database);
            }
            catch (System.Exception ex)
            {
                Report(ex);
            }
        }

        [CommandMethod("GSTEXT", CommandFlags.Modal)]
        public static void TextBlocks() => Stub("GSTEXT", "Текстовые блоки.");

        [CommandMethod("GSSTAMP", CommandFlags.Modal)]
        public static void Stamp() => Stub("GSSTAMP", "Штамп.");

        [CommandMethod("GSPDF", CommandFlags.Modal)]
        public static void Pdf() => Stub("GSPDF", "Экспорт в PDF.");

        private static void Stub(string command, string text)
        {
            Application.DocumentManager.MdiActiveDocument?.Editor
                .WriteMessage("\n{0}: каркас команды. {1}", command, text);
        }

        /// <summary>
        /// Любая ошибка должна стать сообщением в командной строке,
        /// а не диалогом «необрабатываемое исключение».
        /// </summary>
        private static void Report(System.Exception ex)
        {
            var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            ed?.WriteMessage("\nОшибка: {0}", PdfImporter.Describe(ex));
        }
    }
}
