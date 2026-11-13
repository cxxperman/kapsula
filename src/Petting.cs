// Kapsula — «остров» для Windows. Часть исходного кода: Petting.cs
// Зверька можно погладить: поводить по нему курсором туда-сюда — в уголке зверька, на краю панели, мини-плеера
// или карточки и на краю окна, где он гуляет. Он зажмуривается от удовольствия, ластится к руке и мурлычет, а над
// ним всплывают сердечки. Потом ещё какое-то время довольный: в капсуле рядом с ним мигает сердечко.
// Щелчки при этом никуда не уходят и ничего не нажимают — остров только смотрит, где курсор.
using System;
using System.Windows;
using System.Windows.Media;

namespace Kapsula
{
    // Поглаживание — курсор ходит над зверьком туда-сюда: несколько разворотов за полторы секунды,
    // и каждый мазок перед разворотом достаточно длинный (дрожь руки и прямой проход мимо не в счёт)
    class Stroke
    {
        double lastX = double.NaN, lastY, startX, startY;
        int dirX, dirY, lastAt, count;
        readonly int[] turns = new int[6];

        // x, y — курсор; minLen — сколько должен пройти мазок до разворота; need — сколько разворотов нужно
        public bool Move(double x, double y, double minLen, int need, int now)
        {
            if (double.IsNaN(lastX) || unchecked(now - lastAt) > 600) { Reset(x, y, now); return false; }
            lastAt = now;
            bool turned = Axis(x, ref lastX, ref startX, ref dirX, minLen);
            turned |= Axis(y, ref lastY, ref startY, ref dirY, minLen);
            if (turned) { turns[count % turns.Length] = now; count++; }
            int recent = 0;
            for (int i = 0; i < Math.Min(count, turns.Length); i++) if (unchecked(now - turns[i]) < 1500) recent++;
            return recent >= need;
        }

        static bool Axis(double v, ref double last, ref double start, ref int dir, double minLen)
        {
            double d = v - last;
            last = v;
            if (Math.Abs(d) < 0.5) return false;
            int s = d > 0 ? 1 : -1;
            if (dir == 0) { dir = s; start = v - d; return false; }
            if (s == dir) return false;
            bool real = Math.Abs(v - d - start) >= minLen;   // развернулись после настоящего мазка
            dir = s; start = v - d;
            return real;
        }

        void Reset(double x, double y, int now) { lastX = x; lastY = y; startX = x; startY = y; dirX = dirY = 0; lastAt = now; count = 0; }

        // Курсор ушёл со зверька
        public void Leave() { lastX = double.NaN; }
    }

