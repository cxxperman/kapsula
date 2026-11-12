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
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

        readonly Window owner;
        readonly string home;
        Window win;
        WebView2 view;
        IntPtr hwnd;
        int lastX, lastY, lastW, lastH, lastR;
        bool starting, onScreen;
        string backHex = "#000000";

        public bool Ready;          // страница загружается или уже загружена
        public string Error;        // почему браузер не запустился (нет WebView2 Runtime и т. п.)
        public bool Visible { get { return onScreen; } }
        public bool Active { get { return onScreen && win.IsActive; } }
        public Action Changed;      // состояние поменялось — остров перерисует подпись

        public MiniBrowser(Window owner, string home) { this.owner = owner; this.home = home; }

        void Build(Color back)
        {
            win = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
                ShowActivated = false, Owner = owner, Background = new SolidColorBrush(back), Title = "Kapsula — Duck.ai",
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 400, Height = 300
            };
            view = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, back.R, back.G, back.B) };
            win.Content = view;
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
                                    e.Handled = true;
                                    try { if (e.Uri.StartsWith("https://") || e.Uri.StartsWith("http://")) System.Diagnostics.Process.Start(e.Uri); } catch { }
                                };
                                // Страница красится в цвет острова и проявляется плавно — выглядит как часть панели, а не окно браузера.
                                // Содержимое и надписи самой страницы не трогаем.
                                core.AddScriptToExecuteOnDocumentCreatedAsync(
                                    "(function(){var s=new CSSStyleSheet();s.replaceSync('html,body,main,main>section{background:" + backHex + " !important}" +
                                    "html{opacity:0;transition:opacity .22s ease-out}html.kapsula-in{opacity:1}::-webkit-scrollbar{width:0;height:0}');" +
                                    "document.adoptedStyleSheets=document.adoptedStyleSheets.concat([s]);" +
                                    "window.__kapsulaShow=function(v){var h=document.documentElement;if(!h)return;if(!v){h.classList.remove('kapsula-in');return}" +
                                    "requestAnimationFrame(function(){requestAnimationFrame(function(){h.classList.add('kapsula-in')})})};" +
                                    "if(" + (onScreen ? "true" : "window.__kapsulaOn") + ")window.__kapsulaShow(true);" +
                                    "document.addEventListener('DOMContentLoaded',function(){if(window.__kapsulaOn)window.__kapsulaShow(true)})})();");
                                view.ZoomFactor = 0.9;
                                core.DOMContentLoaded += delegate { Fade(onScreen); };
                                core.NavigationCompleted += delegate { Fade(onScreen); FlushAsk(); };
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
        public void Place(int x, int y, int w, int h, int radius, Color back)
        {
            if (Error != null || w < 40 || h < 40) return;
            try
            {
                if (win == null) Build(back);
                if (x != lastX || y != lastY || w != lastW || h != lastH)
                {
                    SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, 0x0010 | 0x0004);   // не активировать, порядок окон не менять
                    lastX = x; lastY = y;
                }
                lastW = w; lastH = h; lastR = radius;
                if (!win.IsVisible) win.Show();
                if (!onScreen) { onScreen = true; Fade(true); }
                Start();
            }
            catch (Exception ex) { Fail(ex); }
        }

        // Спрятать — значит увести окно за край экрана: страница остаётся загруженной и в следующий раз появляется сразу.
        public void Hide()
        {
            if (win == null || !onScreen) return;
            onScreen = false;
            Fade(false);
            SetWindowPos(hwnd, IntPtr.Zero, -20000, -20000, 0, 0, 0x0010 | 0x0004 | 0x0001);
            lastX = lastY = int.MinValue;
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

        public void Ask(string text)
        {
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
                    "a.dispatchEvent(new Event('input',{bubbles:true}));a.focus()}else if(++n>40)clearInterval(i)},150)})(" + json + ")");
            }
            catch { }
        }

        public void GoHome()
        {
            try { if (Ready && view.CoreWebView2 != null) view.CoreWebView2.Navigate(home); } catch { }
        }
    }
}
