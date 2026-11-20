// Kapsula — «остров» для Windows. Часть исходного кода: Merged.cs
// Окно настроек без лишнего. Здесь — правила без окон: по ним строится окно настроек (SettingsWindow.cs) и описание
// настроек для core.txt (CoreHub.cs), их же проверяет tests.ps1.
//  • Сводный выбор вместо двух переключателей, которые вместе описывают одно и то же. Каждый вариант — точное сочетание
//    прежних ключей, а ключ, который при этом варианте ни на что не влияет, выбор не трогает. Поэтому любое сочетание,
//    сохранённое раньше, работает как раньше, а ключи настроек и их смысл не меняются.
//  • «Пока не трогали — не видно»: редкие и сырые настройки видны, только если их значение уже не такое, как по умолчанию, —
//    чтобы их можно было вернуть. Решается один раз, когда открывается окно: строка не пропадает из-под курсора.
//  • Строки, которые появляются, только когда на что-то влияют: сдвиг текста песни — при включённых текстах и так далее.
using System;
using System.Collections.Generic;

namespace Kapsula
{
    // Сводный выбор из двух прежних переключателей A и B
    class PrefPair
    {
        public readonly string Id;          // ключ выбора в описании для core.txt (type choice)
        public readonly string A, B;        // прежние ключи настроек
        public readonly bool Tier;          // B уточняет A и без A ни на что не влияет: «выключено», «A», «A и B»; иначе — все четыре сочетания
        public readonly string[] Values;    // значения вариантов для core.txt — по порядку вариантов
        public readonly string[] Labels;    // подписи вариантов

        public PrefPair(string id, string a, string b, bool tier, string[] values, string[] labels)
        {
            Id = id; A = a; B = b; Tier = tier; Values = values; Labels = labels;
        }
    }

    static class Merged
    {
        // «Уведомления Windows»: notifications — показывать их карточкой на острове; bannersOff — убирать окошко Windows, как
        // только остров его показал (Banners.cs). Без notifications уведомления не читаются вовсе, и bannersOff ни на что не влияет
        public static readonly PrefPair WindowsNotify = new PrefPair("windowsNotify", "notifications", "bannersOff", true,
            new[] { "windows", "both", "island" }, new[] { "Только Windows", "Остров и Windows", "Только остров" });

        // «Уведомления Steam»: A — плагин kapsula.txt в папке Millennium (это файлы плагина, а не ключ настроек);
        // steamHideToasts — не показывать окошко Steam, когда уведомление показал остров. Без плагина его некому прочитать
        public static readonly PrefPair SteamNotify = new PrefPair("steamNotify", "steamPlugin", "steamHideToasts", true,
            new[] { "steam", "both", "island" }, new[] { "Только Steam", "Остров и Steam", "Только остров" });

        // «Край во время музыки»: liquid — край пляшет под звук; liquidSpectrum — как эквалайзер. Без liquid край
        // шевелится только у звенящего таймера, а там эквалайзера нет (StepLiquid)
        public static readonly PrefPair Edge = new PrefPair("musicEdge", "liquid", "liquidSpectrum", true,
            new[] { "flat", "volume", "spectrum" }, new[] { "Ровный", "Пляшет под звук", "Эквалайзер" });

        // «Мини-плеер при наведении»: hoverMini — сначала мини-плеер; miniLyricsOnly — в нём только текст песни.
        // Без hoverMini мини-плеер не открывается (его открывает только наведение), и miniLyricsOnly ни на что не влияет
        public static readonly PrefPair Mini = new PrefPair("miniPlayer", "hoverMini", "miniLyricsOnly", true,
            new[] { "off", "player", "lyrics" }, new[] { "Выключен", "Обычный", "Только текст песни" });

        // «Индикаторы камеры и микрофона»: точки независимы — все четыре сочетания разные
        public static readonly PrefPair Dots = new PrefPair("dots", "micDot", "camDot", false,
            new[] { "off", "mic", "cam", "both" }, new[] { "Выключены", "Микрофон", "Камера", "Микрофон и камера" });

        // «Напоминания о событиях»: calendar — карточка за 10 минут до события; soonPill — отсчёт в капсуле за 15 минут
        // (и у задач со временем). Независимы — все четыре сочетания разные
        // Подписи вариантов короткие: все варианты выбора стоят в одну строку, без одинокого последнего на второй
        public static readonly PrefPair Remind = new PrefPair("remind", "calendar", "soonPill", false,
            new[] { "off", "card", "countdown", "both" }, new[] { "Выключены", "Карточка", "Отсчёт", "Карточка и отсчёт" });

        public static readonly PrefPair[] All = { WindowsNotify, SteamNotify, Edge, Mini, Dots, Remind };

