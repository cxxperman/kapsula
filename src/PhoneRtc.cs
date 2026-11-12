// Kapsula — «остров» для Windows. Часть исходного кода: PhoneRtc.cs
// Приём видео с телефона по WebRTC — так, как это делают рабочие проекты (VDO.Ninja): телефон сжимает видео
// аппаратным кодеком H.264 прямо в браузере, а здесь его принимает и расшифровывает невидимый Chromium (WebView2).
// Расшифрованный кадр страница кладёт в общий с Kapsula буфер памяти, а Kapsula — в память камеры «Kapsula Camera».
// Работает в отдельном потоке со своим циклом сообщений, чтобы приём видео и анимации острова не мешали друг другу.
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Kapsula
{
    static class PhoneRtc
    {
        static Thread thread;
        static Dispatcher disp;
        static CoreWebView2 core;
        static CoreWebView2SharedBuffer buf;
        static volatile bool ready;
        static readonly object answerLock = new object();
        static string answer;
        static ManualResetEvent answerEvent = new ManualResetEvent(false);
        public static volatile int Connected;   // 1 — телефон передаёт видео по WebRTC

        public static void Start()
        {
            if (thread != null) return;
            thread = new Thread(Run) { IsBackground = true, Name = "PhoneRtc" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        static void Run()
        {
            try
            {
                disp = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(disp));
                // окно за краем экрана: страница считается видимой и не притормаживается, но никому не мешает и не берёт фокус
                var win = new Window { Width = 320, Height = 240, Left = -10000, Top = -10000, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Focusable = false, Title = "Kapsula camera receiver" };
                var view = new Microsoft.Web.WebView2.Wpf.WebView2();
                win.Content = view;
                win.SourceInitialized += delegate
                {
                    IntPtr h = new System.Windows.Interop.WindowInteropHelper(win).Handle;
                    SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 | 0x08000000);   // служебное, без активации
                };
                win.Show();
                CoreWebView2Environment.SetLoaderDllFolderPath(WebFiles.Dir);
                // настоящие адреса в домашней сети вместо скрытых *.local — телефону проще найти компьютер
                var opts = new CoreWebView2EnvironmentOptions("--disable-features=WebRtcHideLocalIpsWithMdns --autoplay-policy=no-user-gesture-required");
                string data = Path.Combine(Store.Dir, "webview-cam");
                var ui = System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext();
                CoreWebView2Environment.CreateAsync(null, data, opts).ContinueWith(delegate(System.Threading.Tasks.Task<CoreWebView2Environment> te)
                {
                    if (te.IsFaulted) { PhoneCam.Log("WebRTC: не запустился браузер — " + te.Exception.InnerException.Message); return; }
                    CoreWebView2Environment env = te.Result;
                    view.EnsureCoreWebView2Async(env).ContinueWith(delegate(System.Threading.Tasks.Task t2)
                    {
                        if (t2.IsFaulted) { PhoneCam.Log("WebRTC: " + t2.Exception.InnerException.Message); return; }
                        core = view.CoreWebView2;
                        buf = env.CreateSharedBuffer((ulong)PhoneCam.MapBytes);
                        core.WebMessageReceived += OnMessage;
                        core.NavigationCompleted += delegate
                        {
                            core.PostSharedBufferToScript(buf, CoreWebView2SharedBufferAccess.ReadWrite, null);
                            ready = true;
                            PhoneCam.Log("WebRTC: приёмник готов");
                        };
                        string dir = Path.Combine(Store.Dir, "camrx");
                        Directory.CreateDirectory(dir);
                        File.WriteAllText(Path.Combine(dir, "rx.html"), Page, new UTF8Encoding(false));
                        core.SetVirtualHostNameToFolderMapping("camrx.kapsula", dir, CoreWebView2HostResourceAccessKind.Allow);
                        core.Navigate("https://camrx.kapsula/rx.html");
                    }, ui);
                }, ui);
                Dispatcher.Run();
            }
            catch (Exception ex) { PhoneCam.Log("WebRTC: " + ex.Message); }
        }

        // Проверка без телефона (ключ --camsender секунды файл): невидимый Chromium с тестовой камерой открывает
        // ту же страницу, что и телефон, включает камеру и через заданное время пишет в файл строку состояния страницы
        public static void TestSender(int seconds, string outFile)
        {
            var win = new Window { Width = 400, Height = 300, Left = -9000, Top = -9000, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false };
            var view = new Microsoft.Web.WebView2.Wpf.WebView2();
            win.Content = view; win.Show();
            CoreWebView2Environment.SetLoaderDllFolderPath(WebFiles.Dir);
            var opts = new CoreWebView2EnvironmentOptions("--use-fake-device-for-media-stream --use-fake-ui-for-media-stream --ignore-certificate-errors");
            var ui = System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext();
            CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "kapsula-camsender"), opts).ContinueWith(delegate(System.Threading.Tasks.Task<CoreWebView2Environment> te)
            {
                view.EnsureCoreWebView2Async(te.Result).ContinueWith(delegate(System.Threading.Tasks.Task t2)
                {
                    var c = view.CoreWebView2;
                    c.PermissionRequested += delegate(object o, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Allow; };
                    c.ServerCertificateErrorDetected += delegate(object o, CoreWebView2ServerCertificateErrorDetectedEventArgs e) { e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow; };
                    bool started = false;
                    c.NavigationCompleted += delegate
                    {
                        if (started) return; started = true;
                        c.ExecuteScriptAsync("start()");
                        var tm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                        tm.Tick += delegate
                        {
                            tm.Stop();
                            c.ExecuteScriptAsync("document.getElementById('st').textContent").ContinueWith(delegate(System.Threading.Tasks.Task<string> r)
                            {
                                try { File.WriteAllText(outFile, r.Result); } catch { }
                                Environment.Exit(0);
                            }, ui);
                        };
                        tm.Start();
                    };
                    c.Navigate("https://127.0.0.1:" + PhoneCam.Port + "/?k=" + Phone.Key);
                }, ui);
            }, ui);
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        // Предложение телефона → ответ компьютера (вызывается из потока веб-сервера; ждёт до 10 секунд)
        public static string Answer(string offer)
        {
            for (int i = 0; i < 100 && !ready; i++) Thread.Sleep(100);   // приёмник ещё запускается
            if (!ready) return null;
            lock (answerLock)
            {
                answer = null;
                answerEvent.Reset();
                disp.BeginInvoke(new Action(delegate { core.PostWebMessageAsString("offer\n" + offer); }));
                answerEvent.WaitOne(10000);
                return answer;
            }
        }

        static void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string m;
            try { m = e.TryGetWebMessageAsString(); } catch { return; }
            if (m == null) return;
            if (m.StartsWith("answer\n")) { answer = m.Substring(7); answerEvent.Set(); return; }
            if (m.StartsWith("f "))
            {
                string[] p = m.Split(' ');
                int w, h;
                if (p.Length < 4 || !int.TryParse(p[1], out w) || !int.TryParse(p[2], out h)) { core.PostWebMessageAsString("ok"); return; }
                bool rgba = p[3] == "1";
                IntPtr ptr = buf.Buffer;
                // копируем не в потоке приёмника, чтобы он сразу брался за следующий кадр
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { PhoneCam.WriteRaw(new IntPtr(ptr.ToInt64() + 64), w, h, rgba); } catch { }
                    disp.BeginInvoke(new Action(delegate { try { core.PostWebMessageAsString("ok"); } catch { } }));
                });
                return;
            }
            if (m.StartsWith("state "))
            {
                string st = m.Substring(6);
                PhoneCam.Log("WebRTC: " + st);
                bool on = st == "connected";
                int was = Connected;
                Connected = on ? 1 : (st == "connecting" || st == "new" ? Connected : 0);
                if (was != Connected) PhoneCam.RtcChanged(Connected == 1);
                return;
            }
            if (m.StartsWith("log ")) PhoneCam.Log("WebRTC: " + m.Substring(4));
        }

        const string Page = @"<!doctype html><html><head><meta charset=utf-8></head><body style='margin:0;background:#000'><video id=v muted playsinline autoplay style='width:320px;height:240px'></video><script>
