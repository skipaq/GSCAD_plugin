using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace GeoScheme.AutoCAD
{
    /// <summary>
    /// Отклонения.
    /// Фактические размеры: на каждый выбранный размер ставится такой же размер
    /// (инвертированный — текст под линией) с фактическим значением.
    /// </summary>
    public static class DeviationService
    {
        public static void Sizes(Editor ed, Database db)
        {
            var sel = ed.GetSelection(new PromptSelectionOptions
            {
                MessageForAdding = "\nВыберите размеры (можно весь чертёж, возьмутся только размеры): "
            });

            if (sel.Status != PromptStatus.OK)
                return;

            var dimensions = new List<ObjectId>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in sel.Value.GetObjectIds())
                    if (tr.GetObject(id, OpenMode.ForRead) is Dimension)
                        dimensions.Add(id);

                tr.Commit();
            }

            if (dimensions.Count == 0)
            {
                ed.WriteMessage("\nВ выделении нет размеров.");
                return;
            }

            var range = RangeDialog.Ask();
            if (!range.Ok)
                return;

            Create(ed, db, dimensions, range.From, range.To, false);
        }

        public static void Test(Editor ed, Database db)
        {
            var dimensions = new List<ObjectId>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var msp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (var id in msp)
                    if (tr.GetObject(id, OpenMode.ForRead) is Dimension)
                        dimensions.Add(id);

                tr.Commit();
            }

            Create(ed, db, dimensions, -5, 5, true);
        }

        private static void Create(Editor ed, Database db, List<ObjectId> dimensions,
            int from, int to, bool debug)
        {
            var random = new Random();
            var created = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layerId = Layers.Ensure(db, tr, Layers.DimensionsFact, Layers.DimensionsFactColor, true);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                foreach (var id in dimensions)
                {
                    try
                    {
                        var source = tr.GetObject(id, OpenMode.ForRead) as Dimension;
                        if (source == null)
                            continue;

                        var lfac = GetLfac(tr, source);
                        var nominal = (int)Math.Round(Measure(source) * lfac);
                        var actual = nominal + random.Next(from, to + 1);

                        // Копия размера на том же месте, только текст под линией.
                        var dimension = (Dimension)source.Clone();
                        space.AppendEntity(dimension);
                        tr.AddNewlyCreatedDBObject(dimension, true);

                        dimension.LayerId = layerId;
                        dimension.DimensionText = actual.ToString();
                        dimension.Dimtad = 4; // 4 = текст под размерной линией
                        dimension.UsingDefaultTextPosition = true;

                        space.AppendEntity(dimension);
                        tr.AddNewlyCreatedDBObject(dimension, true);
                        created++;
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage("\n  размер пропущен: {0}", ex.Message);
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage("\nФактических размеров проставлено: {0}. Слой {1}.",
                created, Layers.DimensionsFact);

            if (debug)
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var msp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                    foreach (var id in msp)
                    {
                        var dim = tr.GetObject(id, OpenMode.ForRead) as Dimension;
                        if (dim == null || dim.Layer != Layers.DimensionsFact)
                            continue;

                        ed.WriteMessage("\n  DEBUG факт: text={0} h={1:F3} pos=({2:F0},{3:F0}) meas={4:F0}",
                            dim.DimensionText, dim.Dimtxt, dim.TextPosition.X, dim.TextPosition.Y, dim.Measurement);
                    }

                    tr.Commit();
                }
            }
        }

        /// <summary>Фактическая высота текста размера: свой параметр, иначе из стиля.</summary>
        public static double ActualTextHeight(Transaction tr, Dimension dimension)
        {
            if (dimension.Dimtxt > 0)
                return dimension.Dimtxt;

            var style = tr.GetObject(dimension.DimensionStyle, OpenMode.ForRead) as DimStyleTableRecord;
            return style != null && style.Dimtxt > 0 ? style.Dimtxt : 1.0;
        }

        private static double GetLfac(Transaction tr, Dimension dimension)
        {
            var style = tr.GetObject(dimension.DimensionStyle, OpenMode.ForRead) as DimStyleTableRecord;
            var lfac = style?.Dimlfac ?? 1.0;
            return lfac <= 0 ? 1.0 : lfac;
        }

        private static double Measure(Dimension dimension)
        {
            var a = XLine1(dimension);
            var b = XLine2(dimension);

            if (dimension is RotatedDimension rotated)
            {
                var direction = new Vector3d(Math.Cos(rotated.Rotation), Math.Sin(rotated.Rotation), 0);
                return Math.Abs((b - a).DotProduct(direction));
            }

            return a.DistanceTo(b);
        }

        private static Point3d XLine1(Dimension dimension)
        {
            switch (dimension)
            {
                case RotatedDimension rotated:
                    return rotated.XLine1Point;
                case AlignedDimension aligned:
                    return aligned.XLine1Point;
                default:
                    return Point3d.Origin;
            }
        }

        private static Point3d XLine2(Dimension dimension)
        {
            switch (dimension)
            {
                case RotatedDimension rotated:
                    return rotated.XLine2Point;
                case AlignedDimension aligned:
                    return aligned.XLine2Point;
                default:
                    return Point3d.Origin;
            }
        }

        private static Point3d DimLinePoint(Dimension dimension)
        {
            switch (dimension)
            {
                case RotatedDimension rotated:
                    return rotated.DimLinePoint;
                case AlignedDimension aligned:
                    return aligned.DimLinePoint;
                default:
                    return XLine1(dimension);
            }
        }

        private static double RotationOf(Dimension dimension)
        {
            if (dimension is RotatedDimension rotated)
                return rotated.Rotation;

            var a = XLine1(dimension);
            var b = XLine2(dimension);
            return Math.Atan2(b.Y - a.Y, b.X - a.X);
        }

        // ------------------------------------------------- высотные отклонения

        public static void Heights(Editor ed, Database db)
        {
            var options = DimensionService.GetStyleOptions(db);

            if (options.Count == 0)
            {
                ed.WriteMessage("\nВ чертеже нет размерных стилей.");
                return;
            }

            var axisHeight = DimensionService.AxisTextHeight(db);
            var defaultIndex = 0;
            var best = double.MaxValue;

            for (var i = 0; i < options.Count; i++)
            {
                if (options[i].TextHeight <= 0)
                    continue;

                var delta = Math.Abs(options[i].TextHeight - axisHeight);
                if (delta < best)
                {
                    best = delta;
                    defaultIndex = i;
                }
            }

            var names = options.Select(o => o.Name).ToList();
            var heights = options.Select(o => o.TextHeight).ToList();

            var dialog = HeightDeviationDialog.Ask(names, heights, defaultIndex,
                options[defaultIndex].TextHeight > 0 ? options[defaultIndex].TextHeight : axisHeight);

            if (!dialog.Ok)
                return;

            var height = dialog.Height;
            var radius = dialog.Diameter / 2;

            var textStyle = dialog.StyleIndex >= 0 && dialog.StyleIndex < options.Count
                ? options[dialog.StyleIndex].TextStyle
                : options[defaultIndex].TextStyle;

            var random = new Random();

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

                ed.WriteMessage("\nВыберите места для простановки отклонений (Esc или Enter — выход).");

                var lastPoint = Point3d.Origin;

                while (true)
                {
                    var jig = new CirclePreviewJig(lastPoint, radius);
                    var drag = ed.Drag(jig);

                    if (drag.Status != PromptStatus.OK)
                        break;

                    var point = jig.Center;
                    lastPoint = point;

                    var value = random.Next(dialog.From, dialog.To + 1);
                    var label = (value >= 0 ? "+" : "") + value;

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var layerId = Layers.Ensure(db, tr, Layers.Deviations, Layers.DeviationsColor, true);
                        var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                        var circle = new Circle(point, Vector3d.ZAxis, radius) { LayerId = layerId };
                        space.AppendEntity(circle);
                        tr.AddNewlyCreatedDBObject(circle, true);

                        var text = new DBText
                        {
                            TextString = label,
                            Height = height,
                            LayerId = layerId,
                            HorizontalMode = TextHorizontalMode.TextCenter,
                            VerticalMode = TextVerticalMode.TextVerticalMid,
                            Position = point
                        };

                        if (textStyle != ObjectId.Null)
                            text.TextStyleId = textStyle;

                        text.AlignmentPoint = point;

                        space.AppendEntity(text);
                        tr.AddNewlyCreatedDBObject(text, true);
                        tr.Commit();
                    }
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

        /// <summary>Контур кружка, который едет за курсором.</summary>
        private sealed class CirclePreviewJig : EntityJig
        {
            private Point3d _center;

            public CirclePreviewJig(Point3d start, double radius)
                : base(new Circle(start, Vector3d.ZAxis, radius))
            {
                _center = start;
            }

            public Point3d Center => _center;

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions("\nТочка отклонения: ");
                var result = prompts.AcquirePoint(options);

                if (result.Status != PromptStatus.OK)
                    return SamplerStatus.Cancel;

                if (result.Value.DistanceTo(_center) < 1e-9)
                    return SamplerStatus.NoChange;

                _center = result.Value;
                return SamplerStatus.OK;
            }

            protected override bool Update()
            {
                ((Circle)Entity).Center = _center;
                return true;
            }
        }
    }
}
