using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace GeoScheme.AutoCAD
{
    /// <summary>Библиотека рамок: DXF из комплекта плагина и из папки пользователя.</summary>
    public static class FrameLibrary
    {
        public sealed class FrameInfo
        {
            public string Name;
            public string Path;
            public double Width;
            public double Height;
            public double CenterX;
            public double CenterY;
            public double TextHeight;
            public readonly List<(double x1, double y1, double x2, double y2)> Segments =
                new List<(double, double, double, double)>();
        }

        public static string BuiltInFolder
        {
            get
            {
                var dir = Path.GetDirectoryName(typeof(FrameLibrary).Assembly.Location);
                return Path.Combine(dir ?? string.Empty, "Frames");
            }
        }

        public static string UserFolder
        {
            get
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(root, "GeoScheme", "Frames");
            }
        }

        public static List<FrameInfo> Load()
        {
            var result = new List<FrameInfo>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            LoadFrom(BuiltInFolder, result, names);
            LoadFrom(UserFolder, result, names);

            return result;
        }

        private static void LoadFrom(string folder, List<FrameInfo> result, HashSet<string> names)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return;

            foreach (var path in Directory.GetFiles(folder, "*.dxf"))
            {
                var info = Read(path);

                if (info == null || !names.Add(info.Name))
                    continue;

                result.Add(info);
            }
        }

        public static FrameInfo Read(string path)
        {
            try
            {
                var info = new FrameInfo
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    Path = path
                };

                using (var source = new Database(false, true))
                {
                    source.DxfIn(path, null);

                    using (var tr = source.TransactionManager.StartTransaction())
                    {
                        var space = (BlockTableRecord)tr.GetObject(source.CurrentSpaceId, OpenMode.ForRead);
                        var heights = new List<double>();
                        var minX = double.MaxValue;
                        var minY = double.MaxValue;
                        var maxX = double.MinValue;
                        var maxY = double.MinValue;

                        Collect(tr, space, Matrix3d.Identity, info, heights, ref minX, ref minY, ref maxX, ref maxY);

                        if (minX < maxX && minY < maxY)
                        {
                            info.Width = maxX - minX;
                            info.Height = maxY - minY;
                            info.CenterX = (minX + maxX) / 2;
                            info.CenterY = (minY + maxY) / 2;
                        }

                        info.TextHeight = MostCommon(heights);

                        tr.Commit();
                    }
                }

                return info;
            }
            catch
            {
                return null;
            }
        }

        private static void Collect(Transaction tr, BlockTableRecord space, Matrix3d transform,
            FrameInfo info, List<double> heights, ref double minX, ref double minY, ref double maxX, ref double maxY)
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

                // Размеры и прочий мусор в габариты рамки не берём.
                if (entity is Dimension)
                    continue;

                switch (entity)
                {
                    case Line line:
                        AddSegment(info, line.StartPoint.TransformBy(transform),
                            line.EndPoint.TransformBy(transform), ref minX, ref minY, ref maxX, ref maxY);
                        break;

                    case Polyline pl:
                        for (var i = 0; i < pl.NumberOfVertices; i++)
                        {
                            var a = pl.GetPoint3dAt(i).TransformBy(transform);
                            var b = pl.GetPoint3dAt((i + 1) % Math.Max(1, pl.NumberOfVertices)).TransformBy(transform);

                            if (i + 1 < pl.NumberOfVertices || pl.Closed)
                                AddSegment(info, a, b, ref minX, ref minY, ref maxX, ref maxY);
                        }
                        break;

                    case BlockReference reference:
                        try
                        {
                            var definition = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                            Collect(tr, definition, transform * reference.BlockTransform, info, heights,
                                ref minX, ref minY, ref maxX, ref maxY);
                        }
                        catch
                        {
                            // вложенный блок не прочитался
                        }
                        break;

                    case DBText text:
                        heights.Add(text.Height * ScaleOf(transform));
                        break;

                    case MText mtext:
                        heights.Add(mtext.TextHeight * ScaleOf(transform));
                        break;
                }
            }
        }

        private static void AddSegment(FrameInfo info, Point3d a, Point3d b,
            ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            info.Segments.Add((a.X, a.Y, b.X, b.Y));

            minX = Math.Min(minX, Math.Min(a.X, b.X));
            minY = Math.Min(minY, Math.Min(a.Y, b.Y));
            maxX = Math.Max(maxX, Math.Max(a.X, b.X));
            maxY = Math.Max(maxY, Math.Max(a.Y, b.Y));
        }

        private static double ScaleOf(Matrix3d transform)
        {
            var origin = Point3d.Origin.TransformBy(transform);
            var unit = new Point3d(1, 0, 0).TransformBy(transform);
            return origin.DistanceTo(unit);
        }

        private static double MostCommon(List<double> values)
        {
            if (values == null || values.Count == 0)
                return 0;

            var best = 0.0;
            var bestCount = 0;

            foreach (var value in values)
            {
                var rounded = Math.Round(value, 3);
                var count = 0;

                foreach (var other in values)
                    if (Math.Abs(Math.Round(other, 3) - rounded) < 1e-6)
                        count++;

                if (count > bestCount)
                {
                    bestCount = count;
                    best = value;
                }
            }

            return best > 0 ? best : values[0];
        }

        public static bool AddCustom(string path, out string error)
        {
            error = null;

            try
            {
                Directory.CreateDirectory(UserFolder);
                var target = Path.Combine(UserFolder, Path.GetFileName(path));

                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    return true;

                File.Copy(path, target, true);
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
