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
using System.Windows.Controls;
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
            while (commands.Count > 0)
            {
                if (!first) sb.Append(',');
                sb.Append('"').Append(commands.Dequeue().Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');   // «stream:…» несёт имя экрана
                first = false;
            }
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
            // Discord, который не запускался после «Подключить» в 0.6.1–0.6.2, — починить сразу при запуске Kapsula
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool repaired = false;
                try { repaired = DiscordInstall.RepairBroken(); } catch { }
                if (repaired) Dispatcher.BeginInvoke(new Action(delegate { ClaudeAfter(6000, delegate { Toast("", "Починили подключение Discord — можно запускать", null, -1, accent); }); }));
            });
        }

        void DiscordVoiceMessage(string json)
        {
            Dictionary<string, object> d;
            try { d = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; } catch { return; }
            if (d == null) return;
            bool wasLive = DiscordLive;
            int nowT = Environment.TickCount;
            bool wasCall = dvInCall, wasStreamSeen = dvStreamSeen, wasVideo = dvVideo;
            string wasChannel = dvChannel, wasGuild = dvGuild;
            List<DvPerson> wasPeople = dvPeople;
            dvHeardAt = nowT;
            // Нажатую на острове кнопку не откатываем, пока Discord не подтвердит (или полторы секунды)
            bool inCall = DFlag(d, "inCall"), inMute = DFlag(d, "mute"), inDeaf = DFlag(d, "deaf");
            if (!(dvMuteHold != 0 && unchecked(nowT - dvMuteHold) < 0 && inMute != dvMute)) { dvMute = inMute; dvMuteHold = 0; }
            if (!(dvDeafHold != 0 && unchecked(nowT - dvDeafHold) < 0 && inDeaf != dvDeaf)) { dvDeaf = inDeaf; dvDeafHold = 0; }
            if (!(dvLeaveHold != 0 && unchecked(nowT - dvLeaveHold) < 0 && inCall)) { dvInCall = inCall; dvLeaveHold = 0; }
            // демка: кнопка на острове может опережать Discord (как микрофон), а надпись — только по его словам
            bool inStream = DFlag(d, "stream");
            dvStreamSeen = inStream;
            if (!(dvStreamHold != 0 && unchecked(nowT - dvStreamHold) < 0 && inStream != dvStream)) { dvStream = inStream; dvStreamHold = 0; }
            dvVideo = DFlag(d, "video");
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
            // В ваш канал зашли или из него вышли — зверёк машет (не когда вы сами вошли или перешли в другой канал;
            // плагин присылает до двенадцати человек — в большом канале список сдвигается, там не машем)
            if (wasLive && wasCall && dvInCall && wasChannel == dvChannel && wasGuild == dvGuild && wasPeople.Count > 0 && wasPeople.Count < 12 && people.Count < 12)
            {
                DvPerson came = people.Find(delegate(DvPerson p) { return !p.Me && !wasPeople.Exists(delegate(DvPerson w) { return w.Id == p.Id; }); });
                DvPerson gone = came != null ? null : wasPeople.Find(delegate(DvPerson w) { return !w.Me && !people.Exists(delegate(DvPerson p) { return p.Id == w.Id; }); });
                if (came != null) FriendWave(came.Name, true, people.Count);
                else if (gone != null) FriendWave(gone.Name, false, people.Count);
            }
            dvPeople = people;
            // Зверёк кивает только на ваш голос, а не на чужой: «говорите вы» — от плагина или из списка участников
            object ms;
            if (d.TryGetValue("meSpeaking", out ms) && ms is bool) dvSpeaking = (bool)ms;
            else dvSpeaking = people.Exists(p => p.Me && p.Speaking);
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
                if (dvStreamSeen != wasStreamSeen && (dvStreamSeen || wasCall)) DemoToast(dvStreamSeen);
                else if (dvVideo != wasVideo && (dvVideo || wasCall)) CamToast(dvVideo);
            }
            // Ответ на кнопку демки: что можно показать — меню у кнопки; или почему демка не пошла
            object so;
            if (dvSourcesWanted != 0 && d.TryGetValue("sources", out so) && so is object[])
            {
                bool fresh = unchecked(nowT - dvSourcesWanted) < 0;
                dvSourcesWanted = 0;
                PaintMute();
                if (fresh) StreamMenu((object[])so);
            }
            string notice = DText(d, "notice");
            if (notice == "no-permission") CallNotice("В этом канале нельзя показывать экран");
            else if (notice == "no-stream") CallNotice("Discord не запустил демку");
            // Кто звонит вам прямо сейчас (личный или групповой звонок) — карточкой с кнопками, как на телефоне
            object io;
            var inc = d.TryGetValue("incoming", out io) ? io as Dictionary<string, object> : null;
            dvRingId = inc != null ? DText(inc, "channel") : "";
            dvRingName = inc != null ? DText(inc, "name") : "";
            dvRingCaller = inc != null ? DText(inc, "caller") : "";
            dvRingAvatar = inc != null ? DText(inc, "avatar") : "";
            dvRingGroup = inc != null && DFlag(inc, "group");
            RingTick();
        }

        // ---- Входящий звонок Discord: аватарка, имя, красная «Отклонить» и зелёная «Принять». Висит, пока звонят
        string dvRingId = "", dvRingName = "", dvRingCaller = "", dvRingAvatar = "";
        bool dvRingGroup;
        string ringShownId;   // карточка этого звонка сейчас на острове
        string ringDoneId;    // на этот звонок уже ответили с острова (или пошли отвечать в Discord) — больше не показывать
        string ringSeenId;    // этот звонок уже записан в историю уведомлений
        ScaleTransform ringPulse;

        void RingTick()
        {
            bool live = DiscordLive;
            if (!live || dvRingId.Length == 0) { ringDoneId = null; ringSeenId = null; }   // звонок кончился — следующий снова карточкой
            bool ringing = live && dvRingId.Length > 0 && dvRingId != ringDoneId;
            if (!ringing)
            {
                // перестали звонить, ответили в самом Discord или здесь — карточка уходит
                if (ringShownId != null) { ringShownId = null; RingPulseStop(); if (state == 3) SetState(0); }
                return;
            }
            if (ringSeenId != dvRingId)
            {
                ringSeenId = dvRingId;
                if (Quiet("Discord", RingTitle(), RingSub())) { ringDoneId = dvRingId; return; }   // «Не беспокоить»: звонок только в истории
            }
            if (ringShownId == dvRingId)
            {
                if (state == 3) { cardUntil = Environment.TickCount + 60000; return; }
                // карточку убрали сами (например, открыли панель) — не навязываем её снова: ответить можно и в Discord
                ringDoneId = dvRingId; ringShownId = null; RingPulseStop();
                return;
            }
            if (state == 3 || TestCharacter != null) return;   // на острове другая карточка — звонок покажется сразу после неё
            ShowRing();
        }

        string RingTitle() { return dvRingName.Length > 0 ? dvRingName : dvRingCaller.Length > 0 ? dvRingCaller : "Входящий звонок"; }
        string RingSub() { return dvRingGroup ? (dvRingCaller.Length > 0 ? dvRingCaller + " зовёт в групповой звонок" : "Групповой звонок в Discord") : "Звонит в Discord"; }

        void ShowRing()
        {
            string id = dvRingId;
            cardCarryNext = "phone";   // зверёк приносит телефонную трубку — она трясётся, как у звонящего телефона
            ShowCardEx(RingFace(), "", RingTitle(), RingSub(), delegate { RingOpen(id); }, null, delegate { RingAnswer(id, false); }, true);
            if (state != 3) return;
            ringShownId = id;
            // как на телефоне: красная «Отклонить» и справа зелёная «Принять»
            cardBtn.Background = AppleRed; cardBtn.ToolTip = "Отклонить";
            cardBtnLabel.Text = ""; cardBtnLabel.Foreground = Brushes.White;
            var yes = cardNo.Child as TextBlock;
            if (yes != null) { yes.Text = ""; yes.Foreground = Brushes.White; yes.FontSize = 15; }
            cardNo.Background = AppleGreen; cardNo.ToolTip = "Принять";
            cardNoAction = delegate { RingAnswer(id, true); };
            cardNo.Visibility = Visibility.Visible;
            cardThumb.CornerRadius = new CornerRadius(26);   // аватарка круглая, как у звонка на телефоне
            SetCardBadge(SourceIcon("discord"));
            FitCard();
            cardUntil = Environment.TickCount + 60000;
            if (ReducedMotion) return;
            // зелёная кнопка мягко «дышит», пока звонят
            if (ringPulse == null) { ringPulse = new ScaleTransform(1, 1); cardNo.RenderTransformOrigin = new Point(0.5, 0.5); cardNo.RenderTransform = ringPulse; }
            var beat = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
            var ease = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
            beat.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)));
            beat.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1.12, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), ease));
            beat.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(860)), ease));
            beat.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500))));
            System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(beat, 30);   // мягкому «дыханию» хватает и 30 кадров — вдвое меньше работы
            ringPulse.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
            ringPulse.BeginAnimation(ScaleTransform.ScaleYProperty, beat);
        }

        // Обычной карточке — обычные кнопки: звонок перекрашивал их в красную и зелёную
        void RingCardReset()
        {
            ringShownId = null;
            if (cardBtn == null || cardNo == null) return;
            cardBtn.Background = Chip; cardBtn.ToolTip = null;
            cardBtnLabel.Foreground = Fg;
            var no = cardNo.Child as TextBlock;
            if (no != null) { no.Text = ""; no.Foreground = Fg; no.FontSize = 13; }
            cardNo.Background = Chip; cardNo.ToolTip = null;
            if (cardThumb != null) cardThumb.CornerRadius = new CornerRadius(10);
            RingPulseStop();
        }

        // Карточка ушла — «дыхание» зелёной кнопки тоже: иначе оно крутилось бы невидимо и тратило процессор
        void RingPulseStop()
        {
            if (ringPulse == null) return;
            ringPulse.BeginAnimation(ScaleTransform.ScaleXProperty, null); ringPulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ringPulse.ScaleX = 1; ringPulse.ScaleY = 1;
        }

        // Аватарка звонящего: пока грузится (или её нет) — первая буква имени на цветном круге
        ImageSource RingFace()
        {
            var b = FaceImage(dvRingAvatar, 128) as ImageBrush;
            if (b != null) return b.ImageSource;
            string who = dvRingCaller.Length > 0 ? dvRingCaller : RingTitle();
            return ImageFrom(DemoAvatar(System.Globalization.StringInfo.GetNextTextElement(who).ToUpper(), Color.FromRgb(0x58, 0x65, 0xF2), Color.FromRgb(0x8A, 0x5C, 0xF6)));
        }

        // Аватарка догрузилась, а карточка звонка ещё на острове — поставить её вместо буквы
        void RingFaceReady(string url)
        {
            if (ringShownId == null || state != 3 || url != dvRingAvatar || cardThumb == null) return;
            Brush b;
            if (faceCache.TryGetValue(url, out b) && b != null) cardThumb.Background = b;
        }

        void RingAnswer(string id, bool accept)
        {
            ringDoneId = id; ringShownId = null; RingPulseStop();
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^\\d{1,25}$")) return;
            DiscordVoiceServer.Command((accept ? "accept:" : "decline:") + id);
            if (accept) Toast("", "Подключаемся…", null, -1, AppleGreen);
            else Toast("", "Звонок отклонён", null, -1, AppleRed);
        }

        // Щелчок по самой карточке — открыть этот разговор в Discord и ответить там
        void RingOpen(string id)
        {
            ringDoneId = id; ringShownId = null; RingPulseStop();
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^\\d{1,25}$")) return;
            try { System.Diagnostics.Process.Start("discord://-/channels/@me/" + id); } catch { }
        }

        bool dvStreamSeen;     // демка по словам самого Discord (dvStream — с учётом только что нажатой кнопки)
        int dvStreamHold;
        int dvSourcesWanted;   // ждём от Discord список экранов и окон для кнопки демки — до этого момента

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

        // Как назвать звонок: голосовой канал, а личный звонок — по тем, с кем вы говорите
        string DvCallName(string fallback)
        {
            if (dvChannel.Length > 0) return dvChannel;
            var names = new List<string>();
            foreach (DvPerson p in dvPeople) if (!p.Me && p.Name.Length > 0) names.Add(p.Name);
            if (names.Count == 0) return fallback;
            if (names.Count == 1) return names[0];
            return names[0] + ", " + names[1] + (names.Count > 2 ? " и ещё " + (names.Count - 2) : "");
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
            else if (cmd.StartsWith("stream:")) { dvStream = true; dvStreamHold = Environment.TickCount + 6000; }   // демка запускается дольше
            else if (cmd == "unstream") { dvStream = false; dvStreamHold = Environment.TickCount + 3000; }
            callMuted = dvMute || dvDeaf;
            PaintMute();
            RenderCompact();
        }
    }
}