    partial class IslandWindow
    {
        readonly Stroke stroke = new Stroke();
        PetView strokePet;                 // кого гладят сейчас
        int petLoveUntil, petStrokeLast;   // до какого момента зверёк в капсуле довольный; когда гладили в последний раз
        int testStrokeFrom, testStrokeUntil;   // проверка без мыши: «курсор» сам ходит над зверьком туда-сюда
        static readonly Brush LovePink = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x8A));

        // Раз в опрос: курсор над зверьком, который сейчас на виду, и ходит туда-сюда — его гладят
        void StrokeTick()
        {
            Rect r;
            bool walking;
            PetView pv = StrokeTarget(out r, out walking);
            if (pv == null) { stroke.Leave(); strokePet = null; return; }
            int now = Environment.TickCount;
            double x, y;
            if (Until(testStrokeUntil) && unchecked(now - testStrokeFrom) >= 0)
            {
                double ph = unchecked(now - testStrokeFrom) / 1000.0;
                x = r.X + r.Width / 2 + Math.Sin(ph * Math.PI * 2 * 2.2) * r.Width * 0.42;
                y = r.Y + r.Height * 0.35;
            }
            else
            {
                Native.POINT pt;
                if (!Native.GetCursorPos(out pt)) { stroke.Leave(); return; }
                x = pt.X / dpi; y = pt.Y / dpi;
            }
            double size = r.Width;
            // по краю окна, где зверёк гуляет, люди и так водят мышью (вкладки браузера): там нужно точнее и дольше
            double pad = walking ? 2 : Math.Max(6, size * 0.25);
            r.Inflate(pad, pad);
            if (!r.Contains(x, y)) { stroke.Leave(); return; }
            if (pv != strokePet) { strokePet = pv; stroke.Leave(); }
            if (!stroke.Move(x, y, Math.Max(4, size * 0.18), walking ? 3 : 2, now)) return;
            Petted(pv, now);
        }

        // Кого можно погладить сейчас: зверька на краю окна, попутчика (край панели, мини-плеера, карточки, его место
        // на вкладке) или зверька в уголке. Прямоугольник — его тело на экране, в точках WPF.
        PetView StrokeTarget(out Rect r, out bool walking)
        {
            r = Rect.Empty; walking = false;
            if (!PetProps()) return null;
            if (walkerOut)
            {
                if (walker == null || walkPhase != 2 || !walker.IsVisible) return null;
                walking = true;
                return FigureRect(walker.Pet, out r) ? walker.Pet : null;
            }
            if (state == 0) return null;   // в капсуле наведение раскрывает остров — там не гладят
            if (rider != null && rOut && !rFly && rider.Root.Visibility == Visibility.Visible && rider.Root.Opacity > 0.5 && FigureRect(rider, out r)) return rider;
            if (state == 2 && page == PPet && roomPet != null && roomPet.Root.IsVisible && roomPet.Root.Opacity > 0.5 && FigureRect(roomPet, out r)) return roomPet;
            return null;
        }

        bool FigureRect(PetView pv, out Rect r)
        {
            r = Rect.Empty;
            FrameworkElement f = pv.Figure;
            if (f.ActualWidth <= 0 || PresentationSource.FromVisual(f) == null) return false;
            try
            {
                Point a = f.PointToScreen(new Point(0, 0)), b = f.PointToScreen(new Point(f.ActualWidth, f.ActualHeight));
                r = new Rect(new Point(a.X / dpi, a.Y / dpi), new Point(b.X / dpi, b.Y / dpi));
                return r.Width > 0;
            }
            catch { return false; }
        }

        void Petted(PetView pv, int now)
        {
            if (unchecked(now - petStrokeLast) > 4000)
            {
                // новое поглаживание — в копилку: в уголке зверька видно, сколько раз его гладили
                int n;
                int.TryParse(Store.Get("petPets"), out n);
                Store.Set("petPets", (n + 1).ToString());
                if (state == 2 && page == PPet) RenderPetPage();
            }
            petStrokeLast = now;
            pv.Extend("purr", 1100);
            petLoveUntil = now + 25000;   // ещё какое-то время довольный — и в капсуле с сердечком
            if (petLoveUntil == 0) petLoveUntil = 1;
        }

        // ---- ловит курсор: курсор замер рядом со зверьком на краю окна или в уголке — тот припадает к земле,
        // виляет хвостом и прыгает на него (на окне — настоящим прыжком по краю), потом «поймал!». Раз за одну
        // остановку курсора и не чаще раза в двадцать секунд; гладят — не до охоты.
        double huntX = double.NaN, huntY;
        int huntStillAt, huntAt, huntBusyUntil, testHuntFrom, testHuntUntil;
        bool huntArmed = true, walkGotcha;

        void HuntTick()
        {
            Rect r;
            bool walking;
            PetView pv = HuntTarget(out r, out walking);
            if (pv == null) { huntX = double.NaN; return; }
            int now = Environment.TickCount;
            if (Until(huntBusyUntil) || strokePet == pv && unchecked(now - petStrokeLast) < 2500) return;
            double x, y;
            if (Until(testHuntUntil) && unchecked(now - testHuntFrom) >= 0) { x = r.Right + (walking ? 80 : 45); y = r.Top + r.Height * 0.4; }
            else
            {
                Native.POINT pt;
                if (!Native.GetCursorPos(out pt)) return;
                x = pt.X / dpi; y = pt.Y / dpi;
            }
            if (double.IsNaN(huntX) || Math.Abs(x - huntX) > 3 || Math.Abs(y - huntY) > 3)
            {
                // курсор движется — ждём, пока замрёт; ушёл подальше — снова можно охотиться
                if (!double.IsNaN(huntX) && (Math.Abs(x - huntX) > 40 || Math.Abs(y - huntY) > 40)) huntArmed = true;
                huntX = x; huntY = y; huntStillAt = now;
                return;
            }
            if (!huntArmed || unchecked(now - huntStillAt) < 700 || huntAt != 0 && unchecked(now - huntAt) < 20000) return;
            double dx = x - (r.X + r.Width / 2), dy = y - (r.Y + r.Height / 2);
            if (Math.Abs(dx) > (walking ? 170 : 95) || Math.Abs(dy) > (walking ? 70 : 60) || Math.Abs(dx) < r.Width * 0.6) return;   // далеко или вплотную
            huntArmed = false; huntAt = now; huntBusyUntil = now + 2300;
            int dir = dx > 0 ? 1 : -1;
            pv.Glance(dir, 2600);
            pv.Play("hunt", 800);
            if (walking)
            {
                double target = x - dir * 4;   // приземлится лапами у самого курсора
                ClaudeAfter(800, delegate
                {
                    IntPtr w; Native.RECT wr;
                    if (!walkerOut || walker == null || walkPhase != 2 || walkMoving || !WalkTarget(out w, out wr) || w != walkWin) return;
                    walkGotcha = true;
                    WalkFly(EdgeX(wr, target), wr.T / dpi, 1, 2);
                });
            }
            else
            {
                ClaudeAfter(800, delegate { pv.Play("pounce", 550); });
                ClaudeAfter(1350, delegate { pv.Play("gotcha", 800); });
            }
        }

        // На кого охотиться: зверёк на краю окна или в уголке зверька (на краю панели он не прыгает — там работают)
        PetView HuntTarget(out Rect r, out bool walking)
        {
            r = Rect.Empty; walking = false;
            if (!PetProps() || ReducedMotion) return null;
            if (walkerOut)
            {
                if (walker == null || walkPhase != 2 || walkMoving || !walker.IsVisible) return null;
                walking = true;
                return FigureRect(walker.Pet, out r) ? walker.Pet : null;
            }
            if (state != 2 || page != PPet) return null;
            if (rider != null && rOut && !rFly && rKind == 2 && rSeat == roomPet && FigureRect(rider, out r)) return rider;
            if (roomPet != null && roomPet.Root.IsVisible && roomPet.Root.Opacity > 0.5 && FigureRect(roomPet, out r)) return roomPet;
            return null;
        }

        // Курсор рядом со зверьком на краю окна — опрашивать мышь чаще, чтобы заметить поглаживание
        bool NearWalker(Native.POINT pt)
        {
            if (!walkerOut || walker == null || walkPhase != 2 || !walker.IsVisible) return false;
            double x = pt.X / dpi - (walker.Left + WalkerWindow.FootX), y = pt.Y / dpi - (walker.Top + WalkerWindow.FootY);
            return Math.Abs(x) < 120 && Math.Abs(y) < 90;
        }
    }
}
