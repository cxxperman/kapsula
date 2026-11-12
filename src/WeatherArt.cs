// Kapsula — «остров» для Windows. Часть исходного кода: WeatherArt.cs
// Цветные значки погоды, нарисованные фигурами: солнце, луна, облака, дождь, снег, гроза, туман.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Kapsula
{
    static class WeatherArt
    {
        static readonly Color SunA = Color.FromRgb(0xFF, 0xDD, 0x55), SunB = Color.FromRgb(0xFF, 0xA8, 0x1E);
        static readonly Brush Ray = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x36));
        static readonly Brush Moon = new LinearGradientBrush(Color.FromRgb(0xFB, 0xF1, 0xC6), Color.FromRgb(0xE2, 0xD0, 0x8E), 90);
        static readonly Brush Drop = new SolidColorBrush(Color.FromRgb(0x4D, 0xA8, 0xFF));
        static readonly Brush Flake = new SolidColorBrush(Color.FromRgb(0xEA, 0xF4, 0xFF));
        static readonly Brush Bolt = new LinearGradientBrush(Color.FromRgb(0xFF, 0xE0, 0x4A), Color.FromRgb(0xFF, 0x9F, 0x0A), 90);
        static readonly Brush Fog = new SolidColorBrush(Color.FromRgb(0xC2, 0xCB, 0xD6));

        // Значок размером size×size. code — код погоды WMO, как в данных Open-Meteo.
        public static FrameworkElement Icon(int code, bool day, double size)
        {
            var c = new Canvas { Width = 32, Height = 32 };
            bool fog = code == 45 || code == 48;
            bool drizzle = code >= 51 && code <= 57;
            bool rain = (code >= 61 && code <= 67) || (code >= 80 && code <= 82);
            bool snow = (code >= 71 && code <= 77) || code == 85 || code == 86;
            bool storm = code >= 95;
            if (code <= 0) Body(c, day, 16, 16, 1);
            else if (code <= 2)
            {
                // малооблачно и переменная облачность: светило выглядывает из-за облака
                Body(c, day, 12, 12, code == 1 ? 0.85 : 0.72);
                Cloud(c, code == 1 ? 9 : 7, code == 1 ? 13 : 11, code == 1 ? 0.75 : 0.9, false);
            }
            else if (fog)
            {
                Cloud(c, 4, 4, 0.8, true);
                for (int i = 0; i < 3; i++) c.Children.Add(new Line { X1 = 6 + i * 2, X2 = 26 - i * 2, Y1 = 22 + i * 3.4, Y2 = 22 + i * 3.4, Stroke = Fog, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            }
            else
            {
                bool dark = rain || storm || code == 3;
                Cloud(c, 3, snow || rain || drizzle || storm ? 1 : 5, 0.95, dark);
                if (rain || drizzle)
                {
                    int n = drizzle ? 2 : 3;
                    for (int i = 0; i < n; i++)
                    {
                        double x = (n == 2 ? 13 : 10) + i * 6;
                        c.Children.Add(new Line { X1 = x, Y1 = 23, X2 = x - 2, Y2 = drizzle ? 27 : 29, Stroke = Drop, StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                    }
                }
                if (snow)
                    for (int i = 0; i < 3; i++)
                    {
                        var f = new Ellipse { Width = 3.6, Height = 3.6, Fill = Flake };
                        Canvas.SetLeft(f, 8.2 + i * 6); Canvas.SetTop(f, i == 1 ? 26 : 23.5);
                        c.Children.Add(f);
                    }
                if (storm)
                    c.Children.Add(new Polygon { Fill = Bolt, Points = new PointCollection { new Point(17, 20), new Point(12, 27), new Point(16, 27), new Point(14, 32), new Point(21, 24), new Point(17, 24), new Point(19, 20) } });
            }
            return new Viewbox { Width = size, Height = size, Child = c };
        }

        // Солнце днём, луна ночью; k — масштаб, (cx, cy) — центр
        static void Body(Canvas c, bool day, double cx, double cy, double k)
        {
            if (day)
            {
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    c.Children.Add(new Line { X1 = cx + Math.Cos(a) * 9.6 * k, Y1 = cy + Math.Sin(a) * 9.6 * k, X2 = cx + Math.Cos(a) * 13 * k, Y2 = cy + Math.Sin(a) * 13 * k, Stroke = Ray, StrokeThickness = 2.1 * k, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                }
                double r = 6.6 * k;
                var sun = new Ellipse { Width = r * 2, Height = r * 2, Fill = new RadialGradientBrush(SunA, SunB) { GradientOrigin = new Point(0.35, 0.3) } };
                Canvas.SetLeft(sun, cx - r); Canvas.SetTop(sun, cy - r);
                c.Children.Add(sun);
            }
            else
            {
                double r = 8.5 * k;
                var full = new EllipseGeometry(new Point(cx, cy), r, r);
                var cut = new EllipseGeometry(new Point(cx + r * 0.55, cy - r * 0.35), r * 0.85, r * 0.85);
                c.Children.Add(new Path { Fill = Moon, Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, cut) });
            }
        }

        // Облако из трёх кругов и основания; dark — тяжёлое серое
        static void Cloud(Canvas c, double x, double y, double k, bool dark)
        {
            var g = new GeometryGroup { FillRule = FillRule.Nonzero };
            g.Children.Add(new EllipseGeometry(new Point(x + 8 * k, y + 15 * k), 6 * k, 6 * k));
            g.Children.Add(new EllipseGeometry(new Point(x + 15 * k, y + 11 * k), 8 * k, 8 * k));
            g.Children.Add(new EllipseGeometry(new Point(x + 22 * k, y + 15 * k), 5.5 * k, 5.5 * k));
            g.Children.Add(new RectangleGeometry(new Rect(x + 8 * k, y + 13 * k, 14 * k, 8 * k), 3 * k, 3 * k));
            Brush fill = dark
                ? new LinearGradientBrush(Color.FromRgb(0xC9, 0xD1, 0xDC), Color.FromRgb(0x8B, 0x97, 0xA8), 90)
                : new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xD5, 0xDE, 0xEA), 90);
            c.Children.Add(new Path { Data = g, Fill = fill });
        }
    }
}
