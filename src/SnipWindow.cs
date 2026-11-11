// Kapsula — «остров» для Windows. Часть исходного кода: SnipWindow.cs
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
    // ============================================ Выбор области и пипетка
    class SnipWindow : Window
    {
        System.Drawing.Bitmap bmp;
        bool colorMode, done;
        double scale;
        public Action<BitmapSource> AreaPicked;
        // Режим «только выбрать прямоугольник» — для распознавания текста и записи области.
        public bool RectOnly;
        public Action<System.Drawing.Rectangle> RectPicked;
        public Action<System.Drawing.Color> ColorPicked;

        // Разметка выделенной области
        BitmapSource src;
        bool editing;
        Canvas ink;
        Border toolbar;
        int tool; // 0 стрелка, 1 рамка, 2 маркер, 3 текст, 4 скрыть
        Color color = Color.FromRgb(0xFF, 0x3B, 0x30);
        List<UIElement> items = new List<UIElement>();
        List<Border> toolBtns = new List<Border>();
        List<Border> colorBtns = new List<Border>();
        static readonly Color[] Palette = { Color.FromRgb(0xFF, 0x3B, 0x30), Color.FromRgb(0xFF, 0xCC, 0x00), Color.FromRgb(0x34, 0xC7, 0x59), Color.FromRgb(0x0A, 0x84, 0xFF), Colors.White, Colors.Black };
        UIElement cur;
        Point inkStart;
        Rect selRect;
        Int32Rect selPx;
        public Action Cancelled;
        Path dim;
        Rectangle sel;
        Point start;
        bool dragging;
        Border mag, swatch;
        ImageBrush magBrush;
        TextBlock magText, sizeText;
        Canvas canvas;

        public SnipWindow(System.Drawing.Bitmap bitmap, BitmapSource src, bool color, double dpiScale)
        {
            bmp = bitmap; colorMode = color; scale = dpiScale;
            this.src = src;
            Focusable = true;
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Left = 0; Top = 0;
            Width = bmp.Width / scale; Height = bmp.Height / scale;
            Background = new ImageBrush(src) { Stretch = Stretch.Fill };
            Cursor = Cursors.Cross;
            canvas = new Canvas { Focusable = true, FocusVisualStyle = null };
            Content = canvas;
            Loaded += delegate { Keyboard.Focus(canvas); };

            if (!colorMode)
            {
                dim = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), Data = new RectangleGeometry(new Rect(0, 0, Width, Height)) };
                canvas.Children.Add(dim);
                sel = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1.5, Visibility = Visibility.Collapsed, SnapsToDevicePixels = true };
                canvas.Children.Add(sel);
                sizeText = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), Padding = new Thickness(6, 2, 6, 2), Visibility = Visibility.Collapsed };
                canvas.Children.Add(sizeText);
            }
            else
            {
                magBrush = new ImageBrush(src) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
                var lens = new Border { Width = 132, Height = 132, CornerRadius = new CornerRadius(66), Background = magBrush, BorderBrush = Brushes.White, BorderThickness = new Thickness(2) };
                RenderOptions.SetBitmapScalingMode(lens, BitmapScalingMode.NearestNeighbor);
                var cross = new Rectangle { Width = 10, Height = 10, Stroke = Brushes.White, StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var lensGrid = new Grid();
                lensGrid.Children.Add(lens);
                lensGrid.Children.Add(cross);
                swatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                magText = new TextBlock { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                row.Children.Add(swatch);
                row.Children.Add(magText);
                var label = new Border { Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0A, 0x0A, 0x0C)), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 5, 12, 5), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Child = row };
                var sp = new StackPanel();
                sp.Children.Add(lensGrid);
                sp.Children.Add(label);
                mag = new Border { Child = sp, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                canvas.Children.Add(mag);
            }

            PreviewKeyDown += OnKey;
            MouseRightButtonDown += delegate { if (!editing) Finish(true); };
            Deactivated += delegate { if (!editing) Finish(true); };
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
        }

        void Finish(bool cancel)
        {
            if (done) return;
            done = true;
            try { Close(); } catch { }
            if (cancel && Cancelled != null) Cancelled();
        }

        System.Drawing.Color PixelAt(Point p)
        {
            int x = Math.Max(0, Math.Min(bmp.Width - 1, (int)(p.X * scale)));
            int y = Math.Max(0, Math.Min(bmp.Height - 1, (int)(p.Y * scale)));
            return bmp.GetPixel(x, y);
        }

        void OnDown(object s, MouseButtonEventArgs e)
        {
            if (editing) return;
            Point p = e.GetPosition(this);
            if (colorMode)
            {
                var c = PixelAt(p);
                Finish(false);
                if (ColorPicked != null) ColorPicked(c);
                return;
            }
            start = p; dragging = true; CaptureMouse();
            sel.Visibility = Visibility.Visible; sizeText.Visibility = Visibility.Visible;
            UpdateSel(p);
        }

        void UpdateSel(Point p)
        {
            var r = new Rect(start, p);
            Canvas.SetLeft(sel, r.X); Canvas.SetTop(sel, r.Y);
            sel.Width = r.Width; sel.Height = r.Height;
            dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Width, Height)), new RectangleGeometry(r));
            sizeText.Text = (int)(r.Width * scale) + " × " + (int)(r.Height * scale);
            Canvas.SetLeft(sizeText, r.X);
            Canvas.SetTop(sizeText, r.Y > 26 ? r.Y - 24 : r.Bottom + 6);
        }

        void OnMove(object s, MouseEventArgs e)
        {
            if (editing) return;
            Point p = e.GetPosition(this);
            if (colorMode)
            {
                mag.Visibility = Visibility.Visible;
                magBrush.Viewbox = new Rect(Math.Floor(p.X * scale) - 5, Math.Floor(p.Y * scale) - 5, 11, 11);
                var c = PixelAt(p);
                swatch.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                magText.Text = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                double x = p.X + 24, y = p.Y + 24;
                if (x + 150 > Width) x = p.X - 160;
                if (y + 190 > Height) y = p.Y - 196;
                Canvas.SetLeft(mag, x); Canvas.SetTop(mag, y);
                return;
            }
            if (dragging) UpdateSel(p);
        }

        void OnUp(object s, MouseButtonEventArgs e)
        {
            if (colorMode || !dragging || editing) return;
            dragging = false; ReleaseMouseCapture();
            var r = new Rect(start, e.GetPosition(this));
            r.Intersect(new Rect(0, 0, Width, Height));
            if (r.IsEmpty || r.Width * scale < 8 || r.Height * scale < 8) { Finish(true); return; }
            if (RectOnly)
            {
                var px = new System.Drawing.Rectangle((int)Math.Round(r.X * scale), (int)Math.Round(r.Y * scale), (int)Math.Round(r.Width * scale), (int)Math.Round(r.Height * scale));
                px.Intersect(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                Finish(false);
                if (RectPicked != null) RectPicked(px);
                return;
            }
            BeginEdit(r);
        }

        // ---------------------------------------------------------- разметка
        void BeginEdit(Rect r)
        {
            editing = true;
            Cursor = Cursors.Arrow;
            selRect = r;
            int px = (int)Math.Round(r.X * scale), py = (int)Math.Round(r.Y * scale);
            int pw = Math.Min(bmp.Width - px, (int)Math.Round(r.Width * scale)), ph = Math.Min(bmp.Height - py, (int)Math.Round(r.Height * scale));
            selPx = new Int32Rect(px, py, Math.Max(1, pw), Math.Max(1, ph));
            UpdateSel(r.BottomRight);
            sizeText.Visibility = Visibility.Collapsed;

            ink = new Canvas { Width = r.Width, Height = r.Height, ClipToBounds = true, Background = Brushes.Transparent, Cursor = Cursors.Cross };
            Canvas.SetLeft(ink, r.X); Canvas.SetTop(ink, r.Y);
            ink.MouseLeftButtonDown += InkDown;
            ink.MouseMove += InkMove;
            ink.MouseLeftButtonUp += InkUp;
            canvas.Children.Add(ink);

            BuildToolbar();
            toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double tw = toolbar.DesiredSize.Width, th = toolbar.DesiredSize.Height;
            double x = Math.Max(8, Math.Min(Width - tw - 8, r.X + r.Width / 2 - tw / 2));
            double y = r.Bottom + 12;
            if (y + th + 8 > Height) y = r.Y - th - 12;
            if (y < 8) y = r.Bottom - th - 12;
            Canvas.SetLeft(toolbar, x); Canvas.SetTop(toolbar, y);
            Keyboard.Focus(canvas);
        }

        Border ToolButton(string text, bool mdl2, string tip, Action click)
        {
            var tb = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = mdl2 ? 15 : 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            tb.FontFamily = new FontFamily(mdl2 ? "Segoe MDL2 Assets" : "Segoe UI Symbol");
            var b = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, Child = tb, Cursor = Cursors.Hand, ToolTip = tip, Margin = new Thickness(1, 0, 1, 0) };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        void BuildToolbar()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            string[] glyphs = { "↗", "", "", "T", "" };
            bool[] mdl = { false, true, true, false, true };
            string[] tips = { "Стрелка (1)", "Рамка (2)", "Маркер (3)", "Текст (4)", "Скрыть (5)" };
            for (int i = 0; i < glyphs.Length; i++)
            {
                int idx = i;
                var b = ToolButton(glyphs[i], mdl[i], tips[i], delegate { SelectTool(idx); });
                toolBtns.Add(b);
                row.Children.Add(b);
            }
            row.Children.Add(new Border { Width = 1, Height = 20, Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40)), Margin = new Thickness(8, 0, 8, 0) });
            for (int i = 0; i < Palette.Length; i++)
            {
                Color c = Palette[i];
                var dot = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(c), BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x5C)), BorderThickness = new Thickness(c == Colors.Black ? 1 : 0) };
                var ring = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Child = dot, Cursor = Cursors.Hand, Margin = new Thickness(1, 0, 1, 0), Tag = c };
                ring.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; color = c; RefreshToolbar(); };
                colorBtns.Add(ring);
                row.Children.Add(ring);
            }
            row.Children.Add(new Border { Width = 1, Height = 20, Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40)), Margin = new Thickness(8, 0, 8, 0) });
            row.Children.Add(ToolButton("", true, "Отменить (Ctrl+Z)", Undo));
            row.Children.Add(ToolButton("", true, "Закрыть (Esc)", delegate { Finish(true); }));
            var doneText = new TextBlock { Text = "Готово", Foreground = Brushes.Black, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var doneBtn = new Border { Height = 32, CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6)), Padding = new Thickness(16, 0, 16, 1), Child = doneText, Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Скопировать и сохранить (Enter)" };
            doneBtn.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; Done(); };
            row.Children.Add(doneBtn);

            toolbar = new Border { Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0C)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2F)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(24), Padding = new Thickness(8, 6, 8, 6), Child = row };
            canvas.Children.Add(toolbar);
            RefreshToolbar();
        }

        void SelectTool(int t) { CommitText(); tool = t; RefreshToolbar(); }

        void RefreshToolbar()
        {
            var on = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x36));
            for (int i = 0; i < toolBtns.Count; i++) toolBtns[i].Background = i == tool ? (Brush)on : Brushes.Transparent;
            foreach (Border r in colorBtns) r.BorderBrush = (Color)r.Tag == color ? Brushes.White : Brushes.Transparent;
            if (ink != null) ink.Cursor = tool == 3 ? Cursors.IBeam : Cursors.Cross;
        }

        void OnKey(object s, KeyEventArgs e)
        {
            bool typing = Keyboard.FocusedElement is TextBox;
            if (e.Key == Key.Escape)
            {
                // Esc во время ввода завершает ввод; если фокус увести не удалось — закрываем окно, чтобы оно не зависло.
                if (typing) { CommitText(); e.Handled = true; if (!(Keyboard.FocusedElement is TextBox)) return; }
                Finish(true); e.Handled = true; return;
            }
            if (!editing) return;
            if (e.Key == Key.Enter) { e.Handled = true; if (typing) CommitText(); else Done(); return; }
            if (typing) return;
            if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Undo(); e.Handled = true; return; }
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Done(); e.Handled = true; return; }
            if (e.Key >= Key.D1 && e.Key <= Key.D5) { SelectTool(e.Key - Key.D1); e.Handled = true; }
        }

        void Undo()
        {
            CommitText();
            if (items.Count == 0) return;
            ink.Children.Remove(items[items.Count - 1]);
            items.RemoveAt(items.Count - 1);
        }

        void CommitText()
        {
            var tb = Keyboard.FocusedElement as TextBox;
            // Фокус уводим на холст: само окно его не принимает, и поле ввода оставалось активным.
            Keyboard.Focus(canvas);
            if (Keyboard.FocusedElement is TextBox) Keyboard.ClearFocus();
            if (tb != null)
            {
                tb.IsReadOnly = true;
                tb.IsHitTestVisible = false;
                tb.Focusable = false;
                if (tb.Text.Trim().Length == 0 && ink != null) { ink.Children.Remove(tb); items.Remove(tb); }
            }
        }

        static Point ClampTo(Point p, double w, double h) { return new Point(Math.Max(0, Math.Min(w, p.X)), Math.Max(0, Math.Min(h, p.Y))); }

        static Geometry ArrowGeometry(Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            var g = new StreamGeometry();
            if (len < 1) return g;
            double ux = dx / len, uy = dy / len, hl = Math.Min(20, len * 0.6), hw = hl * 0.45;
            Point bas = new Point(b.X - ux * hl, b.Y - uy * hl);
            using (var c = g.Open())
            {
                c.BeginFigure(a, false, false);
                c.LineTo(bas, true, true);
                c.BeginFigure(b, true, true);
                c.LineTo(new Point(bas.X - uy * hw, bas.Y + ux * hw), true, true);
                c.LineTo(new Point(bas.X + uy * hw, bas.Y - ux * hw), true, true);
            }
            g.Freeze();
            return g;
        }

        void InkDown(object s, MouseButtonEventArgs e)
        {
            e.Handled = true;
            bool wasTyping = Keyboard.FocusedElement is TextBox;
            CommitText();
            Point p = e.GetPosition(ink);
            var brush = new SolidColorBrush(color);
            if (tool == 3)
            {
                if (wasTyping) return; // первый клик мимо поля просто завершает ввод
                var tb = new TextBox
                {
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = brush, CaretBrush = brush,
                    FontSize = 20, FontWeight = FontWeights.SemiBold, MinWidth = 24, Padding = new Thickness(0), FontFamily = FontFamily
                };
                tb.FocusVisualStyle = null;
                Canvas.SetLeft(tb, p.X); Canvas.SetTop(tb, p.Y - 14);
                ink.Children.Add(tb);
                items.Add(tb);
                tb.Focus();
                return;
            }
            inkStart = p;
            ink.CaptureMouse();
            if (tool == 0) cur = new Path { Stroke = brush, Fill = brush, StrokeThickness = 3.5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            else if (tool == 1) cur = new Rectangle { Stroke = brush, StrokeThickness = 3, RadiusX = 4, RadiusY = 4 };
            else if (tool == 2)
            {
                var pl = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb(0x70, color.R, color.G, color.B)), StrokeThickness = 16, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                pl.Points.Add(p);
                cur = pl;
            }
            else cur = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }), Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)) };
            ink.Children.Add(cur);
        }

        void InkMove(object s, MouseEventArgs e)
        {
            if (!ink.IsMouseCaptured || cur == null) return;
            Point p = ClampTo(e.GetPosition(ink), ink.Width, ink.Height);
            if (tool == 0) ((Path)cur).Data = ArrowGeometry(inkStart, p);
            else if (tool == 2) ((Polyline)cur).Points.Add(p);
            else
            {
                var r = new Rect(inkStart, p);
                var rc = (Rectangle)cur;
                Canvas.SetLeft(rc, r.X); Canvas.SetTop(rc, r.Y);
                rc.Width = r.Width; rc.Height = r.Height;
            }
        }

        void InkUp(object s, MouseButtonEventArgs e)
        {
            if (!ink.IsMouseCaptured || cur == null) return;
            ink.ReleaseMouseCapture();
            Point p = ClampTo(e.GetPosition(ink), ink.Width, ink.Height);
            var r = new Rect(inkStart, p);
            bool tiny = tool == 2 ? ((Polyline)cur).Points.Count < 2 : (Math.Abs(p.X - inkStart.X) < 4 && Math.Abs(p.Y - inkStart.Y) < 4);
            UIElement made = cur;
            cur = null;
            if (tiny) { ink.Children.Remove(made); return; }
            if (tool == 4)
            {
                ink.Children.Remove(made);
                made = Mosaic(r);
                if (made == null) return;
                ink.Children.Add(made);
            }
            items.Add(made);
        }

        // Скрытие: участок заменяется крупной мозаикой из исходного снимка.
        UIElement Mosaic(Rect r)
        {
            try
            {
                int x = selPx.X + (int)Math.Round(r.X * scale), y = selPx.Y + (int)Math.Round(r.Y * scale);
                int w = (int)Math.Round(r.Width * scale), h = (int)Math.Round(r.Height * scale);
                w = Math.Min(w, src.PixelWidth - x); h = Math.Min(h, src.PixelHeight - y);
                if (w < 4 || h < 4) return null;
                double block = Math.Max(8, Math.Round(12 * scale));
                var crop = new CroppedBitmap(src, new Int32Rect(x, y, w, h));
                var small = new TransformedBitmap(crop, new ScaleTransform(Math.Max(1.0 / w, 1 / block), Math.Max(1.0 / h, 1 / block)));
                small.Freeze();
                var img = new Image { Source = small, Width = r.Width, Height = r.Height, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(img, r.X); Canvas.SetTop(img, r.Y);
                return img;
            }
            catch { return null; }
        }

        void Done()
        {
            if (done) return;
            CommitText();
            BitmapSource result;
            try
            {
                foreach (UIElement el in items) { var tb = el as TextBox; if (tb != null) { tb.IsReadOnly = true; tb.Focusable = false; tb.CaretBrush = Brushes.Transparent; tb.SelectionLength = 0; } }
                ink.UpdateLayout();
                var crop = new CroppedBitmap(src, selPx);
                var full = new Rect(0, 0, selPx.Width, selPx.Height);
                // Сначала отдельно рисуем слой разметки в размере снимка, потом кладём его поверх кадра.
                RenderTargetBitmap layer = null;
                if (items.Count > 0)
                {
                    canvas.Children.Remove(ink);
                    var host = new Border { Width = ink.Width, Height = ink.Height, Child = ink };
                    var box = new Viewbox { Width = selPx.Width, Height = selPx.Height, Stretch = Stretch.Fill, Child = host };
                    box.Measure(new Size(selPx.Width, selPx.Height));
                    box.Arrange(full);
                    box.UpdateLayout();
                    layer = new RenderTargetBitmap(selPx.Width, selPx.Height, 96, 96, PixelFormats.Pbgra32);
                    layer.Render(box);
                }
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawImage(crop, full);
                    if (layer != null) dc.DrawImage(layer, full);
                }
                var rtb = new RenderTargetBitmap(selPx.Width, selPx.Height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                result = rtb;
            }
            catch (Exception ex) { Store.LogError("разметка", ex); Finish(true); return; }
            Finish(false);
            if (AreaPicked != null) AreaPicked(result);
        }
    }
}
