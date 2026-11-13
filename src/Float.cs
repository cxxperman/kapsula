// Kapsula — «остров» для Windows. Часть исходного кода: Float.cs
// Отцепляемый остров. Потяните остров за пустое место — он отцепится от верхнего края и поедет за мышью:
// «уши», которыми он держится за край экрана, тают, верхние углы скругляются. Отпустите — останется там,
// а у краёв экрана прилипнет. Поднесите к верхнему краю — уши вырастут снова, и остров встанет на место.
// Отцепленный остров раскрывается как обычно, а если панели не хватает места внизу или сбоку — сам
// приподнимается и сдвигается, пока она открыта. Прятаться за край (автоскрытие, игры) ему некуда — он тает.
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using WF = System.Windows.Forms;

namespace Kapsula
{
    partial class IslandWindow
    {
        public bool AllowDetach = true;
        public bool Detached { get { return detached; } }
        bool detached;
        double detFx = 0.5, detFy = 0.1;                    // где стоит отцепленный остров: центр по ширине и верх — доли рабочей области экрана
        readonly Spring se = new Spring(1, 320, 36);        // 1 — держится ушами за верхний край, 0 — отцеплен
        readonly Spring sLift = new Spring(0, 240, 31);     // насколько приподнять, чтобы раскрытая панель влезла по высоте
        readonly Spring sNudge = new Spring(0, 240, 31);    // насколько сдвинуть вбок — по ширине
        readonly Spring sFade = new Spring(1, 300, 35);     // отцепленный остров прячется, растворяясь
        bool dragArm, dragging;
        Point dragStart; double dragL, dragT;
        bool glideOn; double glFromL, glFromT, glToL, glToT; int glAt;
        const double SnapTop = 26, SnapEdge = 24, EdgeGap = 8;

