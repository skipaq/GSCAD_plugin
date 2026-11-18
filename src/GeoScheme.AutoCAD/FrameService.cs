using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;

namespace GeoScheme.AutoCAD
{
    /// <summary>
    /// Вставка рамки. Блок уникальный (правка одной рамки не задевает другие),
    /// сначала масштабируется под высоту размерного текста, ставится по курсору
    /// центром наружной рамки, после масштабирования разбивается на объекты.
    /// </summary>
    public static class FrameService
    {
        private static ObjectId _pending;
        private static bool _hooked;

        private static ObjectId _editBlock = ObjectId.Null;
        private static string _editPath;
        private static Database _editDb;
        private static int _editStage;

        public static void Insert(Editor ed, Database db, FrameLibrary.FrameInfo frame)
        {
            var scale = GetScale(db, frame);
            var blockId = CreateBlock(db, frame, out var error);

            if (blockId == ObjectId.Null)
            {
                ed.WriteMessage("\nНе удалось прочитать рамку: {0}", error);
                return;
            }

            var preview = new BlockReference(Point3d.Origin, blockId)
            {
                ScaleFactors = new Scale3d(scale)
            };

            var jig = new BlockJig(preview);
            var drag = ed.Drag(jig);

            if (drag.Status != PromptStatus.OK)
                return;

            ObjectId placed;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                var reference = new BlockReference(jig.Position, blockId)
                {
                    ScaleFactors = new Scale3d(scale)
                };

                space.AppendEntity(reference);
                tr.AddNewlyCreatedDBObject(reference, true);
                placed = reference.ObjectId;
                tr.Commit();
            }

            ed.WriteMessage("\nРамка вставлена (масштаб {0:F4}) блоком. Разбить на объекты — GSFRAMEEXPLODE.", scale);
        }

        /// <summary>
        /// Редактирование рамки: вставляем блок, открываем редактор блоков,
        /// после закрытия сохраняем обратно в DXF.
        /// </summary>
        public static void Edit(Editor ed, Database db, FrameLibrary.FrameInfo frame)
        {
            var blockId = CreateBlock(db, frame, out var error);

            if (blockId == ObjectId.Null)
            {
                ed.WriteMessage("\nНе удалось открыть рамку: {0}", error);
                return;
            }

            string name;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                name = ((BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead)).Name;
                tr.Commit();
            }

            _editBlock = blockId;
            _editPath = frame.Path;
            _editDb = db;
            _editStage = 0;

            Application.DocumentManager.DocumentActivated += OnDocumentActivated;

