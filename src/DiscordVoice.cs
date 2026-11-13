// Kapsula — «остров» для Windows. Часть исходного кода: DiscordVoice.cs
// Звонки Discord напрямую. Плагин Kapsula внутри Discord (наша сборка Vencord — «Подключить Discord» в настройках)
// раз в полсекунды сообщает сюда, что происходит в голосе: вы в звонке или в голосовом канале и с какого момента,
// микрофон выключен, полный мут, идёт демонстрация экрана, включена камера, говорит ли кто-нибудь. В ответ остров
// отдаёт ему команды: выключить микрофон, полный мут, выйти из звонка. Канал только для этого компьютера
// (127.0.0.1:42073), и только для самого Discord: страницы в браузере сюда не пускаются.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace Kapsula
{
    static class DiscordVoiceServer
    {
        public const int Port = 42073;
        public static event Action<string> Received;   // состояние голоса (JSON) — в фоновом потоке
        public static volatile string Error;
        static readonly Queue<string> commands = new Queue<string>();
        static System.Net.HttpListener listener;

        public static void Start()
        {
            if (listener != null) return;
            int port = Port;
            int p;
            if (int.TryParse(Environment.GetEnvironmentVariable("KAPSULA_DISCORD_PORT"), out p) && p > 0) port = p;   // проверки рядом с вашей Kapsula
            try
            {
                var l = new System.Net.HttpListener();
                l.Prefixes.Add("http://127.0.0.1:" + port + "/");
                l.Start();
                listener = l;
            }
            catch (Exception) { Error = "порт " + port + " занят другой программой"; return; }
            var t = new Thread(Loop) { IsBackground = true };
            t.Start(listener);
        }

        // Команда плагину: уйдёт в ответе на его следующее сообщение (через полсекунды)
        public static void Command(string cmd)
        {
            lock (commands) { if (commands.Count < 8) commands.Enqueue(cmd); }
        }

        static void Loop(object o)
        {
            var l = (System.Net.HttpListener)o;
            while (true)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = l.GetContext(); } catch { return; }
                Serve(ctx);
            }
        }

        static void Serve(System.Net.HttpListenerContext ctx)
        {
            try
            {
                var rq = ctx.Request;
                // У браузерной страницы всегда есть Origin, у Discord (плагин шлёт из его основного процесса) — нет
                if (rq.HttpMethod != "POST" || rq.Url.AbsolutePath != "/discord" || !string.IsNullOrEmpty(rq.Headers["Origin"]) || rq.ContentLength64 > 65536)
                {
                    ctx.Response.StatusCode = 403; ctx.Response.Close(); return;
                }
                string body;
                using (var r = new System.IO.StreamReader(rq.InputStream, Encoding.UTF8)) body = r.ReadToEnd();
                var h = Received;
                if (h != null) h(body);
                var sb = new StringBuilder("{\"commands\":[");
                lock (commands)
                {
                    bool first = true;
                    while (commands.Count > 0) { if (!first) sb.Append(','); sb.Append('"').Append(commands.Dequeue()).Append('"'); first = false; }
                }
                sb.Append("]}");
                byte[] b = Encoding.UTF8.GetBytes(sb.ToString());
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = b.Length;
                ctx.Response.OutputStream.Write(b, 0, b.Length);
                ctx.Response.Close();
            }
            catch { try { ctx.Response.Abort(); } catch { } }
        }
    }

    partial class IslandWindow
    {
        bool dvInCall, dvMute, dvDeaf, dvStream, dvVideo, dvSpeaking;
        string dvChannel = "", dvGuild = "";
        int dvUsers, dvHeardAt;
        DateTime dvSince;

        // Плагин на связи: присылал что-нибудь в последние восемь секунд (он отзывается хотя бы раз в три)
        public bool DiscordLive { get { return dvHeardAt != 0 && unchecked(Environment.TickCount - dvHeardAt) < 8000; } }

        void DiscordVoiceInit()
        {
            DiscordVoiceServer.Received += delegate(string json) { Dispatcher.BeginInvoke(new Action(delegate { DiscordVoiceMessage(json); })); };
            DiscordVoiceServer.Start();
        }

        void DiscordVoiceMessage(string json)
        {
            Dictionary<string, object> d;
            try { d = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; } catch { return; }
            if (d == null) return;
            bool wasLive = DiscordLive;
            dvHeardAt = Environment.TickCount;
            dvInCall = DFlag(d, "inCall"); dvMute = DFlag(d, "mute"); dvDeaf = DFlag(d, "deaf");
            dvStream = DFlag(d, "stream"); dvVideo = DFlag(d, "video"); dvSpeaking = DFlag(d, "speaking");
            dvChannel = DText(d, "channel"); dvGuild = DText(d, "guild");
            object o;
            dvUsers = d.TryGetValue("users", out o) && o is int ? (int)o : 0;
            double since = d.TryGetValue("since", out o) && o != null ? Convert.ToDouble(o) : 0;
            dvSince = since > 0 ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(since).ToLocalTime() : DateTime.Now;
            if (dvSince > DateTime.Now) dvSince = DateTime.Now;
            if (!wasLive) Store.Set("discordPluginSeen", "1");   // в настройках: «плагин на связи»
            DiscordCallTick();   // не ждать полсекунды до следующего опроса
        }

        static bool DFlag(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static string DText(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o is string ? (string)o : ""; }

        // Звонок по словам самого Discord — вместо догадки по микрофону
        void DiscordCallTick()
        {
            string app = dvInCall ? "Discord" : null;
            if (app != callApp)
            {
                callApp = app;
                if (app != null) callStart = dvSince;
                EnsureCallCard();
                ApplyHomeLayout();
                RenderCompact(); SetTargets();
            }
            if (callApp == null) return;
            callStart = dvSince;   // время звонка — как в самом Discord: с того момента, как вы зашли
            callMuted = dvMute || dvDeaf;
            if (dvSpeaking) callVoiceAt = Environment.TickCount;
            if (callTimeText != null) callTimeText.Text = CallTime();
            PaintMute();
            if (state == 0) RenderCompact();
        }

        // Значок состояния звонка справа в капсуле (шрифт значков Windows), с небольшим зазором от предыдущего
        void CallIcon(int code, Brush brush)
        {
            if (cRight.Inlines.Count > 0) cRight.Inlines.Add(new System.Windows.Documents.Run("  ") { FontSize = 7 });
            cRight.Inlines.Add(new System.Windows.Documents.Run(((char)code).ToString()) { FontFamily = Icons, FontSize = 14, Foreground = brush });
        }

        // Кнопки на острове: состояние меняем сразу (зверёк и капсула отзываются), Discord подтвердит через полсекунды
        void DiscordCommand(string cmd)
        {
            DiscordVoiceServer.Command(cmd);
            if (cmd == "mute") dvMute = !dvMute;
            else if (cmd == "deaf") dvDeaf = !dvDeaf;
            callMuted = dvMute || dvDeaf;
            PaintMute();
            RenderCompact();
        }
    }
}
