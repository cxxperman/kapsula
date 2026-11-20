// kit.txt. Family.cs — общее для всей семьи dev.txt™ поверх хаба: один значок в трее, «Не беспокоить», игра на весь
// экран, уведомления на острове и список горячих клавиш. Что и зачем — в hub/PROTOCOL.md, раздел «Семья».
// Копируется в приложение как есть, рядом с HubLink.cs. Пишется на C# 5: его понимает компилятор, встроенный в Windows.
//
//   var link = new HubLink("smena", "smena.txt", "0.5.0");
//   link.Topics = …; link.Commands = …; link.OnState = …; link.OnCommand = …;   // свои обработчики — как раньше
//   var family = new Family(link);                 // после своих обработчиков и до link.Start()
//   family.Changed = delegate { … };               // core.family или hub.apps поменялись (фоновый поток!)
//   family.SetTray(new[] { new TrayItem("show", "Открыть", null, Show), new TrayItem("pause", "Приостановить", paused, Pause) });
//   link.Start();
//   …
//   tray.Visible = !family.HideOwnTray;            // свой значок — только когда core.txt его не взял
//   if (!family.Notify("metla.txt", "Можно освободить 12 ГБ", true)) СвоёУведомление();
//
// Все обратные вызовы (Changed, TrayItem.Run) приходят в фоновом потоке хаба: к окнам из них — только через Dispatcher.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace DevTxt
{
    // Что core.txt сообщает всем приложениям — состояние core.family
    class FamilyState
    {
        public bool Tray;            // значок в трее у семьи один — у core.txt; свои приложения прячут
        public bool Focus;           // «Не беспокоить» для всех
        public long FocusUntil;      // когда «Не беспокоить» выключится само: миллисекунды с 1970 года (UTC); 0 — пока не выключат
        public bool Game;            // впереди игра или другое окно на весь экран
        public string GameApp = "";  // имя процесса этого окна без .exe; может быть пустым

        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long Now() { return (long)(DateTime.UtcNow - Epoch).TotalMilliseconds; }

        // Когда «Не беспокоить» кончится, по местному времени; null — выключено или пока не выключат
        public DateTime? FocusEnds
        {
            get { if (!Focus || FocusUntil <= 0) return null; return Epoch.AddMilliseconds(FocusUntil).ToLocalTime(); }
        }

        // null — пришло не то (или core.txt сказал, что состояния больше нет)
        public static FamilyState From(object data)
        {
            var d = data as Dictionary<string, object>;
            if (d == null) return null;
            var s = new FamilyState();
            s.Tray = Flag(d, "tray");
            s.Focus = Flag(d, "focus");
            s.Game = Flag(d, "game");
            s.FocusUntil = Number(d, "focusUntil");
            object v;
            s.GameApp = d.TryGetValue("gameApp", out v) && v is string ? (string)v : "";
            return s;
        }

        public Dictionary<string, object> ToData()
        {
            return HubLink.Msg("tray", Tray, "focus", Focus, "focusUntil", FocusUntil, "game", Game, "gameApp", GameApp ?? "");
        }

        static bool Flag(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v is bool && (bool)v;
        }

        static long Number(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null || v is bool || v is string) return 0;
            try
            {
                double n = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                return double.IsNaN(n) || double.IsInfinity(n) || n < 0 ? 0 : (long)n;
            }
            catch { return 0; }
        }
    }

    // Пункт быстрого меню приложения в общем трее core.txt
    class TrayItem
    {
        public readonly string Key, Label;
        public readonly bool? Check;     // null — просто действие, иначе пункт с галочкой
        public readonly bool Enabled;
        public readonly Action Run;      // вызывается в потоке хаба: к окнам — через Dispatcher

        public TrayItem(string key, string label, bool? check, Action run) : this(key, label, check, true, run) { }

        public TrayItem(string key, string label, bool? check, bool enabled, Action run)
        {
            Key = key; Label = label; Check = check; Enabled = enabled; Run = run;
        }

        public Dictionary<string, object> ToData()
        {
            var d = HubLink.Msg("key", Key, "label", Label);
            if (Check.HasValue) d["check"] = Check.Value;
            if (!Enabled) d["enabled"] = false;
            return d;
        }

        // Пункты из чужого <app>.tray (для core.txt): без Run, пустые и лишние отброшены
        public static List<TrayItem> From(object data)
        {
            var list = new List<TrayItem>();
            var d = data as Dictionary<string, object>;
            object items;
            if (d == null || !d.TryGetValue("items", out items)) return list;
            var all = items as System.Collections.IEnumerable;
            if (all == null || items is string) return list;
            foreach (object o in all)
            {
                var i = o as Dictionary<string, object>;
                if (i == null) continue;
                object k, l, c, e;
                i.TryGetValue("key", out k); i.TryGetValue("label", out l); i.TryGetValue("check", out c); i.TryGetValue("enabled", out e);
                string key = k as string, label = l as string;
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(label)) continue;
                list.Add(new TrayItem(key, label.Length > 48 ? label.Substring(0, 47) + "…" : label, c is bool ? (bool?)(bool)c : null, !(e is bool) || (bool)e, null));
                if (list.Count == Family.MaxTray) break;
            }
            return list;
        }
    }

    // Горячая клавиша приложения — для общей страницы «Клавиши» в core.txt
    class KeyInfo
    {
        public readonly string Id, Label, Keys;
        public readonly bool On;

        public KeyInfo(string id, string label, string keys, bool on) { Id = id; Label = label; Keys = keys ?? ""; On = on; }

        public Dictionary<string, object> ToData() { return HubLink.Msg("id", Id, "label", Label, "keys", Keys, "on", On); }

        // Сочетания из чужого <app>.keys (для core.txt)
        public static List<KeyInfo> From(object data)
        {
            var list = new List<KeyInfo>();
            var d = data as Dictionary<string, object>;
            object keys;
            if (d == null || !d.TryGetValue("keys", out keys)) return list;
            var all = keys as System.Collections.IEnumerable;
            if (all == null || keys is string) return list;
            foreach (object o in all)
            {
                var i = o as Dictionary<string, object>;
                if (i == null) continue;
                object id, l, k, on;
                i.TryGetValue("id", out id); i.TryGetValue("label", out l); i.TryGetValue("keys", out k); i.TryGetValue("on", out on);
                if (!(l is string) || !(k is string)) continue;
                list.Add(new KeyInfo(id as string ?? "", (string)l, (string)k, !(on is bool) || (bool)on));
                if (list.Count == 64) break;
            }
            return list;
        }
    }

    // Сочетания клавиш одной строкой: Ctrl, Alt, Shift, Win в этом порядке, затем клавиша — «Ctrl+Alt+S», «Win», «Alt+Tab»
    static class KeyNames
    {
        // mods — как у RegisterHotKey: 1 Alt, 2 Ctrl, 4 Shift, 8 Win; vk — код клавиши Windows (0 — одни модификаторы)
        public static string Text(int mods, int vk)
        {
            var parts = new List<string>();
            if ((mods & 2) != 0) parts.Add("Ctrl");
            if ((mods & 1) != 0) parts.Add("Alt");
            if ((mods & 4) != 0) parts.Add("Shift");
            if ((mods & 8) != 0 || vk == 0x5B || vk == 0x5C) parts.Add("Win");
            if (vk != 0 && vk != 0x5B && vk != 0x5C && !IsModifier(vk)) parts.Add(Name(vk));
            return string.Join("+", parts.ToArray());
        }

        static bool IsModifier(int vk) { return vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5); }

        public static string Name(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);
            switch (vk)
            {
                case 0x08: return "Backspace";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x13: return "Pause";
                case 0x14: return "CapsLock";
                case 0x1B: return "Esc";
                case 0x20: return "Space";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x2C: return "PrintScreen";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x6A: return "Num*";
                case 0x6B: return "Num+";
                case 0x6D: return "Num-";
                case 0x6E: return "Num.";
                case 0x6F: return "Num/";
                case 0xAD: return "Mute";
                case 0xAE: return "VolumeDown";
                case 0xAF: return "VolumeUp";
                case 0xB0: return "NextTrack";
                case 0xB1: return "PrevTrack";
                case 0xB3: return "PlayPause";
                case 0xBA: return ";";
                case 0xBB: return "=";
                case 0xBC: return ",";
                case 0xBD: return "-";
                case 0xBE: return ".";
                case 0xBF: return "/";
                case 0xC0: return "`";
                case 0xDB: return "[";
                case 0xDC: return "\\";
                case 0xDD: return "]";
                case 0xDE: return "'";
            }
            return "0x" + vk.ToString("X2");
        }

        static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ctrl", "Ctrl" }, { "control", "Ctrl" }, { "ctl", "Ctrl" }, { "commandorcontrol", "Ctrl" }, { "cmdorctrl", "Ctrl" },
            { "alt", "Alt" }, { "option", "Alt" }, { "menu", "Alt" }, { "altgr", "Alt" },
            { "shift", "Shift" },
            { "win", "Win" }, { "windows", "Win" }, { "meta", "Win" }, { "super", "Win" }, { "cmd", "Win" }, { "command", "Win" }, { "lwin", "Win" }, { "rwin", "Win" },
            { "esc", "Esc" }, { "escape", "Esc" }, { "return", "Enter" }, { "enter", "Enter" }, { "space", "Space" }, { "spacebar", "Space" },
            { "tab", "Tab" }, { "backspace", "Backspace" }, { "del", "Delete" }, { "delete", "Delete" }, { "ins", "Insert" }, { "insert", "Insert" },
            { "home", "Home" }, { "end", "End" }, { "pgup", "PageUp" }, { "pageup", "PageUp" }, { "pgdn", "PageDown" }, { "pagedown", "PageDown" },
            { "left", "Left" }, { "right", "Right" }, { "up", "Up" }, { "down", "Down" },
            { "prtsc", "PrintScreen" }, { "printscreen", "PrintScreen" }, { "print", "PrintScreen" }, { "pause", "Pause" }, { "capslock", "CapsLock" },
            { "backquote", "`" }, { "grave", "`" }, { "~", "`" }, { "plus", "=" },
            { "mediaplaypause", "PlayPause" }, { "playpause", "PlayPause" }, { "medianexttrack", "NextTrack" }, { "nexttrack", "NextTrack" },
            { "mediaprevioustrack", "PrevTrack" }, { "prevtrack", "PrevTrack" }, { "volumemute", "Mute" }, { "mute", "Mute" },
            { "volumeup", "VolumeUp" }, { "volumedown", "VolumeDown" }
        };

        // Одна запись для сравнения: регистр, пробелы и порядок модификаторов не важны («alt + ctrl+s» → «Ctrl+Alt+S»).
        // Пустая строка — сочетания нет.
        public static string Normal(string keys)
        {
            if (string.IsNullOrEmpty(keys)) return "";
            bool ctrl = false, alt = false, shift = false, win = false;
            string key = null;
            string text = keys.Trim();
            // «Ctrl++» — плюс сам по себе клавиша
            if (text.EndsWith("++")) { key = "="; text = text.Substring(0, text.Length - 2); }
            foreach (string raw in text.Split('+'))
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                string a;
                if (!Aliases.TryGetValue(p, out a)) a = p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p.Substring(1);
                if (a == "Ctrl") ctrl = true;
                else if (a == "Alt") alt = true;
                else if (a == "Shift") shift = true;
                else if (a == "Win") win = true;
                else key = a;
            }
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            if (win) parts.Add("Win");
            if (key != null) parts.Add(key);
            return string.Join("+", parts.ToArray());
        }
    }

    // Окно на весь экран впереди — одно правило для всей семьи. core.txt проверяет его за всех и сообщает в core.family;
    // приложение зовёт само только без core.txt или когда ответ нужен прямо сейчас (smena.txt в момент Alt+Tab).
    static class FullScreen
    {
        // Окна самих приложений dev.txt (снимок области, заставка) игрой не считаются
        static readonly string[] Family = { "Core", "Kapsula", "Aura", "Dock", "Smena", "Metla" };
        static readonly int Self = Process.GetCurrentProcess().Id;
        static int lastPid;
        static string lastName = "";

        // Окно ровно на весь свой монитор, без заголовка и рамки, не развёрнутое. Развёрнутое окно без заголовка (Steam,
        // программы на Electron) — обычное окно: оно развёрнуто, у него есть рамка или оно выходит за края монитора.
        public static bool Is(int left, int top, int right, int bottom, int monLeft, int monTop, int monRight, int monBottom, bool caption, bool maximized, bool thickFrame)
        {
            return !caption && !maximized && !thickFrame && left == monLeft && top == monTop && right == monRight && bottom == monBottom;
        }

        public static bool Ahead() { string p; return Ahead(out p); }

        // process — имя процесса этого окна без .exe (пустое, если не на весь экран или не узнать)
        public static bool Ahead(out string process)
        {
            process = "";
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                long ex = Long(fg, -20);   // GWL_EXSTYLE
                if ((ex & (0x80 | 0x08000000 | 0x20)) != 0) return false;   // WS_EX_TOOLWINDOW, WS_EX_NOACTIVATE, WS_EX_TRANSPARENT: невидимые слои поверх экрана
                int cloaked;
                if (DwmGetWindowAttribute(fg, 14, out cloaked, 4) == 0 && cloaked != 0) return false;   // DWMWA_CLOAKED: окно спрятано системой
                var cls = new StringBuilder(64);
                GetClassName(fg, cls, cls.Capacity);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" || c == "XamlExplorerHostIslandWindow"
                    || c == "Windows.UI.Core.CoreWindow" || c == "CEF-OSC-WIDGET" || c == "MultitaskingViewFrame" || c == "ForegroundStaging") return false;
                RECT r;
                if (!GetWindowRect(fg, out r)) return false;
                var info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (!GetMonitorInfo(MonitorFromWindow(fg, 2), ref info)) return false;
                long style = Long(fg, -16);   // GWL_STYLE
                const long Caption = 0x00C00000L, Maximize = 0x01000000L, ThickFrame = 0x00040000L;
                RECT m = info.Monitor;
                if (!Is(r.Left, r.Top, r.Right, r.Bottom, m.Left, m.Top, m.Right, m.Bottom, (style & Caption) == Caption, (style & Maximize) != 0, (style & ThickFrame) != 0)) return false;
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                if ((int)pid == Self) return false;
                string name = Name((int)pid);
                foreach (string f in Family) if (string.Equals(name, f, StringComparison.OrdinalIgnoreCase)) return false;
                process = name;
                return true;
            }
            catch { return false; }
        }

        // Имя процесса: окно впереди меняется редко, поэтому последнее запоминается
        static string Name(int pid)
        {
            if (pid == lastPid) return lastName;
            string name = "";
            try { using (var p = Process.GetProcessById(pid)) name = p.ProcessName; } catch { }
            lastPid = pid; lastName = name;
            return name;
        }

        static long Long(IntPtr h, int index) { return IntPtr.Size == 8 ? GetWindowLongPtr(h, index).ToInt64() : GetWindowLong(h, index); }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder name, int max);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO info);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    }

    // Связь приложения с семьёй: слушает core.family и hub.apps, отвечает на tray.run, отдаёт <app>.tray и <app>.keys
    class Family
    {
        public const string Topic = "core.family", TrayRun = "tray.run", NotifyCommand = "notify", FocusSet = "focus.set", QuitCommand = "quit";
        public const int MaxTray = 4;

        readonly HubLink link;
        readonly string app;
        readonly object gate = new object();
        FamilyState state;                                                        // null — core.txt не на связи
        readonly Dictionary<string, HashSet<string>> apps = new Dictionary<string, HashSet<string>>();   // кто подключён → что умеет
        TrayItem[] tray = new TrayItem[0];
        string sentTray, sentKeys;                                                // что ушло последним — одно и то же не шлём

        // core.family или hub.apps поменялись, связь пропала. Фоновый поток!
        public Action Changed = null;

        // Создаётся после того, как приложение назначило свои Topics, Commands и обработчики, и до link.Start()
        public Family(HubLink link, string app) : this(link, app, null) { }

        // quit — как закрыть приложение по пункту «Закрыть» в общем трее core.txt (пока своего значка нет, это
        // единственный выход). Вызывается в потоке хаба: к окнам — через Dispatcher. null — команды quit у приложения нет.
        public Family(HubLink link, string app, Action quit)
        {
            this.link = link;
            this.app = app;
            link.Topics = With(link.Topics, Topic, "hub.apps");
            link.Commands = quit != null ? With(link.Commands, TrayRun, QuitCommand) : With(link.Commands, TrayRun);
            var ownState = link.OnState;
            var ownCommand = link.OnCommand;
            var ownStatus = link.OnStatus;
            link.OnState = delegate(string topic, object data, string from)
            {
                bool ours = false;
                if (topic == Topic && from == "core") { lock (gate) state = FamilyState.From(data); ours = true; }
                else if (topic == "hub.apps" && from == "hub") { lock (gate) TakeApps(data); ours = true; }
                if (ours) Raise();
                if (ownState != null) ownState(topic, data, from);
            };
            link.OnCommand = delegate(string name, Dictionary<string, object> args, string from)
            {
                if (name == QuitCommand && quit != null) { quit(); return null; }
                if (name != TrayRun)
                {
                    if (ownCommand == null) throw new Exception("unknown-command");
                    return ownCommand(name, args, from);
                }
                object k;
                args.TryGetValue("key", out k);
                TrayItem item = null;
                lock (gate) foreach (var t in tray) if (t.Key == k as string) item = t;
                if (item == null) throw new Exception("такого пункта нет");
                if (!item.Enabled) throw new Exception("пункт сейчас недоступен");
                if (item.Run != null) item.Run();
                return null;
            };
            link.OnStatus = delegate(bool on)
            {
                if (!on) { lock (gate) { state = null; apps.Clear(); } Raise(); }
                if (ownStatus != null) ownStatus(on);
            };
        }

        static string[] With(string[] list, params string[] more)
        {
            var all = new List<string>(list ?? new string[0]);
            foreach (string m in more) if (!all.Contains(m)) all.Add(m);
            return all.ToArray();
        }

        void TakeApps(object data)
        {
            apps.Clear();
            var all = data as System.Collections.IEnumerable;
            if (all == null || data is string) return;
            foreach (object o in all)
            {
                var d = o as Dictionary<string, object>;
                object a, c;
                if (d == null || !d.TryGetValue("app", out a) || !(a is string)) continue;
                var set = new HashSet<string>();
                var cmds = d.TryGetValue("commands", out c) ? c as System.Collections.IEnumerable : null;
                if (cmds != null && !(c is string)) foreach (object x in cmds) if (x is string) set.Add((string)x);
                apps[(string)a] = set;
            }
        }

        void Raise()
        {
            var h = Changed;
            if (h == null) return;
            try { h(); } catch { }
        }

        // Что сейчас сообщил core.txt; null — его нет на связи
        public FamilyState State { get { if (!link.Online) return null; lock (gate) return state; } }

        public bool CoreOnline { get { return State != null; } }

        // Свой значок в трее не нужен: core.txt показывает один на всех
        public bool HideOwnTray { get { var s = State; return s != null && s.Tray; } }

        // «Не беспокоить», включённое в core.txt
        public bool Focus { get { var s = State; return s != null && s.Focus; } }

        // Впереди игра или другое окно на весь экран — по общему правилу core.txt
        public bool Game { get { var s = State; return s != null && s.Game; } }

        // Приложение other подключено и понимает команду command
        public bool Has(string other, string command)
        {
            if (!link.Online) return false;
            lock (gate) { HashSet<string> c; return apps.TryGetValue(other, out c) && c.Contains(command); }
        }

        // Остров kapsula.txt на связи и принимает уведомления
        public bool IslandOnline { get { return Has("kapsula", NotifyCommand); } }

        // Уведомление на острове. false — острова нет: покажите своё, как раньше.
        // open — щелчок по карточке пришлёт этому приложению команду show.
        public bool Notify(string title, string text, bool open)
        {
            if (!IslandOnline) return false;
            var args = HubLink.Msg("title", Cut(title, 60), "text", Cut(text, 200));
            if (open) args["open"] = true;
            link.Command("kapsula", NotifyCommand, args);
            return true;
        }

        // Включить или выключить «Не беспокоить» для всех. minutes <= 0 — пока не выключат.
        // false — core.txt нет: у приложения остаётся своё «Не беспокоить».
        public bool SetFocus(bool on, int minutes)
        {
            if (!Has("core", FocusSet)) return false;
            var args = HubLink.Msg("on", on);
            if (on && minutes > 0) args["minutes"] = minutes;
            link.Command("core", FocusSet, args);
            return true;
        }

        // Быстрые действия для общего меню в трее (не больше четырёх). Шлётся, только если что-то изменилось.
        public void SetTray(IList<TrayItem> items)
        {
            var list = new List<TrayItem>();
            if (items != null) foreach (var t in items) if (t != null && list.Count < MaxTray) list.Add(t);
            var data = new List<object>();
            foreach (var t in list) data.Add(t.ToData());
            var state = HubLink.Msg("items", data.ToArray());
            string text = Text(state);
            lock (gate)
            {
                tray = list.ToArray();
                if (text == sentTray) return;
                sentTray = text;
            }
            link.State(app + ".tray", state);
        }

        // Горячие клавиши приложения для страницы «Клавиши» в core.txt. Шлётся, только если что-то изменилось.
        public void SetKeys(IList<KeyInfo> keys)
        {
            var data = new List<object>();
            if (keys != null) foreach (var k in keys) if (k != null && data.Count < 64) data.Add(k.ToData());
            var state = HubLink.Msg("keys", data.ToArray());
            string text = Text(state);
            lock (gate)
            {
                if (text == sentKeys) return;
                sentKeys = text;
            }
            link.State(app + ".keys", state);
        }

        static string Text(object o)
        {
            try { return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(o); } catch { return Guid.NewGuid().ToString(); }
        }

        static string Cut(string s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}
