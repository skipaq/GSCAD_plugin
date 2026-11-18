using System;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace GeoScheme.AutoCAD
{
    /// <summary>Вставка текстового блока: МТекст едет за курсором, высота — по ближайшему тексту.</summary>
    public static class TextBlockService
    {
        public static void Insert(Editor ed, Database db, TextBlockItem block)
        {
            var height = DefaultHeight(db);

            object ortho = 0;
            object autosnap = 0;

            try { ortho = Application.GetSystemVariable("ORTHOMODE"); } catch { }
            try { autosnap = Application.GetSystemVariable("AUTOSNAP"); } catch { }

            var orthoOn = Convert.ToInt32(ortho) != 0;
            var autosnapValue = Convert.ToInt32(autosnap);
            var polarOn = (autosnapValue & 1) != 0;

            try
            {
                if (orthoOn)
                    Application.SetSystemVariable("ORTHOMODE", 0);

                if (polarOn)
                    Application.SetSystemVariable("AUTOSNAP", autosnapValue & ~1);

                var mtext = new MText
                {
                    Contents = block.Text,
                    Location = Point3d.Origin,
                    TextHeight = height,
                    Attachment = AttachmentPoint.MiddleCenter,
                    Width = 0
                };

                var jig = new MTextJig(mtext);
                var drag = ed.Drag(jig);

                if (drag.Status != PromptStatus.OK)
                    return;

                var point = jig.Location;
                var target = NearestTextHeight(db, point, height);

                mtext.TextHeight = target;
                mtext.Location = point;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    space.AppendEntity(mtext);
                    tr.AddNewlyCreatedDBObject(mtext, true);
                    tr.Commit();
                }
            }
            finally
            {
                if (orthoOn)
                    Application.SetSystemVariable("ORTHOMODE", ortho);

                if (polarOn)
                    Application.SetSystemVariable("AUTOSNAP", autosnap);
            }
        }

        private static double DefaultHeight(Database db)
        {
            var height = DimensionService.AxisTextHeight(db);
            return height > 0 ? height : 0.15;
        }

        /// <summary>Высота ближайшего текста к точке вставки.</summary>
        private static double NearestTextHeight(Database db, Point3d point, double fallback)
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

            // Слишком далеко — берём значение по умолчанию.
            if (height <= 0 || best > fallback * 100)
                return fallback;

            return height;
        }

        private sealed class MTextJig : EntityJig
        {
            private Point3d _location;

            public MTextJig(MText mtext)
                : base(mtext)
            {
                _location = mtext.Location;
            }

            public Point3d Location => _location;

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions("\nТочка вставки текста: ");
                var result = prompts.AcquirePoint(options);

                if (result.Status != PromptStatus.OK)
                    return SamplerStatus.Cancel;

                if (result.Value.DistanceTo(_location) < 1e-9)
                    return SamplerStatus.NoChange;

                _location = result.Value;
                return SamplerStatus.OK;
            }

            protected override bool Update()
            {
                ((MText)Entity).Location = _location;
                return true;
            }
        }
    }
}
