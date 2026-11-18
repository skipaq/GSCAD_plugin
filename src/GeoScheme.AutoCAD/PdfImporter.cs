using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace GeoScheme.AutoCAD
{
    /// <summary>
    /// Импорт листа PDF штатным PDFIMPORT на слой «Подложка_ГЕО».
    /// Никаких блоков на этом шаге: сначала вставляем и калибруем,
    /// блок собирается уже после калибровки.
    /// </summary>
    public static class PdfImporter
    {
        public static string UnderlayLayer => Layers.Underlay;

        public static bool Import(string pdfPath, int page, Point3d insertPoint,
            out string error, out List<ObjectId> added)
        {
            error = null;
            added = new List<ObjectId>();

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                error = "Нет активного документа.";
                return false;
            }

            var ed = doc.Editor;
            var db = doc.Database;

            var previousLayer = db.Clayer;
            var underlayLayer = GetOrCreateUnderlayLayer(db, out var createdLayer);

            try
            {
                db.Clayer = underlayLayer;
                return ImportCore(ed, db, pdfPath, page, insertPoint, underlayLayer, out error, out added);
            }
            catch (System.Exception ex)
            {
                error = Describe(ex);
                return false;
            }
            finally
            {
                db.Clayer = previousLayer;

                if (createdLayer)
                    ed.WriteMessage("\nСоздан слой «{0}» (серый, не печатается).", UnderlayLayer);
            }
        }

        private static bool ImportCore(Editor ed, Database db, string pdfPath, int page,
            Point3d insertPoint, ObjectId underlayLayer, out string error, out List<ObjectId> added)
        {
            error = null;
            added = new List<ObjectId>();

            ObjectId[] before;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var msp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                before = msp.Cast<ObjectId>().ToArray();
                tr.Commit();
            }

            object oldFileDia = Application.GetSystemVariable("FILEDIA");
            object oldCmdDia = Application.GetSystemVariable("CMDDIA");

            try
            {
                Application.SetSystemVariable("FILEDIA", 0);
                Application.SetSystemVariable("CMDDIA", 0);

                ed.Command(
                    "_.-PDFIMPORT",
                    "_File",
                    pdfPath,
                    page.ToString(),
                    $"{insertPoint.X},{insertPoint.Y}",
                    "1",
                    "0");
            }
            catch (System.Exception ex)
            {
                error = Describe(ex);
                return false;
            }
            finally
            {
                Application.SetSystemVariable("FILEDIA", oldFileDia);
                Application.SetSystemVariable("CMDDIA", oldCmdDia);
            }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var msp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var known = new HashSet<ObjectId>(before);
                foreach (var id in msp)
                    if (!known.Contains(id))
                        added.Add(id);

                tr.Commit();
            }

            if (added.Count == 0)
            {
                error = "PDFIMPORT отработал, но объектов в модели нет. Проверь номер листа и что он векторный.";
                return false;
            }

            // PDFIMPORT кладёт объекты куда хочет (часто на слой «0»),
            // поэтому принудительно переносим всё на слой подложки.
            MoveToLayer(db, added, underlayLayer);

            return true;
        }

        public static ObjectId GetOrCreateUnderlayLayer(Database db)
        {
            return GetOrCreateUnderlayLayer(db, out _);
        }

        private static void MoveToLayer(Database db, List<ObjectId> ids, ObjectId layerId)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    try
                    {
                        var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent != null && ent.LayerId != layerId)
                            ent.LayerId = layerId;
                    }
                    catch
                    {
                        // объект на заблокированном слое — оставляем как есть
                    }
                }

                tr.Commit();
            }
        }

        public static string Describe(System.Exception ex)
        {
            var text = ex.Message ?? string.Empty;
            if (text.IndexOf("eOnLockedLayer", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Слой заблокирован. Сними блокировку и повтори.";

            return text;
        }

        public static ObjectId GetOrCreateUnderlayLayer(Database db, out bool created)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                created = !table.Has(Layers.Underlay);

                var id = Layers.Ensure(db, tr, Layers.Underlay, Layers.UnderlayColor, false);
                tr.Commit();
                return id;
            }
        }
    }
}
