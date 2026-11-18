using System;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace GeoScheme.AutoCAD
{
    /// <summary>Вставка DXF-блока: масштаб под ближайший текст, блок едет за курсором.</summary>
    public static class BlockService
    {
        public static void Insert(Editor ed, Database db, FrameLibrary.FrameInfo block)
        {
            var defaultHeight = DimensionService.AxisTextHeight(db);
            var previewScale = block.TextHeight > 0 && defaultHeight > 0
                ? defaultHeight / block.TextHeight
                : 1.0;

            var name = "GS_BLOCK_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            ObjectId blockId;

            try
            {
                using (var source = new Database(false, true))
                {
                    source.DxfIn(block.Path, null);
                    blockId = db.Insert(name, source, false);
                }

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var definition = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForWrite);
                    var shift = Matrix3d.Displacement(new Vector3d(-block.CenterX, -block.CenterY, 0));

                    foreach (var id in definition)
                    {
                        var entity = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        entity?.TransformBy(shift);
                    }

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nНе удалось прочитать блок: {0}", ex.Message);
                return;
            }

            object ortho = 0;

            try { ortho = Application.GetSystemVariable("ORTHOMODE"); } catch { }
            var orthoOn = Convert.ToInt32(ortho) != 0;

            try
            {
                if (orthoOn)
                    Application.SetSystemVariable("ORTHOMODE", 0);

                var preview = new BlockReference(Point3d.Origin, blockId)
                {
                    ScaleFactors = new Scale3d(previewScale)
                };

                var jig = new BlockJig(preview);
                var drag = ed.Drag(jig);

                if (drag.Status != PromptStatus.OK)
                    return;

                var point = jig.Position;
                var target = NearestTextHeight(db, point);

                if (target <= 0)
                    target = defaultHeight;

                var scale = block.TextHeight > 0 && target > 0 ? target / block.TextHeight : 1.0;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var reference = new BlockReference(point, blockId)
                    {
                        ScaleFactors = new Scale3d(scale)
                    };

                    space.AppendEntity(reference);
                    tr.AddNewlyCreatedDBObject(reference, true);
                    tr.Commit();
                }

                ed.WriteMessage("\nБлок вставлен (масштаб {0:F4}). Разбить — GSFRAMEEXPLODE.", scale);
            }
            finally
            {
                if (orthoOn)
                    Application.SetSystemVariable("ORTHOMODE", ortho);
            }
        }

        /// <summary>Высота ближайшего к курсору текста.</summary>
        private static double NearestTextHeight(Database db, Point3d point)
        {
            var best = double.MaxValue;
            var height = 0.0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                foreach (var id in space)
                {
                    var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity == null)
                        continue;

                    Point3d position;
                    double candidate;

                    switch (entity)
                    {
                        case MText mtext:
                            position = mtext.Location;
                            candidate = mtext.TextHeight;
                            break;

                        case DBText text:
                            position = text.Position;
                            candidate = text.Height;
                            break;

                        default:
                            continue;
                    }

                    if (candidate <= 0)
                        continue;

                    var distance = position.DistanceTo(point);

                    if (distance < best)
                    {
                        best = distance;
                        height = candidate;
                    }
                }

                tr.Commit();
            }

            return height;
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
                var options = new JigPromptPointOptions("\nТочка вставки блока: ");
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
