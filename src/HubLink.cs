// kit.txt — клиент хаба для C#: один файл без зависимостей. Копируется в приложение как есть
// (Kapsula и всё, что появится на C#); протокол — в hub/PROTOCOL.md, тот же клиент для JS — hub/client.js.
// Пишется на C# 5: его понимает компилятор, встроенный в Windows.
//
//   var hub = new HubLink("kapsula", "Kapsula", "0.7.6");
//   hub.Topics = new[] { "hub.theme", "aura.voice" };
//   hub.Commands = new[] { "settings.set" };
//   hub.OnCommand = (name, args, from) => { …; return null; };   // исключение уйдёт ответом «не получилось» с его текстом
//   hub.OnState = (topic, data, from) => { … };
//   hub.Start();
//   hub.State("kapsula.settings", описание);    // хаб помнит и отдаёт тем, кто подпишется позже
//
// Хаба может не быть — это нормально: клиент тихо пробует снова и ничего не бросает.
// Все обратные вызовы приходят в фоновом потоке: к окнам из них — только через Dispatcher.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DevTxt
{
    class HubLink
    {
        public const int Protocol = 1, DefaultPort = 42080;
        const int MaxMessage = 1024 * 1024;

        readonly string app, name, version;
        readonly int port;
        readonly object gate = new object();
        readonly Dictionary<string, object> own = new Dictionary<string, object>();   // что это приложение сказало последним — повторяется после переподключения
        // Отправка — своим потоком: вызывающий (обычно поток окна) только кладёт сообщение в очередь и идёт дальше
        readonly Queue<byte[]> outbox = new Queue<byte[]>();
        readonly AutoResetEvent outboxSignal = new AutoResetEvent(false);
        ClientWebSocket socket;
        Thread thread, sender;
        volatile bool stopped, online;

        public string[] Topics = new string[0], Commands = new string[0];
        public Func<string, Dictionary<string, object>, string, object> OnCommand = null;   // (команда, аргументы, от кого) → данные ответа
        public Action<string, object, string> OnState = null, OnEvent = null;                      // (тема, данные, от кого)
        public Action<bool> OnStatus = null;

        public HubLink(string app, string name, string version) : this(app, name, version, DefaultPort) { }
        public HubLink(string app, string name, string version, int port) { this.app = app; this.name = name; this.version = version; this.port = port; }

        public bool Online { get { return online; } }

        public static Dictionary<string, object> Msg(params object[] pairs)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) d[(string)pairs[i]] = pairs[i + 1];
            return d;
        }

        static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = MaxMessage * 2 }; }

        public void Start()
        {
            if (thread != null) return;
            thread = new Thread(Loop) { IsBackground = true, Name = "dev.txt hub" };
            thread.Start();
            sender = new Thread(SendLoop) { IsBackground = true, Name = "dev.txt hub send" };
            sender.Start();
        }

        public void Stop()
        {
            Send(Msg("type", "bye"));
            stopped = true;
            outboxSignal.Set();
            if (sender != null) sender.Join(1500);   // дать «до свидания» уйти
            ClientWebSocket s;
            lock (gate) { s = socket; socket = null; }
            try { if (s != null) s.Abort(); } catch { }
        }

        // Состояние: хаб его помнит. data == null — состояния больше нет.
        public void State(string topic, object data)
        {
            lock (gate) { if (data == null) own.Remove(topic); else own[topic] = data; }
            Send(Msg("type", "state", "topic", topic, "data", data));
        }

        // Событие: получают только те, кто слушает прямо сейчас
        public void Event(string topic, object data) { Send(Msg("type", "event", "topic", topic, "data", data)); }

        // Просьба к другому приложению. Ответ не ждём: результат виден по его состоянию.
        public void Command(string to, string command, Dictionary<string, object> args)
        {
            Send(Msg("type", "command", "id", app + "-" + Guid.NewGuid().ToString("N").Substring(0, 8), "to", to, "name", command, "args", args ?? new Dictionary<string, object>()));
        }

        void Send(Dictionary<string, object> m)
        {
            byte[] bytes;
            try { bytes = Encoding.UTF8.GetBytes(Json().Serialize(m)); } catch { return; }
            lock (outbox)
            {
                if (outbox.Count > 500) return;   // хаб давно не принимает — не копим без конца; состояния всё равно повторятся после переподключения
                outbox.Enqueue(bytes);
            }
            outboxSignal.Set();
        }

        void SendLoop()
        {
            while (!stopped || OutboxCount() > 0)
            {
                byte[] bytes = null;
                lock (outbox) { if (outbox.Count > 0) bytes = outbox.Dequeue(); }
                if (bytes == null) { outboxSignal.WaitOne(1000); continue; }
                ClientWebSocket s;
                lock (gate) s = socket;
                if (s == null || s.State != WebSocketState.Open) continue;   // связи нет — сообщение пропадает, как и раньше
                try
                {
                    if (!s.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait(5000)) s.Abort();
                }
                catch { }
            }
        }

        int OutboxCount() { lock (outbox) return outbox.Count; }

        void SetOnline(bool on)
        {
            if (online == on) return;
            online = on;
            try { if (OnStatus != null) OnStatus(on); } catch { }
        }

        void Loop()
        {
            int wait = 1000;   // пробуем снова через 1, 2, 4… до 15 секунд
            while (!stopped)
            {
                var s = new ClientWebSocket();
                try
                {
                    s.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    if (!s.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/hub"), CancellationToken.None).Wait(4000)) throw new TimeoutException();
                    lock (outbox) outbox.Clear();   // что писалось прежнему подключению, новому не нужно: первым должно уйти hello
                    lock (gate) socket = s;
                    wait = 1000;
                    Send(Msg("type", "hello", "v", Protocol, "app", app, "name", name, "version", version, "commands", Commands));
                    if (Topics.Length > 0) Send(Msg("type", "subscribe", "topics", Topics));
                    KeyValuePair<string, object>[] mine;
                    lock (gate) { mine = new KeyValuePair<string, object>[own.Count]; ((ICollection<KeyValuePair<string, object>>)own).CopyTo(mine, 0); }
                    foreach (var kv in mine) Send(Msg("type", "state", "topic", kv.Key, "data", kv.Value));
                    Read(s);
                }
                catch { }
                lock (gate) { if (socket == s) socket = null; }
                try { s.Abort(); s.Dispose(); } catch { }
                SetOnline(false);
                for (int slept = 0; slept < wait && !stopped; slept += 200) Thread.Sleep(200);
                wait = Math.Min(wait * 2, 15000);
            }
        }

        void Read(ClientWebSocket s)
        {
            var buf = new byte[16384];
            var ms = new MemoryStream();
            while (!stopped && s.State == WebSocketState.Open)
            {
                var r = s.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
                if (r.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buf, 0, r.Count);
                if (ms.Length > MaxMessage) return;
                if (!r.EndOfMessage) continue;
                string text = Encoding.UTF8.GetString(ms.ToArray());
                ms.SetLength(0);
                Dictionary<string, object> m = null;
                try { m = Json().DeserializeObject(text) as Dictionary<string, object>; } catch { }
                if (m != null) Handle(m);
            }
        }

        static object Get(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) ? v : null; }

        void Handle(Dictionary<string, object> m)
        {
            string type = Get(m, "type") as string;
            if (type == "welcome") { SetOnline(true); return; }
            if (type == "state" || type == "event")
            {
                var h = type == "state" ? OnState : OnEvent;
                try { if (h != null) h(Get(m, "topic") as string, Get(m, "data"), Get(m, "from") as string); } catch { }
                return;
            }
            if (type != "command") return;   // остальное (в том числе из более новой версии протокола) пропускается
            var reply = Msg("type", "result", "id", Get(m, "id"), "to", Get(m, "from"));
            try
            {
                string command = Get(m, "name") as string;
                if (OnCommand == null || Array.IndexOf(Commands, command) < 0) throw new Exception("unknown-command");
                reply["data"] = OnCommand(command, Get(m, "args") as Dictionary<string, object> ?? new Dictionary<string, object>(), Get(m, "from") as string);
                reply["ok"] = true;
            }
            catch (Exception e)
            {
                reply["ok"] = false;
                reply["error"] = e is System.Reflection.TargetInvocationException && e.InnerException != null ? e.InnerException.Message : e.Message;
            }
            Send(reply);
        }
    }
}
