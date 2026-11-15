// Kapsula — «остров» для Windows. Часть исходного кода: Overlays.cs
// Окна поверх всего: полоска с нагрузкой в играх и чат Twitch-канала.
// Оба — прозрачные для мыши и не забирают фокус.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Kapsula
{
    // Окно-подложка: без рамки, не в панели задач, мышь проходит насквозь, фокус не берёт
    class OverlayWindow : Window
    {
        public OverlayWindow()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            SourceInitialized += delegate
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                int ex = GetWindowLong(h, -20);
                SetWindowLong(h, -20, ex | 0x20 | 0x80 | 0x08000000 | 0x80000);   // прозрачное для мыши, служебное, без активации, слоёное
            };
        }

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    }

    partial class IslandWindow
    {
        OverlayWindow stripWin, chatWin;
        TextBlock stripText;
        StackPanel chatLines;
        int overlayN;

        // ---- для пульта с телефона
        public void PhoneMedia(int key) { watcher.Command(key); }
        public string PhoneTrack() { return media.Has ? (media.Artist.Length > 0 ? media.Artist + " — " : "") + media.Title : ""; }

        void StartOverlays()
        {
            TwitchSync();
            OverlaysTick();
        }

        // раз в полсекунды
        void OverlaysTick()
        {
            overlayN++;
            bool fs = FullscreenAppInFront();
            // полоска в играх
            bool wantStrip = Store.Get("gameStrip") == "1" && fs;
            if (wantStrip && stripWin == null)
            {
                stripText = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
                stripWin = new OverlayWindow { Content = new Border { Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 4, 10, 5), Child = stripText } };
                stripWin.Left = 12; stripWin.Top = 10;
                stripWin.Show();
            }
            else if (!wantStrip && stripWin != null) { stripWin.Close(); stripWin = null; }
            if (stripWin != null && overlayN % 2 == 0) UpdateStrip();

            // чат Twitch — поверх всего, справа
            bool wantChat = Store.Get("twitchOverlay") == "1" && Store.Get("twitchChannel").Length > 0 && (fs || Store.Get("twitchAlways") == "1");
            if (wantChat && chatWin == null)
            {
                chatLines = new StackPanel { Width = 300 };
                chatWin = new OverlayWindow { Content = new Border { Background = new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 10, 8), Child = chatLines } };
                var wa = SystemParameters.WorkArea;
                chatWin.Left = wa.Right - 340; chatWin.Top = wa.Top + 120;
                chatWin.Show();
                RenderChatOverlay();
            }
            else if (!wantChat && chatWin != null) { chatWin.Close(); chatWin = null; }
        }

        ulong oIdle, oTotal;
        double oCpu;
        volatile int gpuLoad = -1;
        int gpuBusy;

        void UpdateStrip()
        {
            Native.FILETIME i, k, u;
            if (Native.GetSystemTimes(out i, out k, out u))
            {
                ulong idle = Native.ToU64(i), total = Native.ToU64(k) + Native.ToU64(u);
                if (oTotal != 0 && total > oTotal) oCpu = 1.0 - (double)(idle - oIdle) / (total - oTotal);
                oIdle = idle; oTotal = total;
            }
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
            Native.GlobalMemoryStatusEx(ref m);
            if (Interlocked.CompareExchange(ref gpuBusy, 1, 0) == 0)
                ThreadPool.QueueUserWorkItem(delegate { try { gpuLoad = GpuLoad(); } catch { gpuLoad = -1; } finally { gpuBusy = 0; } });
            stripText.Text = "ЦП " + Math.Round(oCpu * 100).ToString().PadLeft(2) + "%  ·  " + (gpuLoad >= 0 ? "ГП " + gpuLoad.ToString().PadLeft(2) + "%  ·  " : "") + "ОЗУ " + m.dwMemoryLoad + "%  ·  " + DateTime.Now.ToString("H:mm");
        }

        // Загрузка видеокарты: сумма 3D-движков по счётчикам «GPU Engine» (как в Диспетчере задач)
        static PerformanceCounterCategory gpuCat;
        static int GpuLoad()
        {
            if (gpuCat == null) gpuCat = new PerformanceCounterCategory("GPU Engine");
            double sum = 0;
            var counters = new List<PerformanceCounter>();
            foreach (string inst in gpuCat.GetInstanceNames())
                if (inst.EndsWith("engtype_3D")) counters.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true));
            foreach (var c in counters) c.NextValue();
            Thread.Sleep(400);
            foreach (var c in counters) { try { sum += c.NextValue(); } catch { } c.Dispose(); }
            return (int)Math.Min(100, Math.Round(sum));
        }

        // ================================================================ Twitch
        // Чат канала читаем анонимно (как гость «justinfan»): ни входа, ни ключа не нужно, писать в чат нельзя.
        readonly List<string[]> chatMsgs = new List<string[]>();
        string twitchJoined;

        public void TwitchSync()
        {
            string ch = Store.Get("twitchOverlay") == "1" ? Store.Get("twitchChannel").Trim().TrimStart('#').ToLowerInvariant() : "";
            if (ch == twitchJoined) return;
            twitchJoined = ch;
            lock (chatMsgs) chatMsgs.Clear();
            if (ch.Length == 0) return;
            var t = new Thread(delegate() { TwitchLoop(ch); }) { IsBackground = true, Name = "Twitch" };
            t.Start();
        }

        void TwitchLoop(string ch)
        {
            while (twitchJoined == ch)
            {
                try
                {
                    using (var c = new System.Net.Sockets.TcpClient("irc.chat.twitch.tv", 6667))
                    using (var s = c.GetStream())
                    using (var r = new System.IO.StreamReader(s, Encoding.UTF8))
                    using (var w = new System.IO.StreamWriter(s, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true })
                    {
                        c.ReceiveTimeout = 360000;
                        w.WriteLine("CAP REQ :twitch.tv/tags");
                        w.WriteLine("PASS SCHMOOPIIE");
                        w.WriteLine("NICK justinfan" + new Random().Next(10000, 99999));
                        w.WriteLine("JOIN #" + ch);
                        string line;
                        while (twitchJoined == ch && (line = r.ReadLine()) != null)
                        {
                            if (line.StartsWith("PING")) { w.WriteLine("PONG" + line.Substring(4)); continue; }
                            int pm = line.IndexOf(" PRIVMSG #");
                            if (pm < 0) continue;
                            string tags = line.StartsWith("@") ? line.Substring(1, line.IndexOf(' ') - 1) : "";
                            string name = TagValue(tags, "display-name"), color = TagValue(tags, "color");
                            if (name.Length == 0) { int ex = line.IndexOf('!'); int col = line.IndexOf(':'); name = ex > col ? line.Substring(col + 1, ex - col - 1) : "?"; }
                            int msgAt = line.IndexOf(" :", pm);
                            string text = msgAt >= 0 ? line.Substring(msgAt + 2) : "";
                            lock (chatMsgs) { chatMsgs.Add(new string[] { name, text, color }); if (chatMsgs.Count > 12) chatMsgs.RemoveAt(0); }
                            Dispatcher.BeginInvoke(new Action(RenderChatOverlay));
                        }
                    }
                }
                catch { }
                for (int i = 0; i < 20 && twitchJoined == ch; i++) Thread.Sleep(500);   // переподключение через 10 секунд
            }
        }

        static string TagValue(string tags, string key)
        {
            foreach (string kv in tags.Split(';'))
                if (kv.StartsWith(key + "=")) return kv.Substring(key.Length + 1);
            return "";
        }

        void RenderChatOverlay()
        {
            if (chatLines == null) return;
            chatLines.Children.Clear();
            chatLines.Children.Add(new TextBlock { Text = "Чат · " + twitchJoined, Foreground = new SolidColorBrush(Color.FromRgb(0xBF, 0x94, 0xFF)), FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            List<string[]> copy;
            lock (chatMsgs) copy = new List<string[]>(chatMsgs);
            if (copy.Count == 0) chatLines.Children.Add(new TextBlock { Text = "Подключаюсь… сообщения появятся здесь", Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)), FontSize = 12.5 });
            foreach (string[] m in copy)
            {
                var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 1, 0, 1) };
                Color c = Color.FromRgb(0x9E, 0xC9, 0xFF);
                try { if (m[2].Length == 7) c = (Color)ColorConverter.ConvertFromString(m[2]); } catch { }
                tb.Inlines.Add(new System.Windows.Documents.Run(m[0] + ": ") { Foreground = new SolidColorBrush(c), FontWeight = FontWeights.SemiBold });
                tb.Inlines.Add(new System.Windows.Documents.Run(m[1]) { Foreground = Brushes.White });
                chatLines.Children.Add(tb);
            }
        }
    }
}
