// Kapsula — «остров» для Windows. Часть исходного кода: Walker.cs
// Зверёк гуляет по окнам. Когда остров свёрнут и ничего не происходит, зверёк время от времени выпрыгивает
// из капсулы и садится на верхний край окна, в котором вы работаете: сидит, свесив лапки, прохаживается,
// смотрит на курсор, под музыку пританцовывает, засыпает, если вы отошли. Окно двигают — едет вместе с ним; переключились на другое —
// перепрыгивает следом. Что-то случилось на острове — возвращается в капсулу. Сквозь него можно щёлкать:
// он ничего не загораживает. На развёрнутых окнах, в играх и в программах на весь экран не гуляет.
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Kapsula
{
    // Окошко со зверьком: поверх всех окон, сквозь него проходят щелчки, фокус не берёт, в панели задач его нет
    class WalkerWindow : Window
    {
        public const double W = 76, H = 64, FootX = 38, FootY = 54, Px = 2.0;
        public readonly PetView Pet;
        public readonly ScaleTransform Size = new ScaleTransform(1, 1);
        public IntPtr Handle;
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr v);

        public WalkerWindow(Brush eyes)
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Focusable = false;
            Width = W; Height = H; Title = "Kapsula — зверёк";
            Pet = new PetView(Px, eyes);
            Size.CenterX = 5.5 * Px; Size.CenterY = 8 * Px;   // растёт и сжимается от лап
            Pet.Root.RenderTransform = Size;
            var c = new Canvas { Width = W, Height = H, IsHitTestVisible = false };
            Canvas.SetLeft(Pet.Root, FootX - 5.5 * Px); Canvas.SetTop(Pet.Root, FootY - 8 * Px);
            c.Children.Add(Pet.Root);
            Content = c;
            SourceInitialized += delegate
            {
                Handle = new WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongPtr(Handle, Native.GWL_EXSTYLE).ToInt64();
                SetWindowLongPtr(Handle, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | 0x20 /* WS_EX_TRANSPARENT */));
            };
        }

        // Поставить зверька на точку экрана (в точках WPF): сидя — лапки свисают за край, стоя — лапы на краю
        public void Feet(double x, double y) { Left = x - FootX; Top = y - FootY - (Pet.Seated ? 0 : Px * Size.ScaleY); }
    }

    partial class IslandWindow
    {
        public bool WalkOn = true;
        WalkerWindow walker;
        bool walkerOut;                  // зверёк не в капсуле, а на окне или в пути
        int walkPhase;                   // 1 — летит к окну, 2 — на окне, 3 — летит домой
        IntPtr walkWin;                  // окно, на краю которого он сидит
        Native.RECT walkRect;            // где это окно было в прошлый раз (чтобы ехать вместе с ним)
        double wX, wY, wS = 1;           // лапы зверька на экране и масштаб
        double wfX, wfY, wfS, wArc, wGoX;
        int wAt, wMs, walkNextAt, walkUntil, walkStepAt, walkLandAt;
        bool walkMoving;
        DispatcherTimer walkTimer;
        Native.RECT? TestWalkRect;       // KAPSULA_WALKRECT «x,y,ш,в» — для проверок: вместо окна
        static readonly IntPtr TestWalkHwnd = new IntPtr(-7);

        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Native.RECT r, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);

        void WalkInit()
        {
            WalkOn = Store.Get("walk") != "0";
            walkNextAt = Environment.TickCount + 180000 + rnd.Next(120000);   // первая прогулка — через 3–5 минут
            string tr = Environment.GetEnvironmentVariable("KAPSULA_WALKRECT");
            if (!string.IsNullOrEmpty(tr))
            {
                string[] p = tr.Split(',');
                int x, y, w, h;
                if (p.Length == 4 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y) && int.TryParse(p[2], out w) && int.TryParse(p[3], out h))
                    TestWalkRect = new Native.RECT { L = x, T = y, R = x + w, B = y + h };
            }
        }

        // Окно, на краю которого можно посидеть: обычное, не развёрнутое, не на весь экран, достаточно широкое,
        // и над ним есть место для зверька. Прямоугольник — видимая рамка окна (без невидимых краёв и тени).
        bool WalkTarget(out IntPtr win, out Native.RECT r)
        {
            win = IntPtr.Zero; r = new Native.RECT();
            if (TestWalkRect.HasValue) { win = TestWalkHwnd; r = TestWalkRect.Value; return true; }
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == hwnd || walker != null && fg == walker.Handle) return false;
            if (!IsWindowVisible(fg) || IsIconic(fg) || IsZoomed(fg)) return false;
            var sb = new StringBuilder(256);
            Native.GetClassName(fg, sb, 256);
            string c = sb.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" || c == "Windows.UI.Core.CoreWindow" || c == "XamlExplorerHostIslandWindow" || c == "TopLevelWindowForOverflowXamlIsland") return false;
            if ((Native.GetWindowLongPtr(fg, Native.GWL_EXSTYLE).ToInt64() & Native.WS_EX_TOOLWINDOW) != 0) return false;
            int cloaked;
            if (DwmGetWindowAttribute(fg, 14, out cloaked, 4) == 0 && cloaked != 0) return false;   // скрытое окно (другой рабочий стол)
            if (DwmGetWindowAttribute(fg, 9, out r, Marshal.SizeOf(typeof(Native.RECT))) != 0 && !Native.GetWindowRect(fg, out r)) return false;
            if (FullscreenAppInFront()) return false;
            IntPtr mon = Native.MonitorFromWindow(fg, 2);
            if (hwnd != IntPtr.Zero && mon != Native.MonitorFromWindow(hwnd, 2)) return false;   // окно на другом мониторе — туда не прыгает
            var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            if (!Native.GetMonitorInfo(mon, ref mi)) return false;
            if ((r.R - r.L) / dpi < 380 || (r.B - r.T) / dpi < 180) return false;
            if ((r.T - mi.rcWork.T) / dpi < 44) return false;   // окно прижато к верху — сидеть негде
            win = fg;
            return true;
        }

        // Лапы зверька в капсуле — на экране
        bool PillFeet(out double x, out double y, out double s)
        {
            x = y = 0; s = PetPx * Scale / WalkerWindow.Px;
            if (claudeDot == null || claudeDot.Visibility != Visibility.Visible || claudeDot.Children.Count == 0) return false;
            try
            {
                Point p = ((FrameworkElement)claudeDot.Children[0]).PointToScreen(new Point(5.5 * PetPx, 8 * PetPx));
                x = p.X / dpi; y = p.Y / dpi;
                return true;
            }
            catch { return false; }
        }

        // Точка на краю окна: подальше от значка слева и от кнопок «свернуть — закрыть» справа
        double EdgeX(Native.RECT r, double prefer)
        {
            double l = r.L / dpi + 110, rr = r.R / dpi - 180;
            if (rr < l) return (r.L + r.R) / 2.0 / dpi;
            return double.IsNaN(prefer) ? l + rnd.NextDouble() * (rr - l) : Math.Max(l, Math.Min(rr, prefer));
        }

        // Гулять вообще можно: в капсуле живёт зверёк (не стикер и не другой персонаж), не игра и не полный экран.
        // От «Зверька в переходах» не зависит: у прогулок свой переключатель.
        bool WalkAllowed() { return WalkOn && PetProps() && stickerFrames == null && !gameLite && !hiddenFs && !GamerMode && TestCharacter == null; }

        // Зверёк сейчас дома: сидит в капсуле, а не перебирается по острову
        bool PetAtHome()
        {
            if (claudeDot == null || claudeDot.Visibility != Visibility.Visible || claudeDot.Children.Count == 0) return false;
            return RiderAllowed() ? rKind == 1 && !rOut : claudeDot.Opacity > 0.5;
        }

        // Вернулся (или растаял на окне) — показать его в капсуле
        void PetBackHome()
        {
            if (claudeDot != null && (!RiderAllowed() || rKind == 0 || rKind == 1 && !rOut)) claudeDot.Opacity = 1;
        }

        // Лапы попутчика (зверька острова) на экране и его размер в масштабе окошка зверька
        bool RiderFeet(out double x, out double y, out double s)
        {
            x = y = 0; s = 1;
            if (rider == null || !rOut || rider.Root.Visibility != Visibility.Visible) return false;
            try
            {
                Vector bo = VisualTreeHelper.GetOffset(rider.Figure);
                Point p = rider.Root.PointToScreen(new Point(bo.X + 5.5 * RiderPx, bo.Y + 8 * RiderPx));
                x = p.X / dpi; y = p.Y / dpi; s = RiderPx * rS * Scale / WalkerWindow.Px;
                return true;
            }
            catch { return false; }
        }

        // Остров раскрылся, пока зверёк гулял. Если попутчику сейчас на край панели, мини-плеера или карточки,
        // зверёк прыгает с окна прямо туда и там становится попутчиком (тот летит туда же невидимкой).
        // Иначе — тает на месте, а в капсуле появляется сразу.
        void WalkToIsland()
        {
            if (!walkerOut || walker == null) return;
            double tx, ty, ts;
            if (ReducedMotion || !RiderAllowed() || rKind != 3 && rKind != 5) { WalkAbort(); return; }
            // попутчик сразу садится на край (невидимкой): зверёк прыгает прямо туда, а не ловит его на лету из капсулы
            if (rFly && RiderSpot(out tx, out ty, out ts)) ShowRiderAt(tx, ty, ts, false);
            if (!RiderFeet(out tx, out ty, out ts)) { WalkAbort(); return; }
            WalkLog("to island: kind=" + rKind);
            walkPhase = 4; walkMoving = false;
            wfX = wX; wfY = wY; wfS = wS;
            double dx = tx - wX, dy = ty - wY, d = Math.Sqrt(dx * dx + dy * dy);
            wMs = (int)Math.Max(300, Math.Min(640, 240 + d * 0.5));
            wArc = dy < -40 ? 4 : Math.Max(14, Math.Min(60, 14 + d * 0.08));   // вверх — без дуги: иначе перелетит край и окажется на панели
            wAt = Environment.TickCount;
            walker.BeginAnimation(UIElement.OpacityProperty, null);
            walker.Opacity = 1;
            walker.Pet.Seated = false;
            walker.Pet.Mood(rKind == 5 ? "post" : "fly");   // к карточке — с письмом в лапах
            walker.Pet.Context = "";
            rider.Root.Opacity = 0;
            WalkerTop();
            StartWalkTimer();
        }

        // Окошко зверька — над островом: остров время от времени заново встаёт поверх всех окон, а зверёк
        // залетает в капсулу и на край панели, и прятаться за ними не должен. Фокус не берёт.
        void WalkerTop()
        {
            if (walkerOut && walker != null && walker.Handle != IntPtr.Zero)
                Native.SetWindowPos(walker.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        // Долетел: дальше это попутчик острова (он ровно там же), а окошко зверька прячется
        void WalkJoin()
        {
            walkTimer.Stop();
            walkerOut = false; walkPhase = 0; walkMoving = false;
            walker.Hide();
            if (rider != null) { rider.Root.Opacity = 1; if (!rFly) rLandAt = Environment.TickCount; Wake(); }   // приземлился — пружинит
            walkNextAt = Environment.TickCount + 240000 + rnd.Next(240000);
            WalkLog("joined");
        }

        bool WalkCanLeave()
        {
            return WalkAllowed() && state == 0 && !toastOn && PetAtHome()
                && !timerRinging && claudeState == 0 && !Recorder.Active && dlActive == null && steamDlApp == null && !CallVisible()
                && !autoHidden && ForceOpen == -1 && Native.IdleMs() < 30000;
        }

        // Что зверёк делает на краю окна: вы отошли — спит, играет музыка — пританцовывает
        string WalkMood()
        {
            if (Native.IdleMs() > 60000) return "sleep";
            return media.Has && media.Playing ? "dance" : "idle";
        }

        // Опрос: пора ли на прогулку, не пора ли домой, не уехало ли окно
        void WalkTick()
        {
            int now = Environment.TickCount;
            if (!walkerOut)
            {
                if (unchecked(now - walkNextAt) >= 0)
                {
                    if (WalkCanLeave() && WalkGo(false)) return;
                    if (unchecked(now - walkNextAt) >= 0) walkNextAt = now + 20000 + rnd.Next(20000);   // сейчас некстати — заглянуть позже
                }
                return;
            }
            if (walkPhase == 4) { if (!WalkAllowed()) WalkAbort(); return; }   // прыгает на остров — это ведёт WalkStep
            // остров раскрылся, игра, выключили — исчезает сразу
            if (state != 0 || !WalkAllowed()) { WalkLog("abort: state=" + state + " on=" + WalkOn + " pet=" + PetProps() + " sticker=" + (stickerFrames != null) + " lite=" + gameLite + " fs=" + hiddenFs + " gamer=" + GamerMode + " ride=" + RideOn); WalkAbort(); return; }
            if (walkPhase == 3) return;
            // на острове что-то происходит — пора домой
            // (громкость, яркость, смена трека в капсуле — не повод: зверёк остаётся на окне)
            bool busy = claudeState != 0 || timerRinging || Recorder.Active || dlActive != null || steamDlApp != null || CallVisible();
            if (busy || unchecked(now - walkUntil) >= 0 && !toastOn)   // нагулялся — домой, но не посреди всплывашки в капсуле
            {
                WalkLog("home: claude=" + claudeState + " timer=" + timerRinging + " time=" + (unchecked(now - walkUntil) >= 0));
                WalkHome(); return;
            }
            if (walkPhase != 2) return;
            // окно: едем вместе с ним; ушло или сменилось — следом за новым или домой
            IntPtr win; Native.RECT r;
            if (!WalkTarget(out win, out r)) { WalkHome(); return; }
            if (win != walkWin)
            {
                walkWin = win; walkRect = r;
                WalkFly(EdgeX(r, double.NaN), r.T / dpi, 1, 2);
                return;
            }
            if (r.L != walkRect.L || r.T != walkRect.T || r.R != walkRect.R)
            {
                double dx = (r.L - walkRect.L) / dpi, dy = (r.T - walkRect.T) / dpi;
                walkRect = r;
                wX = EdgeX(r, wX + dx); wY = r.T / dpi;   // едет вместе с окном, не сходя с его края
                wGoX = EdgeX(r, wGoX + dx);
                walker.Feet(wX, wY);
            }
            // время от времени прохаживается по краю; вы отошли — засыпает
            if (!walkMoving && unchecked(now - walkStepAt) >= 0)
            {
                walkStepAt = now + 7000 + rnd.Next(8000);
                if (Native.IdleMs() < 60000) { wGoX = EdgeX(r, double.NaN); walkMoving = Math.Abs(wGoX - wX) > 12; if (walkMoving) StartWalkTimer(); }
            }
            if (!walkMoving && pollN % 6 == 0)
            {
                bool asleep = Native.IdleMs() > 60000;
                walker.Pet.Mood(WalkMood());
                walker.Pet.Context = asleep ? "" : "default";
                Native.POINT pt;
                if (!asleep && Native.GetCursorPos(out pt)) { double cxp = pt.X / dpi; walker.Pet.Glance(cxp < wX - 50 ? -1 : cxp > wX + 50 ? 1 : 0, 700); }
            }
        }

        // На прогулку: зверёк выпрыгивает из капсулы и летит на край окна
        bool WalkGo(bool demo)
        {
            IntPtr win; Native.RECT r;
            double px, py, ps;
            bool tg = WalkTarget(out win, out r), pf = PillFeet(out px, out py, out ps);
            WalkLog("go: target=" + tg + " pill=" + pf + " dot=" + (claudeDot == null ? "null" : claudeDot.Visibility + "/" + claudeDot.Children.Count) + " at=" + px.ToString("0") + "," + py.ToString("0"));
            if (!tg || !pf) return false;
            if (walker == null)
            {
                walker = new WalkerWindow(Ink);
                walker.Pet.Root.IsHitTestVisible = false;
            }
            walkerOut = true; walkWin = win; walkRect = r;
            walkUntil = Environment.TickCount + (demo ? 9000 : 45000 + rnd.Next(55000));
            walkStepAt = Environment.TickCount + 5000 + rnd.Next(4000);
            claudeDot.Opacity = 0;   // в капсуле его больше нет
            wX = px; wY = py; wS = ps;
            walker.Size.ScaleX = walker.Size.ScaleY = ps;
            walker.Feet(px, py);
            walker.BeginAnimation(UIElement.OpacityProperty, null);
            walker.Opacity = 1;
            if (!walker.IsVisible) walker.Show();
            if (walker.Handle != IntPtr.Zero) Native.SetWindowDisplayAffinity(walker.Handle, Capturable || ShowInCapture ? 0u : 0x11u);
            WalkFly(EdgeX(r, double.NaN), r.T / dpi, 1, 1);
            return true;
        }

        // Полёт лап зверька из (wX, wY) в (x, y); phase: 1 — к окну, 2 — с окна на окно, 3 — домой
        void WalkFly(double x, double y, double s, int phase)
        {
            wfX = wX; wfY = wY; wfS = wS; wGoX = x;
            walkPhase = phase == 2 ? 1 : phase;
            double dx = x - wX, dy = y - wY, d = Math.Sqrt(dx * dx + dy * dy);
            wMs = (int)Math.Max(320, Math.Min(900, 260 + d * 0.55));
            wArc = dy < -40 ? 4 : Math.Max(18, Math.Min(90, 20 + d * 0.12));   // вверх (домой, в капсулу у края экрана) — без дуги: не улетать за экран
            wAt = Environment.TickCount;
            walkMoving = false;
            walkFlyTo = new Point(x, y); walkFlyS = s;
            walker.Pet.Seated = false;
            walker.Pet.Mood("fly");
            walker.Pet.Context = "";
            if (ReducedMotion) { wMs = 1; }
            WalkerTop();
            StartWalkTimer();
        }
        Point walkFlyTo; double walkFlyS;

        void StartWalkTimer()
        {
            if (walkTimer == null)
            {
                walkTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
                walkTimer.Tick += delegate { WalkStep(); };
            }
            walkTimer.Start();
        }

        // Кадр полёта или шага по краю
        void WalkStep()
        {
            if (walker == null || !walkerOut) { walkTimer.Stop(); return; }
            int now = Environment.TickCount;
            if (walkPhase == 4)
            {
                // прыгает на остров: цель — попутчик, он движется вместе с краем раскрывающейся панели
                double tx, ty, ts;
                if (state == 0 || !RiderAllowed() || rKind != 3 && rKind != 5 || !RiderFeet(out tx, out ty, out ts)) { WalkAbort(); return; }
                rider.Root.Opacity = 0;
                double q = Clamp01(unchecked(now - wAt) / (double)wMs);
                double eq = 1 - Math.Pow(1 - q, 2.4);
                wX = wfX + (tx - wfX) * eq; wY = wfY + (ty - wfY) * eq - wArc * 4 * eq * (1 - eq); wS = wfS + (ts - wfS) * eq;
                double sq = Math.Sin(Math.PI * q) * 0.12;
                walker.Size.ScaleX = wS * (1 - sq * 0.5); walker.Size.ScaleY = wS * (1 + sq);
                walker.Feet(wX, wY);
                if (q >= 1) WalkJoin();
                return;
            }
            if (walkPhase == 1 || walkPhase == 3)
            {
                double gx = walkFlyTo.X, gy = walkFlyTo.Y, gs = walkFlyS;
                double ph; if (walkPhase == 3 && PillFeet(out gx, out gy, out ph)) gs = ph;   // капсула могла сдвинуться
                double p = Clamp01(unchecked(now - wAt) / (double)wMs);
                double e = 1 - Math.Pow(1 - p, 2.4);
                wX = wfX + (gx - wfX) * e; wY = wfY + (gy - wfY) * e - wArc * 4 * e * (1 - e); wS = wfS + (gs - wfS) * e;
                double st = Math.Sin(Math.PI * p) * 0.12;
                walker.Size.ScaleX = wS * (1 - st * 0.5); walker.Size.ScaleY = wS * (1 + st);
                walker.Feet(wX, wY);
                if (p < 1) return;
                if (walkPhase == 3) { WalkDone(); return; }
                walkPhase = 2; walkLandAt = now;
                walker.Pet.Seated = true;
                walker.Pet.Mood(WalkMood());
                walker.Pet.Context = "default";
                walker.Pet.Play("hop", 500);
                return;
            }
            // пружинит после посадки
            int la = unchecked(now - walkLandAt);
            if (la < 320 && !ReducedMotion) { double k = la / 320.0, sq = 0.2 * (1 - k) * (1 - k) * Math.Cos(k * Math.PI * 2.4); walker.Size.ScaleX = wS * (1 + sq * 0.8); walker.Size.ScaleY = wS * (1 - sq); return; }
            walker.Size.ScaleX = walker.Size.ScaleY = wS;
            if (walkMoving)
            {
                // идёт по краю: шаг за шагом, около 45 точек в секунду
                double dir = Math.Sign(wGoX - wX);
                walker.Pet.Seated = false;
                walker.Pet.Mood("walk");
                walker.Pet.Glance((int)dir, 400);
                wX += dir * Math.Min(Math.Abs(wGoX - wX), 0.68);
                walker.Feet(wX, wY);
                if (Math.Abs(wGoX - wX) < 0.5) { walkMoving = false; walker.Pet.Mood(WalkMood()); walker.Pet.Seated = true; }
                return;
            }
            walkTimer.Stop();   // сидит — анимацию ведёт сам зверёк, окно стоит
        }

        // Домой: летит обратно в капсулу
        void WalkHome()
        {
            if (!walkerOut || walker == null || walkPhase == 3 || walkPhase == 4) return;
            double x, y, s;
            if (!PillFeet(out x, out y, out s)) { WalkAbort(); return; }
            WalkFly(x, y, s, 3);
        }

        // Проверка (--ridertest): что делает гуляющий зверёк — сразу в файл
        void WalkLog(string s)
        {
            if (TestRider == null) return;
            try { System.IO.File.AppendAllText(TestRider, Environment.TickCount + " WALK " + s + "\r\n"); } catch { }
        }

        void WalkDone()
        {
            walkTimer.Stop();
            walkerOut = false; walkPhase = 0; walkMoving = false;
            if (walker != null) walker.Hide();
            PetBackHome();
            walkNextAt = Environment.TickCount + 240000 + rnd.Next(240000);   // следующая прогулка — через 4–8 минут
        }

        // Срочно домой (остров раскрылся, игра): тает на месте, а в капсуле появляется сразу
        void WalkAbort()
        {
            if (!walkerOut) return;
            bool joining = walkPhase == 4;
            if (walkTimer != null) walkTimer.Stop();
            walkerOut = false; walkPhase = 0; walkMoving = false;
            if (joining && rider != null) rider.Root.Opacity = 1;   // попутчик больше не прячется
            if (walker != null)
            {
                var w = walker;
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
                fade.Completed += delegate { if (!walkerOut) w.Hide(); };
                w.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            PetBackHome();
            walkNextAt = Environment.TickCount + 240000 + rnd.Next(240000);
        }
    }
}
