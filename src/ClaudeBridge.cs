// Kapsula — «остров» для Windows. Часть исходного кода: ClaudeBridge.cs
// Связь с Claude Code: запросы разрешений («можно выполнить команду?») и уведомления приходят на остров.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Kapsula
{
    // Claude Code умеет при событиях отправлять запрос на локальный адрес («хуки» типа http). Kapsula слушает
    // http://127.0.0.1:42072 и на запрос разрешения показывает карточку «Разрешить / Отклонить». Ответ уходит
    // обратно в Claude Code. Не ответили за полминуты — Claude Code спрашивает сам, как обычно.
    // Адрес доступен только с этого компьютера; каждый запрос должен нести секретную метку из настроек Claude Code.
    // Расход Claude Code за сегодня. Считается по журналам сессий, которые Claude Code сам ведёт на этом компьютере
    // (папка .claude\projects): из каждой записи ответа берутся только числа токенов. Текст разговоров не читается
    // и никуда не передаётся. Журналы бывают большими, поэтому каждый читается один раз, дальше — только новый хвост.
    static class ClaudeUsage
    {
        public static long In, Out, CacheRead, CacheWrite;
        public static int Replies;
        public static volatile bool Ready;
        public static event Action Updated;
        static readonly Dictionary<string, long> offsets = new Dictionary<string, long>();
        static readonly HashSet<string> seen = new HashSet<string>();
        static DateTime day;
        static int running;

        static readonly System.Text.RegularExpressions.Regex rxId = new System.Text.RegularExpressions.Regex("\"id\":\"(msg_[A-Za-z0-9_]+)\"");
        static readonly System.Text.RegularExpressions.Regex rxTime = new System.Text.RegularExpressions.Regex("\"timestamp\":\"([0-9T:\\.\\-Z+]+)\"");

        static long Num(string line, int from, string key)
        {
            int i = line.IndexOf("\"" + key + "\":", from, StringComparison.Ordinal);
            if (i < 0) return 0;
            i += key.Length + 3;
            long v = 0; bool any = false;
            while (i < line.Length && line[i] >= '0' && line[i] <= '9') { v = v * 10 + (line[i] - '0'); i++; any = true; }
            return any ? v : 0;
        }

        // Разбор одной строки журнала. Возвращает true, если это новый ответ за сегодня и его числа учтены.
        public static bool Count(string line, DateTime today)
        {
            int u = line.IndexOf("\"usage\":{", StringComparison.Ordinal);
            if (u < 0 || line.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) < 0) return false;
            var tm = rxTime.Match(line);
            DateTime when;
            if (!tm.Success || !DateTime.TryParse(tm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out when)) return false;
            DateTime date = when.ToLocalTime().Date;
            if (date > today || date < today.AddDays(-6)) return false;
            var im = rxId.Match(line);
            if (im.Success && !seen.Add(im.Groups[1].Value)) return false;   // один ответ записан несколькими строками — считаем раз
            long outTokens = Num(line, u, "output_tokens");
            long was;
            lock (Days) { Days.TryGetValue(date, out was); Days[date] = was + outTokens; }
            if (date != today) return false;   // прошлые дни идут только в недельную сводку
            var pm = rxCwd.Match(line);
            if (pm.Success)
            {
                string cwd = pm.Groups[1].Value.Replace("\\\\", "\\").TrimEnd('\\', '/');
                int cut = Math.Max(cwd.LastIndexOf('\\'), cwd.LastIndexOf('/'));
                string name = cut >= 0 ? cwd.Substring(cut + 1) : cwd;
                if (name.Length > 0) { Projects.TryGetValue(name, out was); Projects[name] = was + outTokens; }
            }
            In += Num(line, u, "input_tokens");
            Out += outTokens;
            CacheRead += Num(line, u, "cache_read_input_tokens");
            CacheWrite += Num(line, u, "cache_creation_input_tokens");
            Replies++;
            return true;
        }

        // Написанные токены по дням за неделю и по проектам за сегодня
        public static readonly Dictionary<DateTime, long> Days = new Dictionary<DateTime, long>();
        public static readonly Dictionary<string, long> Projects = new Dictionary<string, long>();
        static readonly System.Text.RegularExpressions.Regex rxCwd = new System.Text.RegularExpressions.Regex("\"cwd\":\"((?:[^\"\\\\]|\\\\.)*)\"");

        public static long Week { get { long s = 0; lock (Days) foreach (long v in Days.Values) s += v; return s; } }

        public static void Reset() { In = Out = CacheRead = CacheWrite = 0; Replies = 0; seen.Clear(); offsets.Clear(); Days.Clear(); Projects.Clear(); }

        public static void Refresh()
        {
            if (Interlocked.Exchange(ref running, 1) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    DateTime today = DateTime.Now.Date;
                    if (today != day) { Reset(); day = today; }
                    string root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".claude\projects");
                    if (System.IO.Directory.Exists(root))
                        foreach (string f in System.IO.Directory.GetFiles(root, "*.jsonl", System.IO.SearchOption.AllDirectories))
                        {
                            try
                            {
                                var fi = new System.IO.FileInfo(f);
                                if (fi.LastWriteTime.Date < today.AddDays(-6)) continue;   // за последнюю неделю в этот журнал не писали
                                long from;
                                offsets.TryGetValue(f, out from);
                                if (fi.Length <= from) continue;
                                using (var fs = new System.IO.FileStream(f, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete))
                                {
                                    fs.Seek(from, System.IO.SeekOrigin.Begin);
                                    using (var rd = new System.IO.StreamReader(fs, Encoding.UTF8, false, 1 << 16))
                                    {
                                        string line;
                                        while ((line = rd.ReadLine()) != null) Count(line, today);
                                    }
                                    offsets[f] = fi.Length;
                                }
                            }
                            catch { }
                        }
                    Ready = true;
                }
                catch { }
                finally { running = 0; }
                var h = Updated;
                if (h != null) h();
            });
        }

        public static string Short(long n)
        {
            if (n >= 1000000) return (n / 1000000.0).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + " млн";
            if (n >= 1000) return (n / 1000.0).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + " тыс.";
            return n.ToString();
        }
    }

    static class ClaudeBridge
    {
        public const int Port = 42072;
        public const int WaitMs = 30000;          // сколько держим запрос разрешения, пока пользователь думает
        const string Marker = "127.0.0.1:42072/claude/";

        // Запрос, на который Claude Code ждёт ответа. Не ответили вовремя — уходит пустой ответ,
        // и Claude Code ведёт себя как обычно (сам спрашивает, сам заканчивает).
        public class Pending
        {
            System.Net.HttpListenerContext ctx;
            int done;
            Timer timer;
            public string Project = "";   // папка проекта этой сессии
            public string SessionId = "";
            internal Pending(System.Net.HttpListenerContext c, int waitMs) { ctx = c; timer = new Timer(delegate { Pass(); }, null, waitMs, Timeout.Infinite); }
            public bool Answered { get { return done != 0; } }
            public void Pass() { Finish(""); }
            public void Extend(int ms) { try { if (done == 0) timer.Change(ms, Timeout.Infinite); } catch { } }
            protected void Finish(string body)
            {
                if (Interlocked.Exchange(ref done, 1) != 0) return;
                try { timer.Dispose(); } catch { }
                Reply(ctx, 200, body);
            }
            protected static string Json(object o) { return new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Serialize(o); }
            protected static Dictionary<string, object> Obj(params object[] kv)
            {
                var d = new Dictionary<string, object>();
                for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
                return d;
            }
        }

        // Запрос разрешения: инструмент и что именно он хочет сделать
        public class Ask : Pending
        {
            public string Tool, Detail, Cwd;
            internal object Always;   // правило «разрешать всегда», которое предложил сам Claude Code (или null)
            internal Ask(System.Net.HttpListenerContext c) : base(c, WaitMs) { }
            public bool CanAlways { get { return Always != null; } }
            public void Allow() { Finish(Json(Obj("hookSpecificOutput", Obj("hookEventName", "PermissionRequest", "decision", Obj("behavior", "allow"))))); }
            public void AllowAlways()
            {
                if (Always == null) { Allow(); return; }
                Finish(Json(Obj("hookSpecificOutput", Obj("hookEventName", "PermissionRequest", "decision", Obj("behavior", "allow", "updatedPermissions", new object[] { Always })))));
            }
            public void Deny() { Finish(Json(Obj("hookSpecificOutput", Obj("hookEventName", "PermissionRequest", "decision", Obj("behavior", "deny", "message", "Отклонено пользователем на острове Kapsula"))))); }
        }

        // Вопрос с вариантами ответа, который Claude задаёт пользователю
        public class QItem { public string Header, Text; public string[] Options, Notes; public bool Multi; }

        public class Question : Pending
        {
            public QItem[] Items;        // вопросов может быть несколько, у каждого свои варианты
            internal object Questions;   // исходный список вопросов — его нужно вернуть вместе с ответом
            internal Question(System.Net.HttpListenerContext c) : base(c, WaitMs) { }
            // answers: текст вопроса → выбранный вариант (несколько вариантов — через запятую)
            public void Answer(Dictionary<string, string> answers)
            {
                var a = new Dictionary<string, object>();
                foreach (QItem it in Items)
                {
                    string v;
                    if (!answers.TryGetValue(it.Text, out v) || string.IsNullOrEmpty(v)) { Pass(); return; }
                    a[it.Text] = v;
                }
                Finish(Json(Obj("hookSpecificOutput", Obj("hookEventName", "PreToolUse", "permissionDecision", "allow", "updatedInput", Obj("questions", Questions, "answers", a)))));
            }
        }

        // Сессия Claude Code: их может быть открыто несколько сразу
        // Steps — сколько действий сделано в текущем ответе, StartedAt — когда он начался; Cwd и Transcript — папка проекта и файл сессии
        public class Session { public string Id, Name, State, Detail, Last, Cwd = "", Transcript = ""; public int At, Steps, StartedAt; }

        // Запомнить начало последнего ответа сессии — его показывает вкладка «Claude»
        static void Remember(string id, string last) { lock (sessions) { Session s; if (sessions.TryGetValue(id, out s)) s.Last = last; } }
        static readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>();

        // Папка проекта, где Claude Code работал последним (для «claude …» и «открыть в VS Code»)
        public static string LastProject
        {
            get { return Store.Get("claudeLastProject"); }
            set { if (Store.Get("claudeLastProject") != value) Store.Set("claudeLastProject", value); }
        }

        // Заполнение контекста сессии: входные токены последнего ответа из её файла (читаем только хвост)
        public static long ContextTokens(string transcript)
        {
            try
            {
                if (string.IsNullOrEmpty(transcript) || !System.IO.File.Exists(transcript)) return -1;
                using (var fs = new System.IO.FileStream(transcript, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete))
                {
                    long from = Math.Max(0, fs.Length - 256 * 1024);
                    fs.Seek(from, System.IO.SeekOrigin.Begin);
                    var buf = new byte[fs.Length - from];
                    int n = fs.Read(buf, 0, buf.Length);
                    string[] lines = Encoding.UTF8.GetString(buf, 0, n).Split('\n');
                    var js = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
                    for (int i = lines.Length - 1; i >= 0; i--)
                    {
                        if (lines[i].IndexOf("\"usage\"", StringComparison.Ordinal) < 0) continue;
                        try
                        {
                            var o = js.DeserializeObject(lines[i]) as Dictionary<string, object>;
                            object m; if (o == null || !o.TryGetValue("message", out m)) continue;
                            var md = m as Dictionary<string, object>; object u;
                            if (md == null || !md.TryGetValue("usage", out u)) continue;
                            var ud = u as Dictionary<string, object>;
                            if (ud == null) continue;
                            long t = 0;
                            foreach (string k in new string[] { "input_tokens", "cache_read_input_tokens", "cache_creation_input_tokens" })
                            { object v; if (ud.TryGetValue(k, out v) && v != null) t += Convert.ToInt64(v); }
                            return t;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return -1;
        }

        static Session Touch(Dictionary<string, object> d, string state, string detail)
        {
            string id = Str(d, "session_id");
            if (id.Length == 0) id = "?";
            string cwd = Str(d, "cwd").TrimEnd('\\', '/');
            int cut = Math.Max(cwd.LastIndexOf('\\'), cwd.LastIndexOf('/'));
            string name = Clean(cut >= 0 ? cwd.Substring(cut + 1) : cwd, 28);
            lock (sessions)
            {
                Session s;
                if (!sessions.TryGetValue(id, out s)) { s = new Session { Id = id }; sessions[id] = s; }
                if (name.Length > 0) s.Name = name;
                if (s.Name == null) s.Name = "сессия";
                if (cwd.Length > 0) s.Cwd = cwd;
                string tp = Str(d, "transcript_path");
                if (tp.Length > 0) s.Transcript = tp;
                if (state == "работает")
                {
                    // новый ответ начинается после «закончил»/«остановлен» или долгой тишины
                    if (s.State == null || s.State == "закончил" || s.State == "остановлен" || unchecked(Environment.TickCount - s.At) > 10 * 60000) { s.Steps = 0; s.StartedAt = Environment.TickCount; }
                    s.Steps++;
                }
                s.State = state; s.Detail = detail ?? ""; s.At = Environment.TickCount;
                if (cwd.Length > 0) LastProject = cwd;
                return s;
            }
        }

        // Сессии, от которых что-то приходило за последние полчаса, самые свежие первыми
        public static List<Session> Sessions()
        {
            var list = new List<Session>();
            int now = Environment.TickCount;
            lock (sessions)
            {
                foreach (Session s in sessions.Values) if (unchecked(now - s.At) < 30 * 60000) list.Add(new Session { Id = s.Id, Name = s.Name, State = s.State, Detail = s.Detail, Last = s.Last ?? "", At = s.At, Cwd = s.Cwd, Transcript = s.Transcript, Steps = s.Steps, StartedAt = s.StartedAt });
            }
            list.Sort(delegate(Session a, Session b) { return unchecked(b.At - a.At); });
            return list;
        }

        // Claude закончил отвечать. Пока запрос не отпущен, ему можно написать — он продолжит работу.
        public class Finished : Pending
        {
            public string Text;
            internal Finished(System.Net.HttpListenerContext c) : base(c, FinishWaitMs) { }
            public void Send(string message)
            {
                if (string.IsNullOrEmpty(message)) { Pass(); return; }
                Finish(Json(Obj("hookSpecificOutput", Obj("hookEventName", "Stop", "additionalContext", "Пользователь написал вам с острова Kapsula, не открывая окно Claude Code. Его сообщение: " + message))));
            }
        }

        public const int FinishWaitMs = 12000;    // сколько ждём, не захочет ли пользователь ответить
        public const int TypingWaitMs = 180000;   // а это — пока он печатает ответ

        public static event Action<Ask> Asked;                          // нужно разрешение
        public static event Action<Question> Questioned;                // нужен выбор из вариантов
        public static event Action<Finished> Done;                      // закончил отвечать
        public static event Action<string, string, string> Working;     // сейчас выполняет: инструмент, что именно, проект
        public static event Action<string> Waiting;                     // ждёт ответа пользователя (проект)

        // Просьба остановиться: сессия узнаёт о ней перед своим следующим действием. Просьба живёт две минуты.
        static readonly Dictionary<string, int> stops = new Dictionary<string, int>();
        public static void RequestStop(string sessionId)
        {
            int until = Environment.TickCount + 120000;
            lock (stops)
            {
                if (sessionId != null) { stops[sessionId] = until; return; }
                foreach (Session s in Sessions()) if (s.State == "работает") stops[s.Id] = until;   // без имени — все, кто сейчас работает
                stops["*"] = until;                                                                    // и первая, что подаст голос
            }
        }
        static bool TakeStop(string sessionId)
        {
            int now = Environment.TickCount;
            lock (stops)
            {
                int until;
                foreach (string key in new string[] { sessionId, "*" })
                    if (stops.TryGetValue(key, out until))
                    {
                        stops.Remove(key);
                        if (unchecked(now - until) < 0) { if (key == sessionId) stops.Remove("*"); return true; }
                    }
                return false;
            }
        }
        public static bool Busy { get { foreach (Session s in Sessions()) if (s.State == "работает" && unchecked(Environment.TickCount - s.At) < 20000) return true; return false; } }
        public static volatile string Error;
        static System.Net.HttpListener listener;
        static readonly object gate = new object();

        public static bool ForceOn;   // проверочный режим: слушать, не глядя на настройку
        public static bool Enabled { get { return ForceOn || Store.Get("claudeCode") == "1"; } }
        public static bool Listening { get { return listener != null; } }

        static string TokenFile { get { return System.IO.Path.Combine(Store.Dir, "claude-token.txt"); } }

        // Секретная метка: случайная строка, которую знают только Kapsula и настройки Claude Code на этом компьютере.
        public static string Token()
        {
            try
            {
                if (System.IO.File.Exists(TokenFile))
                {
                    string t = System.IO.File.ReadAllText(TokenFile).Trim();
                    if (t.Length >= 32) return t;
                }
                var b = new byte[24];
                using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider()) rng.GetBytes(b);
                string fresh = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
                System.IO.Directory.CreateDirectory(Store.Dir);
                System.IO.File.WriteAllText(TokenFile, fresh);
                return fresh;
            }
            catch { return ""; }
        }

        public static void Start()
        {
            lock (gate)
            {
                if (listener != null || !Enabled) return;
                try
                {
                    var l = new System.Net.HttpListener();
                    l.Prefixes.Add("http://127.0.0.1:" + Port + "/");   // только этот компьютер
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
            string token = Token();
            while (true)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = l.GetContext(); } catch { return; }
                try { Handle(ctx, token); } catch { Reply(ctx, 200, ""); }
            }
        }

        static void Handle(System.Net.HttpListenerContext ctx, string token)
        {
            var rq = ctx.Request;
            // Страницы в браузере тоже могут стучаться на локальный адрес: они всегда присылают Origin и не знают метку.
            if (rq.HttpMethod != "POST" || !string.IsNullOrEmpty(rq.Headers["Origin"]) || token.Length == 0 || rq.Headers["X-Kapsula-Token"] != token)
            {
                Reply(ctx, 403, "");
                return;
            }
            string body;
            using (var rd = new System.IO.StreamReader(rq.InputStream, Encoding.UTF8))
            {
                var buf = new char[256 * 1024];
                int n = rd.ReadBlock(buf, 0, buf.Length);   // больше четверти мегабайта не читаем
                body = new string(buf, 0, n);
            }
            Dictionary<string, object> d = null;
            try { d = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.DeserializeObject(body) as Dictionary<string, object>; } catch { }
            string path = rq.Url.AbsolutePath;
            if (path == "/mcp") { HandleMcp(ctx, d); return; }
            if (d == null) { Reply(ctx, 200, ""); return; }

            object ti;
            var input = d.TryGetValue("tool_input", out ti) ? ti as Dictionary<string, object> : null;
            string tool = Clean(Str(d, "tool_name"), 40);

            if (path == "/claude/permission")
            {
                Session s = Touch(d, "ждёт разрешения", tool);
                var ask = new Ask(ctx) { Tool = tool, Cwd = Str(d, "cwd"), Detail = Clean(Describe(input), 160), Project = s.Name, SessionId = s.Id };
                ask.Always = FirstAllowRule(d);
                var h = Asked;
                if (h == null || ask.Tool.Length == 0) { ask.Pass(); return; }
                h(ask);
                return;
            }
            if (path == "/claude/pre")
            {
                // Перед каждым действием Claude Code: здесь его можно остановить, а на вопрос с вариантами — ответить.
                if (TakeStop(Str(d, "session_id")))
                {
                    Touch(d, "остановлен", "");
                    Reply(ctx, 200, "{\"continue\":false,\"stopReason\":\"Остановлено с острова Kapsula\"}");
                    return;
                }
                if (tool == "AskUserQuestion")
                {
                    Session qs = Touch(d, "спрашивает", "");
                    Question q = ParseQuestion(ctx, input);
                    var qh = Questioned;
                    if (q == null) { Reply(ctx, 200, ""); return; }
                    q.Project = qs.Name; q.SessionId = qs.Id;
                    if (qh == null) { q.Pass(); return; }
                    qh(q);
                    return;
                }
                string detail = Clean(Describe(input), 120);
                Session ws = Touch(d, "работает", tool + (detail.Length > 0 ? ": " + detail : ""));
                Reply(ctx, 200, "");
                var wh = Working;
                if (wh != null && tool.Length > 0) wh(tool, detail, ws.Name);
                return;
            }
            if (path == "/claude/stop")
            {
                Session ss = Touch(d, "закончил", "");
                var fin = new Finished(ctx) { Text = Clean(Str(d, "last_assistant_message"), 400), Project = ss.Name, SessionId = ss.Id };
                Remember(ss.Id, fin.Text);
                var dh = Done;
                if (dh == null) { fin.Pass(); return; }
                dh(fin);
                return;
            }
            Reply(ctx, 200, "");   // уведомлениям ответ не нужен
            if (path == "/claude/notify") { Session ns = Touch(d, "ждёт ответа", ""); var nh = Waiting; if (nh != null) nh(ns.Name); }
        }

        // Правило «разрешать всегда» из предложений самого Claude Code: берём первое разрешающее.
        static object FirstAllowRule(Dictionary<string, object> d)
        {
            object so;
            var arr = d.TryGetValue("permission_suggestions", out so) ? so as object[] : null;
            if (arr == null) return null;
            foreach (object o in arr)
            {
                var s = o as Dictionary<string, object>;
                if (s != null && Str(s, "type") == "addRules" && Str(s, "behavior") == "allow") return s;
            }
            return null;
        }

        // До четырёх вопросов, у каждого от двух до четырёх вариантов, с одним или несколькими ответами.
        // Что-то сложнее или непонятное — возвращаем null, и Claude Code спросит сам.
        static Question ParseQuestion(System.Net.HttpListenerContext ctx, Dictionary<string, object> input)
        {
            object qo;
            var qs = input != null && input.TryGetValue("questions", out qo) ? qo as object[] : null;
            if (qs == null || qs.Length < 1 || qs.Length > 4) return null;
            var items = new List<QItem>();
            var seen = new HashSet<string>();
            foreach (object q in qs)
            {
                var one = q as Dictionary<string, object>;
                if (one == null) return null;
                object ms, oo;
                var opts = one.TryGetValue("options", out oo) ? oo as object[] : null;
                if (opts == null || opts.Length < 2 || opts.Length > 4) return null;
                var labels = new List<string>();
                var notes = new List<string>();
                foreach (object o in opts)
                {
                    var od = o as Dictionary<string, object>;
                    string l = od != null ? Str(od, "label") : "";
                    if (l.Length == 0) return null;
                    labels.Add(l);
                    notes.Add(Clean(Str(od, "description"), 60));
                }
                string text = Str(one, "question");
                if (text.Length == 0 || !seen.Add(text)) return null;
                items.Add(new QItem { Header = Clean(Str(one, "header"), 40), Text = text, Options = labels.ToArray(), Notes = notes.ToArray(), Multi = one.TryGetValue("multiSelect", out ms) && ms is bool && (bool)ms });
            }
            return new Question(ctx) { Items = items.ToArray(), Questions = qs };
        }

        static string Str(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }

        // Коротко: что именно собирается сделать инструмент
        public static string Describe(Dictionary<string, object> input)
        {
            if (input == null) return "";
            foreach (string k in new string[] { "command", "file_path", "path", "url", "query", "pattern", "description", "prompt" })
            {
                string v = Str(input, k);
                if (v.Length > 0) return v;
            }
            foreach (KeyValuePair<string, object> kv in input) if (kv.Value is string && ((string)kv.Value).Length > 0) return (string)kv.Value;
            return "";
        }

        public static string Clean(string s, int max)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            foreach (char c in s) { if (c == '\r' || c == '\n' || c == '\t') sb.Append(' '); else if (!char.IsControl(c)) sb.Append(c); }
            string r = sb.ToString().Trim();
            return r.Length > max ? r.Substring(0, max - 1) + "…" : r;
        }

        static string MJson(object o) { return new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Serialize(o); }
        static Dictionary<string, object> MObj(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }
        // ---- MCP (Model Context Protocol) по HTTP: любой агент — Codex, Gemini CLI, Claude Code — может вызвать
        // инструменты Kapsula «notify» (показать карточку) и «ask» (спросить с вариантами и дождаться ответа).
        public class McpQuestion
        {
            public string Agent = "", Question = "";
            public string[] Options = new string[0];
            System.Net.HttpListenerContext ctx; object id; int done; Timer timer;
            internal McpQuestion(System.Net.HttpListenerContext c, object rid)
            {
                ctx = c; id = rid;
                timer = new Timer(delegate { Answer(null); }, null, 120000, Timeout.Infinite);
            }
            public void Answer(string choice)
            {
                if (Interlocked.Exchange(ref done, 1) != 0) return;
                try { timer.Dispose(); } catch { }
                string text = choice == null ? "Пользователь не ответил за две минуты." : "Пользователь выбрал: " + choice;
                Reply(ctx, 200, MJson(MObj("jsonrpc", "2.0", "id", id, "result", MObj("content", new object[] { MObj("type", "text", "text", text) }))));
            }
        }
        public static event Action<string, string, string> McpNotified;   // агент, заголовок, текст
        public static event Action<McpQuestion> McpAsked;

        static void HandleMcp(System.Net.HttpListenerContext ctx, Dictionary<string, object> d)
        {
            if (d == null) { Reply(ctx, 400, ""); return; }
            object id; d.TryGetValue("id", out id);
            string method = Str(d, "method");
            object po; var prm = d.TryGetValue("params", out po) ? po as Dictionary<string, object> : null;
            if (id == null) { Reply(ctx, 202, ""); return; }   // уведомления протокола ответа не ждут
            if (method == "initialize")
            {
                string ver = prm != null ? Str(prm, "protocolVersion") : "";
                Reply(ctx, 200, MJson(MObj("jsonrpc", "2.0", "id", id, "result", MObj("protocolVersion", ver.Length > 0 ? ver : "2025-03-26", "capabilities", MObj("tools", MObj()), "serverInfo", MObj("name", "kapsula", "version", "1")))));
                return;
            }
            if (method == "ping") { Reply(ctx, 200, MJson(MObj("jsonrpc", "2.0", "id", id, "result", MObj()))); return; }
            if (method == "tools/list")
            {
                var notify = MObj("name", "notify", "description", "Показать человеку карточку на острове Kapsula вверху экрана: например, что задача готова. Используй, когда закончил долгую работу.",
                    "inputSchema", MObj("type", "object", "properties", MObj("title", MObj("type", "string", "description", "Короткий заголовок"), "message", MObj("type", "string", "description", "Подробности в одну-две строки")), "required", new object[] { "title" }));
                var ask = MObj("name", "ask", "description", "Задать человеку вопрос с вариантами ответа на острове Kapsula и дождаться выбора (до двух минут).",
                    "inputSchema", MObj("type", "object", "properties", MObj("question", MObj("type", "string"), "options", MObj("type", "array", "items", MObj("type", "string"), "description", "От двух до трёх вариантов")), "required", new object[] { "question", "options" }));
                Reply(ctx, 200, MJson(MObj("jsonrpc", "2.0", "id", id, "result", MObj("tools", new object[] { notify, ask }))));
                return;
            }
            if (method == "tools/call" && prm != null)
            {
                string name = Str(prm, "name");
                object ao; var args = prm.TryGetValue("arguments", out ao) ? ao as Dictionary<string, object> : null;
                if (args == null) args = new Dictionary<string, object>();
                string agent = Clean(ctx.Request.Headers["X-Kapsula-Agent"] ?? "", 24);
                if (agent.Length == 0) agent = "Агент";
                if (name == "notify")
                {
                    var h = McpNotified;
                    if (h != null) h(agent, Clean(Str(args, "title"), 80), Clean(Str(args, "message"), 160));
                    Reply(ctx, 200, MJson(MObj("jsonrpc", "2.0", "id", id, "result", MObj("content", new object[] { MObj("type", "text", "text", "Показано на острове.") }))));
                    return;
                }
                if (name == "ask")
                {
                    var q = new McpQuestion(ctx, id) { Agent = agent, Question = Clean(Str(args, "question"), 160) };
                    object oo; var list = new List<string>();
                    if (args.TryGetValue("options", out oo) && oo is object[]) foreach (object x in (object[])oo) { string t = Clean(Convert.ToString(x), 40); if (t.Length > 0 && list.Count < 3) list.Add(t); }
                    q.Options = list.ToArray();
                    var h = McpAsked;
                    if (h == null || q.Options.Length == 0) { q.Answer(null); return; }
                    h(q);
                    return;
                }
            }
            Reply(ctx, 200, MJson(MObj("jsonrpc", "2.0", "id", id, "error", MObj("code", -32601, "message", "Неизвестный метод"))));
        }

        // Строка для подключения: MCP-сервер Kapsula на этом компьютере
        public static string McpUrl { get { return "http://127.0.0.1:" + Port + "/mcp"; } }

        static void Reply(System.Net.HttpListenerContext ctx, int code, string body)
        {
            try
            {
                ctx.Response.StatusCode = code;
                if (!string.IsNullOrEmpty(body))
                {
                    byte[] b = Encoding.UTF8.GetBytes(body);
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = b.Length;
                    ctx.Response.OutputStream.Write(b, 0, b.Length);
                }
                ctx.Response.Close();
            }
            catch { }
        }

        // ---------------------------------------------------------------- настройки Claude Code
        public static string SettingsOverride;   // для проверок: другой файл вместо настоящих настроек Claude Code
        public static string SettingsPath { get { return SettingsOverride ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".claude\settings.json"); } }

        public static bool ClaudeCodeFound { get { return System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(SettingsPath)); } }

        // Прописаны ли наши хуки в настройках Claude Code
        public static bool Installed
        {
            get { try { return System.IO.File.Exists(SettingsPath) && System.IO.File.ReadAllText(SettingsPath).Contains(Marker); } catch { return false; } }
        }

        static readonly string[][] Events =
        {
            new string[] { "PreToolUse", "pre?v=2", "200" },   // долгий срок нужен только вопросам с вариантами; остальному Kapsula отвечает сразу
            new string[] { "PermissionRequest", "permission", "40" },
            new string[] { "Notification", "notify", "5" },
            new string[] { "Stop", "stop", "200" }
        };

        // Прописаны ли все нынешние хуки (после обновления Kapsula их может стать больше)
        public static bool UpToDate
        {
            get
            {
                try
                {
                    string t = System.IO.File.Exists(SettingsPath) ? System.IO.File.ReadAllText(SettingsPath) : "";
                    foreach (string[] ev in Events) if (!t.Contains(Marker + ev[1] + "\"")) return false;
                    return true;
                }
                catch { return false; }
            }
        }

        // Добавляет в настройки Claude Code три хука, ведущие на Kapsula. Остальное содержимое файла не меняется;
        // прежняя версия сохраняется рядом как settings.json.kapsula-bak. Возвращает null или текст ошибки.
        public static string Install()
        {
            try
            {
                string token = Token();
                if (token.Length == 0) return "не удалось создать секретную метку";
                Dictionary<string, object> root = ReadSettings();
                if (root == null) return "файл настроек Claude Code не читается — правлю только исправный файл";
                RemoveOurs(root);
                object ho;
                var hooks = root.TryGetValue("hooks", out ho) ? ho as Dictionary<string, object> : null;
                if (hooks == null) { hooks = new Dictionary<string, object>(); root["hooks"] = hooks; }
                foreach (string[] ev in Events)
                {
                    var hook = new Dictionary<string, object>();
                    hook["type"] = "http";
                    hook["url"] = "http://" + Marker + ev[1];
                    hook["timeout"] = int.Parse(ev[2]);
                    var headers = new Dictionary<string, object>();
                    headers["X-Kapsula-Token"] = token;
                    hook["headers"] = headers;
                    var group = new Dictionary<string, object>();
                    group["hooks"] = new object[] { hook };
                    var list = new List<object>();
                    object existing;
                    if (hooks.TryGetValue(ev[0], out existing) && existing is object[]) list.AddRange((object[])existing);
                    list.Add(group);
                    hooks[ev[0]] = list.ToArray();
                }
                WriteSettings(root);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static string Remove()
        {
            try
            {
                if (!System.IO.File.Exists(SettingsPath)) return null;
                Dictionary<string, object> root = ReadSettings();
                if (root == null) return "файл настроек Claude Code не читается";
                RemoveOurs(root);
                WriteSettings(root);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        static Dictionary<string, object> ReadSettings()
        {
            if (!System.IO.File.Exists(SettingsPath)) return new Dictionary<string, object>();
            string text = System.IO.File.ReadAllText(SettingsPath);
            if (text.Trim().Length == 0) return new Dictionary<string, object>();
            return new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.DeserializeObject(text) as Dictionary<string, object>;
        }

        static void WriteSettings(Dictionary<string, object> root)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath));
            if (System.IO.File.Exists(SettingsPath)) System.IO.File.Copy(SettingsPath, SettingsPath + ".kapsula-bak", true);
            string json = Pretty(new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Serialize(root));
            System.IO.File.WriteAllText(SettingsPath, json, new UTF8Encoding(false));
        }

        // Убирает из настроек только хуки, ведущие на Kapsula; чужие не трогает.
        static void RemoveOurs(Dictionary<string, object> root)
        {
            object ho;
            var hooks = root.TryGetValue("hooks", out ho) ? ho as Dictionary<string, object> : null;
            if (hooks == null) return;
            foreach (string ev in new List<string>(hooks.Keys))
            {
                var groups = hooks[ev] as object[];
                if (groups == null) continue;
                var keepGroups = new List<object>();
                foreach (object g in groups)
                {
                    var gd = g as Dictionary<string, object>;
                    object inner;
                    var arr = gd != null && gd.TryGetValue("hooks", out inner) ? inner as object[] : null;
                    if (arr == null) { keepGroups.Add(g); continue; }
                    var keep = new List<object>();
                    foreach (object h in arr)
                    {
                        var hd = h as Dictionary<string, object>;
                        if (hd != null && Str(hd, "url").Contains(Marker)) continue;
                        keep.Add(h);
                    }
                    if (keep.Count == 0) continue;
                    gd["hooks"] = keep.ToArray();
                    keepGroups.Add(gd);
                }
                if (keepGroups.Count == 0) hooks.Remove(ev); else hooks[ev] = keepGroups.ToArray();
            }
            if (hooks.Count == 0) root.Remove("hooks");
        }

        // Расставляет отступы в JSON, чтобы файл настроек оставался читаемым
        public static string Pretty(string json)
        {
            var sb = new StringBuilder();
            int depth = 0;
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; sb.Append(c); }
                else if (c == '{' || c == '[')
                {
                    char close = c == '{' ? '}' : ']';
                    if (i + 1 < json.Length && json[i + 1] == close) { sb.Append(c).Append(close); i++; continue; }
                    sb.Append(c).Append('\n').Append(' ', ++depth * 2);
                }
                else if (c == '}' || c == ']') sb.Append('\n').Append(' ', --depth * 2).Append(c);
                else if (c == ',') sb.Append(c).Append('\n').Append(' ', depth * 2);
                else if (c == ':') sb.Append(": ");
                else sb.Append(c);
            }
            return sb.Append('\n').ToString();
        }
    }
}
