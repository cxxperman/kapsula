// Kapsula — «остров» для Windows. Часть исходного кода: Board.cs
// Конструктор вкладки: блоки лежат на сетке, и в режиме правки их можно перетаскивать куда угодно мышью.
// Пока блок тащат, остальные заранее разъезжаются, а место, куда он встанет, подсвечивается: зелёным — помещается,
// красным — нет. Задетые блоки сначала пробуют встать туда, откуда взяли перетаскиваемый, а если не выходит — каждый
// ищет ближайшее свободное место. «−» прячет блок, спрятанные лежат в полоске снизу; щелчок возвращает их.
// Раскладка запоминается. Esc или «Готово» заканчивает правку.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Kapsula
{
    class Board
    {
        public class Item
        {
            public string Id, Name;
            public FrameworkElement El;      // обёртка, которую двигаем и качаем
            public int C, R, W, H, DefC, DefR;
            public bool Hidden, DefHidden;
            public Border Overlay;
            public RotateTransform Wiggle;
        }

        public readonly string Key;
        public readonly int Cols, Rows;
        public readonly double CellW, CellH, Gap;
        public readonly Canvas Root;
        public readonly List<Item> Items = new List<Item>();
        public bool Editing { get; private set; }
        public Action Changed;          // раскладка поменялась
        public Action<string> Say;      // короткая подсказка на острове
        public Action Done;             // правка закончилась
        public Brush Accent = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
        readonly Canvas overlayLayer = new Canvas();
        readonly Border tray, target;
        readonly WrapPanel trayItems;
        static readonly Brush OkFill = new SolidColorBrush(Color.FromArgb(0x30, 0x30, 0xD1, 0x58)), OkLine = new SolidColorBrush(Color.FromArgb(0xD0, 0x30, 0xD1, 0x58));
        static readonly Brush BadFill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x45, 0x3A)), BadLine = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0x45, 0x3A));

        public Board(string key, double width, double height, int cols, int rows, double gap)
        {
            Key = key; Cols = cols; Rows = rows; Gap = gap;
            CellW = (width - gap * (cols - 1)) / cols;
            CellH = (height - gap * (rows - 1)) / rows;
            Root = new Canvas { Width = width, Height = height, ClipToBounds = false };
            // подсветка места, куда встанет блок
            target = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(2), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            target.BorderBrush = OkLine; target.Background = OkFill;
            Panel.SetZIndex(target, 30);
            Root.Children.Add(target);
            Panel.SetZIndex(overlayLayer, 50);
            Root.Children.Add(overlayLayer);
            // полоска снизу в режиме правки: спрятанные блоки, «Как было», «Готово»
            trayItems = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            var trayRow = new DockPanel { LastChildFill = true };
            var done = TrayBtn("Готово", true, delegate { SetEditing(false); });
            DockPanel.SetDock(done, Dock.Right); trayRow.Children.Add(done);
            var reset = TrayBtn("Как было", false, delegate { Reset(); });
            reset.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(reset, Dock.Right); trayRow.Children.Add(reset);
            trayRow.Children.Add(trayItems);
            tray = new Border
            {
                Child = trayRow, Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1A, 0x1A, 0x1F)), CornerRadius = new CornerRadius(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 6, 6),
                Width = width, Visibility = Visibility.Collapsed
            };
            Canvas.SetLeft(tray, 0); Canvas.SetTop(tray, height + 10);
            Panel.SetZIndex(tray, 60);
            Root.Children.Add(tray);
        }

        static Border TrayBtn(string text, bool main, Action click)
        {
            var b = new Border
            {
                Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(13, 0, 13, 1), Cursor = Cursors.Hand,
                Background = main ? new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }
            };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            Fx.Press(b, 0.94);
            return b;
        }

        // На сколько панель должна стать выше, пока идёт правка (под блоками — полоска)
        public const double TrayH = 56;

        public void Add(string id, string name, FrameworkElement el, int c, int r, int w, int h, bool hiddenByDefault)
        {
            // блок кладётся в обёртку: её двигаем и качаем, а у самого блока остаются его собственные эффекты
            var host = new Grid { Width = w * CellW + (w - 1) * Gap, Height = h * CellH + (h - 1) * Gap };
            var oldParent = el.Parent as Panel; if (oldParent != null) oldParent.Children.Remove(el);
            var oldBorder = el.Parent as Decorator; if (oldBorder != null) oldBorder.Child = null;
            el.Width = double.NaN; el.Height = double.NaN;
            el.HorizontalAlignment = HorizontalAlignment.Stretch; el.VerticalAlignment = VerticalAlignment.Stretch;
            el.Margin = new Thickness(0);
            host.Children.Add(el);
            Items.Add(new Item { Id = id, Name = name, El = host, C = c, R = r, W = w, H = h, DefC = c, DefR = r, Hidden = hiddenByDefault, DefHidden = hiddenByDefault });
            Root.Children.Add(host);
        }

        // ---- запоминание: "id:c,r,h;…" (h — спрятан)
        public void Load()
        {
            string s = Store.Get("board." + Key);
            if (s.Length > 0)
                foreach (string part in s.Split(';'))
                {
                    int colon = part.IndexOf(':'); if (colon <= 0) continue;
                    Item it = Find(part.Substring(0, colon)); if (it == null) continue;
                    string[] v = part.Substring(colon + 1).Split(',');
                    int c, r;
                    if (v.Length >= 3 && int.TryParse(v[0], out c) && int.TryParse(v[1], out r)) { it.C = c; it.R = r; it.Hidden = v[2] == "1"; }
                }
            // после обновления программы что-то могло не поместиться — ищем такому блоку место, иначе прячем
            foreach (Item it in Items)
                if (!it.Hidden && (!InBounds(it.C, it.R, it.W, it.H) || CollidesWithShown(it, it.C, it.R)))
                {
                    int[] spot = FreeSpot(it, Committed(), it.C, it.R);
                    if (spot != null) { it.C = spot[0]; it.R = spot[1]; } else it.Hidden = true;
                }
            Layout(false);
        }

        void Save()
        {
            var parts = new List<string>();
            foreach (Item it in Items) parts.Add(it.Id + ":" + it.C + "," + it.R + "," + (it.Hidden ? "1" : "0"));
            Store.Set("board." + Key, string.Join(";", parts.ToArray()));
            if (Changed != null) Changed();
        }

        public void Reset()
        {
            foreach (Item it in Items) { it.C = it.DefC; it.R = it.DefR; it.Hidden = it.DefHidden; }
            Store.Set("board." + Key, "");
            Layout(true);
            if (Changed != null) Changed();
        }

        // Высота до нижнего видимого блока; в режиме правки — вся сетка, чтобы было куда тащить
        public double UsedHeight()
        {
            if (Editing) return Root.Height;
            int bottom = 0;
            foreach (Item it in Items) if (!it.Hidden) bottom = Math.Max(bottom, it.R + it.H);
            return bottom == 0 ? 0 : Y(bottom) - Gap;
        }

        public Item Find(string id) { foreach (Item it in Items) if (it.Id == id) return it; return null; }
        public bool IsShown(string id) { Item it = Find(id); return it != null && !it.Hidden; }

        bool InBounds(int c, int r, int w, int h) { return c >= 0 && r >= 0 && c + w <= Cols && r + h <= Rows; }
        static bool Overlap(int c1, int r1, int w1, int h1, int c2, int r2, int w2, int h2) { return c1 < c2 + w2 && c2 < c1 + w1 && r1 < r2 + h2 && r2 < r1 + h1; }
        bool CollidesWithShown(Item self, int c, int r)
        {
            foreach (Item o in Items) if (o != self && !o.Hidden && Overlap(c, r, self.W, self.H, o.C, o.R, o.W, o.H)) return true;
            return false;
        }

        double X(int c) { return c * (CellW + Gap); }
        double Y(int r) { return r * (CellH + Gap); }

        // текущие места всех видимых блоков
        Dictionary<Item, int[]> Committed()
        {
            var d = new Dictionary<Item, int[]>();
            foreach (Item it in Items) if (!it.Hidden) d[it] = new[] { it.C, it.R };
            return d;
        }

        static bool Free(Dictionary<Item, int[]> pos, Item self, int c, int r, int w, int h)
        {
            foreach (var kv in pos) if (kv.Key != self && Overlap(c, r, w, h, kv.Value[0], kv.Value[1], kv.Key.W, kv.Key.H)) return false;
            return true;
        }

        // Ближайшее к (nc, nr) свободное место для блока при раскладке pos
        int[] FreeSpot(Item it, Dictionary<Item, int[]> pos, int nc, int nr)
        {
            int[] best = null; double bestD = double.MaxValue;
            for (int r = 0; r <= Rows - it.H; r++)
                for (int c = 0; c <= Cols - it.W; c++)
                {
                    if (!Free(pos, it, c, r, it.W, it.H)) continue;
                    double d = (c - nc) * (c - nc) + (r - nr) * (r - nr) * 1.4;
                    if (d < bestD) { bestD = d; best = new[] { c, r }; }
                }
            return best;
        }

        // Раскладка, если блок встанет в (c, r); null — не помещается
        Dictionary<Item, int[]> Plan(Item it, int c, int r)
        {
            if (!InBounds(c, r, it.W, it.H)) return null;
            var basePos = Committed();
            var hit = new List<Item>();
            foreach (var kv in basePos) if (kv.Key != it && Overlap(c, r, it.W, it.H, kv.Value[0], kv.Value[1], kv.Key.W, kv.Key.H)) hit.Add(kv.Key);
            var res = new Dictionary<Item, int[]>(basePos);
            res[it] = new[] { c, r };
            if (hit.Count == 0) return res;
            // 1) задетые сдвигаются все вместе туда, откуда взяли блок
            int minC = int.MaxValue, minR = int.MaxValue;
            foreach (Item o in hit) { minC = Math.Min(minC, o.C); minR = Math.Min(minR, o.R); }
            foreach (int[] d in new[] { new[] { it.C - c, it.R - r }, new[] { it.C - minC, it.R - minR } })
            {
                var tryPos = new Dictionary<Item, int[]>(res);
                bool ok = true;
                foreach (Item o in hit) tryPos[o] = new[] { o.C + d[0], o.R + d[1] };
                foreach (Item o in hit)
                {
                    int[] p = tryPos[o];
                    if (!InBounds(p[0], p[1], o.W, o.H) || !Free(tryPos, o, p[0], p[1], o.W, o.H)) { ok = false; break; }
                }
                if (ok) return tryPos;
            }
            // 2) каждый задетый ищет ближайшее свободное место — крупные первыми
            var order = new List<Item>(hit);
            order.Sort(delegate(Item a, Item b) { return (b.W * b.H).CompareTo(a.W * a.H); });
            var cur = new Dictionary<Item, int[]>(res);
            foreach (Item o in order) cur.Remove(o);
            foreach (Item o in order)
            {
                int[] spot = FreeSpot(o, cur, o.C, o.R);
                if (spot == null) return null;
                cur[o] = spot;
            }
            return cur;
        }

        void Apply(Dictionary<Item, int[]> plan)
        {
            foreach (var kv in plan) { kv.Key.C = kv.Value[0]; kv.Key.R = kv.Value[1]; }
            Save();
        }

        // Раскладка на экране: committed или (во время перетаскивания) предварительная
        void Layout(bool animate) { Show(Committed(), null, animate); }

        void Show(Dictionary<Item, int[]> pos, Item skip, bool animate)
        {
            foreach (Item it in Items)
            {
                bool hidden = !pos.ContainsKey(it);
                it.El.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
                if (it.Overlay != null) it.Overlay.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
                if (hidden || it == skip) continue;
                int[] p = pos[it];
                Place(it.El, X(p[0]), Y(p[1]), animate);
                if (it.Overlay != null) Place(it.Overlay, X(p[0]), Y(p[1]), animate);
            }
            RenderTray();
        }

        static void Place(UIElement e, double x, double y, bool animate)
        {
            if (!animate || double.IsNaN(Canvas.GetLeft(e)))
            {
                e.BeginAnimation(Canvas.LeftProperty, null); e.BeginAnimation(Canvas.TopProperty, null);
                Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
                return;
            }
            if (Math.Abs(Canvas.GetLeft(e) - x) < 0.5 && Math.Abs(Canvas.GetTop(e) - y) < 0.5) return;
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            e.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
            e.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
        }

        // ---- режим правки
        public void SetEditing(bool on)
        {
            if (on == Editing) return;
            Editing = on;
            overlayLayer.Children.Clear();
            target.Visibility = Visibility.Collapsed;
            foreach (Item it in Items)
            {
                if (it.Wiggle != null) it.Wiggle.BeginAnimation(RotateTransform.AngleProperty, null);
                if (!on) { it.Overlay = null; it.El.RenderTransform = null; it.Wiggle = null; continue; }
                it.Overlay = MakeOverlay(it);
                overlayLayer.Children.Add(it.Overlay);
                // блоки еле заметно покачиваются, как значки на телефоне; крупные — меньше, чтобы не рябило
                it.Wiggle = new RotateTransform(0);
                it.El.RenderTransformOrigin = new Point(0.5, 0.5); it.El.RenderTransform = it.Wiggle;
                it.Overlay.RenderTransformOrigin = new Point(0.5, 0.5); it.Overlay.RenderTransform = it.Wiggle;
                double amp = Math.Max(0.25, Math.Min(1.1, 70 / Math.Max(1, Math.Max(it.El.Width, it.El.Height))));
                var a = new DoubleAnimation(-amp, amp, TimeSpan.FromMilliseconds(170 + (it.Id.GetHashCode() & 63))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
                it.Wiggle.BeginAnimation(RotateTransform.AngleProperty, a);
            }
            tray.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            Layout(false);
            if (!on && Done != null) Done();
        }

        Border MakeOverlay(Item it)
        {
            var g = new Grid();
            var close = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(Color.FromRgb(0x48, 0x48, 0x50)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-7, -7, 0, 0), Cursor = Cursors.Hand, ToolTip = "Спрятать «" + it.Name + "»",
                Child = new Rectangle { Width = 9, Height = 1.8, RadiusX = 0.9, RadiusY = 0.9, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            close.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; it.Hidden = true; Layout(true); Save(); };
            Fx.Press(close, 0.85);
            g.Children.Add(close);
            var line = Accent.Clone(); line.Opacity = 0.75;
            var ov = new Border
            {
                Width = it.El.Width, Height = it.El.Height, Child = g, CornerRadius = new CornerRadius(16), Cursor = Cursors.SizeAll, ToolTip = it.Name + " — перетащите, чтобы переставить",
                Background = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)), BorderBrush = line, BorderThickness = new Thickness(1.5)
            };
            // ---- перетаскивание: блок едет за курсором, остальные заранее расступаются
            Point grab = new Point(); bool dragging = false; Dictionary<Item, int[]> plan = null; int lastC = -1, lastR = -1;
            ov.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true; dragging = true; plan = null; lastC = it.C; lastR = it.R;
                grab = e.GetPosition(Root);
                ov.CaptureMouse();
                Panel.SetZIndex(it.El, 45); Panel.SetZIndex(ov, 55);
                // «поднять» блок: чуть крупнее и с тенью
                it.El.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Opacity = 0.55, Color = Colors.Black };
                var lift = new ScaleTransform(1, 1);
                var tg = new TransformGroup(); tg.Children.Add(lift); if (it.Wiggle != null) tg.Children.Add(it.Wiggle);
                it.El.RenderTransform = tg;
                var up = new DoubleAnimation(1.04, TimeSpan.FromMilliseconds(140));
                lift.BeginAnimation(ScaleTransform.ScaleXProperty, up); lift.BeginAnimation(ScaleTransform.ScaleYProperty, up);
                ShowTarget(it, it.C, it.R, true);
            };
            ov.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!dragging) return;
                Point p = e.GetPosition(Root);
                double x = X(it.C) + p.X - grab.X, y = Y(it.R) + p.Y - grab.Y;
                Place(it.El, x, y, false); Place(ov, x, y, false);
                int c = Math.Max(0, Math.Min(Cols - it.W, (int)Math.Round(x / (CellW + Gap))));
                int r = Math.Max(0, Math.Min(Rows - it.H, (int)Math.Round(y / (CellH + Gap))));
                if (c == lastC && r == lastR) return;
                lastC = c; lastR = r;
                plan = c == it.C && r == it.R ? null : Plan(it, c, r);
                Show(plan ?? Committed(), it, true);   // остальные расступаются заранее
                ShowTarget(it, c, r, c == it.C && r == it.R || plan != null);
            };
            ov.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (!dragging) return;
                dragging = false; ov.ReleaseMouseCapture();
                Panel.SetZIndex(it.El, 0); Panel.SetZIndex(ov, 0);
                it.El.Effect = null; it.El.RenderTransform = it.Wiggle;
                target.Visibility = Visibility.Collapsed;
                bool moved = lastC != it.C || lastR != it.R;
                if (plan != null) Apply(plan);
                else if (moved && Say != null) Say("Сюда не помещается");
                plan = null;
                Layout(true);
            };
            ov.LostMouseCapture += delegate { if (dragging) { dragging = false; it.El.Effect = null; it.El.RenderTransform = it.Wiggle; target.Visibility = Visibility.Collapsed; plan = null; Layout(true); } };
            return ov;
        }

        void ShowTarget(Item it, int c, int r, bool ok)
        {
            target.Width = it.El.Width; target.Height = it.El.Height;
            target.BorderBrush = ok ? OkLine : BadLine; target.Background = ok ? OkFill : BadFill;
            Place(target, X(c), Y(r), target.Visibility == Visibility.Visible);
            target.Visibility = Visibility.Visible;
        }

        // Для проверки без мыши: то же, что отпустить блок в ячейке (c, r)
        public bool TestMove(string id, int c, int r) { Item it = Find(id); var p = it == null ? null : Plan(it, c, r); if (p != null) Apply(p); Layout(false); return p != null; }
        public void TestHide(string id) { Item it = Find(id); if (it != null) { it.Hidden = true; Layout(false); Save(); } }

        // Вернуть спрятанный блок на ближайшее к его обычному месту свободное место
        void Restore(Item it)
        {
            int[] spot = FreeSpot(it, Committed(), it.DefC, it.DefR);
            if (spot == null) { if (Say != null) Say("Нет свободного места — сначала уберите что-нибудь"); return; }
            it.C = spot[0]; it.R = spot[1]; it.Hidden = false;
            Layout(true); Save();
        }

        void RenderTray()
        {
            trayItems.Children.Clear();
            int n = 0;
            foreach (Item it in Items)
            {
                if (!it.Hidden) continue;
                n++;
                Item cur = it;
                var chip = new Border
                {
                    Height = 26, CornerRadius = new CornerRadius(13), Padding = new Thickness(9, 0, 11, 1), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand,
                    Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), ToolTip = "Вернуть на свободное место",
                    Child = new TextBlock { Text = "+  " + it.Name, Foreground = Brushes.White, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center }
                };
                chip.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; Restore(cur); };
                Fx.Press(chip, 0.94);
                trayItems.Children.Add(chip);
            }
            if (n == 0) trayItems.Children.Add(new TextBlock { Text = "Тащите блоки мышью · «−» прячет · Esc — готово", Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) });
        }
    }
}