        // Ключи, включённые по умолчанию: читаются как «не "0"», остальные — как «"1"». Так же их читает остальной код острова
        static readonly string[] DefaultOn = { "liquid", "liquidSpectrum", "hoverMini", "calendar", "soonPill", "steamHideToasts", "hubShare", "hubUnread", "ciderRate", "earbuds" };

        public static bool On(string key, string raw)
        {
            return Array.IndexOf(DefaultOn, key) >= 0 ? raw != "0" : raw == "1";
        }

        // Какой вариант выбран при этих значениях прежних ключей
        public static int Choice(PrefPair p, bool a, bool b)
        {
            if (p.Tier) return !a ? 0 : b ? 2 : 1;
            return (a ? 1 : 0) + (b ? 2 : 0);
        }

        // Что станет с прежними ключами, если выбрать вариант choice. При «выключено» уточнение B не трогается — оно тогда
        // ни на что не влияет и вернётся таким же, когда функцию включат снова. false — такого варианта нет
        public static bool Apply(PrefPair p, int choice, ref bool a, ref bool b)
        {
            if (choice < 0 || choice >= p.Values.Length) return false;
            if (p.Tier) { a = choice > 0; if (choice > 0) b = choice == 2; }
            else { a = (choice & 1) != 0; b = (choice & 2) != 0; }
            return true;
        }

        // Что записать, чтобы выбрать вариант choice, когда сейчас стоят a0 и b0: только ключи, у которых значение и правда
        // меняется, и уточнение B — раньше главного A (включённая функция сразу работает с нужным уточнением).
        // Пусто — уже выбран этот вариант или такого варианта нет
        public static List<KeyValuePair<string, bool>> Writes(PrefPair p, int choice, bool a0, bool b0)
        {
            var list = new List<KeyValuePair<string, bool>>();
            bool a = a0, b = b0;
            if (!Apply(p, choice, ref a, ref b)) return list;
            if (b != b0) list.Add(new KeyValuePair<string, bool>(p.B, b));
            if (a != a0) list.Add(new KeyValuePair<string, bool>(p.A, a));
            return list;
        }

        // Как прежняя строка настроек ставила свой ключ (SetPref): banners — Banners.Set (убирает окошки Windows);
        // miniLyrics — SetMiniLyricsOnly (мини-плеер перестраивается); steam — SetFlag и сразу настройка плагину Steam;
        // store — просто запись (ключ читают по ходу); flag — SetFlag, как у переключателей острова
        public static string Route(string key)
        {
            switch (key)
            {
                case "bannersOff": return "banners";
                case "miniLyricsOnly": return "miniLyrics";
                case "steamHideToasts": return "steam";
                case "steamPlugin": return "plugin";   // это файлы плагина в папке Millennium: их ставит и убирает строка «Уведомления Steam»
                case "liquidSpectrum": case "micDot": case "camDot": case "calendar": case "soonPill": return "store";
            }
            return "flag";   // notifications, liquid, hoverMini, hubShare, hubUnread
        }

        // Значение выбора для core.txt и обратно (-1 — не строка или такого варианта нет)
        public static string Value(PrefPair p, bool a, bool b) { return p.Values[Choice(p, a, b)]; }
        public static int Index(PrefPair p, object value) { string s = value as string; return s == null ? -1 : Array.IndexOf(p.Values, s); }

        // «Делиться с приложениями dev.txt» — один переключатель на hubShare и hubUnread, только пока они совпадают.
        // Разошлись (кто-то раньше выключил одно из двух) — видны оба прежних переключателя: один не сказал бы правду
        public static bool OneSwitch(bool share, bool unread) { return share == unread; }

        // Что из этого показывать (в окне и в описании для core.txt): share — общий переключатель, hubShare и hubUnread — прежние
        public static bool ShareShown(string key, bool share, bool unread)
        {
            if (key == "share") return OneSwitch(share, unread);
            if (key == "hubShare" || key == "hubUnread") return !OneSwitch(share, unread);
            return true;
        }

        // «Пока не трогали — не видно»: строка видна, только если значение уже не такое, как по умолчанию.
        // sysAnim — включены ли анимации в Windows: от них зависит «Характер анимаций» по умолчанию (LoadMotion)
        public static readonly string[] Quiet = { "uiaRate", "vscodeButton", "callMode", "font", "motion", "cascade", "followWin" };

        public static bool Used(string key, string raw, bool sysAnim)
        {
            raw = raw ?? "";
            switch (key)
            {
                case "uiaRate": case "vscodeButton": case "callMode": return raw == "1";   // альфа: по умолчанию выключена
                case "font": return raw.Trim().Length > 0;                                 // свой шрифт; по умолчанию — как в Windows
                case "motion":
                    int m;
                    return int.TryParse(raw, out m) && m >= 0 && m < IslandWindow.MotionNames.Length && m != (sysAnim ? 0 : 5);
                case "cascade": return raw == "0";                                         // по умолчанию блоки идут по очереди
                case "followWin": return raw != "1";                                       // по умолчанию остров следует теме Windows
            }
            return true;
        }

