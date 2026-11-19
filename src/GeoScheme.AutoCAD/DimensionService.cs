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
    /// Модуль автоматических размеров. Написан по правилам заказчика:
    ///
    /// 1. Сначала считаются отступ и размерный стиль (ближайший по высоте текста).
    /// 2. Обход объекта по часовой стрелке, сторона за стороной.
    /// 3. Для каждой стороны находится ближайшая ось и возможны три случая:
    ///      A. ось пересекает сторону — размеры от оси к первому углу и от оси ко второму;
    ///      B. ось перед началом стороны — от начала до оси, затем длина стороны от начала;
    ///      C. ось за концом стороны — от конца к началу, затем от конца до оси.
    /// 4. Текст центрируется, если помещается между точками размера.
    ///    Если не помещается — выносится наружу: первый размер стороны за ближний
    ///    конец, последний — за дальний.
    /// 5. Если размеры соседних сторон накладываются — увеличивается отступ
    ///    у всей стороны.
    /// </summary>
    public static class DimensionService
    {
        private enum Side { Top, Bottom, Left, Right }

        private sealed class Request
        {
            public bool Vertical;
            public Point3d P1;
            public Point3d P2;
            public Side Side;
            public double SpanMin;
            public double SpanMax;
            public double BaseCoord;
            public double TextWidth;
            public double TextHeight;
            public int Level;
            public bool FirstOnSide;
            public bool LastOnSide;
            public bool TextOutside;
            public bool OutsideAtFar;
            public Point3d Near;
            public Point3d Far;
            public int Order;
        }

        private sealed class AxisSet
        {
            public List<double> Vertical = new List<double>();
            public List<double> Horizontal = new List<double>();
        }

        private sealed class Box
        {
            public double MinX, MaxX, MinY, MaxY;

            public bool Intersects(Box other) =>
                MaxX > other.MinX && other.MaxX > MinX &&
                MaxY > other.MinY && other.MaxY > MinY;
        }

        private sealed class DimStyleInfo
        {
            public string Name;
            public ObjectId Id;
            public double TextHeight;
        }

        // ---------------------------------------------------------------- ручной

        public static void Manual(Editor ed, Database db)
        {
            var height = AxisTextHeight(db);
            var styles = GetDimStyles(db);

            if (styles.Count > 0)
                db.Dimstyle = styles[ClosestStyleIndex(styles, height)].Id;

            Application.DocumentManager.MdiActiveDocument
                .SendStringToExecute("_.DIMLINEAR ", true, false, false);
        }

        // ------------------------------------------------------------ автоматич.

        public static void Auto(Editor ed, Database db)
        {
            var height = AxisTextHeight(db);
            var mmPerUnit = DrawingSettings.MillimetersPerUnit(db);

            var styles = GetDimStyles(db);
            var choice = OffsetDialog.Ask(
                height * 2.5 * mmPerUnit,
                styles.Select(s => s.Name).ToList(),
                styles.Select(s => s.TextHeight * mmPerUnit).ToList(),
                ClosestStyleIndex(styles, height));

            if (!choice.Ok || choice.OffsetMm <= 0)
                return;

            var sel = ed.GetSelection(new PromptSelectionOptions
            {
                MessageForAdding = "\nВыберите объекты и оси для простановки размеров: "
            });

            if (sel.Status != PromptStatus.OK)
                return;

            var axisIds = new List<ObjectId>();
            var objectIds = new List<ObjectId>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in sel.Value.GetObjectIds())
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null)
                        continue;

                    if (IsAxis(ent))
                        axisIds.Add(id);
                    else
                        objectIds.Add(id);
                }

                tr.Commit();
            }

            var styleId = styles[Math.Max(0, Math.Min(choice.StyleIndex, styles.Count - 1))].Id;

            Run(ed, db, objectIds, axisIds, choice.OffsetMm / mmPerUnit, styleId, height, false);
        }

        /// <summary>Тестовый прогон: все полилинии модели и все оси, без выбора.</summary>
        public static void Test(Editor ed, Database db)
        {
            var height = AxisTextHeight(db);
            var styles = GetDimStyles(db);
            var styleId = styles[ClosestStyleIndex(styles, height)].Id;

            var axisIds = new List<ObjectId>();
            var objectIds = new List<ObjectId>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var msp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                foreach (var id in msp)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null)
                        continue;

                    if (IsAxis(ent))
                        axisIds.Add(id);
                    else if (ent is Polyline)
                        objectIds.Add(id);
                }

                tr.Commit();
            }

            Run(ed, db, objectIds, axisIds, height * 2.5, styleId, height, true);
        }

        // ---------------------------------------------------------------- ядро

        private static void Run(Editor ed, Database db, List<ObjectId> objectIds, List<ObjectId> axisIds,
            double offset, ObjectId styleId, double height, bool debug)
        {
            var axes = axisIds.Count > 0 ? ReadAxes(db, axisIds) : ReadAxes(db, null);

            if (axes.Vertical.Count == 0 && axes.Horizontal.Count == 0)
            {
                ed.WriteMessage("\nОси не найдены. Выдели оси вместе с объектами или нарисуй их на слое {0}.", Layers.Axis);
                return;
            }

            var lfac = GetDimlfac(db, styleId);
            var requests = new List<Request>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in objectIds)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    var shape = GetShape(ent);
                    if (shape.Vertices.Count < 2)
                        continue;

                    BuildObject(requests, axes, shape, height, lfac);
                }

                tr.Commit();
            }

            DropExisting(db, requests);

            if (requests.Count == 0)
            {
                ed.WriteMessage("\nНечего проставлять: размеры уже стоят или оси не найдены рядом.");
                return;
            }

            PlaceLevels(requests, offset, height);

            var created = new List<(ObjectId Id, Request Request, Point3d LinePoint)>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layerId = Layers.Ensure(db, tr, Layers.Dimensions, Layers.DimensionsColor, true);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                foreach (var request in requests)
                {
                    var linePoint = DimLinePoint(request, offset, height);
                    var rotation = request.Vertical ? Math.PI / 2 : 0;

                    var dimension = new RotatedDimension(
                        rotation, request.P1, request.P2, linePoint, "", styleId)
                    {
                        LayerId = layerId
                    };

                    space.AppendEntity(dimension);
                    tr.AddNewlyCreatedDBObject(dimension, true);
                    created.Add((dimension.ObjectId, request, linePoint));
                }

                tr.Commit();
            }

            ed.WriteMessage("\nРазмеров проставлено: {0}.", created.Count);

            if (debug)
            {
                foreach (var item in created)
                    ed.WriteMessage("\n  DEBUG {0} {1} L{2}: {3:F0}..{4:F0} outside={5}",
                        item.Request.Vertical ? "V" : "H",
                        item.Request.Side,
                        item.Request.Level,
                        item.Request.SpanMin,
                        item.Request.SpanMax,
                        item.Request.TextOutside);
            }
        }

        // ------------------------------------------------------------ построение

        private static void BuildObject(List<Request> requests, AxisSet axes, Shape shape,
            double height, double lfac)
        {
            var v = shape.Vertices;
            var count = v.Count;

            var minX = v.Min(p => p.X);
            var maxX = v.Max(p => p.X);
            var minY = v.Min(p => p.Y);
            var maxY = v.Max(p => p.Y);
            var midX = (minX + maxX) / 2;
            var midY = (minY + maxY) / 2;

            var tolerance = Math.Max(1e-9, (maxX - minX + maxY - minY) * 1e-6);
            var edges = shape.Closed ? count : count - 1;

            for (var i = 0; i < edges; i++)
            {
                var v1 = v[i];
                var v2 = v[(i + 1) % count];

                var dx = Math.Abs(v2.X - v1.X);
                var dy = Math.Abs(v2.Y - v1.Y);

                if (dx < tolerance && dy < tolerance)
                    continue;

                var horizontal = dx >= dy;
                var side = horizontal
                    ? (v1.Y >= midY ? Side.Top : Side.Bottom)
                    : (v1.X >= midX ? Side.Right : Side.Left);

                var baseCoord = horizontal
                    ? (side == Side.Top ? maxY : minY)
                    : (side == Side.Right ? maxX : minX);

                var axis = horizontal
                    ? Nearest(axes.Vertical, v1.X, tolerance)
                    : Nearest(axes.Horizontal, v1.Y, tolerance);

                if (!axis.HasValue)
                    continue;

                var from = horizontal ? v1.X : v1.Y;
                var to = horizontal ? v2.X : v2.Y;
                var a = axis.Value;

                var sign = Math.Sign(to - from);

                // Куда попадает проекция оси относительно стороны.
                var inside = (a - from) * sign > tolerance && (to - a) * sign > tolerance;
                var beyondFar = (a - to) * sign > tolerance;

                var sideRequests = new List<Request>();

                if (inside)
                {
                    // A. ось пересекает сторону: от оси к первому углу и от оси ко второму.
                    sideRequests.Add(Make(horizontal, AxisPoint(v1, a, horizontal), v1,
                        side, baseCoord, height, lfac));
                    sideRequests.Add(Make(horizontal, AxisPoint(v1, a, horizontal), v2,
                        side, baseCoord, height, lfac));
                }
                else if (beyondFar)
                {
                    // C. ось за концом: от конца к началу, затем от конца до оси.
                    sideRequests.Add(Make(horizontal, v2, v1, side, baseCoord, height, lfac));
                    sideRequests.Add(Make(horizontal, v2, AxisPoint(v2, a, horizontal),
                        side, baseCoord, height, lfac));
                }
                else
                {
                    // B. ось перед началом: от начала до оси, затем длина стороны от начала.
                    sideRequests.Add(Make(horizontal, v1, AxisPoint(v1, a, horizontal),
                        side, baseCoord, height, lfac));
                    sideRequests.Add(Make(horizontal, v1, v2, side, baseCoord, height, lfac));
                }

                sideRequests = sideRequests.Where(r => r != null).ToList();

                for (var k = 0; k < sideRequests.Count; k++)
                {
                    sideRequests[k].FirstOnSide = k == 0;
                    sideRequests[k].LastOnSide = k == sideRequests.Count - 1;
                    sideRequests[k].Order = requests.Count;
                    sideRequests[k].Near = v1;
                    sideRequests[k].Far = v2;
                    requests.Add(sideRequests[k]);
                }
            }
        }

        private static Point3d AxisPoint(Point3d point, double axisValue, bool horizontal) =>
            horizontal ? new Point3d(axisValue, point.Y, 0) : new Point3d(point.X, axisValue, 0);

        private static Request Make(bool horizontal, Point3d p1, Point3d p2, Side side,
            double baseCoord, double height, double lfac)
        {
            var span = horizontal ? Math.Abs(p2.X - p1.X) : Math.Abs(p2.Y - p1.Y);
            if (span < 1e-9)
                return null;

            var spanMin = horizontal ? Math.Min(p1.X, p2.X) : Math.Min(p1.Y, p2.Y);
            var spanMax = horizontal ? Math.Max(p1.X, p2.X) : Math.Max(p1.Y, p2.Y);

            var shown = Math.Round(span * lfac).ToString("F0");

            return new Request
            {
                Vertical = !horizontal,
                P1 = p1,
                P2 = p2,
                Side = side,
                SpanMin = spanMin,
                SpanMax = spanMax,
                BaseCoord = baseCoord,
                TextWidth = height * 0.65 * Math.Max(1, shown.Length),
                TextHeight = height
            };
        }

        // ------------------------------------------------------ уровни и отступы

        private static void PlaceLevels(List<Request> requests, double offset, double height)
        {
            // Раскладываем размеры по уровням: сторона уезжает дальше от объекта,
            // только если её текст налез на текст другой стороны. Внутри одной
            // стороны тексты не разводим — они и так на одной линии.
            var groups = SplitSides(requests);

            if (groups.Count == 0)
                return;

            var levels = new int[groups.Count];

            for (var pass = 0; pass < 12; pass++)
            {
                var boxes = new List<List<Box>>();

                for (var i = 0; i < groups.Count; i++)
                {
                    foreach (var request in groups[i])
                        request.Level = levels[i];

                    boxes.Add(groups[i].Select(r => TextBox(r, offset, height)).ToList());
                }

                var moved = false;

                for (var i = 0; i < groups.Count; i++)
                {
                    if (levels[i] >= 6)
                        continue;

                    var clash = false;

                    for (var j = 0; j < groups.Count && !clash; j++)
                    {
                        if (i == j)
                            continue;

                        foreach (var a in boxes[i])
                        {
                            if (clash)
                                break;

                            foreach (var b in boxes[j])
                            {
                                if (!a.Intersects(b))
                                    continue;

                                clash = true;
                                break;
                            }
                        }
                    }

                    if (!clash)
                        continue;

                    levels[i]++;
                    moved = true;
                }

                if (!moved)
                    break;
            }

            for (var i = 0; i < groups.Count; i++)
                foreach (var request in groups[i])
                    request.Level = levels[i];
        }

        private static List<List<Request>> SplitSides(List<Request> requests)
        {
            var result = new List<List<Request>>();
            List<Request> current = null;

            foreach (var request in requests.OrderBy(r => r.Order))
            {
                if (current == null || request.FirstOnSide)
                {
                    current = new List<Request>();
                    result.Add(current);
                }

                current.Add(request);
            }

            return result;
        }

        private static Box TextBox(Request request, double offset, double height)
        {
            var line = DimLinePoint(request, offset, height);
            var middle = (request.SpanMin + request.SpanMax) / 2;

            if (request.Vertical)
            {
                return new Box
                {
                    MinX = line.X - height,
                    MaxX = line.X + height,
                    MinY = middle - request.TextWidth / 2,
                    MaxY = middle + request.TextWidth / 2
                };
            }

            return new Box
            {
                MinX = middle - request.TextWidth / 2,
                MaxX = middle + request.TextWidth / 2,
                MinY = line.Y - height,
                MaxY = line.Y + height
            };
        }

        private static Point3d TextAnchor(Request request, Point3d linePoint, double height)
        {
            if (!request.TextOutside)
                return linePoint;

            var gap = height * 0.5;
            var anchor = request.OutsideAtFar ? request.Far : request.Near;
            var other = request.OutsideAtFar ? request.Near : request.Far;

            // Текст ставим над размерной линией, а не на неё.
            var lift = height * 0.7;

            if (!request.Vertical)
            {
                var dir = Math.Sign(anchor.X - other.X);
                var x = anchor.X + (dir == 0 ? 1 : dir) * (request.TextWidth / 2 + gap);
                return new Point3d(x, linePoint.Y + lift, 0);
            }

            var dirY = Math.Sign(anchor.Y - other.Y);
            var y2 = anchor.Y + (dirY == 0 ? 1 : dirY) * (request.TextWidth / 2 + gap);
            return new Point3d(linePoint.X - lift, y2, 0);
        }

        private static Point3d DimLinePoint(Request request, double offset, double height)
        {
            var distance = offset + request.Level * height * 2.5;
            var mid = (request.SpanMin + request.SpanMax) / 2;

            switch (request.Side)
            {
                case Side.Top:
                    return new Point3d(mid, request.BaseCoord + distance, 0);
                case Side.Bottom:
                    return new Point3d(mid, request.BaseCoord - distance, 0);
                case Side.Left:
                    return new Point3d(request.BaseCoord - distance, mid, 0);
                default:
                    return new Point3d(request.BaseCoord + distance, mid, 0);
            }
        }

        // ------------------------------------------------------------------ оси

        private static bool IsAxis(Entity ent)
        {
            var line = ent as Line;
            return line != null && (IsAxisLayer(line.Layer));
        }

        private static AxisSet ReadAxes(Database db, List<ObjectId> ids)
        {
            var axes = new AxisSet();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (ids != null)
                {
                    foreach (var id in ids)
                        AddAxis(axes, tr.GetObject(id, OpenMode.ForRead) as Line);
                }
                else
                {
                    var msp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                    foreach (var id in msp)
                    {
                        var line = tr.GetObject(id, OpenMode.ForRead) as Line;
                        if (line != null && line.Layer == Layers.Axis)
                            AddAxis(axes, line);
                    }
                }

                tr.Commit();
            }

            axes.Vertical = axes.Vertical.Distinct().ToList();
            axes.Horizontal = axes.Horizontal.Distinct().ToList();
            return axes;
        }

        private static void AddAxis(AxisSet axes, Line line)
        {
            if (line == null)
                return;

            var dx = Math.Abs(line.EndPoint.X - line.StartPoint.X);
            var dy = Math.Abs(line.EndPoint.Y - line.StartPoint.Y);

            if (dx <= dy)
                axes.Vertical.Add((line.StartPoint.X + line.EndPoint.X) / 2);
            else
                axes.Horizontal.Add((line.StartPoint.Y + line.EndPoint.Y) / 2);
        }

        private static double? Nearest(List<double> values, double target, double tolerance)
        {
            var best = double.MaxValue;
            double? result = null;

            foreach (var value in values)
            {
                var distance = Math.Abs(value - target);
                if (distance <= tolerance)
                    continue;

                if (distance < best)
                {
                    best = distance;
                    result = value;
                }
            }

            return result;
        }

        // --------------------------------------------------------------- прочее

        private sealed class Shape
        {
            public List<Point3d> Vertices = new List<Point3d>();
            public bool Closed;
        }

        private static Shape GetShape(Entity ent)
        {
            var shape = new Shape();

            switch (ent)
            {
                case Polyline pl:
                    for (var i = 0; i < pl.NumberOfVertices; i++)
                        shape.Vertices.Add(pl.GetPoint3dAt(i));
                    shape.Closed = pl.Closed;
                    break;

                case Line line:
                    shape.Vertices.Add(line.StartPoint);
                    shape.Vertices.Add(line.EndPoint);
                    break;
            }

            return shape;
        }

        private static void DropExisting(Database db, List<Request> requests)
        {
            var existing = new List<Point3d[]>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var msp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                foreach (var id in msp)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead);

                    if (ent is RotatedDimension rotated)
                        existing.Add(new[] { rotated.XLine1Point, rotated.XLine2Point });
                    else if (ent is AlignedDimension aligned)
                        existing.Add(new[] { aligned.XLine1Point, aligned.XLine2Point });
                }

                tr.Commit();
            }

            const double eps = 1e-6;

            requests.RemoveAll(r => existing.Any(segment =>
                (segment[0].DistanceTo(r.P1) < eps && segment[1].DistanceTo(r.P2) < eps) ||
                (segment[0].DistanceTo(r.P2) < eps && segment[1].DistanceTo(r.P1) < eps)));
        }

        public sealed class DimStyleOption
        {
            public string Name;
            public double TextHeight;
            public ObjectId Id;
            public ObjectId TextStyle;
        }

        public static List<DimStyleOption> GetStyleOptions(Database db)
        {
            var result = new List<DimStyleOption>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var table = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);

                foreach (var id in table)
                {
                    var style = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    result.Add(new DimStyleOption
                    {
                        Name = style.Name,
                        TextHeight = style.Dimtxt,
                        Id = id,
                        TextStyle = style.Dimtxsty
                    });
                }

                tr.Commit();
            }

            return result;
        }

        /// <summary>Стиль, ближайший по высоте текста к подписям осей.</summary>
        public static ObjectId PreferredStyle(Database db)
        {
            var styles = GetDimStyles(db);
            if (styles.Count == 0)
                return ObjectId.Null;

            return styles[ClosestStyleIndex(styles, AxisTextHeight(db))].Id;
        }

        public static double AxisTextHeight(Database db)
        {
            double axis = 0;
            double any = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var msp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

                foreach (var id in msp)
                {
                    var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity == null)
                        continue;

                    double height;
                    var axisLayer = false;

                    switch (entity)
                    {
                        case DBText text:
                            height = text.Height;
                            axisLayer = IsAxisLayer(text.Layer);
                            break;

                        case MText mtext:
                            height = mtext.TextHeight;
                            axisLayer = IsAxisLayer(mtext.Layer);
                            break;

                        default:
                            continue;
                    }

                    if (height <= 0)
                        continue;

                    if (axisLayer && height > axis)
                        axis = height;

                    if (height > any)
                        any = height;
                }

                tr.Commit();
            }

            // Подписи осей — приоритет, иначе самая крупная надпись чертежа.
            if (axis > 0)
                return axis;

            if (any > 0)
                return any;

            return DrawingSettings.GetUnits(db) == "mm" ? 150.0 : 0.15;
        }

        private static bool IsAxisLayer(string layer)
        {
            return string.Equals(layer, Layers.Axis, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(layer, "оси", StringComparison.OrdinalIgnoreCase);
        }

        private static List<DimStyleInfo> GetDimStyles(Database db)
        {
            var result = new List<DimStyleInfo>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var table = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);

                foreach (var id in table)
                {
                    var style = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    result.Add(new DimStyleInfo
                    {
                        Name = style.Name,
                        Id = id,
                        TextHeight = style.Dimtxt
                    });
                }

                tr.Commit();
            }

            return result;
        }

        private static int ClosestStyleIndex(List<DimStyleInfo> styles, double textHeight)
        {
            var best = 0;
            var bestDelta = double.MaxValue;

            for (var i = 0; i < styles.Count; i++)
            {
                if (styles[i].TextHeight <= 0)
                    continue;

                var delta = Math.Abs(styles[i].TextHeight - textHeight);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = i;
                }
            }

            return best;
        }

        private static double GetDimlfac(Database db, ObjectId styleId)
        {
            if (styleId == ObjectId.Null)
                return 1.0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var style = tr.GetObject(styleId, OpenMode.ForRead) as DimStyleTableRecord;
                var lfac = style?.Dimlfac ?? 1.0;
                tr.Commit();
                return lfac <= 0 ? 1.0 : lfac;
            }
        }
    }
}
