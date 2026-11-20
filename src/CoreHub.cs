// Kapsula — «остров» для Windows. Часть исходного кода: CoreHub.cs
// Связь с core.txt — лаунчером dev.txt. Он держит на этом компьютере хаб (127.0.0.1:42080), через который
// приложения видят друг друга. Kapsula отдаёт туда свои повседневные настройки — они появляются на общей
// странице настроек в core.txt рядом с настройками остальных приложений — и принимает оттуда изменения.
// Оттуда же приходит общий вид: «Меньше движения» для всех приложений разом.
// И туда же Kapsula рассказывает то, что и так знает: какой трек играет, какая погода, что сегодня в календаре,
// сколько непрочитанного и насколько загружен компьютер, — чтобы остальным приложениям dev.txt не узнавать это заново.
// core.txt может быть не запущен — это нормально: Kapsula работает сама по себе и тихо пробует подключиться снова.
// С core.txt Kapsula живёт по общим правилам семьи dev.txt (Family.cs, раздел «Семья» в PROTOCOL.md): значок в трее
// у семьи один — у core.txt, «Не беспокоить» одно на всех, быстрые действия и сочетания клавиш видны в core.txt,
// а короткие сообщения других приложений (команда notify) приходят карточками на остров.
// Протокол описан в kit.txt (hub/PROTOCOL.md); HubLink.cs и Family.cs скопированы оттуда как есть.
using System;
using System.Collections.Generic;
using System.Windows.Threading;
using DevTxt;

namespace Kapsula
{
    static class CoreHub
    {
        static HubLink link;
        static Family family;
        static IslandWindow island;
        static string last = "";
        static DispatcherTimer timer;

        // Для проверок: хаб на другом порту, рядом с настоящим core.txt (как KAPSULA_DISCORD_PORT у звонков Discord)
        public static int TestPort
        {
            get { int p; return int.TryParse(Environment.GetEnvironmentVariable("KAPSULA_HUB_PORT"), out p) && p > 0 ? p : 0; }
        }

        // Одна настройка-переключатель: как её показать, как узнать значение и как поменять.
        // Названия и подсказки — те же, что в окне настроек Kapsula.
        class Flag
        {
            public string Group, Key, Label, Hint;
            public string Section;     // общая страница core.txt, где показывать (SectionOf); null — страница kapsula.txt
            public Func<bool> Get;
            public Action<bool> Set;   // null — обычный флаг острова: island.SetFlag(Key, v)
            public PrefPair Choice;    // сводный выбор (type choice) из двух прежних ключей (Merged.cs); тогда Get и Set не нужны
            public Func<bool> Shown;   // null — пункт в описании всегда; иначе — только пока Shown(). Команда settings.set меняет его и скрытым
        }

        static List<Flag> flags;

        static void Add(string group, string key, string label, string hint, Func<bool> get) { Add(group, key, label, hint, get, null); }
        static void Add(string group, string key, string label, string hint, Func<bool> get, Action<bool> set)
        {
            flags.Add(new Flag { Group = group, Key = key, Label = label, Hint = hint, Section = SectionOf(key), Get = get, Set = set });
        }

        // Сводный выбор: ключ для core.txt — key (он же PrefPair.Id), значения — варианты выбора (Merged.cs)
        static void Add(string group, string key, string label, string hint, PrefPair choice)
        {
            flags.Add(new Flag { Group = group, Key = key, Label = label, Hint = hint, Section = SectionOf(key), Choice = choice });
        }

        // Последний добавленный пункт виден в описании, только пока when()
        static void When(Func<bool> when) { flags[flags.Count - 1].Shown = when; }

        // Прежний ключ, которого в описании больше нет (его заменил сводный выбор): core.txt или кто-то ещё может прислать
        // его по-старому — и он работает как раньше, с тем же смыслом
        static void Old(string key, Func<bool> get)
        {
            flags.Add(new Flag { Group = "", Key = key, Get = get, Shown = delegate { return false; } });
        }

        // Где core.txt показывает настройку (поле section, PROTOCOL.md — «Семья»): games — страница «Игры»,
        // startup — «Общие» → «Запуск и обновления», family — «Общие» → «Связь между приложениями»; null — страница kapsula.txt.
        // Ключи и значения от этого не меняются — переезжает только место, где их видно.
        public static string SectionOf(string key)
        {
            switch (key)
            {
                case "showInGames": case "fsTrack": case "gameLoad": return "games";
                case "autostart": case "autoUpdate": return "startup";
                case "share": case "hubShare": case "hubUnread": return "family";
            }
            return null;
        }

