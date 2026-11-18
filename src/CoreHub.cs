// Kapsula — «остров» для Windows. Часть исходного кода: CoreHub.cs
// Связь с core.txt — лаунчером dev.txt. Он держит на этом компьютере хаб (127.0.0.1:42080), через который
// приложения видят друг друга. Kapsula отдаёт туда свои повседневные настройки — они появляются на общей
// странице настроек в core.txt рядом с настройками остальных приложений — и принимает оттуда изменения.
// Оттуда же приходит общий вид: «Меньше движения» для всех приложений разом.
// И туда же Kapsula рассказывает то, что и так знает: какой трек играет, какая погода, что сегодня в календаре,
// сколько непрочитанного и насколько загружен компьютер, — чтобы остальным приложениям dev.txt не узнавать это заново.
// core.txt может быть не запущен — это нормально: Kapsula работает сама по себе и тихо пробует подключиться снова.
// Протокол описан в kit.txt (hub/PROTOCOL.md); HubLink.cs скопирован оттуда как есть.
using System;
using System.Collections.Generic;
using System.Windows.Threading;
using DevTxt;

namespace Kapsula
{
    static class CoreHub
    {
        static HubLink link;
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
            public Func<bool> Get;
            public Action<bool> Set;   // null — обычный флаг острова: island.SetFlag(Key, v)
        }

        static List<Flag> flags;

        static void Add(string group, string key, string label, string hint, Func<bool> get) { Add(group, key, label, hint, get, null); }
        static void Add(string group, string key, string label, string hint, Func<bool> get, Action<bool> set)
        {
            flags.Add(new Flag { Group = group, Key = key, Label = label, Hint = hint, Get = get, Set = set });
        }

        static void Describe()
        {
            flags = new List<Flag>();
            Add("", "autoHide", "Автоскрытие", "Остров уезжает за край и возвращается, когда курсор у верхней кромки экрана или есть что показать", delegate { return island.AutoHide; });
            Add("", "seconds", "Секунды в часах", "В капсуле вместо 12:30 будет 12:30:45", delegate { return island.ShowSeconds; });
            Add("", "notifications", "Уведомления Windows на острове", "Новые уведомления других программ показываются карточкой. Нигде не сохраняются", delegate { return island.ShowNotifications; });
            Add("", "rim", "Светлая кромка", "Тонкий блик по краю острова — он не сливается с тёмными обоями и окнами", delegate { return island.RimOn; });
            Add("", "shadow", "Тень под островом", "Мягкая тень: на светлых окнах остров словно парит над ними", delegate { return island.ShadowOn; });

            Add("Зверёк", "eyes", "Зверёк в капсуле", "Следит за курсором, пугается таймера, радуется удаче", delegate { return island.ShowEyes; });
            Add("Зверёк", "petRide", "Зверёк в переходах", "Выпрыгивает из капсулы на край панели и переходит туда, куда вы смотрите", delegate { return island.RideOn; });
            Add("Зверёк", "walk", "Зверёк гуляет по окнам", "Когда остров свёрнут и ничего не происходит, выходит посидеть на краю окна", delegate { return island.WalkOn; });
            Add("Зверёк", "petSeason", "Сезонные наряды зверька", "Колпак под Новый год, шляпа в конце октября, шарф зимой", delegate { return Store.Get("petSeason") != "0"; });

            Add("Музыка", "hoverMini", "Мини-плеер при наведении", "Во время музыки сначала открывается мини-плеер, клик по нему — вся панель", delegate { return island.HoverMini; });
            Add("Музыка", "liquid", "Жидкий край", "Нижний край пляшет под звук", delegate { return island.LiquidOn; });
            Add("Музыка", "lyrics", "Тексты песен", "Загружаются с lrclib.net для играющего трека", delegate { return island.LyricsEnabled; });

            // Чем Kapsula делится с остальными приложениями dev.txt. Выключено — соответствующие состояния уходят как null
            Add("Связь с другими приложениями", "hubShare", "Показывать музыку, погоду и события другим приложениям dev.txt", "Трек, который сейчас играет, погода и сегодняшние события календаря — только название и время. Дальше этого компьютера не уходит", delegate { return ShareOn; });
            Add("Связь с другими приложениями", "hubUnread", "Показывать счётчик непрочитанного", "Сколько сообщений из Discord и Steam вы ещё не видели — только числа, без текста", delegate { return ShareUnreadOn; });

            Add("Запуск и обновления", "autostart", "Запускать вместе с Windows", null, delegate { return Autostart.IsOn(); }, delegate(bool v) { Autostart.Set(v); });
            Add("Запуск и обновления", "autoUpdate", "Автообновление", "Kapsula сама скачивает новые версии с GitHub и сверяет контрольную сумму файла", delegate { return Store.Get("autoUpdate") != "0"; }, delegate(bool v) { Store.Set("autoUpdate", v ? "1" : "0"); });
        }

        public static void Start(IslandWindow w)
        {
            if (link != null) return;
            island = w;
            Describe();
            link = new HubLink("kapsula", "Kapsula", Updater.CurrentText, TestPort > 0 ? TestPort : HubLink.DefaultPort);
            link.Topics = new string[] { "hub.theme" };
            link.Commands = new string[] { "settings.set", "show", "media.toggle", "media.prev", "media.next", "discord.connect" };
            link.OnCommand = Command;
            link.OnState = delegate(string topic, object data, string from)
            {
                if (topic != "hub.theme") return;
                var theme = data as Dictionary<string, object>;
                object reduced = null;
                bool on = theme != null && theme.TryGetValue("reducedMotion", out reduced) && reduced is bool && (bool)reduced;
                island.Dispatcher.BeginInvoke(new Action(delegate { island.SetSharedReducedMotion(on); }));
            };
            link.Start();
            // Настройки меняют и в окне Kapsula, и горячими клавишами — проще сверяться раз в пару секунд, чем ловить каждое место
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += delegate { Publish(false); island.HubShareAll(); };
            timer.Start();
            Publish(true);
            island.HubShareAll();
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
                if (group == null || (string)group["title"] != f.Group)
                {
                    if (group != null) group["items"] = items.ToArray();
                    items = new List<object>();
                    group = HubLink.Msg("title", f.Group);
                    groups.Add(group);
                }
                bool value;
                try { value = f.Get(); } catch { continue; }
                var item = HubLink.Msg("key", f.Key, "type", "switch", "label", f.Label, "value", value);
                if (f.Hint != null) item["hint"] = f.Hint;
                items.Add(item);
            }
            if (group != null) group["items"] = items.ToArray();
            return HubLink.Msg("groups", groups.ToArray());
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
        public static void Apply(string key, object value)
        {
            if (!(value is bool)) throw new Exception("нужно «да» или «нет»");
            Flag f = flags.Find(delegate(Flag x) { return x.Key == key; });
            if (f == null) throw new Exception("такой настройки нет");
            if (f.Set != null) f.Set((bool)value); else island.SetFlag(key, (bool)value);
        }

        // Приходит в фоновом потоке
        static object Command(string name, Dictionary<string, object> args, string from)
        {
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
                    if (name == "show") island.OpenSettings();
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
        public static bool ShareOn { get { return Store.Get("hubShare") != "0"; } }
        public static bool ShareUnreadOn { get { return Store.Get("hubUnread") != "0"; } }

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