        void LoadDetach()
        {
            AllowDetach = Store.Get("detach") != "0";
            string[] p = Store.Get("islandPos").Split(';');
            double fx, fy;
            if (AllowDetach && p.Length == 2 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out fx) && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out fy))
            {
                detached = true; detFx = fx; detFy = fy;
                se.X = se.Target = 0;
            }
        }

        WF.Screen IslandScreen()
        {
            var all = WF.Screen.AllScreens;
            return MonitorIndex >= 0 && MonitorIndex < all.Length ? all[MonitorIndex] : WF.Screen.PrimaryScreen;
        }

        Rect WorkDip() { var w = IslandScreen().WorkingArea; return new Rect(w.X / dpi, w.Y / dpi, w.Width / dpi, w.Height / dpi); }
        Rect ScreenDip() { var b = IslandScreen().Bounds; return new Rect(b.X / dpi, b.Y / dpi, b.Width / dpi, b.Height / dpi); }

        // Окно отцепленного острова там, где его оставили, с поправкой на раскрытую панель
        void PlaceDetached(bool glide)
        {
            Rect wa = WorkDip();
            double l = wa.Left + detFx * wa.Width - Width / 2 + sNudge.X;
            double t = wa.Top + detFy * wa.Height - sLift.X;
            if (glide) GlideTo(l, t); else { Left = l; Top = t; }
        }

        // Плавно перевести окно (когда остров встаёт на место или прилипает к краю)
        void GlideTo(double l, double t)
        {
            if (ReducedMotion) { Left = l; Top = t; return; }
            glFromL = Left; glFromT = Top; glToL = l; glToT = t; glAt = Environment.TickCount; glideOn = true;
            Wake();
        }

        // Каждый кадр: шаг пружин, перелёт окна, подъём под раскрытую панель
        bool FloatFrame(double dt)
        {
            bool moving = se.Step(dt);
            moving |= sFade.Step(dt);
            bool shift = sLift.Step(dt);
            shift |= sNudge.Step(dt);
            if (glideOn)
            {
                double p = Clamp01(unchecked(Environment.TickCount - glAt) / 260.0);
                double e = 1 - Math.Pow(1 - p, 3);
                Left = glFromL + (glToL - glFromL) * e;
                Top = glFromT + (glToT - glFromT) * e;
                if (p >= 1) glideOn = false;
                return true;
            }
            if (shift && detached && !dragging) PlaceDetached(false);
            return moving || shift;
        }

        // Сколько приподнять и сдвинуть отцепленный остров, чтобы текущий вид целиком влез на экран
        void FloatTargets()
        {
            if (!detached || dragging) { sLift.Target = 0; sNudge.Target = 0; return; }
            Rect wa = WorkDip();
            double w = StateW() * Scale, h = StateH() * Scale;
            double top = wa.Top + detFy * wa.Height, cx = wa.Left + detFx * wa.Width;
            double lift = Math.Max(0, top + h + EdgeGap - wa.Bottom);
            lift = Math.Min(lift, Math.Max(0, top - wa.Top - EdgeGap));
            double nudge = 0;
            if (cx - w / 2 < wa.Left + EdgeGap) nudge = wa.Left + EdgeGap - (cx - w / 2);
            else if (cx + w / 2 > wa.Right - EdgeGap) nudge = wa.Right - EdgeGap - (cx + w / 2);
            sLift.Target = lift;   // в точках экрана (DIP)
            sNudge.Target = nudge;
        }

        // ---- перетаскивание: за пустое место острова (кнопки, ползунки, поля ввода свои щелчки забирают сами)
        void BuildDrag()
        {
            root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.Handled || !AllowDetach || Mouse.Captured != null || state == 4 || EditingBoard() != null || ForceOpen != -1) return;
                Native.POINT pt;
                if (!Native.GetCursorPos(out pt)) return;
                dragArm = true; dragging = false;
                dragStart = new Point(pt.X / dpi, pt.Y / dpi);
                dragL = Left; dragT = Top;
            };
            root.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!dragArm) return;
                if (e.LeftButton != MouseButtonState.Pressed) { dragArm = false; return; }
                Native.POINT pt;
                if (!Native.GetCursorPos(out pt)) return;
                double dx = pt.X / dpi - dragStart.X, dy = pt.Y / dpi - dragStart.Y;
                if (!dragging)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) < 9) return;
                    dragging = true;
                    root.CaptureMouse();
                    glideOn = false;
                    sLift.X = sLift.Target = 0; sNudge.X = sNudge.Target = 0;
                    dragL = Left; dragT = Top;   // от того места, где окно сейчас
                    if (!detached) { detached = true; if (Store.Get("detachHint") != "1") { Store.Set("detachHint", "1"); Toast(((char)0xE7C2).ToString(), "Остров отцеплен — к верхнему краю, чтобы вернуть", null, -1, accent); } }
                    holdOpenUntil = Environment.TickCount + 60000;
                }
                Rect sc = ScreenDip();
                double l = dragL + dx, t = Math.Max(sc.Top, Math.Min(sc.Bottom - pillH * Scale, dragT + dy));
                Left = l; Top = t;
                se.Target = t - sc.Top < SnapTop ? 1 : 0;   // у верхнего края уши вырастают — отпустите, и он встанет на место
                Wake();
            };
            root.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                bool was = dragging;
                dragArm = false;
                if (!was) return;
                dragging = false;
                root.ReleaseMouseCapture();
                holdOpenUntil = Environment.TickCount + 1200;
                e.Handled = true;
                Rect sc = ScreenDip();
                if (Top - sc.Top < SnapTop) { Attach(true); return; }
                // остаётся тут; у краёв рабочей области — прилипает
                Rect wa = WorkDip();
                double pillW = CompactWidth() * Scale, cx = Left + Width / 2, top = Top;
                if (cx - pillW / 2 < wa.Left + SnapEdge) cx = wa.Left + EdgeGap + pillW / 2;
                if (cx + pillW / 2 > wa.Right - SnapEdge) cx = wa.Right - EdgeGap - pillW / 2;
                if (top + pillH * Scale > wa.Bottom - SnapEdge) top = wa.Bottom - EdgeGap - pillH * Scale;
                if (top < wa.Top + EdgeGap) top = wa.Top + EdgeGap;
                detFx = (cx - wa.Left) / wa.Width; detFy = (top - wa.Top) / wa.Height;
                Store.Set("islandPos", detFx.ToString("0.####", CultureInfo.InvariantCulture) + ";" + detFy.ToString("0.####", CultureInfo.InvariantCulture));
                se.Target = 0;
                FloatTargets();
                PlaceDetached(true);
                if (rider != null && rOut && !rFly) rider.Play("hop", 700);
            };
            root.LostMouseCapture += delegate { if (dragging) { dragging = false; dragArm = false; } };
        }

        // Вернуть остров к верхнему краю
        public void Attach(bool glide)
        {
            bool was = detached;
            detached = false;
            dragging = false;
            se.Target = 1;
            sLift.X = sLift.Target = 0; sNudge.X = sNudge.Target = 0;
            Store.Set("islandPos", "");
            WF.Screen scr = IslandScreen();
            double l = scr.Bounds.X / dpi + (scr.Bounds.Width / dpi - Width) / 2, t = scr.Bounds.Y / dpi + TestDY;
            if (glide && was) GlideTo(l, t); else { Left = l; Top = t; }
            SetTargets();
        }
    }
}
