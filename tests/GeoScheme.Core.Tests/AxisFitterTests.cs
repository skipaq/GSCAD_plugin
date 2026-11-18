using System;
using System.Collections.Generic;
using GeoScheme.Core;
using Xunit;

namespace GeoScheme.Core.Tests
{
    public class AxisFitterTests
    {
        private static List<Point2> Line(double x0, double y0, double dx, double dy, int count, double noise = 0)
        {
            var pts = new List<Point2>();
            var rnd = new Random(42);
            for (int i = 0; i < count; i++)
            {
                var n = noise == 0 ? 0 : (rnd.NextDouble() * 2 - 1) * noise;
                // Шум по перпендикуляру.
                var len = Math.Sqrt(dx * dx + dy * dy);
                var px = -dy / len;
                var py = dx / len;
                pts.Add(new Point2(x0 + dx * i + px * n, y0 + dy * i + py * n));
            }
            return pts;
        }

        [Fact]
        public void HorizontalBrokenLine_IsOneAxis()
        {
            var pts = Line(0, 0, 1, 0, 40);
            var fit = AxisFitter.Fit(pts);

            Assert.NotNull(fit);
            Assert.True(fit.MaxDeviation < 1e-9);
            Assert.InRange(fit.Length, 38.9, 39.1);
            Assert.True(Math.Abs(fit.Direction.X) > 0.99);
        }

        [Fact]
        public void DiagonalAxis_KeepsDirection()
        {
            var pts = Line(0, 0, 1, 1, 10);
            var fit = AxisFitter.Fit(pts);

            Assert.NotNull(fit);
            Assert.True(fit.MaxDeviation < 1e-9);
            Assert.InRange(Math.Abs(fit.Direction.X), 0.70, 0.71);
            Assert.InRange(Math.Abs(fit.Direction.Y), 0.70, 0.71);
        }

        [Fact]
        public void NoisyButSameAxis_StaysWithinTolerance()
        {
            var pts = Line(0, 0, 1, 0, 100, noise: 0.01);
            var fit = AxisFitter.Fit(pts);

            Assert.NotNull(fit);
            Assert.True(fit.MaxDeviation < 0.05, $"отклонение {fit.MaxDeviation}");
        }

        [Fact]
        public void TwoDifferentAxes_HasLargeDeviation()
        {
            var pts = Line(0, 0, 1, 0, 20);
            pts.AddRange(Line(0, 1, 1, 0, 20)); // вторая линия параллельно, но выше

            var fit = AxisFitter.Fit(pts);

            Assert.NotNull(fit);
            Assert.True(fit.MaxDeviation > 0.4, $"отклонение {fit.MaxDeviation}");
        }

        [Fact]
        public void Extend_MarkerAtLeft_GoesRightToGeometry()
        {
            var pts = Line(0, 0, 1, 0, 10);
            var fit = AxisFitter.Fit(pts);

            AxisFitter.Extend(fit, new Point2(-1, 0), 2, out var start, out var end);

            Assert.InRange(start.X, -1.0001, -0.9999);
            Assert.InRange(end.X, 10.999, 11.001);
        }

        [Fact]
        public void Extend_MarkerAtRight_GoesLeftToGeometry()
        {
            var pts = Line(0, 0, 1, 0, 10);
            var fit = AxisFitter.Fit(pts);

            AxisFitter.Extend(fit, new Point2(11, 0), 2, out var start, out var end);

            Assert.InRange(start.X, -2.001, -1.999);
            Assert.InRange(end.X, 10.999, 11.001);
        }

        [Fact]
        public void Extend_NoMarker_AddsMarginOnBothSides()
        {
            var pts = Line(0, 0, 1, 0, 10);
            var fit = AxisFitter.Fit(pts);

            AxisFitter.Extend(fit, null, 3, out var start, out var end);

            Assert.InRange(start.X, -3.001, -2.999);
            Assert.InRange(end.X, 11.999, 12.001);
        }

        [Fact]
        public void Fit_TooFewPoints_ReturnsNull()
        {
            Assert.Null(AxisFitter.Fit(new List<Point2>()));
            Assert.Null(AxisFitter.Fit(new List<Point2> { new Point2(1, 1) }));
        }

        [Fact]
        public void Fit_CoincidentPoints_ReturnsNull()
        {
            var pts = new List<Point2> { new Point2(5, 5), new Point2(5, 5), new Point2(5, 5) };
            Assert.Null(AxisFitter.Fit(pts));
        }
    }
}
