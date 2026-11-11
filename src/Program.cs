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
            bool created;
            var mutex = new Mutex(true, "Kapsula_single_instance", out created);
            if (!created)
            {
                // «--wait» передаёт сама Kapsula при перезапуске после обновления: ждём, пока прежняя копия закроется.
                if (Array.IndexOf(args, "--wait") < 0) return;
                try { if (!mutex.WaitOne(10000)) return; }
                catch (AbandonedMutexException) { }
            }

            // Анимации WPF по умолчанию идут на 60 кадрах — поднимаем до частоты монитора.
            int hz = Native.RefreshRate();
            Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(hz));

            Store.Load();
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                e.Handled = true;
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(Store.Dir, "error.log"), DateTime.Now + " " + e.Exception + "\r\n"); } catch { }
            };
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
                if (args[i] == "--fstest") { w.Demo = true; w.TestFs = true; }
                if (args[i] == "--lyrictest") w.TestLyrics = true;
                if (args[i] == "--notetest") w.TestNote = true;
                if (args[i] == "--settings") w.TestSettings = true;
                if (args[i] == "--settingstop") { w.TestSettings = true; w.TestSettingsTop = true; }
                if (args[i] == "--bt") w.TestBt = true;
                if (args[i] == "--chattest") w.TestChat = true;
                if (args[i] == "--notif") w.ShowNotifications = true;   // только на этот запуск
                if (args[i] == "--eyetest") w.TestEyes = true;
                if (args[i] == "--rectest") { w.TestRec = true; w.ForceOpen = -3; }
                if (args[i] == "--color" && i + 1 < args.Length) w.PreviewOnly(args[i + 1]);
                if (args[i] == "--scale" && i + 1 < args.Length) w.PreviewScale(args[i + 1]);
                if (args[i] == "--card") { w.TestCard = true; w.ForceOpen = -3; }
                if (args[i] == "--cmd" && i + 1 < args.Length) { w.TestCmd = args[i + 1]; w.ForceOpen = -3; }
                if (args[i] == "--pill") w.ForceOpen = -3;
                if (args[i] == "--y" && i + 1 < args.Length && double.TryParse(args[i + 1], out y)) w.Top = y;
                if (args[i] == "--open" && i + 1 < args.Length && int.TryParse(args[i + 1], out n)) w.ForceOpen = n;
            }
            w.Show();
            if (plain) Updater.Start(w);
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