        static void Describe()
        {
            flags = new List<Flag>();
            Add("", "autoHide", "Автоскрытие", "Остров уезжает за край и возвращается, когда курсор у верхней кромки экрана или есть что показать", delegate { return island.AutoHide; });
            Add("", "seconds", "Секунды в часах", "В капсуле вместо 12:30 будет 12:30:45", delegate { return island.ShowSeconds; });
            // «Уведомления Windows» — один выбор вместо двух переключателей, как в окне настроек (Merged.WindowsNotify)
            Add("", "windowsNotify", "Уведомления Windows", "Новые уведомления других программ — карточкой на острове; на диск не сохраняются. «Только остров» убирает окошко Windows", Merged.WindowsNotify);
            Add("", "rim", "Светлая кромка", "Тонкий блик по краю острова — он не сливается с тёмными обоями и окнами", delegate { return island.RimOn; });
            Add("", "shadow", "Тень под островом", "Мягкая тень: на светлых окнах остров словно парит над ними", delegate { return island.ShadowOn; });

            Add("Зверёк", "eyes", "Зверёк в капсуле", "Следит за курсором, пугается таймера, радуется удаче", delegate { return island.ShowEyes; });
            Add("Зверёк", "petRide", "Зверёк в переходах", "Выпрыгивает из капсулы на край панели и переходит туда, куда вы смотрите", delegate { return island.RideOn; });
            Add("Зверёк", "walk", "Зверёк гуляет по окнам", "Когда остров свёрнут и ничего не происходит, выходит посидеть на краю окна", delegate { return island.WalkOn; });
            Add("Зверёк", "petSeason", "Сезонные наряды зверька", "Колпак под Новый год, шляпа в конце октября, шарф зимой", delegate { return Store.Get("petSeason") != "0"; });

            Add("Музыка", "miniPlayer", "Мини-плеер при наведении", "Во время музыки наведение сначала открывает мини-плеер, клик по нему — всю панель", Merged.Mini);
            Add("Музыка", "musicEdge", "Край во время музыки", "Нижний край острова: ровный, пляшет от громкости или как эквалайзер — слева бас, справа высокие", Merged.Edge);
            Add("Музыка", "lyrics", "Тексты песен", "Загружаются с lrclib.net для играющего трека", delegate { return island.LyricsEnabled; });

            // Что остров делает, пока впереди игра или видео на весь экран, — на странице «Игры» в core.txt.
            // Меняются так же, как в окне настроек: island.SetFlag(ключ, значение)
            Add("Игры", "showInGames", "Показывать остров в играх", "Остров остаётся на месте поверх игр и видео на весь экран, а не прячется за край", delegate { return Store.Get("showInGames") == "1"; });
            Add("Игры", "fsTrack", "Новый трек поверх игр и видео", "Спрятанный от игры остров выглядывает на пару секунд с названием нового трека", delegate { return Store.Get("fsTrack") != "0"; });
            Add("Игры", "gameLoad", "Нагрузка в капсуле во время игры", "Пока остров показан поверх игры («Показывать остров в играх»), капсула вместо часов показывает процессор, видеокарту и память", delegate { return Store.Get("gameLoad") == "1"; });

            // Чем Kapsula делится с остальными приложениями dev.txt. Выключено — соответствующие состояния уходят как null.
            // Один переключатель на оба ключа, пока они совпадают; разошлись — два прежних, чтобы было видно как есть (Merged.ShareShown)
            Add("Связь с другими приложениями", "share", "Делиться с приложениями dev.txt", "Трек, погода и сегодняшние события — только название и время; непрочитанное в Discord и Steam — только числа. Дальше этого компьютера не уходит", delegate { return ShareOn && ShareUnreadOn; }, delegate(bool v) { island.SetFlag("hubShare", v); island.SetFlag("hubUnread", v); });
            When(delegate { return Merged.ShareShown("share", ShareOn, ShareUnreadOn); });
            Add("Связь с другими приложениями", "hubShare", "Показывать музыку, погоду и события другим приложениям dev.txt", "Трек, который сейчас играет, погода и сегодняшние события календаря — только название и время. Дальше этого компьютера не уходит", delegate { return ShareOn; });
            When(delegate { return Merged.ShareShown("hubShare", ShareOn, ShareUnreadOn); });
            Add("Связь с другими приложениями", "hubUnread", "Показывать счётчик непрочитанного", "Сколько сообщений из Discord и Steam вы ещё не видели — только числа, без текста", delegate { return ShareUnreadOn; });
            When(delegate { return Merged.ShareShown("hubUnread", ShareOn, ShareUnreadOn); });

            Add("Запуск и обновления", "autostart", "Запускать вместе с Windows", null, delegate { return Autostart.IsOn(); }, delegate(bool v) { Autostart.Set(v); });
            Add("Запуск и обновления", "autoUpdate", "Автообновление", "kapsula.txt сама скачивает новые версии с GitHub и сверяет контрольную сумму файла", delegate { return Store.Get("autoUpdate") != "0"; }, delegate(bool v) { Store.Set("autoUpdate", v ? "1" : "0"); });

            // Прежние ключи, которые заменили сводные выборы: в описании их нет, но settings.set с ними работает по-старому
            Old("notifications", delegate { return island.ShowNotifications; });
            Old("hoverMini", delegate { return island.HoverMini; });
            Old("liquid", delegate { return island.LiquidOn; });
        }

        // Какие ключи знает Kapsula: published — только те, что сейчас в описании для core.txt (без прежних и скрытых);
        // иначе — все, которые принимает settings.set. Для проверок
        public static string[] Keys(bool published)
        {
            if (flags == null) Describe();
            var keys = new List<string>();
            foreach (var f in flags)
            {
                bool shown = true;
                if (published) { try { shown = f.Shown == null || f.Shown(); } catch { shown = false; } }
                if (shown) keys.Add(f.Key);
            }
            return keys.ToArray();
        }

        public static void Start(IslandWindow w)
        {
            if (link != null) return;
            island = w;
            Describe();
            link = new HubLink("kapsula", "kapsula.txt", Updater.CurrentText, TestPort > 0 ? TestPort : HubLink.DefaultPort);
            link.Topics = new string[] { "hub.theme" };
            link.Commands = new string[] { "settings.set", "show", Family.NotifyCommand, "media.toggle", "media.prev", "media.next", "discord.connect" };
            link.OnCommand = Command;
            link.OnState = delegate(string topic, object data, string from)
            {
                if (topic != "hub.theme") return;
                var theme = data as Dictionary<string, object>;
                object reduced = null;
                bool on = theme != null && theme.TryGetValue("reducedMotion", out reduced) && reduced is bool && (bool)reduced;
                string accent = ThemeAccent(data);
                island.Dispatcher.BeginInvoke(new Action(delegate { island.SetSharedReducedMotion(on); island.SetSharedAccent(accent); }));
            };
            // Семья dev.txt (Family.cs): слушает core.family и hub.apps, отвечает на tray.run. Создаётся после своих
            // обработчиков и до link.Start(); Changed приходит в фоновом потоке хаба — к окну только через Dispatcher
            family = new Family(link, "kapsula", delegate { island.Dispatcher.BeginInvoke(new Action(delegate { island.Quit(); })); });   // «Закрыть» в трее core.txt
            family.Changed = delegate { island.Dispatcher.BeginInvoke(new Action(delegate { island.FamilyChanged(); })); };
            link.Start();
            // Настройки меняют и в окне Kapsula, и горячими клавишами — проще сверяться раз в пару секунд, чем ловить каждое место.
            // Быстрые действия и сочетания клавиш — так же: Family отправляет их, только если что-то поменялось
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += delegate { Publish(false); island.HubShareAll(); PublishFamily(); };
            timer.Start();
            Publish(true);
            island.HubShareAll();
            PublishFamily();
        }

        public static void Stop()
        {
            if (link != null) link.Stop();
        }

        // Описание настроек — в том виде, в каком его ждёт core.txt. Вызывается в потоке окна.
        public static Dictionary<string, object> Settings()
        {
            var groups = new List<object>();
            Dictionary<string, object> group = null;
            List<object> items = null;
            foreach (var f in flags)
            {
                bool shown;
                try { shown = f.Label != null && (f.Shown == null || f.Shown()); } catch { continue; }
                if (!shown) continue;   // прежний ключ или пункт, который сейчас не показывается (Shown)
                if (group == null || (string)group["title"] != f.Group)
                {
                    if (group != null) group["items"] = items.ToArray();
                    items = new List<object>();
                    group = HubLink.Msg("title", f.Group);
                    groups.Add(group);
                }
                Dictionary<string, object> item;
                try { item = Item(f); } catch { continue; }
                items.Add(item);
            }
            if (group != null) group["items"] = items.ToArray();
            return HubLink.Msg("groups", groups.ToArray());
        }

        // Один пункт описания: переключатель (switch) или сводный выбор (choice) с вариантами — PROTOCOL.md, «Настройки приложения»
        static Dictionary<string, object> Item(Flag f)
        {
            Dictionary<string, object> item;
            if (f.Choice != null)
            {
                var options = new List<object>();
                for (int i = 0; i < f.Choice.Values.Length; i++) options.Add(HubLink.Msg("value", f.Choice.Values[i], "label", f.Choice.Labels[i]));
                string value = Merged.Value(f.Choice, IslandWindow.Pref(f.Choice.A), IslandWindow.Pref(f.Choice.B));
                item = HubLink.Msg("key", f.Key, "type", "choice", "label", f.Label, "value", value, "options", options.ToArray());
            }
            else item = HubLink.Msg("key", f.Key, "type", "switch", "label", f.Label, "value", f.Get());
            if (f.Hint != null) item["hint"] = f.Hint;
            if (f.Section != null) item["section"] = f.Section;
            return item;
        }