var buf=null,pc=null,busy=false,v=document.getElementById('v'),useCanvas=false,cv=null,cx=null,n=0,t0=performance.now(),cbOn=false;
chrome.webview.addEventListener('sharedbufferreceived',function(e){buf=e.getBuffer();});
chrome.webview.addEventListener('message',function(e){var d=e.data;if(typeof d!=='string')return;
 if(d==='ok'){busy=false;return}
 if(d.indexOf('offer\n')===0)accept(d.substring(6)).catch(function(x){chrome.webview.postMessage('log offer: '+x)});});
async function accept(sdp){
 if(pc){try{pc.close()}catch(x){}}
 var my=pc=new RTCPeerConnection({iceServers:[]});
 my.ontrack=function(ev){v.srcObject=ev.streams&&ev.streams[0]?ev.streams[0]:new MediaStream([ev.track]);v.play().catch(function(){});
  try{var r=my.getReceivers()[0];if(r&&'jitterBufferTarget' in r)r.jitterBufferTarget=0;}catch(x){}
  if(!cbOn){cbOn=true;v.requestVideoFrameCallback(onFrame);}};
 my.onconnectionstatechange=function(){if(my===pc)chrome.webview.postMessage('state '+my.connectionState)};
 await my.setRemoteDescription({type:'offer',sdp:sdp});
 await my.setLocalDescription(await my.createAnswer());
 await new Promise(function(r){if(my.iceGatheringState==='complete')return r();var t=setTimeout(r,1500);my.onicegatheringstatechange=function(){if(my.iceGatheringState==='complete'){clearTimeout(t);r()}}});
 chrome.webview.postMessage('answer\n'+my.localDescription.sdp);
}
async function onFrame(now){
 v.requestVideoFrameCallback(onFrame);
 if(busy||!buf)return;
 var w=v.videoWidth,h=v.videoHeight;if(!w||w*h*4+64>buf.byteLength)return;
 busy=true;
 try{
  if(!useCanvas){var f=new VideoFrame(v,{timestamp:Math.round(now*1000)});
   try{await f.copyTo(buf,{format:'BGRX',layout:[{offset:64,stride:w*4}]});}finally{f.close();}
   chrome.webview.postMessage('f '+w+' '+h+' 0');}
  else{if(!cv){cv=new OffscreenCanvas(w,h);cx=cv.getContext('2d',{willReadFrequently:true});}
   if(cv.width!==w||cv.height!==h){cv.width=w;cv.height=h;}
   cx.drawImage(v,0,0,w,h);new Uint8Array(buf,64,w*h*4).set(cx.getImageData(0,0,w,h).data);
   chrome.webview.postMessage('f '+w+' '+h+' 1');}
 }catch(e){busy=false;if(!useCanvas){useCanvas=true;chrome.webview.postMessage('log copyTo не сработал ('+e.name+'), беру кадры через canvas');}}
 n++;if(now-t0>=5000){chrome.webview.postMessage('log приём '+Math.round(n*1000/(now-t0))+' к/с, '+w+'x'+h);n=0;t0=now;}
}
</script></body></html>";
    }
}
