// Kapsula — «остров» для Windows. Часть исходного кода: Browser.cs
// Встроенный мини-браузер (Microsoft WebView2, движок Edge) для вкладки «Чат»: в нём открывается Duck.ai.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Kapsula
{
    // Библиотеки WebView2 лежат внутри Kapsula.exe. При первом обращении они достаются в папку настроек.
    // Этот класс сам к WebView2 не обращается — его можно вызывать до того, как библиотеки найдены.
    static class WebFiles
    {
        static readonly string[] Names = { "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.Wpf.dll", "WebView2Loader.dll" };
        static bool extracted;

        public static string Dir { get { return Path.Combine(Store.Dir, "webview2"); } }
        public static string DataDir { get { return Path.Combine(Dir, "data"); } }

        public static void Hook()
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e)
            {
                string name = new AssemblyName(e.Name).Name;
                if (!name.StartsWith("Microsoft.Web.WebView2.", StringComparison.OrdinalIgnoreCase)) return null;
                try
                {
                    Extract();
                    string path = Path.Combine(Dir, name + ".dll");
                    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
                }
                catch { return null; }
            };
        }

        public static void Extract()
        {
            if (extracted) return;
            Directory.CreateDirectory(Dir);
            Assembly me = Assembly.GetExecutingAssembly();
            foreach (string n in Names)
            {
                using (Stream src = me.GetManifestResourceStream("Kapsula.webview2." + n))
                {
                    if (src == null) continue;
                    string path = Path.Combine(Dir, n);
                    // файл уже на месте и той же длины — не трогаем (он может быть занят запущенным браузером)
                    if (File.Exists(path) && new FileInfo(path).Length == src.Length) continue;
                    try { using (FileStream dst = File.Create(path)) src.CopyTo(dst); } catch { }
                }
            }
            extracted = true;
        }
    }

    // Отдельное окно без рамки, которое лежит точно поверх страницы острова. Остров — прозрачное окно,
    // а внутри прозрачного окна Windows браузер не рисует, поэтому он живёт в своём окне-спутнике.
    class MiniBrowser
    {
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

        readonly Window owner;
        string home;
        Window win;
        WebView2 view;
        IntPtr hwnd;
        int lastX, lastY, lastW, lastH, lastR;
        bool starting, onScreen;
        string backHex = "#000000";

        public bool Ready;          // страница загружается или уже загружена
        public bool Loaded;         // страница хотя бы раз загрузилась до конца
        public string Error;        // почему браузер не запустился (нет WebView2 Runtime и т. п.)
        public bool Visible { get { return onScreen; } }
        public bool Active { get { return onScreen && win.IsActive; } }
        public Action Changed;      // состояние поменялось — остров перерисует подпись

        public MiniBrowser(Window owner, string home) { this.owner = owner; this.home = home; }

        // Журнал для проверки (переменная окружения KAPSULA_BROWSERLOG — путь к файлу): когда окно показывали, прятали и когда страница грузилась
        static readonly string LogPath = Environment.GetEnvironmentVariable("KAPSULA_BROWSERLOG");
        static void Log(string what) { if (!string.IsNullOrEmpty(LogPath)) try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + what + "\r\n"); } catch { } }

        void Build(Color back)
        {
            win = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
                ShowActivated = false, Owner = owner, Background = new SolidColorBrush(back), Title = "Kapsula — Duck.ai",
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 400, Height = 300
            };
            view = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, back.R, back.G, back.B), VerticalAlignment = VerticalAlignment.Top };
            win.Content = new System.Windows.Controls.Grid { ClipToBounds = true, Children = { view } };
            hwnd = new WindowInteropHelper(win).EnsureHandle();
            // Углы скругляет сама Windows 11 (и без серой рамки вокруг). Обрезать окно своей фигурой нельзя:
            // из-под неё по углам торчат серые уголки. В Windows 10 окно остаётся прямоугольным.
            try
            {
                int round = 2, noBorder = unchecked((int)0xFFFFFFFE);
                DwmSetWindowAttribute(hwnd, 33, ref round, 4);
                DwmSetWindowAttribute(hwnd, 34, ref noBorder, 4);
            }
            catch { }
            backHex = "#" + back.R.ToString("x2") + back.G.ToString("x2") + back.B.ToString("x2");
            win.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { e.Cancel = true; Hide(); };   // Alt+F4 только прячет
        }

        void Start()
        {
            if (starting) return;
            starting = true;
            try
            {
                WebFiles.Extract();
                CoreWebView2Environment.SetLoaderDllFolderPath(WebFiles.Dir);
                TaskScheduler ui = TaskScheduler.FromCurrentSynchronizationContext();
                CoreWebView2Environment.CreateAsync(null, WebFiles.DataDir, null).ContinueWith(delegate(Task<CoreWebView2Environment> te)
                {
                    if (te.IsFaulted || te.IsCanceled) { Fail(te.Exception); return; }
                    try
                    {
                        view.EnsureCoreWebView2Async(te.Result).ContinueWith(delegate(Task t2)
                        {
                            if (t2.IsFaulted || t2.IsCanceled) { Fail(t2.Exception); return; }
                            try
                            {
                                CoreWebView2 core = view.CoreWebView2;
                                core.Settings.AreDevToolsEnabled = false;
                                core.Settings.IsStatusBarEnabled = false;
                                core.Settings.AreDefaultContextMenusEnabled = true;
                                try { core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark; } catch { }
                                // Ссылки «в новом окне» открываются в обычном браузере, а не плодят окна
                                core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e)
                                {
                                    // окно входа через Google, Apple, Microsoft или GitHub открывается здесь же, иначе вход не завершится
                                    try
                                    {
                                        string host = new Uri(e.Uri).Host;
                                        if (host == "accounts.google.com" || host == "appleid.apple.com" || host.EndsWith("login.microsoftonline.com") || host == "login.live.com" || host == "github.com") return;
                                    }
                                    catch { }
                                    e.Handled = true;
                                    try { if (e.Uri.StartsWith("https://") || e.Uri.StartsWith("http://")) System.Diagnostics.Process.Start(e.Uri); } catch { }
                                };
                                // Страница красится в цвет острова и проявляется плавно — выглядит как часть панели, а не окно браузера.
                                // Содержимое и надписи самой страницы не трогаем.
                                core.AddScriptToExecuteOnDocumentCreatedAsync(
                                    "(function(){var duck=/(^|\\.)duck\\.ai$/.test(location.hostname);var s=new CSSStyleSheet();s.replaceSync((duck?':root,html[data-theme]{--ds-surface-backdrop:" + backHex + " !important;--ds-surface-canvas:" + backHex + " !important;--theme-col-bg-page:" + backHex + " !important;--theme-col-bg-ui:" + backHex + " !important;--sds-color-background-01:" + backHex + " !important;--sds-color-background-dark:" + backHex + " !important;--theme-col-metabar-bg:" + backHex + " !important}" +
                                    "html,body{background:" + backHex + " !important}" +
                                    // рекламные плашки самого Duck.ai («скачайте наш браузер» и подобные): узнаём по ссылке внутри, а не по служебным именам
                                    "[role=button]:has(a[href*=\"duckduckgo.com/browser\"]),[role=button]:has(a[href*=\"origin=funnel_\"]),[role=button]:has(a[href*=\"duckduckgo.com/app\"]){display:none !important}':'')+" +
                                    // остальное — для любого сайта: плавное появление и без полос прокрутки
                                    "'html{opacity:0;transition:opacity .22s ease-out}html.kapsula-in{opacity:1}::-webkit-scrollbar{width:0;height:0}');" +
                                    "document.adoptedStyleSheets=document.adoptedStyleSheets.concat([s]);" +
                                    "window.__kapsulaShow=function(v){var h=document.documentElement;if(!h)return;if(!v){h.classList.remove('kapsula-in');return}" +
                                    "requestAnimationFrame(function(){requestAnimationFrame(function(){h.classList.add('kapsula-in')})})};" +
                                    "if(" + (onScreen ? "true" : "window.__kapsulaOn") + ")window.__kapsulaShow(true);" +
                                    "document.addEventListener('DOMContentLoaded',function(){if(window.__kapsulaOn)window.__kapsulaShow(true)})})();");
                                view.ZoomFactor = 0.9;
                                core.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs e) { Log("navigate " + e.Uri); };
                                core.ProcessFailed += delegate(object s, CoreWebView2ProcessFailedEventArgs e) { Log("process failed " + e.ProcessFailedKind); };
                                core.DOMContentLoaded += delegate { Log("dom loaded"); Fade(true); if (!Loaded) { Loaded = true; if (Changed != null) Changed(); } };
                                core.NavigationCompleted += delegate { Fade(true); FlushAsk(); };
                                // проверочный режим: через пару секунд записать в журнал результат скрипта из файла (KAPSULA_BROWSERPROBE)
                                string probeFile = Environment.GetEnvironmentVariable("KAPSULA_BROWSERPROBE");
                                if (!string.IsNullOrEmpty(probeFile) && File.Exists(probeFile))
                                    core.NavigationCompleted += delegate
                                    {
                                        var pt = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                                        pt.Tick += delegate
                                        {
                                            pt.Stop();
                                            try { view.CoreWebView2.ExecuteScriptAsync(File.ReadAllText(probeFile)).ContinueWith(delegate(Task<string> r) { Log("probe " + (r.IsFaulted ? "failed" : r.Result)); }); } catch { }
                                        };
                                        pt.Start();
                                    };
                                core.Navigate(home);
                                Ready = true;
                                if (Changed != null) Changed();
                            }
                            catch (Exception ex) { Fail(ex); }
                        }, ui);
                    }
                    catch (Exception ex) { Fail(ex); }
                }, ui);
            }
            catch (Exception ex) { Fail(ex); }
        }

        void Fail(Exception ex)
        {
            Exception inner = ex;
            while (inner != null && inner.InnerException != null) inner = inner.InnerException;
            Error = inner is WebView2RuntimeNotFoundException ? "Не найден Microsoft Edge WebView2 Runtime." : "Встроенный браузер не запустился" + (inner != null ? ": " + inner.Message : ".");
            try { if (win != null) win.Hide(); } catch { }
            onScreen = false;
            if (Changed != null) Changed();
        }

        // Поставить окно на место (координаты — в точках экрана) и показать. Вызывается часто: если ничего не изменилось, ничего не делает.
        // visible — сколько точек по высоте сейчас видно. Пока панель раскрывается, растёт только само окно, а страница
        // внутри него сразу стоит в полном размере и просто открывается сверху вниз: она не перестраивается и не дёргается.
        int lastVisible = -1;

        public void Place(int x, int y, int w, int h, int visible, int radius, Color back)
        {
            if (Error != null || w < 40 || h < 40 || visible < 8) return;
            if (Environment.GetEnvironmentVariable("KAPSULA_RGNTEST") == "1") visible = h / 2;
            if (visible > h) visible = h;
            try
            {
                if (win == null) Build(back);
                if (h != lastH || w != lastW)
                {
                    // страница — всегда в полный размер панели, независимо от того, сколько окна сейчас видно
                    double k = 1;
                    var src = PresentationSource.FromVisual(win);
                    if (src != null && src.CompositionTarget != null) k = src.CompositionTarget.TransformFromDevice.M22;
                    view.Height = h * k;
                }
                if (x != lastX || y != lastY || w != lastW || visible != lastVisible)
                {
                    SetWindowPos(hwnd, IntPtr.Zero, x, y, w, visible, 0x0010 | 0x0004);   // не активировать, порядок окон не менять
                    lastX = x; lastY = y; lastVisible = visible;
                }
                lastW = w; lastH = h; lastR = radius;
                if (!win.IsVisible) win.Show();
                if (!onScreen) { onScreen = true; Fade(true); Log("show " + x + "," + y + " " + w + "x" + h); }
                Start();
            }
            catch (Exception ex) { Fail(ex); }
        }

        // Спрятать — значит увести окно за край экрана: страница остаётся загруженной и в следующий раз появляется сразу.
        public void Hide()
        {
            if (win == null || !onScreen) return;
            onScreen = false;
            Log("hide");   // страницу не гасим: при следующем показе она должна быть на месте сразу, без проявления
            SetWindowPos(hwnd, IntPtr.Zero, -20000, -20000, 0, 0, 0x0010 | 0x0004 | 0x0001);
            lastX = lastY = int.MinValue; lastVisible = -1;
        }

        // Загрузить страницу заранее, не показывая окно: к первому открытию вкладки она уже готова
        public void Warm(Color back)
        {
            if (Error != null || win != null) return;
            try { Build(back); win.Show(); Start(); } catch (Exception ex) { Fail(ex); }
        }

        void Fade(bool show)
        {
            try
            {
                if (!Ready || view.CoreWebView2 == null) return;
                view.CoreWebView2.ExecuteScriptAsync("window.__kapsulaOn=" + (show ? "true" : "false") + ";if(window.__kapsulaShow)window.__kapsulaShow(" + (show ? "true" : "false") + ")");
            }
            catch { }
        }

        // Вставить вопрос в поле ввода страницы и поставить туда курсор. Отправляет его сам человек — клавишей Enter.
        string pendingAsk;

        bool sendAsk;   // только для проверки (ключ --duckask): отправить вопрос самому

        public void Ask(string text) { Ask(text, false); }

        public void Ask(string text, bool send)
        {
            sendAsk = send;
            pendingAsk = text;
            FlushAsk();
            try { win.Activate(); view.Focus(); } catch { }
        }

        void FlushAsk()
        {
            if (pendingAsk == null || !Ready || view.CoreWebView2 == null) return;
            string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(pendingAsk);
            pendingAsk = null;
            try
            {
                // поле появляется не сразу — пробуем несколько секунд; значение ставим так, чтобы страница его «увидела»
                view.CoreWebView2.ExecuteScriptAsync("(function(t){var n=0,i=setInterval(function(){var a=document.querySelector('textarea');" +
                    "if(a){clearInterval(i);var d=Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value');d.set.call(a,t);" +
                    "a.dispatchEvent(new Event('input',{bubbles:true}));a.focus();" +
                    (sendAsk ? "setTimeout(function(){var f=a.closest('form');if(!f)return;var b=f.querySelector('button[type=submit]');if(b&&!b.disabled)b.click();else if(f.requestSubmit)f.requestSubmit()},500);" : "") +
                    "}else if(++n>40)clearInterval(i)},150)})(" + json + ")");
            }
            catch { }
        }

        public string Url { get { return home; } }

        // Открыть другой сайт в том же окне
        public void Go(string url)
        {
            if (url == home) return;
            home = url;
            try { if (Ready && view.CoreWebView2 != null) view.CoreWebView2.Navigate(url); } catch { }
        }

        public void GoHome()
        {
            try { if (Ready && view.CoreWebView2 != null) view.CoreWebView2.Navigate(home); } catch { }
        }
    }
}