        // Отправляет описание, только если что-то изменилось (или если просили обязательно)
        static void Publish(bool always)
        {
            if (link == null) return;
            var now = Settings();
            string text = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(now);
            if (!always && text == last) return;
            last = text;
            link.State("kapsula.settings", now);
        }

        // Меняет настройку по её ключу. Вызывается в потоке окна; неизвестный ключ или не то значение — исключение.
        // Сводный выбор ждёт строку-вариант, переключатель — «да» или «нет». Прежние ключи и скрытые пункты тоже принимаются.
        public static void Apply(string key, object value)
        {
            Flag f; int choice; bool on;
            Resolve(key, value, out f, out choice, out on);
            if (f.Choice != null) island.SetPair(f.Choice, choice);
            else if (f.Set != null) f.Set(on);
            else island.SetFlag(key, on);
        }

        // Что сделает settings.set, без самого действия: пункт описания, вариант выбора или «да»/«нет». Не то — исключение
        static void Resolve(string key, object value, out Flag f, out int choice, out bool on)
        {
            if (flags == null) Describe();
            f = flags.Find(delegate(Flag x) { return x.Key == key; });
            choice = -1; on = false;
            if (f == null) throw new Exception("такой настройки нет");
            if (f.Choice != null)
            {
                choice = Merged.Index(f.Choice, value);
                if (choice < 0) throw new Exception("такого варианта нет");
                return;
            }
            if (!(value is bool)) throw new Exception("нужно «да» или «нет»");
            on = (bool)value;
        }

        // Для проверок — что сделает settings.set: «pair windowsNotify 2» (сводный выбор, номер варианта), «own share True»
        // (своё действие пункта), «flag liquid False» (island.SetFlag) или текст ошибки, с которым придёт отказ
        public static string Plan(string key, object value)
        {
            Flag f; int choice; bool on;
            try { Resolve(key, value, out f, out choice, out on); }
            catch (Exception e) { return e.Message; }
            if (f.Choice != null) return "pair " + f.Choice.Id + " " + choice;
            return (f.Set != null ? "own " : "flag ") + key + " " + on;
        }

        // Приходит в фоновом потоке
        static object Command(string name, Dictionary<string, object> args, string from)
        {
            if (name == Family.NotifyCommand)
            {
                // Короткое сообщение другого приложения dev.txt — карточкой на острове (PROTOCOL.md, «Уведомления — на острове»).
                // Окно не ждём: показать сейчас или тихо положить в историю, остров решает сам
                object t, x, o;
                args.TryGetValue("title", out t); args.TryGetValue("text", out x); args.TryGetValue("open", out o);
                string title = NotifyText(t, 60), text = NotifyText(x, 200), sender = from ?? "";
                bool open = o is bool && (bool)o && sender.Length > 0;
                island.Dispatcher.BeginInvoke(new Action(delegate { island.FamilyNotify(sender, title, text, open); }));
                return null;
            }
            if (name == "discord.connect")
            {
                // «Подключить Discord» с карточки Maska в core.txt — то же, что кнопка в настройках Kapsula.
                // Скачивает сборку, поэтому не в потоке окна; пока Discord запущен, откажет и скажет, что его надо закрыть.
                string error = DiscordInstall.Install();
                if (error != null) throw new Exception(error);
                return null;
            }
            Exception failed = null;
            island.Dispatcher.Invoke(new Action(delegate
            {
                try
                {
                    if (name.StartsWith("media.")) { island.HubMediaCommand(name); return; }   // настройки тут ни при чём — заново их не шлём
                    if (name == "show")
                    {
                        // core.txt открывает «Клавиши» того, чьи сочетания показывает: { page: "keys" }; другое или ничего — просто окно
                        object page;
                        args.TryGetValue("page", out page);
                        if (page as string == "keys") island.OpenSettingsAt("Клавиши"); else island.OpenSettings();
                    }
                    else
                    {
                        object key, value;
                        args.TryGetValue("key", out key);
                        args.TryGetValue("value", out value);
                        Apply(key as string, value);
                    }
                }
                catch (Exception e) { failed = e; }
                Publish(true);   // core.txt ждёт полную картину, даже если ничего не изменилось
                island.HubShareAll();   // выключили «Показывать…» — состояния сразу уходят как null, а не через пару секунд
            }));
            if (failed != null) throw failed;
            return null;
        }

        // ------------------------------------------------------------------ чем Kapsula делится с другими приложениями
        // Состояния kapsula.media, kapsula.weather, kapsula.calendar, kapsula.unread и kapsula.system. Каждое — всегда целиком,
        // уходит только когда изменилось, а когда показывать нечего (или показывать запретили) — null.

        public static bool On { get { return link != null; } }
        // core.txt сейчас запущен и на связи
        public static bool Online { get { return link != null && link.Online; } }
        // Просьба к core.txt показать своё окно; page — какую страницу открыть («general», «games», …), null — как обычно.
        // Окно другой программы выходит вперёд, только если ей это разрешили: щелчок был у нас — разрешаем (как core.txt)
        public static void ShowCore() { ShowCore(null); }
        public static void ShowCore(string page)
        {
            if (link == null) return;
            LetForeground();
            link.Command("core", "show", page != null ? HubLink.Msg("page", page) : null);
        }

        static void LetForeground() { try { Native.AllowSetForegroundWindow(-1); } catch { } }

