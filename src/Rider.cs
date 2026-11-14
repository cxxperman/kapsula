// Kapsula — «остров» для Windows. Часть исходного кода: Rider.cs
// Зверёк-попутчик и «характер» движения острова.
//  • Один и тот же зверёк сам переходит туда, куда вы смотрите. Раскрыли остров — выпрыгивает из капсулы и садится
//    на нижний край панели, свесив лапки. Переключили вкладку — перескакивает следом. Где на странице живёт «свой»
//    зверёк (погода, таймер, пустые списки, строка команд, карточка), запрыгивает на его место. Под музыку качается
//    на жидком крае. Свернули — прыгает обратно в капсулу. Пока вкладку перестраивают — в каске следит за стройкой.
//  • «Характер» анимаций: пружина, плавно, желе, мяч, капля или меньше движения.
//  • Светлая кромка и мягкая тень острова; блоки страниц появляются по очереди.
//  • Зоны сброса: файл над островом — «На полку», «В ярлыки», «В чат».
//  • Экономный опрос: свёрнутый остров вдали от курсора проверяет его реже.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Kapsula
{
    partial class IslandWindow
    {
        // ================================================================ характер движения
        public static readonly string[] MotionNames = { "Пружина", "Плавно", "Желе", "Мяч", "Капля", "Меньше движения" };
        public int MotionStyle;
        public bool ReducedMotion { get { return MotionStyle == 5; } }
        public bool RimOn = true, ShadowOn, CascadeOn = true, RideOn = true;

        void LoadMotion()
        {
            int m;
            if (!int.TryParse(Store.Get("motion"), out m) || m < 0 || m >= MotionNames.Length)
                m = SystemParameters.ClientAreaAnimation ? 0 : 5;   // в Windows выключены анимации — и у острова меньше движения
            ApplyMotion(m);
            RideOn = Store.Get("petRide") != "0";
            CascadeOn = Store.Get("cascade") != "0";
            RimOn = Store.Get("rim") != "0";
            ShadowOn = Store.Get("shadow") == "1";
            // для проверок: характер и украшения без записи в настройки
            string tm = Environment.GetEnvironmentVariable("KAPSULA_MOTION"), td = Environment.GetEnvironmentVariable("KAPSULA_DECOR");
            if (tm != null && int.TryParse(tm, out m) && m >= 0 && m < MotionNames.Length) ApplyMotion(m);
            if (td != null) { RimOn = td.Contains("rim"); ShadowOn = td.Contains("shadow"); }
        }

        public void SetMotion(int m)
        {
            if (m < 0 || m >= MotionNames.Length) return;
            Store.Set("motion", m.ToString());
            ApplyMotion(m);
            DemoMotion();
        }

        // Жёсткость и вязкость пружин формы. Пружина без вязкости колышется, вязкая — подходит плавно.
        void ApplyMotion(int m)
        {
            MotionStyle = m;
            double kw = 250, cw = 23, kh = 250, ch = 23, kr = 250, cr = 26, ko = 340, co = 37;
            switch (m)
            {
                case 1: kw = kh = kr = 210; cw = ch = cr = 29; break;                       // плавно: без отскока
                case 2: kw = kh = 300; cw = ch = 17; kr = 300; cr = 19; break;              // желе: колышется
                case 3: kw = kh = kr = 480; cw = ch = 22; cr = 26; break;                   // мяч: быстро и с отскоком
                case 4: kh = 300; ch = 24; kw = 150; cw = 20; kr = 240; cr = 24; break;      // капля: стекает вниз, потом расплывается вширь
                case 5: kw = kh = kr = 900; cw = ch = cr = 60; ko = 900; co = 60; break;    // меньше движения
            }
            sw.K = kw; sw.C = cw; sh.K = kh; sh.C = ch; sr.K = kr; sr.C = cr;
            foreach (Spring s in so) { s.K = ko; s.C = co; }
        }

        // Показать характер в деле: остров раскрывается и через пару секунд сворачивается сам
        DispatcherTimer demoTimer;
        public void DemoMotion()
        {
            if (ForceOpen != -1 || state == 4 || state == 3) return;
            if (state == 2 && !pinned && EditingBoard() == null)
            {
                // уже открыт — свернуть и раскрыть заново, чтобы было видно новый характер
                SetState(0);
                if (demoTimer == null) { demoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) }; demoTimer.Tick += delegate { demoTimer.Stop(); DemoMotion(); }; }
                demoTimer.Stop(); demoTimer.Start();
                return;
            }
            if (state == 2) return;
            holdOpenUntil = Environment.TickCount + 1600;
            SetState(2);
        }

        void CascadeBlocks(int p, int startMs)
        {
            if (!CascadeOn || ReducedMotion) return;
            Board b = BoardFor(p);
            if (b != null && !b.Editing) b.Cascade(startMs, 26);
        }

        // ================================================================ кромка и тень
        Path rimPath;
        Path[] shadowPaths;
        LinearGradientBrush rimBrush;
        Color decorInk = Color.FromArgb(1, 1, 2, 3);

        void BuildDecor()
        {
            // Мягкая тень — несколько расходящихся полупрозрачных обводок той же формы, чуть ниже острова.
            // Без эффекта размытия: такая тень почти ничего не стоит даже на слабом компьютере.
            double[] th = { 3, 7, 12, 19 };
            byte[] al = { 0x1E, 0x13, 0x0B, 0x06 };
            shadowPaths = new Path[th.Length];
            var down = new TranslateTransform(0, 2.5);
            for (int i = 0; i < th.Length; i++)
            {
                var p = new Path { Stroke = new SolidColorBrush(Color.FromArgb(al[i], 0, 0, 0)), StrokeThickness = th[i], StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false, Visibility = Visibility.Collapsed, RenderTransform = down, SnapsToDevicePixels = false };
                shadowPaths[i] = p;
                root.Children.Insert(0, p);
            }
            // Светлая кромка: тонкий блик по краю, сверху прозрачный, к нижнему краю ярче
            rimBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            rimBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            rimBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.4));
            rimBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            rimPath = new Path { Stroke = rimBrush, StrokeThickness = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed, SnapsToDevicePixels = false };
            root.Children.Insert(root.Children.IndexOf(shape) + 1, rimPath);
        }

        void ApplyDecor(Geometry g)
        {
            if (rimPath == null) return;
            bool rim = RimOn && !gameLite;
            if (rim)
            {
                var sb = Ink as SolidColorBrush;
                Color ic = sb != null ? sb.Color : Colors.Black;
                if (ic != decorInk)
                {
                    // на светлом острове блик тёмный, на тёмном — светлый
                    decorInk = ic;
                    bool light = ic.R + ic.G + ic.B > 380;
                    Color c = light ? Colors.Black : Colors.White;
                    rimBrush.GradientStops[0].Color = Color.FromArgb(0, c.R, c.G, c.B);
                    rimBrush.GradientStops[1].Color = Color.FromArgb((byte)(light ? 0x0A : 0x07), c.R, c.G, c.B);
                    rimBrush.GradientStops[2].Color = Color.FromArgb((byte)(light ? 0x30 : 0x2C), c.R, c.G, c.B);
                }
                rimPath.Data = g;
                rimPath.Opacity = shape.Opacity;
            }
            rimPath.Visibility = rim ? Visibility.Visible : Visibility.Collapsed;
            bool shadow = ShadowOn && !gameLite;
            foreach (Path p in shadowPaths)
            {
                if (shadow) p.Data = g;
                p.Visibility = shadow ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // ================================================================ зверёк-попутчик
        PetView rider;
        Canvas petLayer;
        readonly TranslateTransform riderPos = new TranslateTransform();
        readonly ScaleTransform riderSize = new ScaleTransform(1, 1), riderSquash = new ScaleTransform(1, 1);
        const double RiderPx = 1.6;
        // Где зверёк по плану: 0 — нигде, 1 — в капсуле, 2 — на месте «своего» зверька страницы, 3 — на нижнем краю,
        // 4 — на зоне сброса, над которой файл
        int rKind;
        PetView rSeat;                     // чьё место занял или куда летит
        bool rOut;                         // попутчик виден сам: летит, сидит на краю или растворяется
        bool rFly, rHave;                  // rHave — rX, rY, rS знают, где он сидит «внутри» (капсула, чужое место)
        double rX, rY, rS = 1;             // левый верхний угол тела и масштаб — в координатах острова
        double fX, fY, fS, fArc;           // откуда летит и как высоко прыгает
        int fAt, fMs, rLandAt, rFadeAt, rPokes, rPokeAt;
        readonly HashSet<PetView> rTouched = new HashSet<PetView>();   // чьи места занимал: если попутчика выключат, всем вернуть видимость
        List<Point> rimTips;               // нижний край острова в последнем кадре (капли жидкого края)
        double rimH, rimBl, rimBr;
        public string TestRider;           // ключ --ridertest: писать путь зверька в файл

        void BuildRider()
        {
            petLayer = new Canvas { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Width = BaseW, Height = BaseH, ClipToBounds = false };
            rider = new PetView(RiderPx, Ink);
            var tg = new TransformGroup();
            tg.Children.Add(riderSquash); tg.Children.Add(riderSize); tg.Children.Add(riderPos);
            rider.Root.RenderTransform = tg;
            rider.Root.VerticalAlignment = VerticalAlignment.Top;
            rider.Root.IsHitTestVisible = true;
            rider.Root.Cursor = Cursors.Hand;
            rider.Root.ToolTip = null;
            rider.Root.Visibility = Visibility.Collapsed;
            rider.Root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; RiderPoke(); };
            petLayer.Children.Add(rider.Root);
            root.Children.Add(petLayer);
        }

        bool RiderAllowed() { return rider != null && RideOn && PetProps() && stickerFrames == null && !gameLite && !hiddenFs && TestCharacter == null; }

        // Куда зверьку сейчас: в капсулу, на место «своего» зверька страницы, на край острова или на зону сброса
        int RiderGoal(out PetView seat)
        {
            seat = null;
            if (!RiderAllowed()) return 0;
            switch (state)
            {
                case 0: return claudeDot != null && claudeDot.Visibility == Visibility.Visible ? 1 : 0;
                case 1: return 3;
                case 2:
                    if (dropOn) return dropHover >= 0 ? 4 : 3;
                    {
                        PetView found = pages != null && !(page == PChat && Ai.Duck) ? FindSeat(pages[page]) : null;
                        if (found != null && EditingBoard() == null) { seat = found; return 2; }
                        // в правке вкладки зверёк — прораб на краю; «свой» зверёк страницы уступает ему место, чтобы их не было двое
                        if (found != null && found.Root.Opacity > 0) { found.Root.Opacity = 0; Touch(found); }
                    }
                    return 3;
                // карточка: если на ней свой зверёк (Claude Code, таймер) — на его место; иначе зверёк приносит
                // уведомление и сидит на нижнем краю карточки под значком приложения
                case 3: if (cardPet != null && cardPet.Root.Visibility == Visibility.Visible) { seat = cardPet; return 2; } return 5;
                case 4: if (cmdPet != null && cmdPet.Root.Visibility == Visibility.Visible) { seat = cmdPet; return 2; } return 0;
            }
            return 0;
        }

        // Запомнить зверька, чьё место занимали. Тех, кого страница уже убрала (нарисовала заново), — забыть,
        // иначе на вкладке, которая перерисовывается дважды в секунду, список рос бы без конца.
        void Touch(PetView p)
        {
            rTouched.Add(p);
            if (rTouched.Count < 24) return;
            var gone = new List<PetView>();
            foreach (PetView t in rTouched) if (PresentationSource.FromVisual(t.Root) == null) gone.Add(t);
            foreach (PetView t in gone) rTouched.Remove(t);
        }

        // Первый видимый зверёк страницы, которого не прячет прокрутка
        PetView FindSeat(FrameworkElement pg)
        {
            if (pg == null || pg.Visibility != Visibility.Visible) return null;
            var stack = new Stack<DependencyObject>();
            stack.Push(pg);
            while (stack.Count > 0)
            {
                DependencyObject d = stack.Pop();
                var fe = d as FrameworkElement;
                if (fe != null)
                {
                    if (fe.Visibility != Visibility.Visible) continue;   // свёрнутая ветка
                    var pv = fe.Tag as PetView;
                    if (pv != null && pv != rider && pv.Root == fe && pv.Px >= 1.2 && SeatInView(pv, pg)) return pv;
                }
                int n = VisualTreeHelper.GetChildrenCount(d);
                for (int i = n - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(d, i));
            }
            return null;
        }

        static bool SeatInView(PetView pv, FrameworkElement pg)
        {
            FrameworkElement b = pv.Figure;
            if (b.ActualWidth <= 0 || pg.ActualWidth <= 0) return false;
            Rect r;
            try { r = new Rect(b.TranslatePoint(new Point(0, 0), pg), new Size(b.ActualWidth, b.ActualHeight)); } catch { return false; }
            if (r.Left < -2 || r.Top < -2 || r.Right > pg.ActualWidth + 2 || r.Bottom > pg.ActualHeight + 2) return false;
            for (DependencyObject d = VisualTreeHelper.GetParent(b); d != null && d != pg; d = VisualTreeHelper.GetParent(d))
            {
                var sv = d as ScrollViewer;
                if (sv == null) continue;
                Rect vr;
                try { vr = new Rect(sv.TranslatePoint(new Point(0, 0), pg), new Size(sv.ActualWidth, sv.ActualHeight)); } catch { return false; }
                vr.Inflate(2, 2);
                if (!vr.Contains(r)) return false;
            }
            return true;
        }

        // Левый верхний угол тела зверька страницы в координатах острова (без его собственных подпрыгиваний)
        bool BodyPoint(PetView pv, out Point pt)
        {
            pt = new Point();
            if (pv == null || pv.Figure.ActualWidth <= 0) return false;
            // страница могла уже убрать этого зверька (нарисовала заново): тогда пересчёт координат молча вернул бы ноль
            if (!root.IsAncestorOf(pv.Root)) return false;
            try
            {
                Vector off = VisualTreeHelper.GetOffset(pv.Figure);
                pt = pv.Root.TranslatePoint(new Point(off.X, off.Y), root);
                return true;
            }
            catch { return false; }
        }

        // Где зверёк будет в капсуле, когда она договорит своё движение (пока она ещё меняет ширину)
        bool PillPoint(out Point pt)
        {
            pt = new Point();
            if (claudeDot == null || claudeDot.Visibility != Visibility.Visible || claudeDot.ActualWidth <= 0) return false;
            try
            {
                Point p = claudeDot.TranslatePoint(new Point(0, 0), compactHost);
                Vector off = VisualTreeHelper.GetOffset((Visual)claudeDot.Children[0]);
                double w = (state == 0 ? sw.Target : CompactWidth()) + 2 * Ear;
                double cw = compactHost.ActualWidth > 0 ? compactHost.ActualWidth : w;
                double x = petByCover ? BaseW / 2 + w / 2 - (cw - p.X) : BaseW / 2 - w / 2 + p.X;   // танцует — справа, иначе слева
                pt = new Point(x + off.X, (pillH - 9 * PetPx) / 2);
                return true;
            }
            catch { return false; }
        }

        // Нижний край острова в точке x — вместе с каплями жидкого края (тот же изгиб, что рисует Wave)
        double EdgeY(double x)
        {
            List<Point> tips = rimTips;
            if (tips == null || tips.Count == 0 || x <= rimBl || x >= rimBr) return Math.Max(20, sh.X);
            Point prev = new Point(rimBl, rimH);
            for (int i = 0; i <= tips.Count; i++)
            {
                Point t = i < tips.Count ? tips[i] : new Point(rimBr, rimH);
                if (x <= t.X)
                {
                    double u = t.X > prev.X ? (x - prev.X) / (t.X - prev.X) : 0;
                    double cy = rimH - ((prev.Y - rimH) + (t.Y - rimH)) / 2;
                    return (1 - u) * (1 - u) * prev.Y + 2 * u * (1 - u) * cy + u * u * t.Y;
                }
                prev = t;
            }
            return rimH;
        }

        // Где сидеть на краю: под значком открытой вкладки, в мини-плеере — справа. Встаёт на ближайшую каплю:
        // под музыку она качает зверька.
        double RimX()
        {
            double cx = BaseW / 2, x = cx;
            if (state == 2 && tabs != null)
            {
                Border t = tabs[TabGroups[GroupOf(page)][0]];
                try { if (t.ActualWidth > 0) x = BaseW / 2 - EW / 2 + t.TranslatePoint(new Point(t.ActualWidth / 2, 0), expandedHost).X; } catch { }
            }
            else if (state == 1) x = cx + MiniW() / 2 - 62;
            return cx + Math.Round((x - cx) / Pitch) * Pitch;
        }

        // Цель полёта прямо сейчас: левый верхний угол тела и масштаб
        bool RiderSpot(out double x, out double y, out double s)
        {
            x = y = 0; s = 1;
            Point p;
            switch (rKind)
            {
                case 1: if (!PillPoint(out p)) return false; x = p.X; y = p.Y; s = PetPx / RiderPx; return true;
                case 2: if (!BodyPoint(rSeat, out p)) return false; x = p.X; y = p.Y; s = rSeat.Px / RiderPx; return true;
                case 3:
                {
                    double c = RimX();
                    x = c - 5.5 * RiderPx; y = EdgeY(c) - 8 * RiderPx + 0.6; s = 1;   // тело над краем, лапки свисают
                    return true;
                }
                case 4:
                    if (!DropSeat(out p)) return false;
                    x = p.X - 5.5 * RiderPx; y = p.Y - 9 * RiderPx + 1.5; s = 1;    // стоит на верхней кромке зоны
                    return true;
                case 5:
                {
                    double c = CardSpotX();
                    x = c - 5.5 * RiderPx; y = EdgeY(c) - 8 * RiderPx + 0.6; s = 1;   // на нижнем краю карточки, лапки свисают
                    return true;
                }
            }
            return false;
        }

        // Под значком приложения на карточке: там зверёк отдаёт письмо и ждёт, пока его прочтут
        double CardSpotX()
        {
            double w = cardHost != null && !double.IsNaN(cardHost.Width) ? cardHost.Width : CardW;
            double x = BaseW / 2 - w / 2 + 18 + 26;
            try
            {
                if (cardGrid != null && cardGrid.Visibility == Visibility.Visible && cardThumbBox != null && cardThumbBox.ActualWidth > 0)
                    x = BaseW / 2 - w / 2 + cardThumbBox.TranslatePoint(new Point(cardThumbBox.ActualWidth / 2, 0), cardHost).X;
            }
            catch { }
            return x;
        }

        // Что зверёк несёт на карточку: "" — письмо, "bell" — колокольчик (напоминание). Назначается до показа карточки.
        string cardCarry = "", cardCarryNext = "";

        // Долетел до карточки: отдаёт письмо, а напоминание — звонит в колокольчик
        void RiderHandOver()
        {
            if (cardCarry == "bell") rider.Play("ring", 1300);
            else if (cardCarry == "buds") rider.Play("budson", 2200);   // наушники: надевает и пританцовывает
            else if (cardCarry == "cup") rider.Play("cupraise", 2200);  // достижение: поднимает кубок, вокруг искры
            else if (cardCarry == "phone") rider.Play("ringphone", 1600);   // входящий звонок: трубка трясётся
            else if (riderDigNext) { riderDigNext = false; rider.Play("dig", 2600); }   // пятое письмо за минуту — выкапывается из кучи
            else rider.Play("give", 800);
        }

        // Новая карточка пришла, пока зверёк сидит на прежней, — он отдаёт и её
        void RiderDeliver()
        {
            if (rider != null && rOut && !rFly && rKind == 5 && !ReducedMotion) RiderHandOver();
        }

        // Перед любой сменой: если зверёк сидит «внутри» (в капсуле или на чужом месте), запомнить, где именно
        void RiderCapture()
        {
            if (rider == null || rOut) return;
            double x, y, s;
            if ((rKind == 1 || rKind == 2) && RiderSpot(out x, out y, out s)) { rX = x; rY = y; rS = s; rHave = true; rHaveAt = Environment.TickCount; }
        }
        int rHaveAt;

        // Сменилась цель — зверёк перебирается туда
        void RiderRetarget()
        {
            if (rider == null) return;
            PetView seat;
            int kind = RiderGoal(out seat);
            if (kind == rKind && seat == rSeat)
            {
                // цель та же, но место могло сдвинуться: другая зона сброса, другая вкладка над краем
                if ((kind == 3 || kind == 4 || kind == 5) && rOut && !rFly && rFadeAt == 0)
                {
                    double tx, ty, ts;
                    if (RiderSpot(out tx, out ty, out ts) && (Math.Abs(tx - rX) > 6 || kind == 4 && Math.Abs(ty - rY) > 6))
                    {
                        if (ReducedMotion) SetRider(tx, ty, ts, 1, 1); else StartFlight(rX, rY, rS);
                    }
                }
                return;
            }
            if (kind == 2 && rKind == 2 && !rOut && seat != null && rSeat != null)
            {
                // страница нарисовала своего зверька заново на том же месте — просто пересесть
                Point a;
                if (BodyPoint(seat, out a) && Math.Abs(a.X - rX) < 4 && Math.Abs(a.Y - rY) < 4) { rTouched.Remove(rSeat); rSeat = seat; Touch(seat); return; }
            }
            // откуда прыгать: с того места, где он сейчас (или где сидел «внутри» совсем недавно)
            bool hasStart = rOut || rHave && unchecked(Environment.TickCount - rHaveAt) < 2500;
            int oldKind = rKind; PetView oldSeat = rSeat;
            if (!rOut)
            {
                // выходит наружу: прежнее место пустеет, чтобы зверьков не стало двое
                if (kind != 0 && oldKind == 1 && claudeDot != null) claudeDot.Opacity = 0;
                if (kind != 0 && oldKind == 2 && oldSeat != null) oldSeat.Root.Opacity = 0;
            }
            rKind = kind; rSeat = seat;
            if (seat != null) { seat.Root.Opacity = 0; Touch(seat); }
            if (TestRider != null && seat != null)
            {
                Point dbg; bool okp = BodyPoint(seat, out dbg);
                if (riderLog == null) riderLog = new System.Text.StringBuilder();
                riderLog.AppendLine("SEAT page=" + page + " ok=" + okp + " at=" + dbg.X.ToString("0.0") + "," + dbg.Y.ToString("0.0") + " px=" + seat.Px + " arr=" + seat.Root.IsArrangeValid + " w=" + seat.Figure.ActualWidth.ToString("0.0") + " src=" + (PresentationSource.FromVisual(seat.Root) != null) + " off=" + VisualTreeHelper.GetOffset(seat.Figure));
            }
            if (kind == 1 && claudeDot != null) claudeDot.Opacity = 0;
            if (kind == 0)
            {
                if (claudeDot != null && !walkerOut) claudeDot.Opacity = 1;   // гуляет по окну — в капсуле его нет
                if (rOut) StartFade();   // спрятаться некуда — растворяется на месте
                return;
            }
            double gx, gy, gs;
            if (!hasStart || ReducedMotion)
            {
                // взяться неоткуда (или меньше движения): сразу на месте
                if (kind == 1 || kind == 2) { RiderDock(); return; }
                if (!RiderSpot(out gx, out gy, out gs)) return;
                ShowRiderAt(gx, gy, gs, !hasStart);
                return;
            }
            // уже стоит ровно там (ждал, пока страница нарисует своего зверька) — сразу на место, без прыжка
            if (rOut && (kind == 1 || kind == 2) && RiderSpot(out gx, out gy, out gs) && Math.Abs(gx - rX) < 3 && Math.Abs(gy - rY) < 3) { RiderDock(); return; }
            StartFlight(rX, rY, rS);
        }

        void ShowRiderAt(double x, double y, double s, bool pop)
        {
            rFly = false; rOut = true; rFadeAt = 0;
            rider.Root.BeginAnimation(UIElement.OpacityProperty, null);
            rider.Root.Visibility = Visibility.Visible;
            rider.Seated = true;
            rider.Mood(RiderMood());
            SetRider(x, y, s, 1, 1);
            if (pop)
            {
                // появляется «из ниоткуда»: проявляется и чуть подскакивает
                rider.Root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ReducedMotion ? 120 : 220)));
                if (!ReducedMotion) rider.Play("hop", 600);
            }
            rLandAt = Environment.TickCount;
            Wake();
        }

        double lastGX, lastGY, lastGS;   // последняя известная цель полёта
        bool lastGoalOk;

        void StartFlight(double x0, double y0, double s0)
        {
            lastGoalOk = false;
            double gx, gy, gs;
            if (!RiderSpot(out gx, out gy, out gs)) { gx = x0; gy = y0; gs = s0; }
            double dx = gx - x0, dy = gy - y0, d = Math.Sqrt(dx * dx + dy * dy);
            fX = x0; fY = y0; fS = s0; fAt = Environment.TickCount;
            fMs = (int)Math.Max(280, Math.Min(620, 240 + d * 0.85));
            // прыжок дугой; в капсулу — почти без дуги (выше только край экрана), из капсулы вниз — короткий подскок
            fArc = rKind == 1 ? 5 : rKind == 5 ? 6 : dy > 60 ? 9 : Math.Max(12, Math.Min(42, 12 + Math.Abs(dx) * 0.12 + Math.Max(0, -dy) * 0.08));
            // Оба конца у верхней кромки экрана (капсула ↔ строка команд или карточка): прыгать вверх некуда —
            // зверёк «ныряет» дугой вниз, будто перебирается под краем, держась за него
            if (Math.Max(y0, gy) < 60 && Math.Min(y0, gy) - fArc < 2) fArc = -Math.Min(16, Math.Max(8, Math.Abs(dx) * 0.06));
            rFly = true; rOut = true; rFadeAt = 0;
            rider.Root.BeginAnimation(UIElement.OpacityProperty, null);
            rider.Root.Opacity = 1;
            rider.Root.Visibility = Visibility.Visible;
            rider.Seated = false;
            rider.Mood(rKind == 5 ? (cardCarry == "bell" ? "bell" : cardCarry == "buds" ? "buds" : cardCarry == "cup" ? "cup" : cardCarry == "phone" ? "phone" : "post") : "fly");   // к карточке — с письмом в лапах, к напоминанию — с колокольчиком, к наушникам — с наушниками, к достижению — с кубком, к звонку — с трубкой
            SetRider(x0, y0, s0, 1, 1);
            Wake();
        }

        void StartFade()
        {
            rFly = false;
            if (rFadeAt != 0) return;
            rFadeAt = Environment.TickCount | 1;
            rider.Root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
            Wake();
        }

        // Добрался до «своего» места: дальше там живёт тамошний зверёк — он стоит ровно там же
        void RiderDock()
        {
            rFly = false; rOut = false; rHave = false; rFadeAt = 0;
            if (rKind == 1 && claudeDot != null && !walkerOut) claudeDot.Opacity = 1;
            if (rKind == 2 && rSeat != null) { rSeat.Root.Opacity = 1; rSeat.Glance(0, 300); }
            rider.Root.BeginAnimation(UIElement.OpacityProperty, null);
            rider.Root.Visibility = Visibility.Collapsed;
            rider.Seated = false;
        }

        void SetRider(double x, double y, double s, double sx, double sy)
        {
            rX = x; rY = y; rS = s;
            Vector bo = VisualTreeHelper.GetOffset(rider.Figure);
            riderSize.ScaleX = riderSize.ScaleY = s;
            riderPos.X = x - bo.X * s; riderPos.Y = y - bo.Y * s;
            riderSquash.CenterX = bo.X + 5.5 * RiderPx; riderSquash.CenterY = bo.Y + 9 * RiderPx;   // сплющивается к лапкам
            riderSquash.ScaleX = sx; riderSquash.ScaleY = sy;
        }

        // Не вылетать за остров, пока он меняет форму: не ниже края (лапки свисают), не за боковые стороны
        void ClampInside(ref double x, ref double y, double s)
        {
            double w = Math.Max(60, sw.X), bw = 11 * RiderPx * s;
            double l = BaseW / 2 - w / 2 + 4, r = BaseW / 2 + w / 2 - 4 - bw;
            if (r > l) x = Math.Max(l, Math.Min(r, x));
            double maxY = EdgeY(x + bw / 2) - 8 * RiderPx * s + 0.6;
            if (y > maxY) y = maxY;
            if (y < 1) y = 1;
        }

        bool RiderBusy()
        {
            if (rider == null || !rOut) return false;
            return rFly || rFadeAt != 0 || unchecked(Environment.TickCount - rLandAt) < 360;
        }

        // Каждый кадр, пока зверёк снаружи: полёт, посадка, езда на краю
        void RiderPlace()
        {
            if (rider == null || !rOut) return;
            if (walkerOut && walkPhase == 4) rider.Root.Opacity = 0;   // к нему с окна прыгает гуляющий зверёк — пока не показывать
            int now = Environment.TickCount;
            if (rFadeAt != 0)
            {
                if (unchecked(now - rFadeAt) < 170) return;
                rFadeAt = 0; rOut = false; rHave = false;
                rider.Root.BeginAnimation(UIElement.OpacityProperty, null);
                rider.Root.Opacity = 1;
                rider.Root.Visibility = Visibility.Collapsed;
                return;
            }
            double gx, gy, gs;
            bool goal = RiderSpot(out gx, out gy, out gs);
            if (rFly)
            {
                if (!goal && rKind == 2)
                {
                    // страница нарисовала своего зверька заново, пока попутчик летел, — лететь к новому
                    PetView ns;
                    if (RiderGoal(out ns) == 2 && ns != null && ns != rSeat) { rTouched.Remove(rSeat); rSeat = ns; ns.Root.Opacity = 0; Touch(ns); goal = RiderSpot(out gx, out gy, out gs); }
                    // новый ещё не разложен — долетаем туда, где был прежний: он появится на том же месте
                    if (!goal && lastGoalOk) { gx = lastGX; gy = lastGY; gs = lastGS; goal = true; }
                }
                if (!goal) { StartFade(); return; }
                lastGX = gx; lastGY = gy; lastGS = gs; lastGoalOk = true;
                double p = Clamp01(unchecked(now - fAt) / (double)fMs);
                // отталкивается резко и мягко приземляется; дуга — парабола по пройденному пути, вершина посередине
                double e = 1 - Math.Pow(1 - p, 2.4);
                double x = fX + (gx - fX) * e, y = fY + (gy - fY) * e - fArc * 4 * e * (1 - e), s = fS + (gs - fS) * e;
                if (rKind != 2 && rKind != 4) ClampInside(ref x, ref y, s);
                else if (y < 1) y = 1;   // выше края экрана не улетать
                double st = Math.Sin(Math.PI * p) * 0.13;   // в полёте вытягивается
                SetRider(x, y, s, 1 - st * 0.6, 1 + st);
                if (TestRider != null) RiderLog(now, x, y, s);
                if (p >= 1)
                {
                    rLandAt = now;
                    lastGoalOk = false;
                    Point chk;
                    if (rKind == 2 && !BodyPoint(rSeat, out chk)) { rFly = false; return; }   // места пока нет — ждёт рядом, опрос найдёт нового зверька
                    if (rKind == 1 || rKind == 2) { RiderDock(); return; }
                    rFly = false;
                    rider.Seated = true;
                    rider.Mood(RiderMood());
                    if (rKind == 5 && !ReducedMotion) RiderHandOver();   // принёс уведомление — отдаёт письмо (или звонит в колокольчик)
                }
                return;
            }
            if (!goal) return;
            if ((rKind == 3 || rKind == 5) && Math.Abs(gx - rX) > 6 && !ReducedMotion) { StartFlight(rX, rY, rS); return; }   // вкладку сменили или карточка стала шире — перескочить
            if (rKind == 4 && (Math.Abs(gx - rX) > 6 || Math.Abs(gy - rY) > 6)) { StartFlight(rX, rY, rS); return; }
            // Сидит: едет вместе с краем и каплями; после прыжка пружинит, а при резкой остановке края приседает
            double sq = 0;
            int la = unchecked(now - rLandAt);
            if (la < 340 && !ReducedMotion) { double k = la / 340.0; sq = 0.22 * (1 - k) * (1 - k) * Math.Cos(k * Math.PI * 2.4); }
            if ((rKind == 3 || rKind == 5) && !ReducedMotion) { double acc = -sh.K * (sh.X - sh.Target) - sh.C * sh.V; sq += Math.Max(-0.12, Math.Min(0.18, -acc / 26000)); }
            SetRider(gx, gy, gs, 1 + sq * 0.8, 1 - sq);
            if (TestRider != null && (la < 400 || pollN % 4 == 0)) RiderLog(now, gx, gy, gs);
        }

        // Проверка без мыши (--ridertest файл): остров сам раскрывается, листает вкладки и сворачивается, а путь
        // зверька пишется в файл. Вкладки можно задать переменной KAPSULA_RIDEPAGES, например «1,6,5,7,0».
        void RiderTestScript()
        {
            // KAPSULA_RIDEWALK=1 (вместе с KAPSULA_WALKRECT): на край «окна» → прогулка по краю → домой → снова → остров раскрылся (тает)
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDEWALK") == "1")
            {
                ClaudeAfter(3000, delegate { WalkGo(true); walkUntil = Environment.TickCount + 60000; });
                ClaudeAfter(5200, delegate { Native.RECT r = walkRect; wGoX = EdgeX(r, wX + 140); walkMoving = true; StartWalkTimer(); });
                ClaudeAfter(9000, delegate { WalkHome(); });
                ClaudeAfter(11000, delegate { WalkGo(true); walkUntil = Environment.TickCount + 60000; });
                ClaudeAfter(13200, delegate { holdOpenUntil = Environment.TickCount + 60000; SetState(2); });
                ClaudeAfter(15200, delegate { holdOpenUntil = 0; SetState(0); });
                return;
            }
            // KAPSULA_RIDEPET=1: уголок зверька — его «гладят» (курсор изображает проверка), потом капсула с сердечком;
            // =2 (вместе с KAPSULA_WALKRECT): гладят зверька, который гуляет по краю окна
            string rp = Environment.GetEnvironmentVariable("KAPSULA_RIDEPET");
            if (rp == "1")
            {
                ClaudeAfter(3000, delegate { pinned = true; ShowPage(PPet); SetState(2); });
                ClaudeAfter(6000, delegate { testStrokeFrom = Environment.TickCount; testStrokeUntil = testStrokeFrom + 3500; });
                ClaudeAfter(11500, delegate { pinned = false; SetState(0); });
                return;
            }
            if (rp == "2")
            {
                ClaudeAfter(3000, delegate { WalkGo(true); walkUntil = Environment.TickCount + 60000; });
                ClaudeAfter(7000, delegate { testStrokeFrom = Environment.TickCount; testStrokeUntil = testStrokeFrom + 3500; });
                ClaudeAfter(13000, delegate { WalkHome(); });
                return;
            }
            // =3: уголок зверька — «курсор» замер справа от него: припал, вильнул, прыгнул, «поймал!»
            if (rp == "3")
            {
                ClaudeAfter(3000, delegate { pinned = true; ShowPage(PPet); SetState(2); });
                ClaudeAfter(5500, delegate { testHuntFrom = Environment.TickCount; testHuntUntil = testHuntFrom + 3000; });
                ClaudeAfter(10500, delegate { pinned = false; SetState(0); });
                return;
            }
            // =4 (вместе с KAPSULA_WALKRECT): зверёк на краю окна прыгает на замерший рядом «курсор»
            if (rp == "4")
            {
                ClaudeAfter(3000, delegate { WalkGo(true); walkUntil = Environment.TickCount + 60000; });
                ClaudeAfter(7000, delegate { testHuntFrom = Environment.TickCount; testHuntUntil = testHuntFrom + 3000; });
                ClaudeAfter(13000, delegate { WalkHome(); });
                return;
            }
            // =5: капсула — громкость на максимуме и без звука, чих, мушка, друг зашёл и вышел, «всё сделано» (музыка не играет)
            if (rp == "5")
            {
                ClaudeAfter(1200, delegate { media = new MediaInfo(); RenderMedia(); });   // после выдуманной песни --demo — тишина
                ClaudeAfter(3000, delegate { Toast("", null, "100%", 1.0, null); });
                ClaudeAfter(5500, delegate { Toast("", null, "0%", 0, null); });
                ClaudeAfter(8000, delegate { StartEmote("sneeze", 1300, Environment.TickCount); });
                ClaudeAfter(10000, delegate { StartEmote("fly", 2200, Environment.TickCount); });
                ClaudeAfter(13000, delegate { FriendWave("Марк", true, 3); });
                ClaudeAfter(16000, delegate { FriendWave("Марк", false, 3); });
                ClaudeAfter(19000, delegate { petPartyUntil = Environment.TickCount + 4200; Toast("", "Всё на сегодня сделано!", null, -1, accent); });
                return;
            }
            // =6: пять уведомлений за пять секунд — пятое попутчик не отдаёт, а выкапывается из писем
            if (rp == "6")
            {
                string[] who = { "Саша", "Лена", "Марк", "Дима", "Аня" };
                for (int i = 0; i < 5; i++)
                {
                    int k = i;
                    ClaudeAfter(3000 + i * 1300, delegate { DiscordNotify(who[k], "сообщение " + (k + 1), DemoAvatar(who[k].Substring(0, 1), Color.FromRgb(0x4F, 0x8D, 0xF7), Color.FromRgb(0x8A, 0x5C, 0xF6))); });
                }
                return;
            }
            // =8: остров просто в капсуле — для карточек --democard (кубок за достижение): вкладки не листаются
            if (rp == "8") return;
            // =9: оформление — «Главная», потом карточка с выбором (стеклянные кнопки), потом «Сегодня»
            if (rp == "9")
            {
                ClaudeAfter(3000, delegate { pinned = true; ShowPage(PHome); SetState(2); });
                ToolTip tip = null;   // подсказка-стекло под карандашом (мышь не двигаем — открываем её сами)
                ClaudeAfter(5000, delegate { tip = new ToolTip { Content = pencilBtn.ToolTip, PlacementTarget = pencilBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, IsOpen = true }; });
                ClaudeAfter(7400, delegate { if (tip != null) tip.IsOpen = false; });
                ClaudeAfter(8000, delegate { pinned = false; SetState(0); });
                ClaudeAfter(9000, delegate { SteamInvite("Лена", "Зовёт в игру", DemoAvatar("Л", Color.FromRgb(0xF2, 0x8B, 0x52), Color.FromRgb(0xE8, 0x4A, 0x7A)), "", ""); });
                ClaudeAfter(14000, delegate { pinned = true; ShowPage(PToday); SetState(2); });
                ClaudeAfter(18000, delegate { pinned = false; SetState(0); });
                ClaudeAfter(19500, delegate { SetAwake(true, TimeSpan.FromHours(2), true); });   // «Не засыпать»: надпись и чашка в капсуле
                ClaudeAfter(22000, delegate { pinned = true; ShowPage(PPet); SetState(2); });     // своя вкладка зверька
                ClaudeAfter(26500, delegate { ShowPage(PNote); });                                 // заметка и уведомления — те же стеклянные блоки и переключатели
                ClaudeAfter(30000, delegate { ShowPage(PNotif); });
                ClaudeAfter(33500, delegate { pinned = false; SetState(0); });
                return;
            }
            // =7: «Сегодня» — отмечена последняя открытая задача дня: праздник, потом капсула с конфетти
            if (rp == "7")
            {
                ClaudeAfter(3000, delegate { pinned = true; ShowPage(PToday); SetState(2); });
                ClaudeAfter(5500, delegate
                {
                    DayTask open = Tasks.Load().Find(delegate(DayTask t) { return t.Day == DateTime.Today && !t.Done; });
                    if (open != null) TaskToggle(open.Id);
                });
                ClaudeAfter(7200, delegate { pinned = false; SetState(0); });
                return;
            }
            // KAPSULA_RIDEDISCORD=1: двенадцать секунд в капсуле (состояние звонка присылает проверка вместо плагина), потом «Главная»
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDEDISCORD") == "1")
            {
                ClaudeAfter(4000, delegate { pinned = true; ShowPage(PHome); SetState(2); });
                ClaudeAfter(16000, delegate { pinned = false; SetState(0); });
                return;
            }
            // KAPSULA_RIDEDISCORD=2: всё время в капсуле — надписи «Демка запущена / остановлена», зверёк в звонке
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDEDISCORD") == "2") return;
            // KAPSULA_RIDETODAY=1: вкладка «Сегодня» с задачами и «Скоро» (=2 — просто капсула: ждём карточку задачи)
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDETODAY") == "1")
            {
                ClaudeAfter(3000, delegate { pinned = true; ShowPage(PToday); SetState(2); });
                ClaudeAfter(11000, delegate { pinned = false; SetState(0); });
                return;
            }
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDETODAY") == "2") return;
            // KAPSULA_RIDETODAY=7: «Сегодня» → «Неделя», задача переезжает на завтра (колонка вспыхивает), следующая неделя, снова «Месяц»
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDETODAY") == "7")
            {
                DateTime mon = WeekOf(DateTime.Today);
                Calendar.TestSet(new List<CalEvent> {
                    new CalEvent { Subject = "Планёрка", Location = "", Start = mon.AddDays(1).AddHours(10) },
                    new CalEvent { Subject = "День рождения Лены", Location = "", Start = mon.AddDays(6), AllDay = true },
                    new CalEvent { Subject = "Стоматолог", Location = "", Start = mon.AddDays(10).AddHours(15.5) } });
                ClaudeAfter(3000, delegate { pinned = true; ShowPage(PToday); SetState(2); });
                ClaudeAfter(5500, delegate { SetTodayWeek(true); });
                ClaudeAfter(9000, delegate
                {
                    DayTask t = Tasks.Load().Find(delegate(DayTask x) { return x.Text.StartsWith("Купить билеты"); });
                    if (t != null) TaskMove(t.Id, t.Day.AddDays(1));
                });
                ClaudeAfter(12000, delegate { weekStart = weekStart.AddDays(7); weekSetOn = DateTime.Today; RenderToday(); });
                ClaudeAfter(13500, delegate { AddWeekTasks("в чт в 19 кино"); });   // как будто набрали в поле внизу: ляжет на четверг этой (следующей) недели
                ClaudeAfter(15500, delegate { SetTodayWeek(false); });
                ClaudeAfter(18000, delegate { pinned = false; SetState(0); });
                return;
            }
            // KAPSULA_RIDETODAY=6: в капсуле — отсчёт до задачи со временем (её время — через несколько минут после запуска)
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDETODAY") == "6") return;
            // KAPSULA_RIDETODAY=5: заметка к задаче — раскрыть под первой задачей, «написать» (без клавиатуры), свернуть
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDETODAY") == "5")
            {
                ClaudeAfter(3000, delegate { pinned = true; ShowPage(PToday); SetState(2); });
                ClaudeAfter(5000, delegate
                {
                    DayTask first = Tasks.Load().Find(delegate(DayTask t) { return t.Day == DateTime.Today && !t.Done; });
                    if (first != null) TaskNoteToggle(first.Id);
                });
                ClaudeAfter(6500, delegate { if (taskNoteBox != null) taskNoteBox.Text = "Места у сцены, вход с 18:00\n- взять паспорт\n- зарядить телефон\n- распечатать билеты"; });
                ClaudeAfter(8000, delegate
                {
                    // «щёлкнули» по первому шагу — он отмечается
                    if (taskNoteBox == null) return;
                    string[] lines = taskNoteBox.Text.Split('\n');
                    if (lines.Length > 1) { lines[1] = Tasks.StepToggle(lines[1]); taskNoteBox.Text = string.Join("\n", lines); }
                });
                ClaudeAfter(10000, delegate { if (taskOpenId != null) TaskNoteToggle(taskOpenId); });
                ClaudeAfter(13000, delegate { pinned = false; SetState(0); });
                return;
            }
            // KAPSULA_RIDETODAY=4: строка команд без фокуса — «задача в пятницу в 10 отчёт», потом «задачи» и отметка первой
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDETODAY") == "4")
            {
                ClaudeAfter(3000, delegate { cmdBox.Text = ""; cmdItems.Clear(); RenderCmd(); SetState(4); cmdBox.Text = "задача в пятницу в 10 отчёт"; });
                ClaudeAfter(7000, delegate { cmdBox.Text = "задачи"; });
                ClaudeAfter(10000, delegate { RunCmd(0); });
                ClaudeAfter(13500, delegate { SetState(0); });
                return;
            }
            // KAPSULA_RIDETODAY=3: «Сегодня», и первая задача дня переезжает на послезавтра — как будто её перетащили на число
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDETODAY") == "3")
            {
                ClaudeAfter(3000, delegate { pinned = true; ShowPage(PToday); SetState(2); });
                ClaudeAfter(6000, delegate
                {
                    DayTask first = Tasks.Load().Find(delegate(DayTask t) { return t.Day == DateTime.Today && !t.Done; });
                    if (first != null) TaskMove(first.Id, DateTime.Today.AddDays(2));
                });
                ClaudeAfter(11000, delegate { pinned = false; SetState(0); });
                return;
            }
            // KAPSULA_TRIMTEST=1: замер памяти до и после того, как Kapsula отдала неиспользуемое системе
            if (Environment.GetEnvironmentVariable("KAPSULA_TRIMTEST") == "1")
            {
                // и самая длинная пауза в работе окна (таймер каждые 16 мс) за 5 с до и 10 с после
                int probeLast = 0, maxBefore = 0, maxAfter = 0;
                bool after2 = false;
                var probe = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
                probe.Tick += delegate
                {
                    int n = Environment.TickCount;
                    if (probeLast != 0) { int gap = unchecked(n - probeLast); if (after2) maxAfter = Math.Max(maxAfter, gap); else maxBefore = Math.Max(maxBefore, gap); }
                    probeLast = n;
                };
                ClaudeAfter(15000, delegate { probe.Start(); });
                ClaudeAfter(20000, delegate
                {
                    string before = MemNow();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    TrimMemory();
                    sw.Stop();
                    after2 = true;
                    string after = MemNow();
                    ClaudeAfter(10000, delegate
                    {
                        probe.Stop();
                        try { System.IO.File.WriteAllText(System.IO.Path.Combine(Store.Dir, "trim.txt"), "до: " + before + "\r\nсразу после (" + sw.ElapsedMilliseconds + " мс): " + after + "\r\nчерез 10 с: " + MemNow() + "\r\nсамая длинная пауза окна: до " + maxBefore + " мс, после " + maxAfter + " мс\r\n"); } catch { }
                    });
                });
                return;
            }
            // KAPSULA_RIDEBUDS=1: подключились Bluetooth-наушники с зарядом — зверёк приносит на карточку наушники (после запуска)
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDEBUDS") == "1")
            {
                ClaudeAfter(21000, delegate { BudsAnnounce(new EarbudsState { Model = "AirPods Pro", Single = 80, At = Environment.TickCount }); });
                return;
            }
            // KAPSULA_RIDEDISCORD=3: кнопка демки — «Главная», нажатие (вместо меню берётся первый экран), потом «стоп»
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDEDISCORD") == "3")
            {
                ClaudeAfter(3500, delegate { pinned = true; ShowPage(PHome); SetState(2); });
                ClaudeAfter(6000, delegate { CallKey(4); });
                ClaudeAfter(11000, delegate { CallKey(4); });
                ClaudeAfter(15000, delegate { pinned = false; SetState(0); });
                return;
            }
            // KAPSULA_RIDEDISCORD=4: входящий звонок (его присылает проверка) — карточка, потом «Принять»; =5 — «Отклонить»; =6 — не отвечать
            string ring = Environment.GetEnvironmentVariable("KAPSULA_RIDEDISCORD");
            if (ring == "4" || ring == "5")
            {
                ClaudeAfter(11000, delegate
                {
                    if (state != 3 || ringShownId == null) return;
                    var a = ring == "4" ? cardNoAction : cardBtnAction;   // то же, что нажатие зелёной или красной кнопки
                    hoverBlock = true;
                    SetState(0);
                    if (a != null) a();
                });
                if (ring == "4")
                {
                    // ответили — на «Главной» карточка звонка с тем, кто звонил
                    ClaudeAfter(17000, delegate { pinned = true; ShowPage(PHome); SetState(2); });
                    ClaudeAfter(21000, delegate { pinned = false; SetState(0); });
                }
                return;
            }
            if (ring == "6") return;
            // KAPSULA_CALLTEST=1: «звонок» — восемь секунд в капсуле (зверёк в эфире), потом «Главная» с карточкой звонка
            if (callTest)
            {
                ClaudeAfter(8000, delegate { pinned = true; ShowPage(PHome); SetState(2); });
                ClaudeAfter(11000, delegate { pinned = false; SetState(0); });
                return;
            }
            // KAPSULA_RIDEHOME=1 (вместе с KAPSULA_BATTERY): на «Главной» вместо ярлыков — календарь, сеть и батарея
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDEHOME") == "1")
            {
                ClaudeAfter(2000, delegate
                {
                    testCal = new CalEvent { Subject = "Созвон с командой", Start = DateTime.Now.AddMinutes(25), Location = "" };
                    homeBoard.TestPlace("launcher", 0, 6, false);
                    homeBoard.TestPlace("calendar", 0, 6, true);
                    homeBoard.TestPlace("net", 4, 6, true);
                    homeBoard.TestPlace("battery", 8, 6, true);
                });
                ClaudeAfter(2500, delegate { pinned = true; ShowPage(PHome); SetState(2); });
                ClaudeAfter(9000, delegate { pinned = false; SetState(0); });
                return;
            }
            // KAPSULA_RIDEROOM=1 (вместе с KAPSULA_HABITS): уголок зверька → примерка двух нарядов → напоминание с колокольчиком
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDEROOM") == "1")
            {
                ClaudeAfter(2500, delegate { pinned = true; ShowPage(PPet); SetState(2); });
                ClaudeAfter(5000, delegate { SetOutfit("crown"); RoomChanged(); });
                ClaudeAfter(7000, delegate { SetOutfit("band"); RoomChanged(); });
                ClaudeAfter(9000, delegate
                {
                    pinned = false;
                    cardCarryNext = "bell";
                    ShowCardEx(null, ((char)0xE787).ToString(), "Напоминание", "Полить цветы", delegate { }, "+10 мин", delegate { }, true);
                    cardUntil = Environment.TickCount + 60000;
                });
                ClaudeAfter(13000, delegate { SetState(0); });
                return;
            }
            // KAPSULA_RIDEOUTFIT=1 (вместе с KAPSULA_HABITS): корона получена → карточка → примерка всех нарядов → панель
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDEOUTFIT") == "1")
            {
                ClaudeAfter(2500, delegate { HabitAdd(3, 1); OutfitCardTry(true); });
                ClaudeAfter(6500, delegate { SetState(0); });
                ClaudeAfter(7500, delegate { SetOutfit("phones"); });
                ClaudeAfter(8700, delegate { SetOutfit("band"); });
                ClaudeAfter(9900, delegate { SetOutfit("bow"); });
                ClaudeAfter(11100, delegate { SetOutfit("crown"); });
                ClaudeAfter(12000, delegate { pinned = true; ShowPage(PHome); SetState(2); });
                ClaudeAfter(14500, delegate { pinned = false; SetState(0); });
                return;
            }
            // KAPSULA_RIDEBAT=1 (вместе с KAPSULA_BATTERY=«23,0»): зарядку подключили → отключили → батарея садится → карточку раскрыли
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDEBAT") == "1")
            {
                ClaudeAfter(2500, delegate { batTest = "23,1"; });
                ClaudeAfter(5500, delegate { batTest = "23,0"; });
                ClaudeAfter(8000, delegate { batTest = "19,0"; });
                ClaudeAfter(10800, delegate { cardUntil = Environment.TickCount + 60000; CardExpand(); holdOpenUntil = Environment.TickCount + 60000; });
                ClaudeAfter(13800, delegate { holdOpenUntil = 0; SetState(0); });
                return;
            }
            // KAPSULA_RIDECARD=1: сообщение → раскрыть в панель (улетает в историю) → свернуть → обычная карточка → раскрыть
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDECARD") == "1")
            {
                ClaudeAfter(2500, delegate
                {
                    Quiet("Discord", "Марк", "го вечером в кооп?");
                    ShowCard(null, ((char)0xE8BD).ToString(), "Марк", "го вечером в кооп?", null);
                    SetCardBadge(SourceIcon("discord")); HideCardButton(); CardIsNote();
                    cardUntil = Environment.TickCount + 60000;
                });
                ClaudeAfter(4800, delegate { CardExpand(); holdOpenUntil = Environment.TickCount + 60000; });
                ClaudeAfter(7800, delegate { holdOpenUntil = 0; SetState(0); });
                ClaudeAfter(9300, delegate { ShowCard(null, ((char)0xE896).ToString(), "Загружено", "Отчёт за квартал.pdf · 2,4 МБ", null); cardUntil = Environment.TickCount + 60000; });
                ClaudeAfter(10000, delegate { CardGrabShow(true); });   // «ручка» — как при наведении курсора
                ClaudeAfter(11300, delegate { CardExpand(); holdOpenUntil = Environment.TickCount + 60000; });
                ClaudeAfter(14000, delegate { holdOpenUntil = 0; SetState(0); });
                return;
            }
            // KAPSULA_RIDEFLOAT=1: отцепиться и съехать вниз → раскрыться → к нижнему краю → раскрыться (должен приподняться) → вернуться наверх
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDEFLOAT") == "1")
            {
                Action<double> moveTo = delegate(double topDip)
                {
                    Rect wa = WorkDip();
                    detached = true; se.Target = 0; detFx = 0.5; detFy = (topDip - wa.Top) / wa.Height;
                    FloatTargets(); PlaceDetached(true); SetTargets();
                };
                ClaudeAfter(2500, delegate { moveTo(TestDY + 150); });
                ClaudeAfter(4300, delegate { pinned = true; ShowPage(PHome); SetState(2); });
                ClaudeAfter(6300, delegate { pinned = false; SetState(0); });
                ClaudeAfter(7800, delegate { Rect wa = WorkDip(); moveTo(wa.Bottom - pillH * Scale - 40); });
                ClaudeAfter(9400, delegate { pinned = true; SetState(2); });
                ClaudeAfter(11800, delegate { pinned = false; SetState(0); });
                ClaudeAfter(13400, delegate { Attach(true); });
                ClaudeAfter(15500, delegate { if (riderLog != null && riderLog.Length > 0) { try { System.IO.File.AppendAllText(TestRider, riderLog.ToString()); } catch { } riderLog.Clear(); } });
                return;
            }
            // KAPSULA_RIDESTAGE=1 (вместе с --demo): мини-плеер → «только текст песни» → обратно → яркость: ярко и темно
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDESTAGE") == "1")
            {
                ClaudeAfter(4000, delegate { holdOpenUntil = Environment.TickCount + 60000; SetState(1); });
                ClaudeAfter(6000, delegate { SetMiniLyricsOnly(true); });
                ClaudeAfter(10000, delegate { SetMiniLyricsOnly(false); });
                ClaudeAfter(12000, delegate { holdOpenUntil = 0; SetState(0); });
                ClaudeAfter(13500, delegate { BrightnessShown(85); });
                ClaudeAfter(16000, delegate { BrightnessShown(12); });
                ClaudeAfter(19000, delegate { if (riderLog != null && riderLog.Length > 0) { try { System.IO.File.AppendAllText(TestRider, riderLog.ToString()); } catch { } riderLog.Clear(); } });
                return;
            }
            int[] seq = { PClip, PWeather, PNote, PSystem, PHome };
            string env = Environment.GetEnvironmentVariable("KAPSULA_RIDEPAGES");
            if (!string.IsNullOrEmpty(env))
            {
                var l = new List<int>();
                foreach (string s in env.Split(',')) { int v; if (int.TryParse(s.Trim(), out v) && v >= 0 && v <= PPet) l.Add(v); }
                if (l.Count > 0) seq = l.ToArray();
            }
            // KAPSULA_RIDEMINI=1 — сначала мини-плеер (нужна играющая музыка или --demo), потом панель
            bool mini = Environment.GetEnvironmentVariable("KAPSULA_RIDEMINI") == "1";
            int at = 2500;
            if (mini) { ClaudeAfter(at, delegate { holdOpenUntil = Environment.TickCount + 60000; SetState(1); }); at += 1600; }
            ClaudeAfter(at, delegate { pinned = true; ShowPage(PHome); SetState(2); });
            at += 1800;
            foreach (int pg in seq) { int p = pg; ClaudeAfter(at, delegate { ShowPage(p); }); at += 1300; }
            // KAPSULA_RIDEEDIT=1 — правка «Главной»: каска, удачная и неудачная перестановка
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDEEDIT") == "1")
            {
                ClaudeAfter(at, delegate { ShowPage(PHome); EditPage(PHome, true); }); at += 1500;
                ClaudeAfter(at, delegate { if (homeBoard != null) RiderBoardEvent("ok"); }); at += 1200;
                ClaudeAfter(at, delegate { if (homeBoard != null) RiderBoardEvent("bad"); }); at += 1200;
                ClaudeAfter(at, delegate { EditPage(PHome, false); }); at += 900;
            }
            ClaudeAfter(at, delegate { pinned = false; holdOpenUntil = 0; SetState(0); });
            at += 1800;
            // KAPSULA_RIDEDROP=1 — зоны сброса: зверёк перебегает с зоны на зону (без настоящего перетаскивания)
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDEDROP") == "1")
            {
                ClaudeAfter(at, delegate { pinned = true; ShowPage(PShelf); SetState(2); }); at += 900;
                ClaudeAfter(at, delegate { DropShow(true); dropSeenAt = Environment.TickCount + 60000; }); at += 700;
                for (int z = 0; z < 3; z++) { int zz = z; ClaudeAfter(at, delegate { DropHoverSet(zz); }); at += 900; }
                ClaudeAfter(at, delegate { DropHoverSet(-1); }); at += 700;
                ClaudeAfter(at, delegate { DropHide(); }); at += 700;
                ClaudeAfter(at, delegate { pinned = false; SetState(0); }); at += 1500;
            }
            // KAPSULA_RIDEEXTRA=1 — строка команд и карточка со зверьком
            if (Environment.GetEnvironmentVariable("KAPSULA_RIDEEXTRA") == "1")
            {
                ClaudeAfter(at, delegate { cmdBox.Text = ""; cmdItems.Clear(); RenderCmd(); SetState(4); }); at += 1700;   // строка команд без фокуса: проверка не отнимает его у ваших окон
                ClaudeAfter(at, delegate { SetState(0); }); at += 1500;
                // обычное уведомление: зверёк приносит письмо; поверх него второе — отдаёт и его; потом карточка со своим зверьком
                ClaudeAfter(at, delegate { ShowCard(null, ((char)0xE8BD).ToString(), "Марк", "го вечером в кооп?", null); cardUntil = Environment.TickCount + 3000; }); at += 1800;
                ClaudeAfter(at, delegate { ShowCardEx(null, ((char)0xE8BD).ToString(), "Лена", "скинула фото с прогулки", null, "Ответить", delegate { }, false); cardUntil = Environment.TickCount + 2600; }); at += 4000;
                ClaudeAfter(at, delegate { ShowCard(null, "", "Готово", "Пример карточки", null); CardPet("happy"); cardUntil = Environment.TickCount + 2500; }); at += 3600;
            }
            ClaudeAfter(at, delegate { if (riderLog != null && riderLog.Length > 0) { try { System.IO.File.AppendAllText(TestRider, riderLog.ToString()); } catch { } riderLog.Clear(); } });
        }

        System.Text.StringBuilder riderLog;
        int riderLogAt;
        void RiderLog(int now, double x, double y, double s)
        {
            if (riderLog == null) riderLog = new System.Text.StringBuilder();
            if (unchecked(now - riderLogAt) < 15) return;
            riderLogAt = now;
            riderLog.AppendLine(now + " st=" + state + " k=" + rKind + " fly=" + (rFly ? 1 : 0) + " x=" + x.ToString("0.0") + " y=" + y.ToString("0.0") + " s=" + s.ToString("0.00") + " edge=" + sh.X.ToString("0.0"));
            if (riderLog.Length > 4000) { try { System.IO.File.AppendAllText(TestRider, riderLog.ToString()); } catch { } riderLog.Clear(); }
        }

        // Настроение попутчика — по тому, что происходит на острове
        string RiderMood()
        {
            if (timerRinging) return "alarm";
            if (claudeState == 2) return "ask";
            if (state == 3) return "read";   // на карточке: смотрит на уведомление вместе с вами
            if (chatBusy) return "think";
            if (claudeState == 1) return "work";
            if (unchecked(Environment.TickCount - happyUntil) < 0) return "happy";
            if (media.Has && media.Playing) return "dance";
            if (Native.IdleMs() > 120000) return "sleep";
            return "idle";
        }

        // Свои сценки у каждой вкладки: на полке таскает коробки, в чате «печатает», у таймера постукивает ногой
        string RiderContext()
        {
            if (state == 3) return "";   // на карточке не отвлекается на сценки
            if (state != 2) return "default";
            switch (page)
            {
                case PClip: return "clips";
                case PShelf: return "shelf";
                case PText: case PChat: return "chat";
                case PNote: return "timer";
                case PWeather: return "weather";
                case PSystem: case PDeck: return "nav";
                case PClaude: return "claude";
                case PNotif: return "notif";
            }
            return "default";
        }

        string riderOwn;   // свой наряд попутчика сейчас (наушники на карточке звонка) или null — общий

        // Опрос (30 раз в секунду): следить, куда зверьку пора, и что у него на душе
        void RiderPoll()
        {
            if (rider == null) return;
            if (!RiderAllowed())
            {
                if (rOut || rKind != 0 || rTouched.Count > 0 || claudeDot != null && claudeDot.Opacity < 1 && !walkerOut) RiderReset();
                return;
            }
            // Страница только что перерисовалась и ещё не разложена — её зверьки не на местах; решать в следующий раз
            bool settling = state == 2 && pages != null && (!pages[page].IsMeasureValid || !pages[page].IsArrangeValid);
            if (!rFly && rFadeAt == 0 && !settling) { RiderCapture(); RiderRetarget(); }
            // сел на место зверька карточки звонка — надевает наушники, как он
            string ownWant = rKind == 2 && rSeat != null && rSeat == callPet ? "phones" : null;
            if (ownWant != riderOwn) { riderOwn = ownWant; rider.OwnOutfit = ownWant; }
            if (rOut && !rFly)
            {
                rider.Hat = EditingBoard() != null;
                if (pollN % 6 == 0)
                {
                    rider.Mood(RiderMood());
                    rider.Context = RiderContext();
                    // глаза — за курсором
                    Native.POINT pt;
                    if (Native.GetCursorPos(out pt))
                    {
                        double cxp = (pt.X / dpi - Left) / Scale, cyp = (pt.Y / dpi - Top) / Scale, c = rX + 5.5 * RiderPx * rS;
                        bool overIsland = Math.Abs(cxp - BaseW / 2) <= sw.X / 2 + 10 && cyp <= sh.X + 12;
                        // на карточке читает текст справа от себя, пока курсор не придёт на карточку
                        if (rKind == 5 && !overIsland) rider.Glance(1, 900);
                        else rider.Glance(cxp < c - 40 ? -1 : cxp > c + 40 ? 1 : 0, 700);
                    }
                }
            }
            if (TestRider != null && riderLog != null && pollN % 30 == 0 && riderLog.Length > 0) { try { System.IO.File.AppendAllText(TestRider, riderLog.ToString()); } catch { } riderLog.Clear(); }
        }

        // Выключили попутчика (или он сейчас некстати — игра, стикер): всё как было
        void RiderReset()
        {
            rFly = false; rOut = false; rKind = 0; rSeat = null; rHave = false; rFadeAt = 0;
            if (rider != null) { rider.Root.BeginAnimation(UIElement.OpacityProperty, null); rider.Root.Opacity = 1; rider.Root.Visibility = Visibility.Collapsed; rider.Seated = false; rider.Hat = false; }
            if (claudeDot != null && !walkerOut) claudeDot.Opacity = 1;
            foreach (PetView p in rTouched) p.Root.Opacity = 1;
            rTouched.Clear();
        }

        // Щёлкнули по попутчику: подпрыгивает, пять щелчков подряд — крутится
        void RiderPoke()
        {
            int now = Environment.TickCount;
            rPokes = unchecked(now - rPokeAt) < 1500 ? rPokes + 1 : 1;
            rPokeAt = now;
            if (rPokes >= 5) { rPokes = 0; rider.Play("spin", 1000); } else rider.Play("hop", 700);
            Happy(1600);
        }

        // Прораб: как прошла перестановка блоков
        void RiderBoardEvent(string e)
        {
            if (rider == null || !rOut || rFly) return;
            switch (e)
            {
                case "grab": rider.Play("wow", 600); break;
                case "ok": case "restore": rider.Play("hop", 800); break;
                case "bad": rider.Play("no", 900); break;
                case "hide": rider.Play("wave", 900); break;
                case "reset": rider.Play("spin", 1000); break;
            }
        }

        // ================================================================ зоны сброса
        Grid dropHost;
        Border[] dropZones;
        int dropHover = -1, dropSeenAt;
        bool dropOn, dropImages;
        DispatcherTimer dropTimer;
        static readonly string[] DropTitles = { "На полку", "В ярлыки", "В чат" };
        static readonly string[] DropSubs = { "Файлы под рукой", "Запуск с «Главной»", "Спросить о картинке" };
        static readonly string[] DropGlyphs = { ((char)0xE7B8).ToString(), ((char)0xE71D).ToString(), ((char)0xE8BD).ToString() };
        static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

        void BuildDropZones(Grid g)
        {
            dropHost = new Grid { Visibility = Visibility.Collapsed, Background = Ink, IsHitTestVisible = false, Margin = new Thickness(0, 4, 0, 0) };
            Grid.SetRow(dropHost, 2);
            dropZones = new Border[3];
            for (int i = 0; i < 3; i++)
            {
                dropHost.ColumnDefinitions.Add(new ColumnDefinition());
                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                col.Children.Add(new TextBlock { Text = DropGlyphs[i], FontFamily = Icons, FontSize = 28, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center });
                col.Children.Add(new TextBlock { Text = DropTitles[i], FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) });
                col.Children.Add(new TextBlock { Text = DropSubs[i], FontSize = 11.5, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                var z = new Border { CornerRadius = new CornerRadius(22), Background = Glass.Fill, BorderBrush = Glass.Rim, BorderThickness = new Thickness(1), Margin = new Thickness(i == 0 ? 0 : 5, 14, i == 2 ? 0 : 5, 6), Child = col, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
                Grid.SetColumn(z, i);
                dropZones[i] = z;
                dropHost.Children.Add(z);
            }
            g.Children.Add(dropHost);
        }

        static bool IsImage(string f) { string e = (System.IO.Path.GetExtension(f) ?? "").ToLowerInvariant(); return Array.IndexOf(ImageExt, e) >= 0; }

        // Файл тащат над островом: зоны видны, та, что под курсором, подсвечена, зверёк встаёт на неё
        void DropOver(DragEventArgs e)
        {
            if (dropHost == null) return;
            if (!dropOn)
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                bool images = false;
                if (files != null) foreach (string f in files) if (IsImage(f)) { images = true; break; }
                DropShow(images);
            }
            dropSeenAt = Environment.TickCount;
            int hit = -1;
            for (int i = 0; i < 3; i++)
            {
                Point p = e.GetPosition(dropZones[i]);
                if (p.X >= 0 && p.Y >= -14 && p.X <= dropZones[i].ActualWidth && p.Y <= dropZones[i].ActualHeight + 6) hit = i;
            }
            e.Effects = DragDropEffects.Copy;
            DropHoverSet(hit);
        }

        void DropShow(bool images)
        {
            dropOn = true;
            dropImages = images;
            dropZones[2].Opacity = images ? 1 : 0.38;
            ((TextBlock)((StackPanel)dropZones[2].Child).Children[2]).Text = images ? DropSubs[2] : "Только для картинок";
            dropHost.Visibility = Visibility.Visible;
            dropHost.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ReducedMotion ? 80 : 160)));
            if (dropTimer == null)
            {
                dropTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                dropTimer.Tick += delegate { if (unchecked(Environment.TickCount - dropSeenAt) > 320) DropHide(); };   // файл унесли — зоны прячутся
            }
            dropTimer.Start();
        }

        void DropHoverSet(int hit)
        {
            if (hit == 2 && !dropImages) hit = -1;
            if (hit == dropHover) return;
            dropHover = hit;
            for (int i = 0; i < 3; i++)
            {
                bool on = i == hit;
                dropZones[i].BorderBrush = on ? (Brush)accent : Line;
                dropZones[i].Background = on ? Glass.Strong : Glass.Fill;
                var st = (ScaleTransform)dropZones[i].RenderTransform;
                var a = new DoubleAnimation(on ? 1.035 : 1, TimeSpan.FromMilliseconds(ReducedMotion ? 1 : 160)) { EasingFunction = SlideEase };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            RiderRetarget();
        }

        // Верхняя кромка зоны под курсором — в координатах острова (без масштаба раскрытия)
        bool DropSeat(out Point pt)
        {
            pt = new Point();
            if (!dropOn || dropHover < 0 || dropZones == null) return false;
            Border z = dropZones[dropHover];
            if (z.ActualWidth <= 0) return false;
            try { Point p = z.TranslatePoint(new Point(z.ActualWidth / 2, 0), expandedHost); pt = new Point(BaseW / 2 - EW / 2 + p.X, p.Y); return true; }
            catch { return false; }
        }

        void DropHide()
        {
            if (dropTimer != null) dropTimer.Stop();
            if (!dropOn) return;
            dropOn = false; dropHover = -1;
            foreach (Border z in dropZones)
            {
                z.BorderBrush = Glass.Rim; z.Background = Glass.Fill;
                var st = (ScaleTransform)z.RenderTransform;
                st.BeginAnimation(ScaleTransform.ScaleXProperty, null); st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                st.ScaleX = st.ScaleY = 1;
            }
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(ReducedMotion ? 60 : 150));
            fade.Completed += delegate { if (!dropOn) dropHost.Visibility = Visibility.Collapsed; };
            dropHost.BeginAnimation(UIElement.OpacityProperty, fade);
            RiderRetarget();
        }

        // Отпустили файл: куда — решает зона под курсором; мимо зон — на полку, как раньше
        void DropDone(string[] files)
        {
            int zone = dropHover;
            DropHide();
            if (zone == 1)
            {
                AddToLauncher(files);
                if (homeBoard != null) homeBoard.ShowItem("launcher");
                Store.Set("homeLauncher", "1");
                Toast(((char)0xE71D).ToString(), files.Length == 1 ? "В ярлыках: " + System.IO.Path.GetFileName(files[0]) : "В ярлыках: " + files.Length, null, -1, accent);
            }
            else if (zone == 2)
            {
                string img = null;
                foreach (string f in files) if (IsImage(f)) { img = f; break; }
                if (img != null && AttachImageFile(img))
                {
                    ShowPage(PChat);
                    Toast(((char)0xE8BD).ToString(), Ai.Duck ? "Картинка в буфере — вставьте в чат: Ctrl+V" : "Картинка прикреплена — спросите о ней", null, -1, accent);
                }
                else Toast("", "Эту картинку открыть не получилось", null, -1, null);
            }
            else
            {
                int added = 0;
                foreach (string f in files) if (!shelf.Contains(f)) { shelf.Add(f); added++; }
                SaveShelf();
                RenderShelf();
                if (added > 0) Toast(((char)0xE8F1).ToString(), added == 1 ? "На полке: " + System.IO.Path.GetFileName(files[0]) : "На полке файлов: " + added, null, -1, accent);
            }
            Happy(2200);
            if (rider != null && rOut) rider.Play("hop", 800);
        }

        // Картинка из файла — во вложение чата (для сайтов без ключа — в буфер, чтобы вставить на сайте)
        bool AttachImageFile(string path)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(path); bi.EndInit(); bi.Freeze();
                if (Ai.Duck) { Clipboard.SetImage(bi); return true; }
                BitmapSource src = new FormatConvertedBitmap(bi, PixelFormats.Bgr24, null, 0);
                double k = Math.Min(1, 1568.0 / Math.Max(src.PixelWidth, src.PixelHeight));
                if (k < 1) src = new TransformedBitmap(src, new ScaleTransform(k, k));
                using (var ms = new System.IO.MemoryStream())
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(src));
                    enc.Save(ms);
                    SetAttachment(Convert.ToBase64String(ms.ToArray()));
                }
                return true;
            }
            catch (Exception ex) { Store.LogError("картинка в чат", ex); return false; }
        }

        // ================================================================ экономный опрос
        // Свёрнутый остров без музыки, когда курсор далеко, проверяет его 10 раз в секунду, а не 30: приближение
        // курсора всё равно замечается вовремя, а компьютер лишний раз не просыпается.
        bool calm;
        void CalmCheck()
        {
            if (pollTimer == null) return;
            bool want = false;
            if (state == 0 && !toastOn && !(media.Has && media.Playing) && !GamerMode && !timerRinging && claudeState == 0 && dlActive == null && steamDlApp == null
                && !Recorder.Active && !rOut && !walkerOut && ForceOpen == -1 && unchecked(Environment.TickCount - happyUntil) >= 0)
            {
                Native.POINT pt;
                if (Native.GetCursorPos(out pt))
                {
                    double x = (pt.X / dpi - Left) / Scale, y = (pt.Y / dpi - Top) / Scale;
                    want = Math.Abs(x - BaseW / 2) > sw.X / 2 + 180 || y > sh.X + 160;
                }
            }
            if (want == calm) return;
            calm = want;
            pollTimer.Interval = TimeSpan.FromMilliseconds(want ? 100 : 33);
        }
    }
}
