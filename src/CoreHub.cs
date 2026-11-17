// Kapsula — «остров» для Windows. Часть исходного кода: CoreHub.cs
// Связь с core.txt — лаунчером dev.txt. Он держит на этом компьютере хаб (127.0.0.1:42080), через который
// приложения видят друг друга. Kapsula отдаёт туда свои повседневные настройки — они появляются на общей
// странице настроек в core.txt рядом с настройками остальных приложений — и принимает оттуда изменения.
// Оттуда же приходит общий вид: «Меньше движения» для всех приложений разом.
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
            link.Commands = new string[] { "settings.set", "show" };
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
            timer.Tick += delegate { Publish(false); };
            timer.Start();
            Publish(true);
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
            Exception failed = null;
            island.Dispatcher.Invoke(new Action(delegate
            {
                try
                {
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
            }));
            if (failed != null) throw failed;
            return null;
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
    }
}
