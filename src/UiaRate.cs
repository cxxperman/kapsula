// Kapsula — «остров» для Windows. Часть исходного кода: UiaRate.cs
// «В избранное» и «Не интересует» для остальных плееров — без расширений и ключей.
// Стандартное управление музыкой в Windows оценок не знает, зато браузеры (Chrome, Edge, Яндекс Браузер, Firefox…)
// и приложения на их движке (Spotify, Яндекс Музыка) показывают свои кнопки через специальные возможности Windows
// (UI Automation) — так их видят экранные дикторы. Kapsula находит в окне плеера кнопку «Нравится» / «Like» и нажимает
// её напрямую: мышь не двигается, окно не выходит вперёд. Ограничение: вкладка с плеером должна быть выбранной в своём
// окне (окно может быть свёрнуто или под другими) — фоновые вкладки браузер не показывает.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace Kapsula
{
    static class UiaRate
    {
        public static bool Enabled { get { return Store.Get("uiaRate") == "1"; } }

        // Подписи кнопок (сравниваются целиком, без учёта регистра). Порядок неважен.
        static readonly string[] LikeNames = {
            "Like", "Нравится", "Мне нравится", "Add to Liked Songs", "Добавить в плейлист «Любимые треки»", "Добавить в «Любимые треки»", "Добавить в любимые треки", "Add to Your Library", "Save to Your Library",
            "Сохранить в медиатеке", "Добавить в медиатеку", "Add to library", "Добавить в коллекцию", "Love", "Favorite", "Add to favorites", "В избранное"
        };
        static readonly string[] LikedNames = {
            "Unlike", "Liked", "Remove from Liked Songs", "Удалить из плейлиста «Любимые треки»", "Удалить из «Любимых треков»", "Add to playlist", "Добавить в плейлист", "Удалить из любимых треков", "Remove from Your Library", "Удалить из медиатеки",
            "Remove from library", "Удалить из коллекции", "Убрать из коллекции", "Unlove", "Unfavorite", "Remove from favorites", "Убрать из избранного", "Не нравится больше"
        };
        static readonly string[] DislikeNames = {
            "Dislike", "Не нравится", "Не рекомендовать", "Не интересно", "Не интересует", "Hide song", "Скрыть трек", "Скрыть песню"
        };
        // Программы, в окнах которых ищем плеер
        static readonly string[] Players = { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "browser", "arc", "chromium", "thorium", "spotify", "yandexmusic", "yandex music", "яндекс музыка", "soundcloud", "deezer", "tidal" };
        static readonly string[] Sites = { "Spotify", "YouTube Music", "Яндекс Музыка", "Яндекс Музыке", "Yandex Music", "SoundCloud", "Deezer", "TIDAL", "VK Музыка", "Звук" };

        public class Found
        {
            public IntPtr Window;
            public AutomationElement Like, Dislike;
            public bool? Liked;
            public string Where = "";
        }

        // ---------------------------------------------------------------- окна
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, int cmd);

        static string Norm(string s) { return (s ?? "").Trim().ToLowerInvariant(); }

        // Окна-кандидаты: самые подходящие первыми
        static List<IntPtr> Windows(string source, string title)
        {
            var scored = new List<KeyValuePair<int, IntPtr>>();
            string src = Norm(source), t = Norm(title);
            int me = Process.GetCurrentProcess().Id;
            var names = new Dictionary<int, string>();
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero) return true;   // 4 = GW_OWNER: всплывающие окна не нужны
                var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
                string wt = sb.ToString(); if (wt.Length == 0) return true;
                int pid; GetWindowThreadProcessId(h, out pid); if (pid == me) return true;
                string pn;
                if (!names.TryGetValue(pid, out pn)) { try { using (var p = Process.GetProcessById(pid)) pn = Norm(p.ProcessName); } catch { pn = ""; } names[pid] = pn; }
                bool player = false; foreach (string n in Players) if (pn == n) { player = true; break; }
                bool fromSource = pn.Length > 2 && src.Contains(pn) || src.Length > 2 && pn.Contains(src.Replace(".exe", ""));
                if (!player && !fromSource) return true;
                string w = Norm(wt);
                int score = 0;
                if (t.Length > 1 && w.Contains(t)) score += 4;
                if (fromSource) score += 2;
                foreach (string s in Sites) if (w.Contains(Norm(s))) { score += 1; break; }
                if (score >= 2 || (fromSource && player)) scored.Add(new KeyValuePair<int, IntPtr>(score, h));
                return true;
            }, IntPtr.Zero);
            scored.Sort(delegate(KeyValuePair<int, IntPtr> a, KeyValuePair<int, IntPtr> b) { return b.Key.CompareTo(a.Key); });
            var res = new List<IntPtr>();
            foreach (var kv in scored) if (res.Count < 4) res.Add(kv.Value);
            return res;
        }

        // ---------------------------------------------------------------- кнопки
        static int Kind(string name)
        {
            string n = Norm(name);
            if (n.Length == 0 || n.Length > 40) return 0;
            foreach (string s in LikedNames) if (n == Norm(s)) return 2;
            foreach (string s in LikeNames) if (n == Norm(s)) return 1;
            foreach (string s in DislikeNames) if (n == Norm(s)) return 3;
            return 0;
        }

        static readonly Condition ButtonCond = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox));

        // Ищет кнопки плеера в окне. Если подходящих кнопок несколько (лента треков), берём самую нижнюю —
        // в плеерах полоса управления внизу; у свёрнутого окна координат нет — тогда последнюю по порядку.
        public static Found FindIn(IntPtr hwnd)
        {
            AutomationElement root;
            try { root = AutomationElement.FromHandle(hwnd); } catch { return null; }
            var cr = new CacheRequest();
            cr.Add(AutomationElement.NameProperty);
            cr.Add(AutomationElement.BoundingRectangleProperty);
            cr.Add(AutomationElement.IsOffscreenProperty);
            cr.Add(TogglePattern.ToggleStateProperty);
            cr.Add(AutomationElement.IsTogglePatternAvailableProperty);
            cr.Add(AutomationElement.IsInvokePatternAvailableProperty);
            AutomationElementCollection all;
            try { using (cr.Activate()) all = root.FindAll(TreeScope.Descendants, ButtonCond); }
            catch { return null; }
            AutomationElement like = null, dis = null; int likeKind = 0; double likeY = double.MinValue, disY = double.MinValue;
            foreach (AutomationElement e in all)
            {
                string name; System.Windows.Rect r;
                try { name = (string)e.GetCachedPropertyValue(AutomationElement.NameProperty); r = (System.Windows.Rect)e.GetCachedPropertyValue(AutomationElement.BoundingRectangleProperty); }
                catch { continue; }
                int k = Kind(name); if (k == 0) continue;
                double y = r.IsEmpty ? 0 : r.Bottom;
                if (k == 3) { if (y >= disY) { dis = e; disY = y; } }
                else if (y >= likeY) { like = e; likeY = y; likeKind = k; }
            }
            if (like == null && dis == null) return null;
            var f = new Found { Window = hwnd, Like = like, Dislike = dis };
            if (like != null)
            {
                f.Liked = likeKind == 2 ? (bool?)true : null;
                try
                {
                    if ((bool)like.GetCachedPropertyValue(AutomationElement.IsTogglePatternAvailableProperty))
                    {
                        var st = (ToggleState)like.GetCachedPropertyValue(TogglePattern.ToggleStateProperty);
                        f.Liked = st == ToggleState.On;
                    }
                }
                catch { }
                if (f.Liked == null && likeKind == 1) f.Liked = false;
            }
            try { var sb = new StringBuilder(256); GetWindowText(hwnd, sb, 256); f.Where = sb.ToString(); } catch { }
            return f;
        }

        // Кнопки плеера, который сейчас играет. null — не нашли (вкладка фоновая, сайт не знаем, окно закрыто)
        public static Found Find(string source, string title)
        {
            // Первый запрос будит специальные возможности браузера, и он может ответить пустым деревом — тогда ещё раз
            var wins = Windows(source, title);
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (IntPtr h in wins)
                {
                    Found f = FindIn(h);
                    if (f != null && f.Like != null) return f;
                }
                if (wins.Count == 0) break;
                System.Threading.Thread.Sleep(400);
            }
            return null;
        }

        static bool Press(AutomationElement e)
        {
            if (e == null) return false;
            try
            {
                object p;
                if (e.TryGetCurrentPattern(InvokePattern.Pattern, out p)) { ((InvokePattern)p).Invoke(); return true; }
                if (e.TryGetCurrentPattern(TogglePattern.Pattern, out p)) { ((TogglePattern)p).Toggle(); return true; }
            }
            catch { }
            return false;
        }

        // Проверка (ключ --uiaprobe адрес файл [секунды] [нажать]): невидимое окно со встроенным браузером открывает страницу,
        // потом Kapsula ищет в нём кнопки так же, как в настоящем плеере, и пишет в файл, что нашла
        public static void Probe(string url, string outFile, int seconds, bool press)
        {
            var w = new System.Windows.Window { Width = 1280, Height = 800, Left = -4000, Top = 0, ShowActivated = false, ShowInTaskbar = false, WindowStyle = System.Windows.WindowStyle.None, Title = "Kapsula UIA probe" };
            var view = new Microsoft.Web.WebView2.Wpf.WebView2();
            w.Content = view;
            w.Show();
            var env = Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kapsula-uiaprobe")).Result;
            view.EnsureCoreWebView2Async(env).ContinueWith(delegate
            {
                w.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (url.StartsWith("file:") || url.StartsWith("http")) view.CoreWebView2.Navigate(url); else view.CoreWebView2.NavigateToString(System.IO.File.ReadAllText(url));
                }));
            });
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            var tm = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            tm.Tick += delegate
            {
                tm.Stop();
                var log = new StringBuilder();
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        var root = AutomationElement.FromHandle(hwnd);
                        root.FindAll(TreeScope.Descendants, ButtonCond); System.Threading.Thread.Sleep(1500);   // прогрев
                        sw.Restart();
                        var all = root.FindAll(TreeScope.Descendants, ButtonCond);
                        log.AppendLine("buttons " + all.Count + " in " + sw.ElapsedMilliseconds + " ms");
                        int n = 0;
                        foreach (AutomationElement e in all) { if (n++ > 80) break; try { log.AppendLine("  [" + e.Current.Name + "] " + e.Current.ControlType.ProgrammaticName); } catch { } }
                        sw.Restart();
                        Found f = FindIn(hwnd);
                        log.AppendLine("find " + sw.ElapsedMilliseconds + " ms: like=" + (f != null && f.Like != null ? f.Like.Current.Name : "-") + " liked=" + (f != null ? f.Liked.ToString() : "-") + " dislike=" + (f != null && f.Dislike != null ? f.Dislike.Current.Name : "-"));
                        if (press && f != null)
                        {
                            log.AppendLine("press like " + PressLike(f));
                            System.Threading.Thread.Sleep(600);
                            Found f2 = FindIn(hwnd);
                            log.AppendLine("after: like=" + (f2 != null && f2.Like != null ? f2.Like.Current.Name : "-") + " liked=" + (f2 != null ? f2.Liked.ToString() : "-"));
                            log.AppendLine("press dislike " + PressDislike(f2));
                            System.Threading.Thread.Sleep(400);
                            string title = (string)w.Dispatcher.Invoke(new Func<string>(delegate { return view.CoreWebView2.DocumentTitle; }));
                            log.AppendLine("title " + title);
                        }
                    }
                    catch (Exception ex) { log.AppendLine("error " + ex.GetType().Name + ": " + ex.Message); }
                    System.IO.File.WriteAllText(outFile, log.ToString(), Encoding.UTF8);
                    w.Dispatcher.BeginInvoke(new Action(delegate { System.Windows.Application.Current.Shutdown(); }));
                });
            };
            tm.Start();
        }

        public static bool PressLike(Found f) { return f != null && Press(f.Like); }
        public static bool PressDislike(Found f) { return f != null && Press(f.Dislike); }
    }
}
