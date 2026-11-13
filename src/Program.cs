// Kapsula — «остров» для Windows. Часть исходного кода: Program.cs
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
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            WebFiles.Hook();   // библиотеки встроенного браузера лежат внутри exe
            // установка и удаление драйвера камеры: этот процесс запущен с правами администратора
            if (args.Length == 3 && args[0] == "--camsender")
            {
                Store.Load();
                var sa = new Application();
                sa.Startup += delegate { PhoneRtc.TestSender(int.Parse(args[1]), args[2]); };
                sa.Run();
                return;
            }
            if (args.Length == 2 && args[0] == "--qwenengines") { var o = new System.Text.StringBuilder(); o.AppendLine("wanted " + QwenLocal.WantedEngine()); foreach (string k in new string[] { "vulkan", "cuda-12", "cuda-13" }) { string u, r; try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; QwenLocal.FindEngine(k, out u, out r); o.AppendLine(k + ": " + u + " | " + r); } catch (Exception ex) { o.AppendLine(k + ": " + ex.Message); } } System.IO.File.WriteAllText(args[1], o.ToString()); return; }
            if (args.Length == 3 && args[0] == "--weathertest") { Store.Load(); System.IO.File.WriteAllText(args[2], Weather.Test(args[1])); return; }
            if (args.Length == 3 && args[0] == "--spectest")
            {
                // проверка: слушаем только указанный плеер 6 секунд и пишем, что услышали
                var o = new System.Text.StringBuilder();
                o.AppendLine("pid " + AppVolume.SessionPid(args[1]) + ", пик плеера " + AppVolume.SessionPeak(args[1], 0).ToString("0.00"));
                Spectrum.Player = args[1];
                var bands = new double[24]; int ok = 0, total = 0; double sum = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 6000) { total++; if (Spectrum.Read(bands)) { ok++; foreach (double v in bands) sum += v; } System.Threading.Thread.Sleep(100); }
                o.AppendLine("спектр получен " + ok + " из " + total + ", средняя высота " + (ok > 0 ? (sum / ok / bands.Length).ToString("0.00") : "-"));
                System.IO.File.WriteAllText(args[2], o.ToString()); return;
            }
            if (args.Length == 3 && args[0] == "--qwentest") { Store.Load(); Environment.Exit(QwenLocal.Test(args[1], args[2])); return; }
            if (args.Length >= 3 && args[0] == "--uiaprobe")
            {
                var ua = new Application();
                ua.Startup += delegate { UiaRate.Probe(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 8, args.Length > 4 && args[4] == "press"); };
                ua.Run();
                return;
            }
            if (args.Length == 2 && args[0] == "--camprobe")
            {
                var pa = new Application();
                pa.Startup += delegate { PhoneCam.Probe(args[1]); };
                pa.Run();
                return;
            }
            if (args.Length == 2 && args[0] == "--camsetup") { Environment.Exit(PhoneCam.RunSetup(args[1] == "install")); return; }
            bool created;
            var mutex = new Mutex(true, "Kapsula_single_instance", out created);
            if (!created)
            {
                // «--wait» передаёт сама Kapsula при перезапуске после обновления: ждём, пока прежняя копия закроется.
                // Уже работает другая копия. Если она старее — это прошлая версия из другой папки: закрываем её и запускаемся сами.
                bool waitArg = Array.IndexOf(args, "--wait") >= 0;
                if (!waitArg && !Takeover.CloseOlder()) return;
                try { if (!mutex.WaitOne(10000)) return; }
                catch (AbandonedMutexException) { }
            }

            // Анимации WPF по умолчанию идут на 60 кадрах — поднимаем до частоты монитора.
            int hz = Native.RefreshRate();
            Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(hz));

            // для проверочных снимков: рисовать без видеокарты (KAPSULA_SOFTWARE=1)
            if (Environment.GetEnvironmentVariable("KAPSULA_SOFTWARE") == "1") System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            Store.Load();
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                e.Handled = true;
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(Store.Dir, "error.log"), DateTime.Now + " " + e.Exception + "\r\n"); } catch { }
            };
            for (int k = 0; k + 1 < args.Length; k++) { int sn; if (args[k] == "--season" && int.TryParse(args[k + 1], out sn)) PetView.TestSeason = sn; }   // до создания окна: зверьки одеваются при постройке
            var w = new IslandWindow();
            bool plain = true;   // обычный запуск, без проверочных ключей — только тогда ищем обновления
            bool updated = false; // нас только что перезапустило автообновление
            for (int i = 0; i < args.Length; i++)
            {
                double y; int n;
                if (args[i] == "--updatefeed" && i + 1 < args.Length) { Updater.FeedOverride = args[i + 1]; i++; continue; }
                if (args[i] == "--updated") { updated = true; continue; }
                if (args[i] == "--noapi") { Updater.SkipApi = true; continue; }
                if (args[i] != "--wait") plain = false;
                if (args[i] == "--capturable") w.Capturable = true;
                if (args[i] == "--mini") w.ForceOpen = -2;
                if (args[i] == "--demo") w.Demo = true;
                if (args[i] == "--steamtest") { w.TestSteam = true; SteamBridge.Logging = true; }
                if (args[i] == "--replytest") w.TestReply = true;
                if (args[i] == "--hinttest" && i + 1 < args.Length) w.TestHint = args[i + 1];
                if (args[i] == "--character" && i + 1 < args.Length) w.TestCharacter = args[i + 1];
                if (args[i] == "--mood" && i + 1 < args.Length) w.TestMood = args[i + 1];
                if (args[i] == "--fstest") { w.Demo = true; w.TestFs = true; }
                if (args[i] == "--lyrictest") w.TestLyrics = true;
                if (args[i] == "--notetest") w.TestNote = true;
                if (args[i] == "--gamertest") w.TestGamer = true;
                if (args[i] == "--edittab" && i + 1 < args.Length) w.TestEditTab = args[i + 1];
                if (args[i] == "--ridertest" && i + 1 < args.Length) w.TestRider = args[i + 1];   // путь зверька-попутчика — в файл
                if (args[i] == "--settings") w.TestSettings = true;
                if (args[i] == "--settingstop") { w.TestSettings = true; w.TestSettingsTop = true; }
                if (args[i] == "--settingsscroll" && i + 1 < args.Length) double.TryParse(args[i + 1], out w.TestSettingsScroll);
                if (args[i] == "--settingstab" && i + 1 < args.Length) { w.TestSettings = true; w.TestSettingsTab = args[i + 1]; }
                if (args[i] == "--bt") w.TestBt = true;
                if (args[i] == "--chattest") w.TestChat = true;
                if (args[i] == "--notif") w.ShowNotifications = true;   // только на этот запуск
                if (args[i] == "--eyetest") w.TestEyes = true;
                if (args[i] == "--rectest") { w.TestRec = true; w.ForceOpen = -3; }
                if (args[i] == "--color" && i + 1 < args.Length) w.PreviewOnly(args[i + 1]);
                if (args[i] == "--scale" && i + 1 < args.Length) w.PreviewScale(args[i + 1]);
                if (args[i] == "--card") { w.TestCard = true; w.ForceOpen = -3; }
                if (args[i] == "--opacity" && i + 1 < args.Length && double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y)) w.ApplyOpacity(y, false);
                if (args[i] == "--chatlong") { w.TestChat = true; w.TestChatLong = true; }
                if (args[i] == "--claudetest" && i + 1 < args.Length) { w.TestClaude = args[i + 1]; w.ForceOpen = -3; ClaudeBridge.ForceOn = true; ClaudeBridge.Start(); if (args[i + 1] == "stopnow") ClaudeBridge.RequestStop(null); }
                if (args[i] == "--pet" && i + 1 < args.Length && int.TryParse(args[i + 1], out n)) { w.TestPet = n; w.ForceOpen = -3; ClaudeBridge.ForceOn = true; }
                if (args[i] == "--budstest" && i + 1 < args.Length && int.TryParse(args[i + 1], out n)) { w.TestBuds = n; w.ForceOpen = -3; }
                if (args[i] == "--chiptest" && i + 1 < args.Length && int.TryParse(args[i + 1], out n)) w.TestChip = n;
                if (args[i] == "--glass") { w.GlassOn = true; w.ApplyEffectiveTheme(); }
                if (args[i] == "--glassdump" && i + 1 < args.Length) { w.GlassOn = true; w.TestGlassDump = args[i + 1]; w.Demo = true; w.ForceOpen = 0; }
                if (args[i] == "--slidetest") { w.TestSlide = true; w.Demo = true; w.ForceOpen = 0; }
                if (args[i] == "--welcome") { w.TestWelcome = true; w.ForceOpen = -3; }
                if (args[i] == "--homeclips") { w.TestHomeClips = true; w.ApplyHomeLayout(); }
                if (args[i] == "--hideapps" && i + 1 < args.Length) w.TestHideApps = args[i + 1];
                if (args[i] == "--democard" && i + 1 < args.Length) { w.TestCard = true; w.TestDemoCard = args[i + 1]; w.ForceOpen = -3; }
                if (args[i] == "--cmd" && i + 1 < args.Length) { w.TestCmd = args[i + 1]; w.ForceOpen = -3; }
                if (args[i] == "--gamelite") w.TestGameLite = true;
                if (args[i] == "--ducktest") Ai.ForceDuck = true;
                if (args[i] == "--duckask" && i + 1 < args.Length) { Ai.ForceDuck = true; w.TestDuckAsk = args[i + 1]; w.ForceOpen = 4; }
                if (args[i] == "--camtest") PhoneCam.TestPattern(w);
                if (args[i] == "--camserve") PhoneCam.Start(w);
                if (args[i] == "--emote" && i + 1 < args.Length) w.TestEmote = args[i + 1];
                if (args[i] == "--steamdltest") w.Loaded += delegate
                {
                    double pct = 5; var st = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                    st.Tick += delegate { pct = Math.Min(100, pct + 2.5); if (pct >= 100) { st.Stop(); w.SteamDownload("0", "", 0, 0, false, null); } else w.SteamDownload("570", "Dota 2", pct, 24e6, false, null); };
                    st.Start();
                };
                if (args[i] == "--qrtest") w.Loaded += delegate { w.Dispatcher.BeginInvoke(new Action(delegate { PhoneCam.Start(w); PhoneCam.ShowQr(); }), System.Windows.Threading.DispatcherPriority.ApplicationIdle); };
                if (args[i] == "--timerdone") { w.TestTimerDone = true; w.ForceOpen = -3; }
                if (args[i] == "--traymenu") { w.TestTray = true; w.ForceOpen = -3; }
                if (args[i] == "--ducktoggle") { Ai.ForceDuck = true; w.TestDuckToggle = true; w.ForceOpen = 4; }
                if (args[i] == "--season" && i + 1 < args.Length && int.TryParse(args[i + 1], out n)) PetView.TestSeason = n;
                if (args[i] == "--pill") w.ForceOpen = -3;
                if (args[i] == "--y" && i + 1 < args.Length && double.TryParse(args[i + 1], out y)) w.Top = y;
                if (args[i] == "--open" && i + 1 < args.Length && int.TryParse(args[i + 1], out n)) w.ForceOpen = n;
            }
            w.Plain = plain;
            w.Show();
            if (plain) { Updater.Start(w); System.Threading.ThreadPool.QueueUserWorkItem(delegate { Takeover.FixAutostart(); }); }
            if (updated)
            {
                var hello = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
                hello.Tick += delegate { hello.Stop(); w.NotifyUpdated(); };
                hello.Start();
            }
            app.Run();
            GC.KeepAlive(mutex);
        }
    }
}
