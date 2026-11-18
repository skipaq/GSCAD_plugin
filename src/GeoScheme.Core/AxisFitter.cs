using System;
using System.Collections.Generic;

namespace GeoScheme.Core
{
    /// <summary>
    /// Восстановление оси по обрывкам: подгонка прямой (метод главных компонент)
    /// и проверка, что выбранные куски действительно лежат на одной оси.
    /// </summary>
    public static class AxisFitter
    {
        /// <summary>
        /// Подгоняет прямую. Возвращает null, если точек меньше двух или они совпадают.
        /// </summary>
        public static AxisFit Fit(IReadOnlyList<Point2> points)
        {
            if (points == null || points.Count < 2)
                return null;

            double sx = 0, sy = 0;
            foreach (var p in points)
            {
                sx += p.X;
                sy += p.Y;
            }

            var n = points.Count;
            var centroid = new Point2(sx / n, sy / n);

            double sxx = 0, syy = 0, sxy = 0;
            foreach (var p in points)
            {
                var dx = p.X - centroid.X;
                var dy = p.Y - centroid.Y;
                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }

            // Все точки совпали — оси нет.
            if (sxx + syy < 1e-18)
                return null;

            // Угол главной оси через удвоенный угол — устойчиво для любой ориентации.
            var theta = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            var direction = new Point2(Math.Cos(theta), Math.Sin(theta)).Normalized();

            var fit = new AxisFit
            {
                Centroid = centroid,
                Direction = direction,
                MinT = double.MaxValue,
                MaxT = double.MinValue
            };

            foreach (var p in points)
            {
                var t = fit.Project(p);
                var dev = Math.Abs((p - centroid).Cross(direction));

                if (dev > fit.MaxDeviation)
                    fit.MaxDeviation = dev;
                if (t < fit.MinT)
                    fit.MinT = t;
                if (t > fit.MaxT)
                    fit.MaxT = t;
            }

            return fit;
        }

        /// <summary>
        /// Строит отрезок оси: от марки (если задана) до дальнего конца геометрии
        /// с выступом. Без марки — по всей геометрии с выступами с обеих сторон.
        /// </summary>
        public static void Extend(AxisFit fit, Point2? marker, double margin, out Point2 start, out Point2 end)
        {
            if (fit == null)
                throw new ArgumentNullException(nameof(fit));

            if (marker.HasValue)
            {
                var tm = fit.Project(marker.Value);
                var markerAtStart = Math.Abs(tm - fit.MinT) <= Math.Abs(tm - fit.MaxT);

                if (markerAtStart)
                {
                    start = fit.At(tm);
                    end = fit.At(fit.MaxT + margin);
                }
                else
                {
                    start = fit.At(fit.MinT - margin);
                    end = fit.At(tm);
                }

                return;
            }

            start = fit.At(fit.MinT - margin);
            end = fit.At(fit.MaxT + margin);
        }
    }
}
