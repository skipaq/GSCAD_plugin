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
    /// Подгон шрифта: высота текста в штампе рамки становится целевой,
    /// выбранные объекты подгоняются под неё.
    /// </summary>
    public static class TextFitService
    {
        private enum Kind { Text, MText, Attribute, Dimension }

        private sealed class Target
        {
            public ObjectId Id;
            public Kind Kind;
            public double Original;
        }

        private static TextFitDialog _dialog;

        public static void Run(Editor ed, Database db)
        {
            var sourceOptions = new PromptEntityOptions("\nВыберите рамку (блок), текст или размер со штампом: ");
            sourceOptions.SetRejectMessage("\nНужен блок, текст или размер.");
            sourceOptions.AddAllowedClass(typeof(BlockReference), false);
            sourceOptions.AddAllowedClass(typeof(DBText), false);
            sourceOptions.AddAllowedClass(typeof(MText), false);
            sourceOptions.AddAllowedClass(typeof(Dimension), false);

            var source = ed.GetEntity(sourceOptions);
            if (source.Status != PromptStatus.OK)
                return;

            var stampHeight = SourceTextHeight(db, source.ObjectId);

            if (stampHeight <= 0)
            {
                ed.WriteMessage("\nНе удалось определить высоту текста.");
                return;
            }

            var selection = ed.GetSelection(new PromptSelectionOptions
            {
                MessageForAdding = "\nВыберите объекты для подгона текста: "
            });

            if (selection.Status != PromptStatus.OK)
                return;

            var targets = Collect(db, selection.Value.GetObjectIds());

            if (targets.Count == 0)
            {
                ed.WriteMessage("\nВ выделении нет текста, который можно подогнать.");
                return;
            }

            var current = targets.Max(t => t.Original);

            if (_dialog != null && !_dialog.IsDisposed)
                _dialog.Close();

            _dialog = new TextFitDialog(stampHeight, current,
                value => Apply(db, targets, value),
                () => Apply(db, targets, null));

            _dialog.Show();
        }

        private static void Apply(Database db, List<Target> targets, double? height)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var circles = ReadCircles(db, tr);
                    var usedCircles = new System.Collections.Generic.HashSet<ObjectId>();

                    foreach (var target in targets)
                    {
                        var value = height ?? target.Original;

                        switch (target.Kind)
                        {
                            case Kind.Text:
                                var text = tr.GetObject(target.Id, OpenMode.ForWrite) as DBText;
                                if (text != null)
                                {
                                    var point = text.Justify != AttachmentPoint.BaseLeft
                                        ? text.AlignmentPoint
                                        : text.Position;

                                    text.Height = value;
                                    ScaleCircle(tr, circles, usedCircles, point, target.Original, value);
                                }
                                break;

                            case Kind.MText:
                                var mtext = tr.GetObject(target.Id, OpenMode.ForWrite) as MText;
                                if (mtext != null)
                                {
                                    var point = mtext.Location;
                                    mtext.TextHeight = value;
                                    ScaleCircle(tr, circles, usedCircles, point, target.Original, value);
                                }
                                break;

                            case Kind.Attribute:
                                var attribute = tr.GetObject(target.Id, OpenMode.ForWrite) as AttributeReference;
                                if (attribute != null)
                                {
                                    attribute.Height = value;
                                    ScaleCircle(tr, circles, usedCircles, attribute.Position,
                                        target.Original, value);
                                }
                                break;

                            case Kind.Dimension:
                                var dimension = tr.GetObject(target.Id, OpenMode.ForWrite) as Dimension;
                                if (dimension != null)
                                    dimension.Dimtxt = value;
                                break;
                        }
                    }

                    tr.Commit();
                }

                doc.Editor.Regen();
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\nНе удалось применить: {0}", ex.Message);
            }
        }

        /// <summary>Все окружности текущего пространства: вокруг них могут быть подписи.</summary>
        private static List<(ObjectId Id, Point3d Center, double Radius)> ReadCircles(Database db, Transaction tr)
        {
            var result = new List<(ObjectId, Point3d, double)>();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

            foreach (var id in space)
            {
                var circle = tr.GetObject(id, OpenMode.ForRead) as Circle;
                if (circle != null)
                    result.Add((id, circle.Center, circle.Radius));
            }

            return result;
        }

        /// <summary>
        /// Масштабирует кружок вместе с текстом: кружок, в который попадает подпись
        /// (осевая, отклонение), растёт/уменьшается во столько же раз.
        /// </summary>
        private static void ScaleCircle(Transaction tr,
            List<(ObjectId Id, Point3d Center, double Radius)> circles,
            System.Collections.Generic.HashSet<ObjectId> used,
            Point3d point, double oldHeight, double newHeight)
        {
            if (oldHeight <= 0 || newHeight <= 0)
                return;

            if (Math.Abs(newHeight - oldHeight) < 1e-9)
                return;

            foreach (var item in circles)
            {
                if (used.Contains(item.Id))
                    continue;

                if (item.Center.DistanceTo(point) > item.Radius * 1.3)
                    continue;

                if (item.Radius < oldHeight * 0.3 || item.Radius > oldHeight * 5)
                    continue;

                var circle = tr.GetObject(item.Id, OpenMode.ForWrite) as Circle;
                if (circle == null)
                    continue;

                circle.Radius = item.Radius * (newHeight / oldHeight);
                used.Add(item.Id);
                return;
            }
        }

        private static List<Target> Collect(Database db, ObjectId[] ids)
        {
            var result = new List<Target>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity == null)
                        continue;

                    switch (entity)
                    {
                        case AttributeReference attribute:
                            result.Add(new Target { Id = id, Kind = Kind.Attribute, Original = attribute.Height });
                            break;

                        case MText mtext:
                            result.Add(new Target { Id = id, Kind = Kind.MText, Original = mtext.TextHeight });
                            break;

                        case DBText text:
                            result.Add(new Target { Id = id, Kind = Kind.Text, Original = text.Height });
                            break;

                        case Dimension dimension:
                            result.Add(new Target
                            {
                                Id = id,
                                Kind = Kind.Dimension,
                                Original = EffectiveDimHeight(tr, dimension)
                            });
                            break;
                    }
                }

                tr.Commit();
            }

            return result;
        }

        private static double EffectiveDimHeight(Transaction tr, Dimension dimension)
        {
            if (dimension.Dimtxt > 0)
                return dimension.Dimtxt;

            var style = tr.GetObject(dimension.DimensionStyle, OpenMode.ForRead) as DimStyleTableRecord;
            return style != null && style.Dimtxt > 0 ? style.Dimtxt : 0;
        }

        /// <summary>Высота текста из выбранного объекта: блок, текст или размер.</summary>
        public static double SourceTextHeight(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var entity = tr.GetObject(id, OpenMode.ForRead);

                double height;

                switch (entity)
                {
                    case BlockReference reference:
                        var scale = Math.Abs(reference.ScaleFactors.X);
                        var heights = new List<double>();
                        var definition = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                        CollectTextHeights(tr, definition, Matrix3d.Identity, heights);

                        for (var i = 0; i < heights.Count; i++)
                            heights[i] *= scale;

                        height = MostCommon(heights);
                        break;

                    case Dimension dimension:
                        height = DeviationService.ActualTextHeight(tr, dimension);
                        break;

                    case MText mtext:
                        height = mtext.TextHeight;
                        break;

                    case DBText text:
                        height = text.Height;
                        break;

                    default:
                        height = 0;
                        break;
                }

                tr.Commit();
                return height;
            }
        }

        /// <summary>Высота текста в штампе: чаще всего встречающаяся в блоке рамки.</summary>
        private static double FrameTextHeight(Database db, ObjectId frameId)
        {
            var heights = new List<double>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reference = tr.GetObject(frameId, OpenMode.ForRead) as BlockReference;
                if (reference != null)
                {
                    var scale = Math.Abs(reference.ScaleFactors.X);

                    var definition = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                    CollectTextHeights(tr, definition, Matrix3d.Identity, heights);

                    for (var i = 0; i < heights.Count; i++)
                        heights[i] *= scale;
                }

                tr.Commit();
            }

            return MostCommon(heights);
        }

        private static void CollectTextHeights(Transaction tr, BlockTableRecord space, Matrix3d transform,
            List<double> heights)
        {
            foreach (var id in space)
            {
                Entity entity;

                try
                {
                    entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                }
                catch
                {
                    continue;
                }

                switch (entity)
                {
                    case AttributeDefinition definition:
                        heights.Add(definition.Height * ScaleOf(transform));
                        break;

                    case MText mtext:
                        heights.Add(mtext.TextHeight * ScaleOf(transform));
                        break;

                    case DBText text:
                        heights.Add(text.Height * ScaleOf(transform));
                        break;

                    case BlockReference reference:
                        try
                        {
                            var inner = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                            CollectTextHeights(tr, inner, transform * reference.BlockTransform, heights);
                        }
                        catch
                        {
                            // вложенный блок не прочитался
                        }
                        break;
                }
            }
        }

        private static double ScaleOf(Matrix3d transform)
        {
            var origin = Point3d.Origin.TransformBy(transform);
            var unit = new Point3d(1, 0, 0).TransformBy(transform);
            return origin.DistanceTo(unit);
        }

        private static double MostCommon(List<double> values)
        {
            if (values.Count == 0)
                return 0;

            var best = 0.0;
            var bestCount = 0;

            foreach (var value in values)
            {
                var rounded = Math.Round(value, 3);
                var count = values.Count(other => Math.Abs(Math.Round(other, 3) - rounded) < 1e-6);

                if (count > bestCount)
                {
                    bestCount = count;
                    best = value;
                }
            }

            return best;
        }
    }
}
