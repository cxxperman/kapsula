// Kapsula — «остров» для Windows. Часть исходного кода: Controls.cs
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WF = System.Windows.Forms;
using Windows.Media.Control;

namespace Kapsula
{
    // =============================================================== Пружина
    // Физическая пружина вместо анимаций по таймеру: шаг считается на каждый кадр монитора.
    class Spring
    {
        public double X, V, Target, K, C;
        public Spring(double x, double k, double c) { X = x; Target = x; K = k; C = c; }

        public bool Step(double dt)
        {
            int n = (int)Math.Ceiling(dt / 0.002);
            if (n < 1) n = 1;
            double h = dt / n;
            for (int i = 0; i < n; i++)
            {
                double a = -K * (X - Target) - C * V;
                V += a * h;
                X += V * h;
            }
            if (Math.Abs(X - Target) < 0.02 && Math.Abs(V) < 0.5) { X = Target; V = 0; return false; }
            return true;
        }
    }

    // ======================================================= Мелкие контролы
    class Bar
    {
        public Grid Root;
        Border fill;
        double w;

        public Bar(double width, double thick, Brush color, double hit)
        {
            w = width;
            Root = new Grid { Width = width, Height = hit, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            Root.Children.Add(new Border { Height = thick, CornerRadius = new CornerRadius(thick / 2), Background = TrackBrush, VerticalAlignment = VerticalAlignment.Center });
            fill = new Border { Height = thick, CornerRadius = new CornerRadius(thick / 2), Background = color, HorizontalAlignment = HorizontalAlignment.Left, Width = 0, VerticalAlignment = VerticalAlignment.Center };
            Root.Children.Add(fill);
        }

        public static readonly SolidColorBrush TrackBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x30));

        public void Set(double v) { if (double.IsNaN(v) || v < 0) v = 0; if (v > 1) v = 1; fill.Width = w * v; }
        public void SetColor(Brush b) { fill.Background = b; }
        public double ValueAt(Point p) { return Math.Max(0, Math.Min(1, p.X / w)); }
    }

    class Tile
    {
        public Border Root;
        public TextBlock Glyph, Label;
        // Общие кисти: при смене темы меняется их цвет, и все плитки перекрашиваются разом.
        public static readonly SolidColorBrush Off = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1E));
        public static readonly SolidColorBrush Hover = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2F));
        public static readonly SolidColorBrush On = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF4));
        public static readonly SolidColorBrush Light = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF4));
        public static readonly SolidColorBrush Dark = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0C));
        bool active;

        public Tile(string glyph, string label, Action click)
        {
            Glyph = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18, Foreground = Light, HorizontalAlignment = HorizontalAlignment.Center };
            Label = new TextBlock { Text = label, FontSize = 11.5, Foreground = Light, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(Glyph);
            sp.Children.Add(Label);
            Root = new Border { CornerRadius = new CornerRadius(16), Background = Off, Child = sp, Cursor = Cursors.Hand };
            Root.MouseEnter += delegate { if (!active) Root.Background = Hover; };
            Root.MouseLeave += delegate { if (!active) Root.Background = Off; };
            Root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
        }

        // Компактный вид: значок слева, подпись справа — для плотной сетки быстрых действий.
        public Tile Compact()
        {
            var sp = (StackPanel)Root.Child;
            sp.Orientation = Orientation.Horizontal;
            sp.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Margin = new Thickness(11, 0, 4, 0);
            Glyph.FontSize = 14; Glyph.VerticalAlignment = VerticalAlignment.Center;
            Label.FontSize = 11; Label.Margin = new Thickness(8, 0, 0, 1); Label.VerticalAlignment = VerticalAlignment.Center;
            Root.CornerRadius = new CornerRadius(12);
            return this;
        }

        public void SetActive(bool on)
        {
            active = on;
            Root.Background = on ? On : Off;
            Glyph.Foreground = on ? Dark : Light;
            Label.Foreground = on ? Dark : Light;
        }
    }
}