        // «Тема как в Windows» видна и по умолчанию, когда она перекрывает выбранный цвет: Windows в светлой теме (остров
        // светлый, какой цвет ни выбери) или выбран светлый цвет, а Windows тёмная (остров остаётся тёмным). Иначе без неё
        // нельзя было бы получить тёмный остров при светлой Windows
        public static bool ThemeRowShown(string followRaw, bool windowsDark, bool chosenLight)
        {
            return Used("followWin", followRaw, true) || !windowsDark || chosenLight;
        }

        // Строки, которые появляются, только когда на что-то влияют: строка → от чего она зависит
        // (eyes — зверёк в капсуле, detached — остров сейчас отцеплен, camera — камера телефона установлена,
        // coreTray — значок у часов держит core.txt; остальное — ключи)
        public static readonly string[][] Needs =
        {
            new[] { "Зверёк в переходах", "eyes" },                     // без зверька в капсуле ему неоткуда выпрыгивать
            new[] { "Зверёк гуляет по окнам", "eyes" },
            new[] { "Вернуть остров наверх", "detached" },               // только когда остров и правда отцеплен
            new[] { "Сдвиг текста песни", "lyrics" },
            new[] { "Ключ Cider", "ciderRate" },                         // ключ нужен только оценкам в Cider
            new[] { "Нагрузка в капсуле во время игры", "showInGames" }, // строка нагрузки — только у острова поверх игры
            new[] { "Канал Twitch", "twitchOverlay" },
            new[] { "Чат Twitch и вне игр", "twitchOverlay" },
            new[] { "Пауза, когда сняли наушники", "earbuds" },          // снятые наушники видно только по их сигналу
            new[] { "Качество камеры", "camera" },
            new[] { "Выйти из kapsula.txt", "coreTray" },                 // только пока значок у часов держит core.txt: своего «Выхода» нет
        };

        // Что сказать о спрятанной зависимой строке, когда её нашёл поиск: когда она появится
        public static string NeedNote(string what)
        {
            switch (what)
            {
                case "eyes": return "Появится, когда включён «Зверёк в капсуле».";
                case "detached": return "Появится, когда остров отцеплен от верхнего края.";
                case "lyrics": return "Появится, когда включены «Тексты песен».";
                case "ciderRate": return "Появится, когда включены оценки Cider — «В избранное» и «Не интересует».";
                case "showInGames": return "Появится, когда включено «Показывать остров в играх».";
                case "twitchOverlay": return "Появится, когда включён «Чат Twitch поверх игры».";
                case "earbuds": return "Появится, когда включён «Заряд AirPods и Beats».";
                case "camera": return "Появится, когда установлена камера телефона — «Телефон как веб-камера».";
                case "coreTray": return "Сейчас выйти можно из меню значка kapsula.txt у часов — пункт «Выход».";
            }
            return "Появится, когда понадобится.";
        }

        // Пункт с section (games, startup, family) при установленном core.txt показывает core.txt, а не окно kapsula.txt
        public static bool ViaCore(bool coreInstalled, string key) { return coreInstalled && CoreHub.SectionOf(key) != null; }
    }

    partial class IslandWindow
    {
        // Прежний переключатель по его ключу (Merged.On — с тем же значением по умолчанию, что у остального кода)
        public static bool Pref(string key) { return Merged.On(key, Store.Get(key)); }

        // Поставить прежний переключатель ровно так, как его ставила его собственная строка в настройках
        public void SetPref(string key, bool v)
        {
            switch (Merged.Route(key))
            {
                case "banners": Banners.Set(v); break;
                case "miniLyrics": SetMiniLyricsOnly(v); break;
                case "steam": SetFlag(key, v); SteamBridge.SendConfig(); break;
                case "store": Store.Set(key, v ? "1" : "0"); break;
                case "plugin": break;
                default: SetFlag(key, v); break;   // notifications, liquid, hoverMini, hubShare, hubUnread — через SetFlag, как их строки
            }
        }

        // Поставить сводный выбор: меняются только те прежние ключи, у которых значение и правда другое (Merged.Writes)
        public void SetPair(PrefPair p, int choice)
        {
            foreach (var w in Merged.Writes(p, choice, Pref(p.A), Pref(p.B))) SetPref(w.Key, w.Value);
        }

        // Остров сейчас отцеплен от верхнего края (Float.cs)
        public bool Detached { get { return detached; } }

        // Windows сейчас в тёмной теме
        public static bool WindowsDark { get { return IsDarkTheme(); } }
    }
}
