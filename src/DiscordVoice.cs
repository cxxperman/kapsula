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
            sweeper = new Timer(Sweep, null, 2000, 2000);
        }

        // Плагин держит открытым запрос «жду команду» — команда уходит в Discord сразу, без ожидания
        static readonly List<KeyValuePair<System.Net.HttpListenerContext, int>> waiting = new List<KeyValuePair<System.Net.HttpListenerContext, int>>();
        static Timer sweeper;

        public static void Command(string cmd)
        {
            List<System.Net.HttpListenerContext> now = null;
            string body = null;
            lock (commands)
            {
                if (commands.Count < 8) commands.Enqueue(cmd);
                if (waiting.Count > 0)
                {
                    body = TakeCommands();
                    now = new List<System.Net.HttpListenerContext>();
                    foreach (var w in waiting) now.Add(w.Key);
                    waiting.Clear();
                }
            }
            if (now != null) foreach (var c in now) Reply(c, body);
        }

        // Все накопленные команды одним ответом (вызывать под замком)
        static string TakeCommands()
        {
            var sb = new StringBuilder("{\"commands\":[");
            bool first = true;
            while (commands.Count > 0) { if (!first) sb.Append(','); sb.Append('"').Append(commands.Dequeue()).Append('"'); first = false; }
            return sb.Append("]}").ToString();
        }

        static void Reply(System.Net.HttpListenerContext ctx, string body)
        {
            try
            {
                byte[] b = Encoding.UTF8.GetBytes(body);
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = b.Length;
                ctx.Response.OutputStream.Write(b, 0, b.Length);
                ctx.Response.Close();
            }
            catch { try { ctx.Response.Abort(); } catch { } }
        }

        // Запросы «жду команду», которым больше двадцати секунд, — отпустить пустыми: плагин тут же пришлёт новый
        static void Sweep(object o)
        {
            var late = new List<System.Net.HttpListenerContext>();
            lock (commands)
            {
                int now = Environment.TickCount;
                for (int i = waiting.Count - 1; i >= 0; i--)
                    if (unchecked(now - waiting[i].Value) >= 0) { late.Add(waiting[i].Key); waiting.RemoveAt(i); }
            }
            foreach (var c in late) Reply(c, "{\"commands\":[]}");
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
                if (!string.IsNullOrEmpty(rq.Headers["Origin"]) || rq.ContentLength64 > 65536) { ctx.Response.StatusCode = 403; ctx.Response.Close(); return; }
                if (rq.HttpMethod == "GET" && rq.Url.AbsolutePath == "/discord/wait")
                {
                    // «жду команду»: ответим, как только на острове нажмут кнопку (или пустым через двадцать секунд)
                    string ready = null;
                    lock (commands)
                    {
                        if (commands.Count > 0) ready = TakeCommands();
                        else waiting.Add(new KeyValuePair<System.Net.HttpListenerContext, int>(ctx, Environment.TickCount + 20000));
                    }
                    if (ready != null) Reply(ctx, ready);
                    return;
                }
                if (rq.HttpMethod != "POST" || rq.Url.AbsolutePath != "/discord") { ctx.Response.StatusCode = 403; ctx.Response.Close(); return; }
                string body;
                using (var r = new System.IO.StreamReader(rq.InputStream, Encoding.UTF8)) body = r.ReadToEnd();
                var h = Received;
                if (h != null) h(body);
                string reply;
                lock (commands) reply = TakeCommands();
                Reply(ctx, reply);
            }
            catch { try { ctx.Response.Abort(); } catch { } }
        }
    }

    partial class IslandWindow
    {
        class DvPerson { public string Id = "", Name = "", Avatar = ""; public bool Speaking, Mute, Deaf, Video, Stream, Me; }
        List<DvPerson> dvPeople = new List<DvPerson>();
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
            int nowT = Environment.TickCount;
            bool wasCall = dvInCall, wasStream = dvStream, wasVideo = dvVideo;
            dvHeardAt = nowT;
            // Нажатую на острове кнопку не откатываем, пока Discord не подтвердит (или полторы секунды)
            bool inCall = DFlag(d, "inCall"), inMute = DFlag(d, "mute"), inDeaf = DFlag(d, "deaf");
            if (!(dvMuteHold != 0 && unchecked(nowT - dvMuteHold) < 0 && inMute != dvMute)) { dvMute = inMute; dvMuteHold = 0; }
            if (!(dvDeafHold != 0 && unchecked(nowT - dvDeafHold) < 0 && inDeaf != dvDeaf)) { dvDeaf = inDeaf; dvDeafHold = 0; }
            if (!(dvLeaveHold != 0 && unchecked(nowT - dvLeaveHold) < 0 && inCall)) { dvInCall = inCall; dvLeaveHold = 0; }
            dvStream = DFlag(d, "stream"); dvVideo = DFlag(d, "video"); dvSpeaking = DFlag(d, "speaking");
            dvChannel = DText(d, "channel"); dvGuild = DText(d, "guild");
            // кто в канале: имя, аватарка, говорит ли, выключен ли микрофон или звук — для карточки звонка
            var people = new List<DvPerson>();
            object po;
            if (d.TryGetValue("people", out po) && po is object[])
                foreach (object x in (object[])po)
                {
                    var p = x as Dictionary<string, object>;
                    if (p == null) continue;
                    people.Add(new DvPerson { Id = DText(p, "id"), Name = DText(p, "name"), Avatar = DText(p, "avatar"), Speaking = DFlag(p, "speaking"), Mute = DFlag(p, "mute"), Deaf = DFlag(p, "deaf"), Video = DFlag(p, "video"), Stream = DFlag(p, "stream"), Me = DFlag(p, "me") });
                }
            dvPeople = people;
            object o;
            dvUsers = d.TryGetValue("users", out o) && o is int ? (int)o : 0;
            double since = d.TryGetValue("since", out o) && o != null ? Convert.ToDouble(o) : 0;
            dvSince = since > 0 ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(since).ToLocalTime() : DateTime.Now;
            if (dvSince > DateTime.Now) dvSince = DateTime.Now;
            if (!wasLive) Store.Set("discordPluginSeen", "1");   // в настройках: «плагин на связи»
            DiscordCallTick();   // не ждать полсекунды до следующего опроса
            // Демку и камеру видно сразу: надпись на острове, как только Discord их включил или выключил
            // (не при первом сообщении после запуска Kapsula и не при выходе из звонка — тогда это не новость)
            if (wasLive && dvInCall)
            {
                if (dvStream != wasStream && (dvStream || wasCall)) DemoToast(dvStream);
                else if (dvVideo != wasVideo && (dvVideo || wasCall)) CamToast(dvVideo);
            }
        }

        // «Демка запущена» с красной меткой LIVE — дольше обычной надписи: важно знать, что ваш экран видят
        bool toastLive;   // Toast() сбрасывает; правый край надписи — красный

        void DemoToast(bool on)
        {
            if (on) { Toast("", "Демка запущена", "LIVE", -1, RecRed); toastLive = true; toastUntil = Environment.TickCount + 3000; RenderCompact(); }
            else Toast("", "Демка остановлена", null, -1, null);
        }

        void CamToast(bool on)
        {
            if (on) { Toast("", "Камера включена", null, -1, AppleGreen); toastUntil = Environment.TickCount + 3000; }
            else Toast("", "Камера выключена", null, -1, null);
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

        // Кнопки на острове: состояние меняем сразу — зверёк, капсула и кнопка отзываются мгновенно, — а Discord
        // получает команду тут же (плагин держит открытым запрос «жду команду») и подтверждает доли секунды спустя
        int dvMuteHold, dvDeafHold, dvLeaveHold;

        void DiscordCommand(string cmd)
        {
            DiscordVoiceServer.Command(cmd);
            int until = Environment.TickCount + 1500;
            if (cmd == "mute") { dvMute = !dvMute; dvMuteHold = until; }
            else if (cmd == "deaf") { dvDeaf = !dvDeaf; dvDeafHold = until; }
            else if (cmd == "leave") { dvInCall = false; dvLeaveHold = until; DiscordCallTick(); }
            callMuted = dvMute || dvDeaf;
            PaintMute();
            RenderCompact();
        }
    }
}
