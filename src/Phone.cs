// Kapsula — «остров» для Windows. Часть исходного кода: Phone.cs
// Телефон как пульт: маленький веб-сервер только в домашней сети. Телефон открывает страницу по QR-коду
// (в адресе — случайный ключ), а дальше управляет музыкой, громкостью, слайдами, курсором и забирает файлы с полки.
// По умолчанию выключено; включается в настройках. Без ключа сервер отвечает «нет доступа».
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kapsula
{
    static class Phone
    {
        public const int Port = 42073;
        static TcpListener listener;
        static IslandWindow island;
        static readonly List<string> shared = new List<string>();
        public static bool Running { get { return listener != null; } }

        public static string Key { get { return Token; } }

        static string Token
        {
            get
            {
                string t = Store.Get("phoneToken");
                if (t.Length < 16)
                {
                    var b = new byte[12];
                    using (var r = System.Security.Cryptography.RandomNumberGenerator.Create()) r.GetBytes(b);
                    t = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
                    Store.Set("phoneToken", t);
                }
                return t;
            }
        }

        // Адрес компьютера в домашней сети (192.168.x.x, 10.x.x.x, 172.16–31.x.x)
        public static string LanIp()
        {
            string best = null;
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    string desc = (ni.Description + ni.Name).ToLowerInvariant();
                    if (desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("hyper-v") || desc.Contains("vpn") || desc.Contains("wsl")) continue;
                    var props = ni.GetIPProperties();
                    if (props.GatewayAddresses.Count == 0) continue;
                    foreach (var a in props.UnicastAddresses)
                    {
                        if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        byte[] b = a.Address.GetAddressBytes();
                        if (b[0] == 192 && b[1] == 168 || b[0] == 10 || b[0] == 172 && b[1] >= 16 && b[1] <= 31) { best = a.Address.ToString(); if (b[0] == 192) return best; }
                    }
                }
            }
            catch { }
            return best;
        }

        public static string Url()
        {
            string ip = LanIp();
            return ip == null ? null : "http://" + ip + ":" + Port + "/?k=" + Token;
        }

        public static void Start(IslandWindow w)
        {
            island = w;
            if (listener != null) return;
            try
            {
                listener = new TcpListener(IPAddress.Any, Port);
                listener.Start();
                var t = new Thread(Loop) { IsBackground = true, Name = "Phone" };
                t.Start();
            }
            catch (Exception ex) { listener = null; Store.LogError("Телефон: сервер", ex); }
        }

        public static void Stop()
        {
            try { if (listener != null) listener.Stop(); } catch { }
            listener = null;
        }

        static void Loop()
        {
            var l = listener;
            while (l != null && listener == l)
            {
                TcpClient c;
                try { c = l.AcceptTcpClient(); } catch { return; }
                // только домашняя сеть и сам компьютер
                var ep = c.Client.RemoteEndPoint as IPEndPoint;
                byte[] b = ep == null ? new byte[4] : ep.Address.MapToIPv4().GetAddressBytes();
                bool lan = b[0] == 127 || b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] >= 16 && b[1] <= 31;
                if (!lan) { c.Close(); continue; }
                ThreadPool.QueueUserWorkItem(delegate { Serve(c); });
            }
        }

        static void Serve(TcpClient c)
        {
            try
            {
                c.ReceiveTimeout = 5000; c.SendTimeout = 15000;
                var s = c.GetStream();
                var buf = new byte[8192];
                int n = s.Read(buf, 0, buf.Length);
                if (n <= 0) return;
                string head = Encoding.UTF8.GetString(buf, 0, n);
                int sp1 = head.IndexOf(' '), sp2 = sp1 < 0 ? -1 : head.IndexOf(' ', sp1 + 1);
                if (sp2 < 0) return;
                string target = head.Substring(sp1 + 1, sp2 - sp1 - 1);
                string path = target, query = "";
                int q = target.IndexOf('?');
                if (q >= 0) { path = target.Substring(0, q); query = target.Substring(q + 1); }
                var args = new Dictionary<string, string>();
                foreach (string kv in query.Split('&'))
                {
                    int e = kv.IndexOf('=');
                    if (e > 0) args[kv.Substring(0, e)] = Uri.UnescapeDataString(kv.Substring(e + 1).Replace('+', ' '));
                }
                string key;
                if (!args.TryGetValue("k", out key) || key != Token) { Send(s, 403, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Нет доступа: откройте страницу по QR-коду из Kapsula")); return; }
                if (path == "/") { Send(s, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page())); var iw = island; if (iw != null) iw.Dispatcher.BeginInvoke(new Action(iw.HideQrInIsland)); return; }
                if (path == "/stats") { Send(s, 200, "application/json", Encoding.UTF8.GetBytes(Stats())); return; }
                if (path == "/file")
                {
                    int i;
                    string f = null;
                    lock (shared) if (args.ContainsKey("i") && int.TryParse(args["i"], out i) && i >= 0 && i < shared.Count) f = shared[i];
                    if (f == null || !System.IO.File.Exists(f)) { Send(s, 404, "text/plain", new byte[0]); return; }
                    SendFile(s, f);
                    return;
                }
                if (path == "/do")
                {
                    string a; args.TryGetValue("a", out a);
                    string v; args.TryGetValue("v", out v);
                    Do(a ?? "", v ?? "");
                    Send(s, 200, "text/plain", Encoding.UTF8.GetBytes("ok"));
                    return;
                }
                Send(s, 404, "text/plain", new byte[0]);
            }
            catch { }
            finally { try { c.Close(); } catch { } }
        }

        static void Send(System.IO.Stream s, int code, string type, byte[] body)
        {
            string h = "HTTP/1.1 " + code + (code == 200 ? " OK" : code == 403 ? " Forbidden" : " Not Found") + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
                "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(h);
            s.Write(hb, 0, hb.Length);
            s.Write(body, 0, body.Length);
        }

        static void SendFile(System.IO.Stream s, string f)
        {
            var fi = new System.IO.FileInfo(f);
            string name = Uri.EscapeDataString(fi.Name);
            string h = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: " + fi.Length + "\r\nContent-Disposition: attachment; filename*=UTF-8''" + name + "\r\nConnection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(h);
            s.Write(hb, 0, hb.Length);
            using (var fs = System.IO.File.OpenRead(f)) fs.CopyTo(s);
        }

        // ---- команды с телефона
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; public int pad1, pad2; }
        [StructLayout(LayoutKind.Explicit)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mi; [FieldOffset(8)] public KEYBDINPUT ki; }
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);

        static void Mouse(int dx, int dy, uint flags, uint data)
        {
            var i = new INPUT { type = 0 };
            i.mi.dx = dx; i.mi.dy = dy; i.mi.flags = flags; i.mi.data = data;
            SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT)));
        }

        static void TypeText(string text)
        {
            var list = new List<INPUT>();
            foreach (char ch in text)
            {
                if (ch == '\n') { list.Add(KeyIn(0x0D, (char)0, 0)); list.Add(KeyIn(0x0D, (char)0, 2)); continue; }
                list.Add(KeyIn(0, ch, 4)); list.Add(KeyIn(0, ch, 4 | 2));   // KEYEVENTF_UNICODE
            }
            if (list.Count > 0) SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        static INPUT KeyIn(ushort vk, char ch, uint flags)
        {
            var i = new INPUT { type = 1 };
            i.ki.vk = vk; i.ki.scan = ch; i.ki.flags = flags;
            return i;
        }

        static readonly string[] GameBlocked = { "move", "click", "rclick", "scroll", "type", "key", "slidenext", "slideprev" };

        static void Do(string a, string v)
        {
            int x = 0, y = 0;
            string[] xy = v.Split(',');
            if (xy.Length == 2) { int.TryParse(xy[0], out x); int.TryParse(xy[1], out y); }
            // Во время игры телефон не двигает курсор и не нажимает клавиши: античит мог бы принять такие
            // нажатия от программы за макрос. Музыка, громкость, блокировка и файлы работают как обычно.
            if (island != null && island.InGame && Array.IndexOf(GameBlocked, a) >= 0)
            {
                island.Dispatcher.BeginInvoke(new Action(delegate { island.PhoneInputBlocked(); }));
                return;
            }
            switch (a)
            {
                case "play": island.Dispatcher.BeginInvoke(new Action(delegate { island.PhoneMedia(0xB3); })); break;
                case "next": island.Dispatcher.BeginInvoke(new Action(delegate { island.PhoneMedia(0xB0); })); break;
                case "prev": island.Dispatcher.BeginInvoke(new Action(delegate { island.PhoneMedia(0xB1); })); break;
                case "volup": Native.MediaKey(0xAF); Native.MediaKey(0xAF); break;
                case "voldown": Native.MediaKey(0xAE); Native.MediaKey(0xAE); break;
                case "mute": Native.MediaKey(0xAD); break;
                case "slidenext": Native.Key(0x27, false); Native.Key(0x27, true); break;
                case "slideprev": Native.Key(0x25, false); Native.Key(0x25, true); break;
                case "move": Mouse(Math.Max(-200, Math.Min(200, x)), Math.Max(-200, Math.Min(200, y)), 0x0001, 0); break;
                case "click": Mouse(0, 0, 0x0002, 0); Mouse(0, 0, 0x0004, 0); break;
                case "rclick": Mouse(0, 0, 0x0008, 0); Mouse(0, 0, 0x0010, 0); break;
                case "scroll": Mouse(0, 0, 0x0800, (uint)(-y * 4)); break;
                case "type": if (v.Length > 0 && v.Length <= 2000) TypeText(v); break;
                case "key":
                    {
                        byte vk = v == "enter" ? (byte)0x0D : v == "back" ? (byte)0x08 : v == "esc" ? (byte)0x1B : v == "tab" ? (byte)0x09 : v == "space" ? (byte)0x20 : (byte)0;
                        if (vk != 0) { Native.Key(vk, false); Native.Key(vk, true); }
                        break;
                    }
                case "app": { int i; if (int.TryParse(v, out i)) island.Dispatcher.BeginInvoke(new Action(delegate { island.RunDeckSlot(i); })); break; }
                case "lock": Native.LockWorkStation(); break;
                case "sleep": Native.SetSuspendState(false, false, false); break;
                case "shutdown": try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/s /t 30") { CreateNoWindow = true, UseShellExecute = false }); } catch { } break;
                case "cancelshutdown": try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true, UseShellExecute = false }); } catch { } break;
            }
        }

        static ulong lastIdle, lastTotal;
        static string Stats()
        {
            double cpu = 0;
            Native.FILETIME i, k, u;
            if (Native.GetSystemTimes(out i, out k, out u))
            {
                ulong idle = Native.ToU64(i), total = Native.ToU64(k) + Native.ToU64(u);
                if (lastTotal != 0 && total > lastTotal) cpu = 1.0 - (double)(idle - lastIdle) / (total - lastTotal);
                lastIdle = idle; lastTotal = total;
            }
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
            Native.GlobalMemoryStatusEx(ref m);
            string track = "";
            try { track = island.Dispatcher.Invoke(new Func<string>(island.PhoneTrack)) as string ?? ""; } catch { }
            var files = new StringBuilder();
            lock (shared)
                for (int n = 0; n < shared.Count; n++) files.Append(n > 0 ? "," : "").Append(Json(System.IO.Path.GetFileName(shared[n])));
            var apps = new StringBuilder();
            try
            {
                string[] names = island.Dispatcher.Invoke(new Func<string[]>(island.DeckNames)) as string[];
                if (names != null) for (int n = 0; n < names.Length; n++) apps.Append(n > 0 ? "," : "").Append(Json(names[n]));
            }
            catch { }
            return "{\"cpu\":" + Math.Round(cpu * 100) + ",\"ram\":" + m.dwMemoryLoad + ",\"track\":" + Json(track) + ",\"files\":[" + files + "],\"apps\":[" + apps + "]}";
        }

        static string Json(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char ch in s ?? "")
            {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < 32 || ch == '<' || ch == '>') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                else sb.Append(ch);
            }
            return sb.Append('"').ToString();
        }

        static string Page()
        {
            return @"<!doctype html><html lang=ru><head><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1,user-scalable=no'>
<title>Kapsula</title><style>
:root{color-scheme:dark}*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
body{margin:0;background:#0b0b0d;color:#f2f2f5;font:15px system-ui,-apple-system,Segoe UI,sans-serif;padding:14px 16px 40px}
h1{font-size:20px;margin:6px 0 2px}.sub{color:#8a8a93;font-size:13px;margin-bottom:14px}
.tabs{display:flex;gap:4px;background:#18181c;border-radius:14px;padding:4px;margin-bottom:14px;overflow-x:auto}
.tabs b{flex:1;text-align:center;padding:9px 8px;border-radius:10px;color:#9a9aa3;font-weight:600;font-size:13px;white-space:nowrap}
.tabs b.on{background:#2a2a30;color:#fff}
.card{background:#151518;border:1px solid #26262c;border-radius:18px;padding:14px;margin-bottom:12px}
.row{display:flex;gap:10px}.row>*{flex:1}
button{background:#232328;color:#f2f2f5;border:0;border-radius:14px;padding:16px 8px;font:600 15px system-ui;width:100%}
button:active{background:#34343b;transform:scale(.97)}button.big{padding:26px 8px;font-size:22px}button.red{background:#3a1d1d;color:#ff8a80}
#pad{height:300px;border-radius:18px;background:#1a1a1f;border:1px solid #2a2a31;touch-action:none;display:flex;align-items:center;justify-content:center;color:#55555d}
input{width:100%;padding:14px;border-radius:14px;border:1px solid #2a2a31;background:#141417;color:#fff;font-size:16px}
.track{font-weight:600;margin-bottom:10px;min-height:20px}.bar{height:6px;border-radius:3px;background:#26262c;overflow:hidden;margin:6px 0 12px}.bar i{display:block;height:100%;background:#f2f2f5}
a.file{display:block;padding:13px;border-radius:12px;background:#1d1d22;color:#fff;text-decoration:none;margin-bottom:6px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.sec{display:none}.sec.on{display:block}.muted{color:#77777f;font-size:13px}
</style></head><body>
<h1>Kapsula</h1><div class=sub>Пульт для компьютера · только в вашей сети</div>
<div class=tabs id=tabs><b data-s=music class=on>Музыка</b><b data-s=pad>Курсор</b><b data-s=keys>Текст</b><b data-s=slides>Слайды</b><b data-s=apps>Программы</b><b data-s=files>Файлы</b><b data-s=pc>Компьютер</b></div>
<div class='sec on' id=music><div class=card><div class=track id=track>—</div>
<div class=row><button onclick=d('prev')>⏮</button><button class=big onclick=d('play')>⏯</button><button onclick=d('next')>⏭</button></div></div>
<div class=card><div class=row><button onclick=d('voldown')>Тише</button><button onclick=d('mute')>Без звука</button><button onclick=d('volup')>Громче</button></div></div></div>
<div class=sec id=pad_><div id=pad>Водите пальцем · касание — клик</div><div class=row style='margin-top:10px'><button onclick=d('click')>Левая</button><button onclick=d('rclick')>Правая</button></div>
<div class=muted style='margin-top:8px'>Двумя пальцами — прокрутка</div></div>
<div class=sec id=keys><div class=card><input id=txt placeholder='Текст для компьютера' enterkeyhint=send>
<div class=row style='margin-top:10px'><button onclick=sendTxt()>Отправить</button><button onclick=d('key','enter')>Enter</button><button onclick=d('key','back')>⌫</button></div></div></div>
<div class=sec id=slides><div class=row><button class=big onclick=d('slideprev')>←</button><button class=big onclick=d('slidenext')>→</button></div></div>
<div class=sec id=apps><div class=card id=applist><div class=muted>Положите программы в «Деку» на острове</div></div></div>
<div class=sec id=files><div class=card id=filelist><div class=muted>Положите файлы на полку острова и нажмите «На телефон»</div></div></div>
<div class=sec id=pc><div class=card>Процессор <span id=cpu></span><div class=bar><i id=cpub></i></div>Память <span id=ram></span><div class=bar><i id=ramb></i></div></div>
<div class=card><div class=row><button onclick=d('lock')>Заблокировать</button><button onclick=conf('sleep','Отправить компьютер в сон?')>Сон</button></div>
<div class=row style='margin-top:10px'><button class=red onclick=conf('shutdown','Выключить компьютер через 30 секунд?')>Выключить</button><button onclick=d('cancelshutdown')>Отменить</button></div></div></div>
<script>
var K=new URLSearchParams(location.search).get('k');
function d(a,v){fetch('/do?k='+K+'&a='+a+(v!==undefined?'&v='+encodeURIComponent(v):''))}
function conf(a,t){if(confirm(t))d(a)}
function sendTxt(){var t=document.getElementById('txt');if(t.value){d('type',t.value);t.value=''}}
document.getElementById('txt').addEventListener('keydown',function(e){if(e.key==='Enter'){e.preventDefault();sendTxt()}});
document.querySelectorAll('#tabs b').forEach(function(b){b.onclick=function(){document.querySelectorAll('#tabs b').forEach(function(x){x.classList.remove('on')});b.classList.add('on');
document.querySelectorAll('.sec').forEach(function(s){s.classList.remove('on')});var s=b.dataset.s;document.getElementById(s==='pad'?'pad_':s).classList.add('on')}});
var pad=document.getElementById('pad'),lx=0,ly=0,moved=0,acc=[0,0],t0=0,two=false,q=null;
pad.addEventListener('touchstart',function(e){var t=e.touches[0];lx=t.clientX;ly=t.clientY;moved=0;t0=Date.now();two=e.touches.length>1;e.preventDefault()},{passive:false});
pad.addEventListener('touchmove',function(e){var t=e.touches[0],dx=t.clientX-lx,dy=t.clientY-ly;lx=t.clientX;ly=t.clientY;moved+=Math.abs(dx)+Math.abs(dy);
acc[0]+=dx*1.8;acc[1]+=dy*1.8;two=e.touches.length>1;if(!q)q=setTimeout(flush,30);e.preventDefault()},{passive:false});
function flush(){q=null;var x=Math.round(acc[0]),y=Math.round(acc[1]);acc=[0,0];if(!x&&!y)return;if(two)d('scroll','0,'+Math.round(y/3));else d('move',x+','+y)}
pad.addEventListener('touchend',function(e){if(moved<8&&Date.now()-t0<300)d('click');e.preventDefault()},{passive:false});
function esc(s){var e=document.createElement('i');e.textContent=s;return e.innerHTML}
function poll(){fetch('/stats?k='+K).then(function(r){return r.json()}).then(function(j){
document.getElementById('track').textContent=j.track||'Ничего не играет';
document.getElementById('cpu').textContent=j.cpu+'%';document.getElementById('cpub').style.width=j.cpu+'%';
document.getElementById('ram').textContent=j.ram+'%';document.getElementById('ramb').style.width=j.ram+'%';
if(j.files.length){document.getElementById('filelist').innerHTML=j.files.map(function(f,i){return ""<a class=file href='/file?k=""+K+""&i=""+i+""'>⬇ ""+esc(f)+""</a>""}).join('')}
if(j.apps.length){document.getElementById('applist').innerHTML=j.apps.map(function(f,i){return ""<button style='margin-bottom:6px' onclick=d('app',""+i+"")>""+esc(f)+""</button>""}).join('')}
}).catch(function(){})}
poll();setInterval(poll,2500);
</script></body></html>";
        }

        // ---- полка → телефон
        public static void Share(IslandWindow w, string[] paths)
        {
            lock (shared)
            {
                shared.Clear();
                foreach (string p in paths) if (System.IO.File.Exists(p)) shared.Add(p);
            }
            ShowQr(w, shared.Count == 0 ? "Папки передать нельзя — только файлы" : null);
        }

        // Окошко с QR-кодом. Если пульт выключен — предлагаем включить.
        public static void ShowQr(IslandWindow w, string note)
        {
            if (!Running)
            {
                if (MessageBox.Show("Включить пульт с телефона?\n\nKapsula откроет страницу только для устройств в вашей домашней сети и только по секретной ссылке из QR-кода. Windows может спросить разрешение для брандмауэра — разрешите доступ в частных сетях.\n\nВыключить можно в настройках → «Телефон».", "Kapsula", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                Store.Set("phoneRemote", "1");
                Start(w);
            }
            string url = Url();
            if (url == null) { MessageBox.Show("Компьютер не подключён к домашней сети — телефону не к чему подключиться.", "Kapsula"); return; }
            var lines = new List<string> { "Телефон — в той же сети Wi-Fi. Ссылка секретная — не пересылайте её; новую можно получить в настройках → «Телефон»." };
            lock (shared) if (shared.Count > 0) lines.Add("Файлов к передаче: " + shared.Count + " — вкладка «Файлы» на телефоне.");
            if (note != null) lines.Add(note);
            island = w;
            w.ShowQrInIsland(url, "Пульт с телефона", lines.ToArray());
        }

        // Окно с QR-кодом для любой ссылки
        public static void QrWindow(string url, string title, string[] lines)
        {
            if (island != null) { island.ShowQrInIsland(url, title, lines); return; }
            var win = new Window
            {
                Title = "Kapsula — " + title, Width = 360, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x17)), Topmost = true, ShowInTaskbar = false
            };
            var sp = new StackPanel { Margin = new Thickness(22) };
            sp.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
            var qrImg = new Image { Source = QrMake.Render(url, 8), Width = 260, Height = 260 };
            RenderOptions.SetBitmapScalingMode(qrImg, BitmapScalingMode.NearestNeighbor);
            sp.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(16), Padding = new Thickness(12), Child = qrImg, HorizontalAlignment = HorizontalAlignment.Center });
            foreach (string l in lines)
                sp.Children.Add(new TextBlock { Text = l, Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB8)), FontSize = 12.5, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
            win.Content = sp;
            win.Deactivated += delegate { try { win.Close(); } catch { } };
            win.Show();
            win.Activate();
        }

        public static void NewToken() { Store.Set("phoneToken", ""); string t = Token; }
    }

    // ---- QR-код: байтовый режим, уровень коррекции L, версии 1–6 (до 134 байт — для ссылки хватает)
    static class QrMake
    {
        static readonly int[] DataCw = { 0, 19, 34, 55, 80, 108, 136 };
        static readonly int[] EcCw = { 0, 7, 10, 15, 20, 26, 18 };     // на блок
        static readonly int[] Blocks = { 0, 1, 1, 1, 1, 1, 2 };
        static readonly int[] Align = { 0, 0, 18, 22, 26, 30, 34 };

        public static bool[,] Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            int ver = 0;
            for (int v = 1; v <= 6; v++) if (data.Length + 2 <= DataCw[v]) { ver = v; break; }
            if (ver == 0) throw new ArgumentException("Слишком длинный текст для QR");
            int size = 17 + ver * 4;
            // биты данных
            var bits = new List<bool>();
            Action<int, int> put = delegate(int val, int len) { for (int i = len - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); };
            put(4, 4); put(data.Length, 8);
            foreach (byte b in data) put(b, 8);
            int cap = DataCw[ver] * 8;
            put(0, Math.Min(4, cap - bits.Count));
            while (bits.Count % 8 != 0) bits.Add(false);
            var cw = new List<byte>();
            for (int i = 0; i < bits.Count; i += 8) { int x = 0; for (int j = 0; j < 8; j++) x = x << 1 | (bits[i + j] ? 1 : 0); cw.Add((byte)x); }
            for (int p = 0; cw.Count < DataCw[ver]; p++) cw.Add(p % 2 == 0 ? (byte)0xEC : (byte)0x11);
            // блоки и коррекция ошибок, чередование
            int nb = Blocks[ver], per = DataCw[ver] / nb, ec = EcCw[ver];
            byte[] div = RsDivisor(ec);
            var blocks = new List<byte[]>(); var ecs = new List<byte[]>();
            for (int b = 0; b < nb; b++) { byte[] blk = cw.GetRange(b * per, per).ToArray(); blocks.Add(blk); ecs.Add(RsRemainder(blk, div)); }
            var all = new List<byte>();
            for (int i = 0; i < per; i++) foreach (byte[] blk in blocks) all.Add(blk[i]);
            for (int i = 0; i < ec; i++) foreach (byte[] e in ecs) all.Add(e[i]);

            var mod = new bool[size, size]; var fn = new bool[size, size];
            Action<int, int, bool> set = delegate(int x, int y, bool dark) { mod[y, x] = dark; fn[y, x] = true; };
            for (int i = 0; i < size; i++) { set(6, i, i % 2 == 0); set(i, 6, i % 2 == 0); }
            Action<int, int> finder = delegate(int cx, int cy)
            {
                for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++)
                {
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy)), x = cx + dx, y = cy + dy;
                    if (x >= 0 && x < size && y >= 0 && y < size) set(x, y, d != 2 && d != 4);
                }
            };
            finder(3, 3); finder(size - 4, 3); finder(3, size - 4);
            if (ver >= 2)
            {
                int a = Align[ver];
                for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) set(a + dx, a + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
            DrawFormat(set, size, 0);   // резервируем место
            // раскладка данных змейкой
            int bit = 0;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool up = ((right + 1) & 2) == 0;
                        int y = up ? size - 1 - vert : vert;
                        if (fn[y, x] || bit >= all.Count * 8) continue;
                        mod[y, x] = ((all[bit >> 3] >> (7 - (bit & 7))) & 1) != 0;
                        bit++;
                    }
            }
            // маска: выбираем с наименьшим штрафом
            int bestMask = 0; long bestScore = long.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                ApplyMask(mod, fn, size, m); DrawFormat(set, size, m);
                long sc = Penalty(mod, size);
                if (sc < bestScore) { bestScore = sc; bestMask = m; }
                ApplyMask(mod, fn, size, m);
            }
            ApplyMask(mod, fn, size, bestMask); DrawFormat(set, size, bestMask);
            return mod;
        }

        static void DrawFormat(Action<int, int, bool> set, int size, int mask)
        {
            int data = 1 << 3 | mask;   // уровень L = 01
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            Func<int, bool> g = delegate(int i) { return ((bits >> i) & 1) != 0; };
            for (int i = 0; i <= 5; i++) set(8, i, g(i));
            set(8, 7, g(6)); set(8, 8, g(7)); set(7, 8, g(8));
            for (int i = 9; i < 15; i++) set(14 - i, 8, g(i));
            for (int i = 0; i < 8; i++) set(size - 1 - i, 8, g(i));
            for (int i = 8; i < 15; i++) set(8, size - 15 + i, g(i));
            set(8, size - 8, true);
        }

        static void ApplyMask(bool[,] mod, bool[,] fn, int size, int m)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (fn[y, x]) continue;
                    bool inv;
                    switch (m)
                    {
                        case 0: inv = (x + y) % 2 == 0; break;
                        case 1: inv = y % 2 == 0; break;
                        case 2: inv = x % 3 == 0; break;
                        case 3: inv = (x + y) % 3 == 0; break;
                        case 4: inv = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: inv = x * y % 2 + x * y % 3 == 0; break;
                        case 6: inv = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: inv = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (inv) mod[y, x] = !mod[y, x];
                }
        }

        // упрощённый штраф: длинные ряды одного цвета, квадраты 2×2, баланс тёмного и светлого
        static long Penalty(bool[,] m, int size)
        {
            long p = 0; int dark = 0;
            for (int y = 0; y < size; y++)
            {
                int runX = 1, runY = 1;
                for (int x = 0; x < size; x++)
                {
                    if (m[y, x]) dark++;
                    if (x > 0) { if (m[y, x] == m[y, x - 1]) { runX++; if (runX == 5) p += 3; else if (runX > 5) p++; } else runX = 1; }
                    if (x > 0) { if (m[x, y] == m[x - 1, y]) { runY++; if (runY == 5) p += 3; else if (runY > 5) p++; } else runY = 1; }
                    if (x > 0 && y > 0 && m[y, x] == m[y - 1, x] && m[y, x] == m[y, x - 1] && m[y, x] == m[y - 1, x - 1]) p += 3;
                }
            }
            int total = size * size;
            p += Math.Abs(dark * 20 - total * 10) / total * 10;
            return p;
        }

        static byte Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--) { z = (z << 1) ^ ((z >> 7) * 0x11D); z ^= ((y >> i) & 1) * x; }
            return (byte)z;
        }

        static byte[] RsDivisor(int degree)
        {
            var r = new byte[degree]; r[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++) { r[j] = Mul(r[j], root); if (j + 1 < degree) r[j] ^= r[j + 1]; }
                root = Mul(root, 2);
            }
            return r;
        }

        static byte[] RsRemainder(byte[] data, byte[] div)
        {
            var r = new byte[div.Length];
            foreach (byte b in data)
            {
                int factor = b ^ r[0];
                Array.Copy(r, 1, r, 0, r.Length - 1); r[r.Length - 1] = 0;
                for (int i = 0; i < r.Length; i++) r[i] ^= Mul(div[i], factor);
            }
            return r;
        }

        // Картинка: scale точек на модуль, поля в 4 модуля
        public static BitmapSource Render(string text, int scale)
        {
            bool[,] m = Encode(text);
            int n = m.GetLength(0), full = (n + 8) * scale;
            var px = new byte[full * full];
            for (int i = 0; i < px.Length; i++) px[i] = 255;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (m[y, x])
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++) px[((y + 4) * scale + dy) * full + (x + 4) * scale + dx] = 0;
            var bmp = BitmapSource.Create(full, full, 96, 96, PixelFormats.Gray8, null, px, full);
            bmp.Freeze();
            return bmp;
        }
    }
}