        // Открыть core.txt на странице page («general», «games»): запущенный — просьбой show через хаб; установленный, но не
        // запущенный — запуском сразу на этой странице (core.txt понимает --page и пропускает незнакомую); нет его — скачать
        public static void OpenCore(string page)
        {
            if (Online) { ShowCore(page); return; }
            string args = CoreArgs(page);
            if (args.Length > 0 && DevTxt.CoreApp.Installed)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DevTxt.CoreApp.Path, args) { UseShellExecute = true }); return; }
                catch { }
            }
            DevTxt.CoreApp.Go(false, null);
        }

        // Ключи запуска core.txt на странице: «--page general»; нет страницы или имя странное — без ключей
        public static string CoreArgs(string page)
        {
            return page != null && System.Text.RegularExpressions.Regex.IsMatch(page, "^[a-z]{1,16}$") ? "--page " + page : "";
        }
        public static bool ShareOn { get { return Store.Get("hubShare") != "0"; } }
        public static bool ShareUnreadOn { get { return Store.Get("hubUnread") != "0"; } }

        // ------------------------------------------------------------------ семья dev.txt: что ведёт core.txt (Family.cs)

        // Что сейчас сообщил core.txt (core.family); null — его нет на связи
        public static FamilyState FamilyInfo { get { return family != null ? family.State : null; } }
        // Свой значок в трее не нужен: core.txt показывает один на всю семью
        public static bool HideOwnTray { get { return family != null && family.HideOwnTray; } }
        // «Не беспокоить» включается через core.txt — тогда своё не заводится
        public static bool FocusSettable { get { return family != null && family.Has("core", Family.FocusSet); } }

        // Включить или выключить общее «Не беспокоить» (minutes ≤ 0 — пока не выключат). false — core.txt нет на связи
        // или он этого не умеет: остаётся своё, как раньше
        public static bool SetFocus(bool on, int minutes) { return family != null && family.SetFocus(on, minutes); }

        // Просьба другому приложению dev.txt показать своё окно — щелчок по его уведомлению на острове
        public static void ShowApp(string app)
        {
            if (link == null || AppName(app).Length == 0) return;
            LetForeground();
            link.Command(app, "show", null);
        }

        // «Не беспокоить» сейчас: своё (до localUntil) или общее из core.txt — focus и focusUntil из core.family
        // (миллисекунды с 1970 года, UTC; 0 — пока не выключат). Время вышло, а core.txt ещё не прислал выключение, — уже не тихо.
        public static bool QuietOn(DateTime localUntil, DateTime now, bool focus, long focusUntil, long nowMs)
        {
            return localUntil > now || focus && (focusUntil <= 0 || focusUntil > nowMs);
        }

        // Своё «Не беспокоить» при core.txt на связи, который умеет focus.set (одно место на всех). Своё — до localEnd
        // (мс с 1970 года, UTC; не позже now — своего нет), у core.txt — focus и focusUntil (0 — пока не выключат), asked —
        // до какого времени своё уже просили у core.txt (0 — не просили).
        // QuietSend — попросить core.txt на minutes минут и пока держать своё; QuietClear — core.txt держит тишину сам, своё
        // гасится; QuietKeep — ничего не менять: своего нет или core.txt ещё не ответил (не ответит — тишина остаётся своей)
        public const int QuietKeep = 0, QuietClear = 1, QuietSend = 2;

        public static int QuietHandOver(long localEnd, long now, bool focus, long focusUntil, long asked, out int minutes)
        {
            minutes = 0;
            if (localEnd <= now) return QuietKeep;
            bool coreOn = focus && (focusUntil <= 0 || focusUntil > now);
            if (coreOn && (focusUntil <= 0 || focusUntil >= localEnd - 60000)) return QuietClear;   // у core.txt тихо не меньше своего
            if (asked == localEnd) return coreOn ? QuietClear : QuietKeep;   // уже просили: core.txt ответил — его время главнее; нет — ждём
            minutes = (int)Math.Max(1, Math.Ceiling((localEnd - now) / 60000.0));   // с запасом вверх: тишина не кончится раньше своей
            return QuietSend;
        }

        // Связь с core.txt пропала, а у него было тихо до seenEnd (мс, UTC; 0 — не было или «пока не выключат»): тишина
        // остаётся своей до того же времени, если своя кончается раньше. 0 — своё не меняется
        public static long QuietAdopt(long seenEnd, long now, long localEnd)
        {
            return seenEnd > now && seenEnd > localEnd ? seenEnd : 0;
        }

        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static long UtcMs(DateTime t) { return (long)(t.ToUniversalTime() - Epoch).TotalMilliseconds; }
        public static DateTime FromUtcMs(long ms) { return Epoch.AddMilliseconds(ms).ToLocalTime(); }

        // Своего значка в трее нет, пока общий держит core.txt (hide), пока при запуске ждём его ответа (waitLeftMs > 0)
        // и когда Kapsula уже закрывается (gone)
        public static bool OwnTrayShown(bool gone, bool hide, int waitLeftMs) { return !gone && !hide && waitLeftMs <= 0; }

        // Сколько ещё ждать ответа core.txt при запуске: until — до какого Environment.TickCount (0 — не ждём). Счётчик
        // через 25 дней переходит через ноль — разница считается без переполнения
        public static int TrayWaitLeft(int until, int now) { return until == 0 ? 0 : unchecked(until - now); }

        // Что делать с уведомлением другого приложения dev.txt (в истории оно уже есть): «history» — только история
        // («Не беспокоить» или открыт вопрос Claude Code), «hold» — остров спрятан от игры или видео на весь экран:
        // карточка — когда он снова на месте, «card» — карточка сейчас
        public static string NotifyRoute(bool quiet, bool hiddenFs, bool claudeCard)
        {
            if (quiet) return "history";
            if (hiddenFs) return "hold";
            return claudeCard ? "history" : "card";
        }

        // Текст карточки, когда за время игры пришло несколько уведомлений: показывается последнее, остальные — в истории
        public static string NotifyMore(string text, int more)
        {
            text = text ?? "";
            if (more <= 0) return text;
            return (text.Length > 0 ? text + " · " : "") + "ещё " + more + " в истории";
        }

        // Общий цвет подсветки из hub.theme: «#RRGGBB» — он; null и всё непонятное — у острова свой
        public static string ThemeAccent(object data)
        {
            var d = data as Dictionary<string, object>;
            object a;
            if (d == null || !d.TryGetValue("accent", out a)) return null;
            string s = a as string;
            if (s == null || !System.Text.RegularExpressions.Regex.IsMatch(s, "^#[0-9a-fA-F]{6}$")) return null;
            return s.ToUpperInvariant();
        }

        // Текст уведомления другого приложения: только строка, в одну строку, без лишних пробелов и не длиннее max знаков
        // (длиннее — обрезается и кончается «…»; эмодзи пополам не режется)
        public static string NotifyText(object raw, int max)
        {
            string s = raw as string;
            if (s == null || max < 2) return "";
            var sb = new System.Text.StringBuilder();
            bool gap = false;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) { gap = sb.Length > 0; continue; }
                if (gap) { sb.Append(' '); gap = false; }
                sb.Append(c);
                if (sb.Length > max) break;   // дальше всё равно отрежется
            }
            if (sb.Length <= max) return sb.ToString();
            int n = max - 1;
            if (char.IsHighSurrogate(sb[n - 1])) n--;
            return sb.ToString(0, n).TrimEnd() + "…";
        }

        // Подпись приложения-отправителя: имя продукта строчными, с .txt — «metla.txt». Странное имя — без подписи
        public static string AppName(string app)
        {
            if (string.IsNullOrEmpty(app) || !System.Text.RegularExpressions.Regex.IsMatch(app, "^[a-z][a-z0-9-]{0,31}$")) return "";
            return app + ".txt";
        }

        // Карточка уведомления другого приложения — как у уведомлений Windows: заголовок карточки — приложение-отправитель
        // («metla.txt»), ниже — его заголовок и текст через «—». Заголовок совпал с названием приложения — второй раз не пишется.
        // Без подписи отправителя — его заголовок (или «Уведомление»). Показывать нечего — head пустой
        public static void NotifyCard(string app, string title, string text, out string head, out string body)
        {
            app = app ?? ""; title = title ?? ""; text = text ?? "";
            head = ""; body = text;
            if (title.Length == 0 && text.Length == 0) return;
            if (app.Length == 0) { head = title.Length > 0 ? title : "Уведомление"; return; }
            head = app;
            if (title.Length > 0 && !string.Equals(title, app, StringComparison.OrdinalIgnoreCase)) body = text.Length > 0 ? title + " — " + text : title;
        }

        // Пункты kapsula.txt в общем меню core.txt в трее — не больше четырёх, самое частое; действия те же, что у пунктов
        // своего меню. Вызываются в потоке хаба (без действий — для проверок)
        public static List<TrayItem> TrayItems(bool awake, bool lyrics, Action settings, Action toggleAwake, Action toggleLyrics, Action shots)
        {
            return new List<TrayItem>
            {
                new TrayItem("show", "Настройки", null, settings),   // audit:ok — окно kapsula.txt и есть её настройки; остров виден всегда, «Открыть» обещало бы что-то третье
                new TrayItem("awake", "Не засыпать", awake, toggleAwake),
                new TrayItem("lyrics", "Тексты песен", lyrics, toggleLyrics),
                new TrayItem("shots", "Папка со снимками", null, shots)
            };
        }

        // Состояние kapsula.tray — в том виде, в каком его отправит Family.SetTray
        public static Dictionary<string, object> TrayState(bool awake, bool lyrics)
        {
            var items = new List<object>();
            foreach (TrayItem t in TrayItems(awake, lyrics, null, null, null, null)) items.Add(t.ToData());
            return HubLink.Msg("items", items.ToArray());
        }

        // Сочетания клавиш острова для страницы «Клавиши» в core.txt — как они стоят сейчас («Ctrl+Alt+S»).
        // on — сочетание включено; выключенное (Vk = 0) — пустое и ни с чем не спорит. Занятое другой программой
        // остаётся включённым: так в core.txt видно, с чем оно совпало
        public static List<KeyInfo> KeysOf(List<Hot> hots)
        {
            var list = new List<KeyInfo>();
            if (hots == null) return list;
            foreach (Hot h in hots)
            {
                bool on = h.Vk != 0;
                list.Add(new KeyInfo(h.Key, h.Name, on ? KeyNames.Text((int)h.Mods, (int)h.Vk) : "", on));
            }
            return list;
        }

        // Состояние kapsula.keys — в том виде, в каком его отправит Family.SetKeys
        public static Dictionary<string, object> KeysState(List<Hot> hots)
        {
            var keys = new List<object>();
            foreach (KeyInfo k in KeysOf(hots)) keys.Add(k.ToData());
            return HubLink.Msg("keys", keys.ToArray());
        }

        // Быстрые действия и сочетания клавиш — в core.txt; Family отправляет их, только если что-то поменялось. В потоке окна.
        public static void PublishFamily()
        {
            if (family == null) return;
            family.SetTray(island.FamilyTray());
            family.SetKeys(KeysOf(island.Hots));
        }

        // После пункта из общего меню core.txt: новые галочки и настройки уходят сразу, а не через пару секунд
        public static void Refresh()
        {
            Publish(false);
            PublishFamily();
        }

        static readonly Dictionary<string, string> shared = new Dictionary<string, string>();

        // Отправляет состояние, только если оно не такое, как в прошлый раз. Вызывается в потоке окна.
        public static void Share(string topic, object data)
        {
            if (link == null) return;
            string text = data == null ? "null" : new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(data);
            string was;
            if (shared.TryGetValue(topic, out was) && was == text) return;
            shared[topic] = text;
            link.State(topic, data);
        }

        // Трек, который показывает остров. Нет трека или делиться запретили — null
        public static Dictionary<string, object> MediaState(bool share, bool has, bool playing, string title, string artist, string app)
        {
            if (!share || !has) return null;
            return HubLink.Msg("playing", playing, "title", title ?? "", "artist", artist ?? "", "app", app ?? "");
        }

        // Значок погоды для других приложений — по тем же кодам (WMO, как у Open-Meteo), по которым остров выбирает свой:
        // см. Weather.Describe и Weather.Symbol. Прогноз MET Norway приводится к ним же в Weather.MetCode.
        public static string WeatherIcon(int c)
        {
            if (c == 0 || c == 1) return "clear";
            if (c == 45 || c == 48) return "fog";
            if ((c >= 51 && c <= 67) || (c >= 80 && c <= 82)) return "rain";
            if ((c >= 71 && c <= 77) || c == 85 || c == 86) return "snow";
            if (c >= 95) return "storm";
            return "cloudy";   // 2, 3 и всё незнакомое — как у Weather.Symbol
        }

        // Погода: температура — целые градусы Цельсия, text — та же короткая подпись, что на острове («Пасмурно»)
        public static Dictionary<string, object> WeatherState(bool share, WeatherData w)
        {
            if (!share || w == null || w.Error != null) return null;
            return HubLink.Msg("temp", (int)Math.Round(w.Temp), "text", Weather.Describe(w.Code), "icon", WeatherIcon(w.Code), "city", w.City ?? "");
        }

        // Когда событие кончается. У событий из проверок конца нет: тогда «весь день» длится до полуночи, остальные — миг
        static DateTime EndOf(CalEvent e)
        {
            if (e.End > e.Start) return e.End;
            return e.AllDay ? e.Start.Date.AddDays(1) : e.Start;
        }

        // Сегодняшние события, которые ещё не кончились: по порядку, не больше пяти, только название и время.
        // Прошедшие дни и завтрашний день сюда не попадают; место, участники и описание — тоже.
        // Событие началось вчера или идёт весь день — start «00:00»; кончается ровно в полночь или тянется дольше суток — end «23:59».
        public static Dictionary<string, object>[] TodayEvents(List<CalEvent> all, DateTime now)
        {
            var list = new List<CalEvent>();
            DateTime today = now.Date, tomorrow = today.AddDays(1);
            foreach (CalEvent e in all)
            {
                DateTime end = EndOf(e);
                if (e.Start >= tomorrow) continue;                            // завтра и дальше
                bool ended = end > e.Start ? end <= now : e.Start < now;       // событие без длительности «кончается» в своё начало
                if (ended) continue;
                list.Add(e);
            }
            list.Sort(delegate(CalEvent x, CalEvent y) { return x.Start.CompareTo(y.Start); });
            if (list.Count > 5) list.RemoveRange(5, list.Count - 5);
            var res = new Dictionary<string, object>[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                CalEvent e = list[i];
                DateTime end = EndOf(e);
                string from = e.AllDay || e.Start < today ? "00:00" : e.Start.ToString("HH:mm");
                string to = e.AllDay || end == tomorrow || end >= tomorrow.AddDays(1) ? "23:59" : end.ToString("HH:mm");
                res[i] = HubLink.Msg("title", e.Subject ?? "", "start", from, "end", to);
                if (e.AllDay) res[i]["allDay"] = true;
            }
            return res;
        }

        // Календарь: access — удалось ли вообще прочитать календарь Windows. Не удалось — null, а не пустой список
        public static Dictionary<string, object> CalendarState(bool share, bool access, List<CalEvent> all, DateTime now)
        {
            if (!share || !access) return null;
            return HubLink.Msg("events", TodayEvents(all ?? new List<CalEvent>(), now));
        }

        // Счётчик непрочитанного — тот же, что красный кружок в капсуле. counter — включён ли сам счётчик в настройках Kapsula
        public static Dictionary<string, object> UnreadState(bool share, bool counter, int discord, int steam)
        {
            if (!share || !counter) return null;
            return HubLink.Msg("total", discord + steam, "apps", HubLink.Msg("discord", discord, "steam", steam));
        }

        // Нагрузка: проценты — целые. Видеокарту измерить не удалось — gpu: null; скорость сети ещё не посчитана — полей down и up нет
        public static Dictionary<string, object> SystemState(double cpu, int ram, int gpu, double down, double up)
        {
            var d = HubLink.Msg("cpu", Pct(cpu * 100), "ram", Pct(ram), "gpu", gpu >= 0 ? (object)Pct(gpu) : null);
            if (down >= 0 && up >= 0) { d["down"] = (long)Math.Round(down); d["up"] = (long)Math.Round(up); }
            return d;
        }

        static int Pct(double v) { return (int)Math.Max(0, Math.Min(100, Math.Round(v))); }

        // Строка нагрузки для капсулы в игре: «ЦП 35% · ГП 66% · ОЗУ 48%» — те же сокращения, что у полоски «Нагрузка в играх».
        // Видеокарту измерить не удалось (gpu < 0) — без неё
        public static string LoadLine(double cpu, int gpu, int ram)
        {
            return "ЦП " + Pct(cpu * 100) + "% · " + (gpu >= 0 ? "ГП " + Pct(gpu) + "% · " : "") + "ОЗУ " + Pct(ram) + "%";
        }
    }

    partial class IslandWindow
    {
        bool sharedReduced;

        // «Меньше движения» включили в core.txt для всех приложений разом. Свой выбор «Характер анимаций»
        // при этом не теряется: он остаётся в настройках и возвращается, когда общий переключатель выключат.
        public void SetSharedReducedMotion(bool on)
        {
            if (sharedReduced == on) return;
            sharedReduced = on;
            int own;
            if (!int.TryParse(Store.Get("motion"), out own) || own < 0 || own >= MotionNames.Length) own = System.Windows.SystemParameters.ClientAreaAnimation ? 0 : 5;   // своего выбора нет — как при первом запуске
            if (on) ApplyMotion(5);
            else ApplyMotion(own);
        }

        // ---- семья dev.txt: общий цвет подсветки, один значок в трее, общее «Не беспокоить», уведомления других приложений

        System.Windows.Media.Color? sharedAccent;   // цвет подсветки из core.txt (hub.theme.accent); null — свой
        bool accentFromCover;                       // подсветка сейчас — в цвет обложки играющего трека

        // Свой цвет подсветки острова: общий из core.txt или зелёный Kapsula. У трека с обложкой подсветка — в цвет обложки
        System.Windows.Media.Color OwnAccent { get { return sharedAccent ?? DefaultAccent; } }

        // Подсветку острова задаёт core.txt — в настройках у «Цвета острова» об этом сказано
        public bool SharedAccentOn { get { return sharedAccent.HasValue; } }

        // hub.theme.accent: «#RRGGBB» — подсветка острова в этот цвет, null — снова своя. В настройки Kapsula не пишется.
        // Пока на экране обложка играющего трека, подсветка остаётся в её цвет; плеера уже нет — цвет обложки не держится
        public void SetSharedAccent(string hex)
        {
            System.Windows.Media.Color c;
            System.Windows.Media.Color? next = hex != null && TryHex(hex, out c) ? (System.Windows.Media.Color?)c : null;
            if (Nullable.Equals(next, sharedAccent)) return;
            sharedAccent = next;
            if (glowTop == null || accentFromCover && media.Has && coverBrush != null) return;
            if (accentFromCover) { accentFromCover = false; shownCover = null; }   // тот же трек вернётся — обложка и её цвет встанут заново
            UpdateAccent(OwnAccent);
        }

        bool trayGone;        // Kapsula закрывается или перезапускается — значок больше не нужен
        int trayWaitUntil;    // при запуске ждём ответа core.txt до этого момента (0 — не ждём), иначе значок мигнул бы и пропал

        // Сразу после запуска, когда уже ясно, пробуется ли связь с core.txt (BuildTray ставит в очередь окна)
        void TrayStart()
        {
            // core.txt установлен и связь с ним пробуется — до трёх секунд ждём, возьмёт ли он значок на себя.
            // Не ответил — значок появляется: без входа kapsula.txt не остаётся никогда
            if (CoreHub.On && DevTxt.CoreApp.Installed) trayWaitUntil = (Environment.TickCount + 3000) | 1;
            ApplyTray();
        }

        // Свой значок в трее — только когда core.txt не взял его на себя; связь пропала — сразу виден снова, с полным своим меню
        void ApplyTray()
        {
            if (tray == null) return;
            int left = CoreHub.TrayWaitLeft(trayWaitUntil, Environment.TickCount);
            if (left <= 0) trayWaitUntil = 0;
            bool show = CoreHub.OwnTrayShown(trayGone, CoreHub.HideOwnTray, left);
            if (tray.Visible != show) tray.Visible = show;
        }

        // core.family или список приложений поменялись, связь появилась или пропала (Family.Changed). В потоке окна.
        public void FamilyChanged()
        {
            DevTxt.FamilyState f = CoreHub.FamilyInfo;
            if (f != null || !CoreHub.Online) trayWaitUntil = 0;   // core.txt ответил или связи нет — ждать нечего
            if (!CoreHub.Online)
            {
                // без core.txt — как раньше: свой цвет подсветки и свой «Характер анимаций» (hub.theme вернётся вместе со связью)
                SetSharedAccent(null);
                SetSharedReducedMotion(false);
            }
            if (f != null) quietSeenEnd = f.Focus && f.FocusUntil > 0 ? f.FocusUntil : 0;   // что последним знаем о тишине у core.txt
            else if (quietSeenEnd > 0)
            {
                // связь пропала, а у core.txt было тихо до времени — тишина остаётся своей до того же времени
                long keep = CoreHub.QuietAdopt(quietSeenEnd, DevTxt.FamilyState.Now(), quietUntil > DateTime.Now ? CoreHub.UtcMs(quietUntil) : 0);
                if (keep > 0) quietUntil = CoreHub.FromUtcMs(keep);
                quietSeenEnd = 0;
            }
            QuietHandOver();
            ApplyTray();
            TrayIconTick();
            if (quietTile != null) quietTile.SetActive(QuietNow);
        }

        long quietAsked;     // до какого времени (мс с 1970 года, UTC) своё «Не беспокоить» уже попросили у core.txt; 0 — не просили
        long quietSeenEnd;   // до какого времени тихо у core.txt — по последнему core.family (0 — не тихо или «пока не выключат»)

        // Своё «Не беспокоить» переходит к core.txt: одно место на всех (CoreHub.QuietHandOver). Своё гасится, только когда
        // core.txt подтвердил тишину в core.family; не ответил или связь пропала — тишина остаётся своей
        void QuietHandOver()
        {
            DevTxt.FamilyState f = CoreHub.FamilyInfo;
            if (f == null) { quietAsked = 0; return; }   // связи нет — когда появится, попросим заново
            if (!CoreHub.FocusSettable) return;
            long end = quietUntil > DateTime.Now ? CoreHub.UtcMs(quietUntil) : 0;
            int minutes;
            switch (CoreHub.QuietHandOver(end, DevTxt.FamilyState.Now(), f.Focus, f.FocusUntil, quietAsked, out minutes))
            {
                case CoreHub.QuietClear: quietUntil = DateTime.MinValue; quietAsked = 0; break;
                case CoreHub.QuietSend: if (CoreHub.SetFocus(true, minutes)) quietAsked = end; break;
            }
        }

        // Выйти из kapsula.txt: «Выход» в меню своего значка у часов, а пока значок у core.txt — строка «Выйти из kapsula.txt»
        // в настройках и команда «выход» в строке острова
        public void Quit()
        {
            trayGone = true;
            if (tray != null) tray.Visible = false;
            System.Windows.Application.Current.Shutdown();
        }

        // Команда выхода в строке острова: «выход», «выйти», «выйти из kapsula», «закрыть kapsula.txt», «quit», «exit». Только
        // целиком: «выходные», «закрыть вкладку» или «выход на работу» — не она
        public static bool QuitWord(string q)
        {
            q = (q ?? "").Trim().ToLowerInvariant();
            if (q == "выход" || q == "выйти" || q == "exit" || q == "quit") return true;
            foreach (string start in new[] { "выйти из ", "выход из ", "закрыть ", "exit ", "quit " })
            {
                if (!q.StartsWith(start)) continue;
                string rest = q.Substring(start.Length).Trim();
                if (rest == "kapsula" || rest == "kapsula.txt" || rest == "капсулу" || rest == "капсулы" || rest == "капсула") return true;
            }
            return false;
        }

        // Быстрые действия для общего меню core.txt в трее. Щелчок приходит в потоке хаба: выполняется в потоке окна,
        // и новые галочки сразу уходят в core.txt
        public List<TrayItem> FamilyTray()
        {
            return CoreHub.TrayItems(awakeOn, LyricsOn(),
                OnWindow(delegate { OpenSettings(); }),
                OnWindow(delegate { SetAwake(!awakeOn, TimeSpan.Zero, true); }),
                OnWindow(ToggleLyrics),
                OnWindow(delegate { try { System.Diagnostics.Process.Start(ShotDir()); } catch { } }));
        }

        Action OnWindow(Action a)
        {
            return delegate { Dispatcher.Invoke(new Action(delegate { a(); CoreHub.Refresh(); })); };
        }

        // Окно настроек сразу на нужном разделе — например, на «Клавишах» по просьбе core.txt
        public void OpenSettingsAt(string section)
        {
            openSection = section;
            OpenSettings();
        }

        // Уведомление другого приложения dev.txt (команда notify): карточка как у уведомлений Windows — сверху приложение
        // («metla.txt»), ниже его заголовок и текст, значок приложения в углу (NotifyCard). Всегда попадает в историю;
        // при «Не беспокоить» (своём или общем) — только туда. Пока остров спрятан от игры или видео на весь экран, карточка
        // ждёт: поверх игры не лезет, а когда остров снова на месте — показывается (ShowHeldNote). «Скрывать текст
        // уведомлений» прячет и этот текст. open — щелчок по карточке просит отправителя показать окно (show).
        public void FamilyNotify(string from, string title, string text, bool open)
        {
            string app = CoreHub.AppName(from), head, body;
            CoreHub.NotifyCard(app, title, text, out head, out body);
            if (head.Length == 0) return;   // пустое уведомление
            bool quiet = Quiet(app.Length > 0 ? app : "dev.txt", head, body);
            switch (CoreHub.NotifyRoute(quiet, hiddenFs, ClaudeCardUp()))   // вопрос Claude Code не закрываем: уведомление уже в истории
            {
                case "hold":
                    heldMore = heldHead != null ? heldMore + 1 : 0;
                    heldFrom = from; heldHead = head; heldBody = body; heldOpen = open; heldAt = DateTime.Now;
                    break;
                case "card": FamilyCard(from, head, body, open, 0); break;
            }
        }

        // Уведомление, пришедшее, пока остров был спрятан от игры (FamilyNotify): последнее; сколько ещё — в heldMore
        string heldFrom, heldHead, heldBody;
        bool heldOpen;
        int heldMore;
        DateTime heldAt;

        // Остров снова на месте (OnTick): дождавшаяся карточка показывается, если и сейчас можно. Старше часа — уже не новость,
        // она и так в истории
        void ShowHeldNote()
        {
            if (heldHead == null) return;
            string from = heldFrom, head = heldHead, body = heldBody;
            bool open = heldOpen;
            int more = heldMore;
            DateTime at = heldAt;
            heldHead = null; heldMore = 0;
            if ((DateTime.Now - at).TotalMinutes > 60 || CoreHub.NotifyRoute(QuietNow, false, ClaudeCardUp()) != "card") return;
            FamilyCard(from, head, body, open, more);
        }

        void FamilyCard(string from, string head, string body, bool open, int more)
        {
            Action act = delegate { };
            if (open) act = delegate { CoreHub.ShowApp(from); };
            string shown = Store.Get("hideNotifText") == "1" ? "Новое уведомление" : body;
            ShowCardEx(null, ((char)0xEA8F).ToString(), head, CoreHub.NotifyMore(shown, more), act, null, null, false);
            SetCardLogo(DevAppIcon(from));   // значка не нашлось — остаётся колокольчик
            HideCardButton();
            CardIsNote();
            cardUntil = Environment.TickCount + 5500;
        }

        // Значок приложения dev.txt, приславшего уведомление: оно сейчас запущено — значок берётся у его exe (Metla.exe, Core.exe).
        // Нашёлся — запоминается; не нашёлся — в следующий раз ищется снова
        static readonly Dictionary<string, System.Windows.Media.ImageSource> devIcons = new Dictionary<string, System.Windows.Media.ImageSource>();
        static System.Windows.Media.ImageSource DevAppIcon(string app)
        {
            if (CoreHub.AppName(app).Length == 0) return null;
            System.Windows.Media.ImageSource icon;
            if (devIcons.TryGetValue(app, out icon)) return icon;
            System.Diagnostics.Process[] found = new System.Diagnostics.Process[0];
            try { found = System.Diagnostics.Process.GetProcessesByName(char.ToUpperInvariant(app[0]) + app.Substring(1)); } catch { }
            foreach (System.Diagnostics.Process p in found)
            {
                try { if (icon == null) icon = Shell.IconFor(p.MainModule.FileName); } catch { }
                p.Dispose();
            }
            if (icon != null) devIcons[app] = icon;
            return icon;
        }

        // ---- что остров рассказывает хабу. Каждое состояние уходит оттуда, где значение меняется (RenderMedia, пришёл прогноз,
        // вырос или сбросился счётчик), а раз в пару секунд всё сверяется заново вместе с настройками: так ловятся и
        // переключатели «Показывать…», и события календаря, которые просто кончаются со временем.

        public void HubShareAll()
        {
            if (!CoreHub.On) return;
            HubShareMedia();
            HubShareWeather();
            HubShareCalendar();
            HubShareUnread();
        }

        void HubShareMedia()
        {
            if (!CoreHub.On) return;
            CoreHub.Share("kapsula.media", CoreHub.MediaState(CoreHub.ShareOn, media.Has, media.Playing, media.Title, media.Artist, media.Has ? MediaWatcher.DisplayName(media.Source) : ""));
        }

        void HubShareWeather()
        {
            if (!CoreHub.On) return;
            CoreHub.Share("kapsula.weather", CoreHub.WeatherState(CoreHub.ShareOn, weather));
        }

        int hubCalVersion = -1, hubCalMinute = -1;
        bool hubCalShare;

        // Календарь остров и так перечитывает сам (CalendarTick); здесь список пересчитывается, только когда он дочитался
        // заново или сменилась минута (событие могло кончиться), — а не каждые две секунды
        void HubShareCalendar()
        {
            if (!CoreHub.On) return;
            DateTime now = DateTime.Now;
            int minute = now.Hour * 60 + now.Minute;
            bool share = CoreHub.ShareOn;
            if (Calendar.Version == hubCalVersion && minute == hubCalMinute && share == hubCalShare) return;
            hubCalVersion = Calendar.Version; hubCalMinute = minute; hubCalShare = share;
            bool access = Calendar.Loaded && !Calendar.Failed;
            CoreHub.Share("kapsula.calendar", CoreHub.CalendarState(share, access, share && access ? Calendar.Events() : null, now));
        }

        void HubShareUnread()
        {
            if (!CoreHub.On) return;
            CoreHub.Share("kapsula.unread", CoreHub.UnreadState(CoreHub.ShareUnreadOn, Store.Get("unread") != "0", unreadDiscord, unreadSteam));
        }

        // Команды media.toggle, media.prev и media.next — то же, что кнопки плеера на острове (RoundPlay, MediaBtn)
        public void HubMediaCommand(string name)
        {
            if (Demo)
            {
                // в демо трек выдуманный: команды меняют его, а настоящий плеер на этом компьютере не трогают
                if (!media.Has) return;
                if (name == "media.toggle") { media.Pos = media.Now(); media.PosAt = DateTime.UtcNow; media.Playing = !media.Playing; }
                else
                {
                    bool first = media.Title == "Ночной маршрут";
                    media = new MediaInfo { Has = true, Playing = true, Title = first ? "Утренний перегон" : "Ночной маршрут", Artist = first ? "Другой исполнитель" : "Kapsula Demo", Cover = media.Cover, Pos = 0, Dur = 204, PosAt = DateTime.UtcNow };
                }
                RenderMedia();
                return;
            }
            if (name == "media.toggle")
            {
                // значок меняется сразу, как при нажатии: следующий опрос плеера поправит, если тот не послушался
                playClickWant = !(media.Has && media.Playing); playClickAt = Environment.TickCount;
                watcher.Command(0xB3);
                RenderMedia();
            }
            else if (name == "media.prev") { trackDir = -1; trackDirAt = Environment.TickCount; watcher.Command(0xB1); }
            else if (name == "media.next") { trackDir = 1; trackDirAt = Environment.TickCount; watcher.Command(0xB0); }
        }

        // ---- нагрузка: процессор, память, видеокарта, сеть. Измеряется раз в две секунды и только пока это кому-то видно:
        // открыта вкладка «Система» или в игре показана строка «Нагрузка в игре». Иначе в хаб один раз уходит null, и всё стихает.
        bool sysOn;
        int sysRam, sysSentAt;
        double netDown = -1, netUp = -1;   // байты в секунду; считает NetSample (HomeBlocks.cs)

        // «Нагрузка в игре»: поверх игры на весь экран свёрнутая капсула показывает «ЦП 35% · ГП 66% · ОЗУ 48%».
        // В игровом режиме острова на экране нет, а когда он спрятан от полноэкранной программы — показывать некому.
        bool GameLoadLine() { return gameLite && !GamerMode && !hiddenFs && Store.Get("gameLoad") == "1"; }

        // Раз в две секунды из OnTick
        void SystemTick()
        {
            bool want = expanded && page == PSystem || GameLoadLine();
            if (!want)
            {
                if (sysOn) { sysOn = false; netDown = -1; netUp = -1; CoreHub.Share("kapsula.system", null); }
                return;
            }
            if (!sysOn)
            {
                sysOn = true;
                // прошлые замеры устарели: видеокарту меряем заново, скорость сети — от этой секунды, а не среднюю за весь перерыв
                if (stripWin == null) gpuLoad = -1;
                if (unchecked(Environment.TickCount - netAt) > 5000) netRx = -1;
            }
            // процессор остров и так замеряет каждую секунду (SampleCpu); память — один быстрый вызов
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
            if (Native.GlobalMemoryStatusEx(ref m)) sysRam = (int)m.dwMemoryLoad;
            // видеокарта — тем же способом и в том же фоновом потоке, что у полоски «Нагрузка в играх» (Overlays.cs)
            if (System.Threading.Interlocked.CompareExchange(ref gpuBusy, 1, 0) == 0)
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { try { gpuLoad = GpuLoad(); } catch { gpuLoad = -1; } finally { gpuBusy = 0; } });
            NetSample();
            // в хаб — не чаще раза в две секунды, даже если замер попросили вне очереди (вошли в игру, щёлкнули переключатель)
            if (!CoreHub.On || sysSentAt != 0 && unchecked(Environment.TickCount - sysSentAt) < 1900) return;
            sysSentAt = Environment.TickCount | 1;
            CoreHub.Share("kapsula.system", CoreHub.SystemState(cpu, sysRam, gpuLoad, netDown, netUp));
        }

        // Ширина строки нагрузки при самых длинных числах: капсула держит её и не дёргается, когда 9% сменяется на 10%
        // (каждое изменение ширины — перерисовка окна поверх игры). Меряется отдельной надписью тем же шрифтом, один раз на шрифт.
        System.Windows.Controls.TextBlock loadProbe;
        string loadProbeKey;
        double loadProbeW;

        double GameLoadWidth()
        {
            string wide = CoreHub.LoadLine(1, gpuLoad >= 0 ? 100 : -1, 100);
            string key = wide + "|" + cText.FontFamily + "|" + cText.FontWeight + "|" + cText.FontSize;
            if (key == loadProbeKey) return loadProbeW;
            if (loadProbe == null) loadProbe = new System.Windows.Controls.TextBlock();
            loadProbe.FontFamily = cText.FontFamily; loadProbe.FontWeight = cText.FontWeight; loadProbe.FontSize = cText.FontSize; loadProbe.Text = wide;
            loadProbe.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            loadProbeKey = key; loadProbeW = loadProbe.DesiredSize.Width;
            return loadProbeW;
        }

        // Ветка RenderCompactCore: строка нагрузки по центру вместо часов и названия трека; обложки, эквалайзера и погоды нет
        void RenderGameLoad()
        {
            cCover.Visibility = System.Windows.Visibility.Collapsed; eq.Visibility = System.Windows.Visibility.Collapsed;
            cRight.Visibility = System.Windows.Visibility.Collapsed;
            double side = CompactSide();
            cText.Text = CoreHub.LoadLine(cpu, gpuLoad, sysRam);
            compactW = Math.Min(420, Math.Ceiling(GameLoadWidth() + 2 * side + 12) - pillExtraW);
            cText.MaxWidth = Math.Max(40, CompactWidth() - 2 * side - 12);
        }
    }
}
