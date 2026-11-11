// Kapsula — «остров» для Windows. Часть исходного кода: Integrations.cs
// Связь с другими программами: Discord (через Vencord) и Steam.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Kapsula
{
    // ================================================================ Discord
    // В Vencord есть встроенный плагин XSOverlay: он сам отправляет уведомления о сообщениях и звонках программе
    // на этом же компьютере (ws://127.0.0.1:42070). Kapsula слушает этот порт и показывает их карточками.
    // В Discord при этом ничего не меняется и ничего не устанавливается; канал односторонний — команд в Discord нет.
    static class DiscordBridge
    {
        public const int Port = 42070;
        public static event Action<string, string, byte[]> Notified;   // заголовок, текст, аватар (или null)
        public static volatile string Error;                            // почему не слушаем, если не слушаем
        static int clients;
        public static int Clients { get { return clients; } }
        static System.Net.HttpListener listener;
        static readonly object gate = new object();

        static string Roaming(string name) { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name); }

        public static bool VencordFound
        {
            get { return System.IO.Directory.Exists(Roaming("Vencord")) || System.IO.Directory.Exists(Roaming("vesktop")) || System.IO.Directory.Exists(Roaming("Equicord")); }
        }

        public static bool Enabled { get { return VencordFound && Store.Get("discordVencord") != "0"; } }

        public static void Start()
        {
            lock (gate)
            {
                if (listener != null || !Enabled) return;
                try
                {
                    var l = new System.Net.HttpListener();
                    l.Prefixes.Add("http://127.0.0.1:" + Port + "/");   // только этот компьютер, снаружи порт не виден
                    l.Start();
                    listener = l;
                    Error = null;
                }
                catch (Exception) { Error = "порт " + Port + " занят другой программой"; return; }
                var t = new Thread(Loop);
                t.IsBackground = true;
                t.Start(listener);
            }
        }

        public static void Stop()
        {
            lock (gate)
            {
                if (listener == null) return;
                try { listener.Close(); } catch { }
                listener = null;
            }
        }

        static void Loop(object o)
        {
            var l = (System.Net.HttpListener)o;
            while (true)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = l.GetContext(); } catch { return; }
                // Подключиться к локальному порту может и страница в браузере. Принимаем только сам Discord.
                if (!ctx.Request.IsWebSocketRequest || !OriginAllowed(ctx.Request.Headers["Origin"]))
                {
                    try { ctx.Response.StatusCode = 403; ctx.Response.Close(); } catch { }
                    continue;
                }
                var c = ctx;
                var t = new Thread(delegate() { Serve(c); });
                t.IsBackground = true;
                t.Start();
            }
        }

        public static bool OriginAllowed(string origin)
        {
            if (string.IsNullOrEmpty(origin)) return true;   // не браузер: обычная программа на этом же компьютере
            Uri u;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out u)) return false;
            string h = u.Host.ToLowerInvariant();
            return h == "discord.com" || h.EndsWith(".discord.com") || h == "discordapp.com" || h.EndsWith(".discordapp.com");
        }

        static void Serve(System.Net.HttpListenerContext ctx)
        {
            System.Net.WebSockets.WebSocket ws = null;
            bool counted = false;
            try
            {
                ws = ctx.AcceptWebSocketAsync(null).Result.WebSocket;
                Interlocked.Increment(ref clients); counted = true;
                var buf = new byte[16384];
                var ms = new System.IO.MemoryStream();
                while (ws.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var r = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
                    if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                    ms.Write(buf, 0, r.Count);
                    if (ms.Length > 4 * 1024 * 1024) break;   // аватар в сообщении — десятки килобайт, больше быть не должно
                    if (!r.EndOfMessage) continue;
                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    ms.SetLength(0);
                    string title, text; byte[] icon;
                    if (Parse(json, out title, out text, out icon))
                    {
                        var h = Notified;
                        if (h != null) h(title, text, icon);
                    }
                }
            }
            catch { }
            finally
            {
                if (counted) Interlocked.Decrement(ref clients);
                try { if (ws != null) ws.Dispose(); } catch { }
            }
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? v.ToString() : "";
        }

        // XSOverlay размечает упоминания тегами вида <color=#7289da>@имя</color> — убираем их.
        public static string Clean(string s, int max)
        {
            s = Regex.Replace(s ?? "", "<[^<>]{1,60}>", "");
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > max ? s.Substring(0, max) + "…" : s;
        }

        // Разбирает сообщение плагина XSOverlay: конверт { command: "SendNotification", jsonData: "<JSON уведомления>" }.
        public static bool Parse(string json, out string title, out string text, out byte[] icon)
        {
            title = null; text = null; icon = null;
            try
            {
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
                var env = ser.DeserializeObject(json) as Dictionary<string, object>;
                if (env == null || Str(env, "command") != "SendNotification") return false;
                var n = ser.DeserializeObject(Str(env, "jsonData")) as Dictionary<string, object>;
                if (n == null) return false;
                title = Clean(Str(n, "title"), 80);
                text = Clean(Str(n, "content"), 300);
                if (title == "" && text == "") return false;
                if (title == "") { title = "Discord"; }
                object b64;
                if (n.TryGetValue("useBase64Icon", out b64) && b64 is bool && (bool)b64)
                {
                    try { byte[] data = Convert.FromBase64String(Str(n, "icon")); if (data.Length > 100 && data.Length < 2 * 1024 * 1024) icon = data; } catch { }
                }
                return true;
            }
            catch { return false; }
        }
    }

    // ================================================================== Steam
    class SteamGame
    {
        public string Id = "", Name = "";
        public int State;                 // 4 — установлена полностью
        public long ToDownload, Downloaded;
        public bool Ready { get { return State == 4; } }
    }

    // Всё берётся из папки Steam и реестра на этом компьютере: без входа в аккаунт, без ключей и без сети.
    static class Steam
    {
        public static string Dir()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string p = k == null ? null : k.GetValue("SteamPath") as string;
                    if (string.IsNullOrEmpty(p)) return null;
                    p = p.Replace('/', '\\');
                    return System.IO.Directory.Exists(p) ? p : null;
                }
            }
            catch { return null; }
        }

        public static bool Found { get { return Dir() != null; } }
        public static bool Enabled { get { return Store.Get("steam") != "0" && Found; } }

        // Номер игры, запущенной через Steam прямо сейчас (0 — ничего не запущено)
        public static int RunningAppId()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    object v = k == null ? null : k.GetValue("RunningAppID");
                    return v is int ? (int)v : 0;
                }
            }
            catch { return 0; }
        }

        static string Field(string text, string key)
        {
            var m = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"") : "";
        }

        // Папки библиотек из файла steamapps\libraryfolders.vdf
        public static List<string> LibraryPaths(string vdf)
        {
            var list = new List<string>();
            foreach (Match m in Regex.Matches(vdf ?? "", "\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase))
            {
                string p = m.Groups[1].Value.Replace("\\\\", "\\");
                if (!list.Contains(p)) list.Add(p);
            }
            return list;
        }

        // Одна игра из файла appmanifest_*.acf; null — если это не игра (служебные пакеты Steam)
        public static SteamGame ParseManifest(string acf)
        {
            var g = new SteamGame { Id = Field(acf, "appid"), Name = Field(acf, "name") };
            if (g.Id == "" || g.Name == "") return null;
            string low = g.Name.ToLowerInvariant();
            if (g.Id == "228980" || low.StartsWith("steamworks") || low.StartsWith("proton") || low.StartsWith("steam linux runtime") || low.Contains("redistributable")) return null;
            int.TryParse(Field(acf, "StateFlags"), out g.State);
            long.TryParse(Field(acf, "BytesToDownload"), out g.ToDownload);
            long.TryParse(Field(acf, "BytesDownloaded"), out g.Downloaded);
            return g;
        }

        public static List<SteamGame> Scan()
        {
            var games = new List<SteamGame>();
            string dir = Dir();
            if (dir == null) return games;
            var libs = new List<string> { dir };
            try
            {
                string vdf = System.IO.Path.Combine(dir, @"steamapps\libraryfolders.vdf");
                if (System.IO.File.Exists(vdf))
                    foreach (string p in LibraryPaths(System.IO.File.ReadAllText(vdf)))
                        if (!libs.Exists(delegate(string x) { return string.Equals(x, p, StringComparison.OrdinalIgnoreCase); })) libs.Add(p);
            }
            catch { }
            foreach (string lib in libs)
            {
                try
                {
                    string apps = System.IO.Path.Combine(lib, "steamapps");
                    if (!System.IO.Directory.Exists(apps)) continue;
                    foreach (string f in System.IO.Directory.GetFiles(apps, "appmanifest_*.acf"))
                    {
                        try
                        {
                            SteamGame g = ParseManifest(System.IO.File.ReadAllText(f));
                            if (g != null && !games.Exists(delegate(SteamGame x) { return x.Id == g.Id; })) games.Add(g);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            games.Sort(delegate(SteamGame a, SteamGame b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            return games;
        }

        public static void Launch(string id)
        {
            try { System.Diagnostics.Process.Start("steam://rungameid/" + id); } catch { }
        }
    }
}
