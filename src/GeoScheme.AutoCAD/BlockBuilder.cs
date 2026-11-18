using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace GeoScheme.AutoCAD
{
    /// <summary>
    /// Сборка выделенных объектов в блок.
    /// Делается через API, а не командой -BLOCK: вся операция в одной транзакции,
    /// поэтому при любой ошибке исходные объекты остаются на месте.
    /// </summary>
    public static class BlockBuilder
    {
        public static bool Wrap(Database db, ObjectId[] ids, string baseName, ObjectId layerId,
            out string blockName, out string error)
        {
            blockName = null;
            error = null;

            if (ids == null || ids.Length == 0)
            {
                error = "Нечего собирать в блок.";
                return false;
            }

            if (!GetBasePoint(db, ids, out var min, out _))
            {
                error = "Не удалось определить габариты выделенного.";
                return false;
            }

            var basePoint = new Point3d(min.X, min.Y, 0);

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    blockName = UniqueName(blockTable, baseName);

                    var definition = new BlockTableRecord { Name = blockName };
                    blockTable.Add(definition);
                    tr.AddNewlyCreatedDBObject(definition, true);

                    var msp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var toOrigin = Matrix3d.Displacement(basePoint.GetAsVector().Negate());

                    foreach (var id in ids)
                    {
                        var source = (Entity)tr.GetObject(id, OpenMode.ForWrite);

                        var copy = (Entity)source.Clone();
                        copy.TransformBy(toOrigin);

                        definition.AppendEntity(copy);
                        tr.AddNewlyCreatedDBObject(copy, true);

                        source.Erase();
                    }

                    var reference = new BlockReference(basePoint, definition.ObjectId)
                    {
                        LayerId = layerId
                    };

                    msp.AppendEntity(reference);
                    tr.AddNewlyCreatedDBObject(reference, true);

                    tr.Commit();
                }

                return true;
            }
            catch (System.Exception ex)
            {
                error = PdfImporter.Describe(ex);
                return false;
            }
        }

        /// <summary>Показывает импортированное на экране, чтобы не искать глазами.</summary>
        public static bool ZoomTo(Editor ed, Database db, IEnumerable<ObjectId> ids)
        {
            var list = new List<ObjectId>(ids);
            if (list.Count == 0)
                return false;

            try
            {
                ed.Command("_.ZOOM", "_Object", SelectionSet.FromObjectIds(list.ToArray()), "");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool GetBasePoint(Database db, ObjectId[] ids, out Point3d min, out Point3d max)
        {
            min = new Point3d(double.MaxValue, double.MaxValue, 0);
            max = new Point3d(double.MinValue, double.MinValue, 0);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null)
                        continue;

                    try
                    {
                        var ext = ent.GeometricExtents;
                        min = new Point3d(Math.Min(min.X, ext.MinPoint.X), Math.Min(min.Y, ext.MinPoint.Y), 0);
                        max = new Point3d(Math.Max(max.X, ext.MaxPoint.X), Math.Max(max.Y, ext.MaxPoint.Y), 0);
                    }
                    catch
                    {
                        // у некоторых объектов нет габаритов
                    }
                }

                tr.Commit();
            }

            return min.X <= max.X && min.Y <= max.Y;
        }

        private static string UniqueName(BlockTable table, string baseName)
        {
            var name = baseName;
            var i = 2;
            while (table.Has(name))
                name = baseName + "_" + i++;

            return name;
        }
    }
}
