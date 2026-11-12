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

        public void Set(double v) { if (double.IsNaN(v) || v < 0) v = 0; if (v > 1) v = 1; fill.BeginAnimation(FrameworkElement.WidthProperty, null); fill.Width = w * v; }

        // То же, но плавно: полоса доезжает до нового значения за заданное время.
        public void Glide(double v, int ms)
        {
            if (double.IsNaN(v) || v < 0) v = 0; if (v > 1) v = 1;
            var a = new System.Windows.Media.Animation.DoubleAnimation(w * v, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
            fill.BeginAnimation(FrameworkElement.WidthProperty, a);
        }
        public void SetColor(Brush b) { fill.Background = b; }

        // Меняет длину полосы, сохраняя заполненную долю
        public void Resize(double width)
        {
            if (Math.Abs(width - w) < 0.5) return;
            double v = w > 0 ? fill.Width / w : 0;
            w = width; Root.Width = width;
            Set(v);
        }
        public double ValueAt(Point p) { return Math.Max(0, Math.Min(1, p.X / w)); }
    }

    // Меню значка у часов в стиле Kapsula: тёмное, со скруглённой подсветкой строки и своей галочкой
    class DarkMenuRenderer : System.Windows.Forms.ToolStripProfessionalRenderer
    {
        static readonly System.Drawing.Color Back = System.Drawing.Color.FromArgb(0x1F, 0x1F, 0x23);
        static readonly System.Drawing.Color Hover = System.Drawing.Color.FromArgb(0x0A, 0x84, 0xFF);
        static readonly System.Drawing.Color Text = System.Drawing.Color.FromArgb(0xF4, 0xF4, 0xF6);
        static readonly System.Drawing.Color Off = System.Drawing.Color.FromArgb(0x6E, 0x6E, 0x76);
        static readonly System.Drawing.Color Line = System.Drawing.Color.FromArgb(0x3A, 0x3A, 0x40);

        public static void Apply(System.Windows.Forms.ContextMenuStrip menu) { Apply(menu, 290, true); }

        public static void Apply(System.Windows.Forms.ContextMenuStrip menu, int width, bool checks)
        {
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Back; menu.ForeColor = Text;
            menu.ShowImageMargin = false; menu.ShowCheckMargin = checks;
            menu.Font = new System.Drawing.Font("Segoe UI", 9.75f);
            menu.Padding = new System.Windows.Forms.Padding(4, 6, 4, 6);
            foreach (System.Windows.Forms.ToolStripItem it in menu.Items)
            {
                if (it is System.Windows.Forms.ToolStripSeparator) continue;
                it.AutoSize = false; it.Height = 30; it.Width = width;
            }
            // скруглённые углы, как у системных меню Windows 11 (в Windows 10 вызов ничего не делает)
            menu.Opened += delegate { try { int round = 2; DwmSetWindowAttribute(menu.Handle, 33, ref round, 4); } catch { } };
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        static System.Drawing.Drawing2D.GraphicsPath Round(System.Drawing.Rectangle r, int rad)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnRenderToolStripBackground(System.Windows.Forms.ToolStripRenderEventArgs e) { e.Graphics.Clear(Back); }
        protected override void OnRenderToolStripBorder(System.Windows.Forms.ToolStripRenderEventArgs e)
        {
            using (var pen = new System.Drawing.Pen(Line)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderImageMargin(System.Windows.Forms.ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(System.Windows.Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new System.Drawing.Rectangle(2, 1, e.Item.Width - 5, e.Item.Height - 2);
            using (var path = Round(r, 7)) using (var b = new System.Drawing.SolidBrush(Hover)) e.Graphics.FillPath(b, path);
        }

        protected override void OnRenderItemText(System.Windows.Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Off : e.Item.Selected ? System.Drawing.Color.White : Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(System.Windows.Forms.ToolStripItemImageRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int cx = e.ImageRectangle.X + e.ImageRectangle.Width / 2 + 1, cy = e.Item.Height / 2;
            using (var pen = new System.Drawing.Pen(e.Item.Selected ? System.Drawing.Color.White : Hover, 1.8f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                e.Graphics.DrawLines(pen, new System.Drawing.Point[] { new System.Drawing.Point(cx - 5, cy), new System.Drawing.Point(cx - 1, cy + 4), new System.Drawing.Point(cx + 5, cy - 4) });
        }

        protected override void OnRenderSeparator(System.Windows.Forms.ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new System.Drawing.Pen(Line)) e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }
    }
    // Мелкие эффекты интерфейса: «пружинка» при нажатии и наведении
    static class Fx
    {
        static readonly IEasingFunction Out = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

        public static void Scale(FrameworkElement el, double to, int ms)
        {
            var st = el.RenderTransform as ScaleTransform;
            if (st == null) { st = new ScaleTransform(1, 1); el.RenderTransform = st; el.RenderTransformOrigin = new Point(0.5, 0.5); }
            var a = new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Out };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        // Кнопка чуть сжимается под пальцем и возвращается, когда отпустили или увели курсор
        public static void Press(FrameworkElement el, double down)
        {
            el.PreviewMouseLeftButtonDown += delegate { Scale(el, down, 70); };
            el.PreviewMouseLeftButtonUp += delegate { Scale(el, 1, 220); };
            el.MouseLeave += delegate { Scale(el, 1, 220); };
        }
    }

    class Tile
    {
        public Border Root;
        public TextBlock Glyph, Label;
        Border badge;
        // Общие кисти: при смене темы меняется их цвет, и все плитки перекрашиваются разом.
        public static readonly SolidColorBrush Off = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1E));
        public static readonly SolidColorBrush Hover = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2F));
        public static readonly SolidColorBrush On = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF4));
        public static readonly SolidColorBrush Light = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF4));
        public static readonly SolidColorBrush Dark = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0C));
        public static readonly SolidColorBrush Line = new SolidColorBrush(Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF));    // тонкая обводка
        public static readonly SolidColorBrush Badge = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));   // кружок под значком
        public static readonly SolidColorBrush Accent = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));         // включено
        bool active;

        // Плитка: значок в кружке и подпись. Включённая — кружок заливается цветом, сама плитка остаётся тёмной.
        public Tile(string glyph, string label, Action click)
        {
            Glyph = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, Foreground = Light, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            badge = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = Badge, Child = Glyph, HorizontalAlignment = HorizontalAlignment.Center };
            Label = new TextBlock { Text = label, FontSize = 11.5, Foreground = Light, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(badge);
            sp.Children.Add(Label);
            Root = new Border { CornerRadius = new CornerRadius(16), Background = Off, BorderBrush = Line, BorderThickness = new Thickness(1), Child = sp, Cursor = Cursors.Hand };
            Root.MouseEnter += delegate { Root.Background = Hover; };
            Root.MouseLeave += delegate { Root.Background = Off; };
            Root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            Fx.Press(Root, 0.95);
        }

        // Компактный вид: значок слева, подпись справа — для плотной сетки быстрых действий.
        public Tile Compact()
        {
            var sp = (StackPanel)Root.Child;
            sp.Orientation = Orientation.Horizontal;
            sp.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Margin = new Thickness(7, 0, 3, 0);
            badge.Width = badge.Height = 24; badge.CornerRadius = new CornerRadius(12);
            Glyph.FontSize = 12.5;
            Label.FontSize = 11.5; Label.Margin = new Thickness(7, 0, 0, 1); Label.VerticalAlignment = VerticalAlignment.Center;
            Label.TextWrapping = TextWrapping.Wrap; Label.TextTrimming = TextTrimming.None; Label.MaxWidth = 52; Label.LineHeight = 13;
            Root.CornerRadius = new CornerRadius(14);
            return this;
        }

        public void SetActive(bool on)
        {
            active = on;
            badge.Background = on ? Accent : Badge;
            Glyph.Foreground = on ? Brushes.White : Light;
        }
    }
}