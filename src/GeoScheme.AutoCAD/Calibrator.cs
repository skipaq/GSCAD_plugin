using System;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace GeoScheme.AutoCAD
{
    /// <summary>
    /// Калибровка импортированного листа: две точки + реальный размер в мм.
    /// </summary>
    public static class Calibrator
    {
        public static bool Run(Editor ed, Database db, out ObjectId[] selected)
        {
            selected = null;

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return false;

            EnsureUnits(db, ed);

            var selOpts = new PromptSelectionOptions
            {
                MessageForAdding = "\nВыберите импортированный лист (или его часть) для калибровки: "
            };

            var sel = ed.GetSelection(selOpts);
            if (sel.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nКалибровка отменена.");
                return false;
            }

            var p1 = ed.GetPoint("\nПервая точка известного размера: ");
            if (p1.Status != PromptStatus.OK)
                return false;

            var p2Opts = new PromptPointOptions("\nВторая точка известного размера: ")
            {
                BasePoint = p1.Value,
                UseBasePoint = true
            };
            var p2 = ed.GetPoint(p2Opts);
            if (p2.Status != PromptStatus.OK)
                return false;

            double measured = p1.Value.DistanceTo(p2.Value);
            if (measured <= 1e-9)
            {
                ed.WriteMessage("\nТочки совпадают, калибровка невозможна.");
                return false;
            }

            var distOpts = new PromptDoubleOptions("\nРеальный размер между точками, мм: ")
            {
                AllowNone = false,
                AllowNegative = false,
                AllowZero = false
            };
            var dist = ed.GetDouble(distOpts);
            if (dist.Status != PromptStatus.OK)
                return false;

            double mmPerUnit = DrawingSettings.MillimetersPerUnit(db);
            double targetUnits = dist.Value / mmPerUnit;
            double factor = targetUnits / measured;

            if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor))
            {
                ed.WriteMessage("\nНекорректный коэффициент.");
                return false;
            }

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var matrix = Matrix3d.Scaling(factor, p1.Value);

                foreach (var id in sel.Value.GetObjectIds())
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    ent?.TransformBy(matrix);
                }

                tr.Commit();
            }

            selected = sel.Value.GetObjectIds();

            ed.WriteMessage("\nОткалибровано: измерено {0:F3}, задано {1:F0} мм, коэффициент {2:F6}",
                measured, dist.Value, factor);

            return true;
        }

        private static void EnsureUnits(Database db, Editor ed)
        {
            // Если в чертеже ещё нет настроек — спрашиваем один раз и запоминаем.
            if (HasUnits(db))
                return;

            var opts = new PromptKeywordOptions("\nЕдиницы чертежа [Метры/Милиметры] <Метры>: ", "Метры Милиметры");
            opts.Keywords.Default = "Метры";
            var res = ed.GetKeywords(opts);

            var units = res.Status == PromptStatus.OK && res.StringResult == "Милиметры" ? "mm" : "m";
            DrawingSettings.SetUnits(db, units);
            ed.WriteMessage("\nЕдиницы чертежа: {0}", units == "mm" ? "миллиметры" : "метры");
        }

        private static bool HasUnits(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                var has = nod.Contains("GEOSCHEME");
                tr.Commit();
                return has;
            }
        }
    }
}
