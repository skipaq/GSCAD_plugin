using System;

namespace GeoScheme.Core
{
    /// <summary>Точка в единицах чертежа. Без зависимостей от AutoCAD — чтобы тестировать отдельно.</summary>
    public struct Point2
    {
        public double X;
        public double Y;

        public Point2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static Point2 operator +(Point2 a, Point2 b) => new Point2(a.X + b.X, a.Y + b.Y);
        public static Point2 operator -(Point2 a, Point2 b) => new Point2(a.X - b.X, a.Y - b.Y);
        public static Point2 operator *(Point2 a, double k) => new Point2(a.X * k, a.Y * k);

        public double Length => Math.Sqrt(X * X + Y * Y);

        public Point2 Normalized()
        {
            var len = Length;
            return len < 1e-12 ? new Point2(0, 0) : new Point2(X / len, Y / len);
        }

        public double Dot(Point2 other) => X * other.X + Y * other.Y;

        /// <summary>Псевдоскалярное произведение: z-компонента векторного произведения.</summary>
        public double Cross(Point2 other) => X * other.Y - Y * other.X;

        public double DistanceTo(Point2 other) => (this - other).Length;

        public override string ToString() => $"({X:F4}, {Y:F4})";
    }

    /// <summary>Результат подгонки прямой по набору точек.</summary>
    public sealed class AxisFit
    {
        public Point2 Centroid;
        public Point2 Direction;
        public double MaxDeviation;
        public double MinT;
        public double MaxT;

        /// <summary>Проекция точки на ось (параметр вдоль направления от центроида).</summary>
        public double Project(Point2 p) => (p - Centroid).Dot(Direction);

        public Point2 At(double t) => Centroid + Direction * t;

        /// <summary>Перпендикулярное отклонение точки от оси.</summary>
        public double Deviation(Point2 p) => Math.Abs((p - Centroid).Cross(Direction));

        public double Length => MaxT - MinT;
    }
}
