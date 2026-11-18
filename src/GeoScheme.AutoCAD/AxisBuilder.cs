using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using GeoScheme.Core;

namespace GeoScheme.AutoCAD
{
    /// <summary>
    /// Оси по линиям подложки. Цикл: линия → (для первой оси) основание → имя → ось.
    /// Первая ось каждого направления задаёт длину и линию основания,
    /// остальные подстраиваются под неё автоматически.
    /// </summary>
    public static class AxisBuilder
    {
        private const double RelativeTolerance = 0.005;
        private const double MinTolerance = 0.01;

        private sealed class OrientationState
        {
            public bool Has;
            public double Baseline;
            public double Length;
        }

        private static OrientationState _vertical = new OrientationState();
        private static OrientationState _horizontal = new OrientationState();
        private static string _document;

        public static void Run(Editor ed, Database db)
        {
            // Каждый вызов команды — новая сессия: опорные точки осей не переносятся.
            _vertical = new OrientationState();
            _horizontal = new OrientationState();

            while (true)
            {
                var options = new PromptEntityOptions("\nВыберите линию оси (Esc — выход): ");
                options.SetRejectMessage("\nНужна линия или полилиния.");
                options.AddAllowedClass(typeof(Line), false);
                options.AddAllowedClass(typeof(Polyline), false);
                options.AddAllowedClass(typeof(Polyline2d), false);

                var pick = ed.GetEntity(options);
                if (pick.Status != PromptStatus.OK)
                    break;

                var points = new List<Point2>();
                CollectPoints(db, new[] { pick.ObjectId }, points);

                var fit = AxisFitter.Fit(points);
                if (fit == null)
                {
                    ed.WriteMessage("\nНе удалось подобрать ось по этой линии.");
                    continue;
                }

                var tolerance = Math.Max(MinTolerance, fit.Length * RelativeTolerance);
                if (fit.MaxDeviation > tolerance)
                {
                    ed.WriteMessage("\nПохоже, это не одна ось: разброс {0:F3} при допуске {1:F3}.",
                        fit.MaxDeviation, tolerance);
                    continue;
                }

                var vertical = Math.Abs(fit.Direction.Y) >= Math.Abs(fit.Direction.X);
                var state = vertical ? _vertical : _horizontal;

                Point2? marker = null;

                // Основание спрашиваем только для первой оси этого направления.
                if (!state.Has)
                {
                    var baseRes = ed.GetPoint("\nТочка основания оси: ");
                    if (baseRes.Status != PromptStatus.OK)
                        break;

                    marker = ToPoint2(baseRes.Value);
                }

                var nameRes = ed.GetString("\nИмя оси: ");
                if (nameRes.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(nameRes.StringResult))
                    break;

                DrawAxis(ed, db, fit, marker, vertical, state, nameRes.StringResult.Trim());
            }

            ed.WriteMessage("\nОси: выход.");
        }

        private static void DrawAxis(Editor ed, Database db, AxisFit fit, Point2? marker,
            bool vertical, OrientationState state, string name)
        {
            var radius = BubbleRadius(db);
            var margin = radius * 2;

            double baseline;
            double length;
            Point2 start;
            Point2 end;

            if (marker.HasValue)
            {
                AxisFitter.Extend(fit, marker, margin, out start, out end);

                var direction = (end - start).Normalized();
                baseline = vertical ? start.Y : start.X;
                length = (end - start).Length;

                if (!state.Has)
                {
                    state.Has = true;
                    state.Baseline = baseline;
                    state.Length = length;
                }
            }
            else
            {
                baseline = state.Baseline;
                length = state.Length;

                var d = fit.Direction;
                var t = vertical
                    ? (baseline - fit.Centroid.Y) / d.Y
                    : (baseline - fit.Centroid.X) / d.X;

                start = fit.At(t);

                // Ведём ось в ту сторону, где лежит геометрия.
                var towardsMax = Math.Abs(t - fit.MinT) <= Math.Abs(t - fit.MaxT);
                var dir = towardsMax ? d : new Point2(-d.X, -d.Y);
                end = start + dir * length;
            }

            var axisDirection = (end - start).Normalized();
            var lineStart = start + axisDirection * radius;
            var linetype = LinetypeHelper.Resolve(db, "осевая", "DASHDOT");

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layerId = Layers.Ensure(db, tr, Layers.Axis, Layers.AxisColor, true);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                var line = new Line(ToPoint3d(lineStart, 0), ToPoint3d(end, 0))
                {
                    LayerId = layerId,
                    Linetype = linetype
                };
                space.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);

                var bubble = new Circle(ToPoint3d(start, 0), Vector3d.ZAxis, radius)
                {
                    LayerId = layerId
                };
                space.AppendEntity(bubble);
                tr.AddNewlyCreatedDBObject(bubble, true);

                var text = new DBText
                {
                    TextString = name,
                    Height = TextHeight(db),
                    LayerId = layerId,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextVerticalMid,
                    Position = ToPoint3d(start, 0),
                    AlignmentPoint = ToPoint3d(start, 0)
                };
                space.AppendEntity(text);
                tr.AddNewlyCreatedDBObject(text, true);

                tr.Commit();
            }

            ed.WriteMessage("\nОсь «{0}»: длина {1:F2}.", name, length);
        }

        private static void ResetIfOtherDocument(string documentName)
        {
            if (_document == documentName)
                return;

            _document = documentName;
            _vertical = new OrientationState();
            _horizontal = new OrientationState();
        }

        private static void CollectPoints(Database db, ObjectId[] ids, List<Point2> points)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null)
                        continue;

                    switch (ent)
                    {
                        case Line line:
                            Add(points, line.StartPoint);
                            Add(points, line.EndPoint);
                            Add(points, Mid(line.StartPoint, line.EndPoint));
                            break;

                        case Polyline pl:
                            for (var i = 0; i < pl.NumberOfVertices; i++)
                            {
                                var p = pl.GetPoint3dAt(i);
                                Add(points, p);
                                if (i > 0)
                                    Add(points, Mid(pl.GetPoint3dAt(i - 1), p));
                            }
                            break;

                        case Polyline2d p2:
                            foreach (var item in p2)
                            {
                                var vert = item as Vertex2d
                                    ?? (item is ObjectId oid
                                        ? tr.GetObject(oid, OpenMode.ForRead) as Vertex2d
                                        : null);
                                if (vert != null)
                                    Add(points, vert.Position);
                            }
                            break;
                    }
                }

                tr.Commit();
            }
        }

        private static void Add(List<Point2> points, Point3d p) => points.Add(ToPoint2(p));

        private static Point3d Mid(Point3d a, Point3d b) =>
            new Point3d((a.X + b.X) / 2, (a.Y + b.Y) / 2, 0);

        private static Point2 ToPoint2(Point3d p) => new Point2(p.X, p.Y);

        private static Point3d ToPoint3d(Point2 p, double z) => new Point3d(p.X, p.Y, z);

        private static double BubbleRadius(Database db) =>
            DrawingSettings.GetUnits(db) == "mm" ? 200.0 : 0.2;

        private static double TextHeight(Database db) =>
            DrawingSettings.GetUnits(db) == "mm" ? 150.0 : 0.15;
    }
}