            ed.Command("_.BEDIT", name);
        }

        private static void OnDocumentActivated(object sender,
            Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventArgs e)
        {
            if (_editBlock == ObjectId.Null)
                return;

            _editStage++;

            // Первое переключение — вход в редактор блоков, второе — возврат в чертёж.
            if (_editStage < 2)
                return;

            Application.DocumentManager.DocumentActivated -= OnDocumentActivated;

            var db = _editDb;
            var blockId = _editBlock;
            var path = _editPath;

            _editBlock = ObjectId.Null;
            _editPath = null;
            _editDb = null;

            try
            {
                SaveBlockToDxf(db, blockId, path);
                EraseBlock(db, blockId);

                var doc = Application.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage("\nРамка изменена.");

                // Возвращаемся в окно выбора рамок.
                doc?.SendStringToExecute("GSFRAME ", true, false, false);
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?
                    .Editor.WriteMessage("\nНе удалось сохранить рамку: {0}", ex.Message);
            }
        }

        private static void SaveBlockToDxf(Database db, ObjectId blockId, string path)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var definition = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                var ids = new ObjectIdCollection();

                foreach (var id in definition)
                    ids.Add(id);

                var wblock = db.Wblock(ids, Point3d.Origin);

                try
                {
                    wblock.DxfOut(path, 16, false);
                }
                finally
                {
                    wblock.Dispose();
                }

                tr.Commit();
            }
        }

        private static void EraseBlock(Database db, ObjectId blockId)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var definition = tr.GetObject(blockId, OpenMode.ForWrite, false) as BlockTableRecord;

                if (definition != null && definition.GetBlockReferenceIds(true, false).Count == 0)
                    definition.Erase();

                tr.Commit();
            }
        }

        private static double GetScale(Database db, FrameLibrary.FrameInfo frame)
        {
            var target = DimensionService.AxisTextHeight(db);
            var styleId = DimensionService.PreferredStyle(db);

            if (styleId != ObjectId.Null)
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var style = tr.GetObject(styleId, OpenMode.ForRead) as DimStyleTableRecord;
                    if (style != null && style.Dimtxt > 0)
                        target = style.Dimtxt;

                    tr.Commit();
                }
            }

            if (frame.TextHeight <= 0 || target <= 0)
                return 1.0;

            return target / frame.TextHeight;
        }

        private static ObjectId CreateBlock(Database db, FrameLibrary.FrameInfo frame, out string error)
        {
            error = null;
            var name = "GS_FRAME_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                ObjectId blockId;

                using (var source = new Database(false, true))
                {
                    source.DxfIn(frame.Path, null);
                    blockId = db.Insert(name, source, false);
                }

                // Центр рамки — в начало координат блока, чтобы она вставала под курсор.
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var definition = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForWrite);

                    // Размеры из исходного DXF выбрасываем: они бывают далеко от рамки
                    // и растягивают габариты блока, из-за чего ручки масштаба уезжают.
                    var extra = new List<ObjectId>();

                    foreach (var id in definition)
                    {
                        var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (entity is Dimension)
                            extra.Add(id);
                    }

                    foreach (var id in extra)
                    {
                        var entity = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        entity?.Erase();
                    }

                    var shift = Matrix3d.Displacement(new Vector3d(-frame.CenterX, -frame.CenterY, 0));

                    foreach (var id in definition)
                    {
                        var entity = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        entity?.TransformBy(shift);
                    }

                    tr.Commit();
                }

                return blockId;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return ObjectId.Null;
            }
        }

        public static void ExplodeSelected(Editor ed, Database db)
        {
            var options = new PromptEntityOptions("\nВыберите рамку для разбиения: ");
            options.SetRejectMessage("\nНужен блок.");
            options.AddAllowedClass(typeof(BlockReference), false);

            var pick = ed.GetEntity(options);

            if (pick.Status != PromptStatus.OK)
                return;

            Explode(pick.ObjectId);
        }

        private static void Hook()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;

            if (_hooked)
            {
                doc.CommandEnded -= OnEnded;
                doc.CommandCancelled -= OnCancelled;
            }

            doc.CommandEnded += OnEnded;
            doc.CommandCancelled += OnCancelled;
            _hooked = true;
        }

        private static void Unhook()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null || !_hooked)
                return;

            doc.CommandEnded -= OnEnded;
            doc.CommandCancelled -= OnCancelled;
            _hooked = false;
        }

        private static void OnEnded(object sender, Autodesk.AutoCAD.ApplicationServices.CommandEventArgs e)
        {
            if (!IsScale(e))
                return;

            Unhook();
            Explode(_pending);
            _pending = ObjectId.Null;
        }

        private static void OnCancelled(object sender, Autodesk.AutoCAD.ApplicationServices.CommandEventArgs e)
        {
            if (!IsScale(e))
                return;

            Unhook();
            _pending = ObjectId.Null;
        }

        private static bool IsScale(Autodesk.AutoCAD.ApplicationServices.CommandEventArgs e) =>
            string.Equals(e.GlobalCommandName, "SCALE", StringComparison.OrdinalIgnoreCase);

        public static void Explode(ObjectId id)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || id == ObjectId.Null)
                return;

            var db = doc.Database;

            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var reference = tr.GetObject(id, OpenMode.ForWrite, false) as BlockReference;
                    if (reference == null)
                        return;

                    var objects = new DBObjectCollection();
                    reference.Explode(objects);

                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    foreach (DBObject obj in objects)
                    {
                        var entity = obj as Entity;
                        if (entity == null)
                            continue;

                        space.AppendEntity(entity);
                        tr.AddNewlyCreatedDBObject(entity, true);
                    }

                    reference.Erase();
                    tr.Commit();
                }

                doc.Editor.WriteMessage("\nРамка разбита на объекты.");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\nНе удалось разбить рамку: {0}", ex.Message);
            }
        }

        /// <summary>Тест без UI.</summary>
        public static void Test(Editor ed, Database db)
        {
            var frames = FrameLibrary.Load();

            if (frames.Count == 0)
            {
                ed.WriteMessage("\nРамки не найдены в {0}", FrameLibrary.BuiltInFolder);
                return;
            }

            var frame = frames[0];
            var scale = GetScale(db, frame);
            var blockId = CreateBlock(db, frame, out var error);

            if (blockId == ObjectId.Null)
            {
                ed.WriteMessage("\nОшибка: {0}", error);
                return;
            }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var reference = new BlockReference(Point3d.Origin, blockId)
                {
                    ScaleFactors = new Scale3d(scale)
                };

                space.AppendEntity(reference);
                tr.AddNewlyCreatedDBObject(reference, true);

                var objects = new DBObjectCollection();
                reference.Explode(objects);

                foreach (DBObject obj in objects)
                {
                    var entity = obj as Entity;
                    if (entity == null)
                        continue;

                    space.AppendEntity(entity);
                    tr.AddNewlyCreatedDBObject(entity, true);
                }

                reference.Erase();
                tr.Commit();
            }

            ed.WriteMessage("\nРамка «{0}»: {1:F0} × {2:F0}, текст {3:F3}, масштаб {4:F4}.",
                frame.Name, frame.Width, frame.Height, frame.TextHeight, scale);
        }

        private sealed class BlockJig : EntityJig
        {
            private Point3d _position;

            public BlockJig(BlockReference reference)
                : base(reference)
            {
                _position = reference.Position;
            }

            public Point3d Position => _position;

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions("\nТочка вставки рамки (центр): ");
                var result = prompts.AcquirePoint(options);

                if (result.Status != PromptStatus.OK)
                    return SamplerStatus.Cancel;

                if (result.Value.DistanceTo(_position) < 1e-9)
                    return SamplerStatus.NoChange;

                _position = result.Value;
                return SamplerStatus.OK;
            }

            protected override bool Update()
            {
                ((BlockReference)Entity).Position = _position;
                return true;
            }
        }
    }
}
