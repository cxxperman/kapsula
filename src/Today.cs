// Kapsula — «остров» для Windows. Часть исходного кода: Today.cs
// Вкладка «Сегодня»: дата и неделя, ближайшее событие, погода и вторые часы, задачи со временем и датой.
// Задачи пишутся обычной фразой: «отчёт, потом в 14:00 созвон» — две задачи; «в пятницу в 10 врач», «15 ноября …»,
// «через неделю …» — на свой день. Под задачами дня — «Скоро»: что впереди на неделю. В свой час задача приходит
// карточкой с колокольчиком. Плюс утреннее приветствие и «25м …» в строке.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Kapsula
{
    // Repeat: "" — один раз; "d" — каждый день; "wd" — по будням; "we" — по выходным; "w:0,2" — по этим дням недели
    // (понедельник — 0); "m:15" — каждый месяц 15-го. Day у повторяющейся — ближайший раз.
    // Note — заметка к задаче (раскрывается под строкой); переводы строк хранятся как «¶».
    class DayTask { public string Id = Guid.NewGuid().ToString("N").Substring(0, 8), Text = "", Repeat = "", Note = ""; public bool Done; public DateTime Day = DateTime.Today; public TimeSpan? Time; }

    static class Tasks
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        // «Когда-нибудь»: задача без дня — хранится с датой 31.12.9999 (прежние версии Kapsula её просто не покажут)
        public static readonly DateTime Someday = new DateTime(9999, 12, 31);

        public static List<DayTask> Load()
        {
            var list = new List<DayTask>();
            foreach (string item in Store.Get("tasks").Split(new char[] { '¦' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = item.Split('~');
                if (p.Length < 5) continue;
                DateTime d;
                if (!DateTime.TryParseExact(p[2], "yyyyMMdd", Inv, DateTimeStyles.None, out d)) continue;
                var t = new DayTask { Id = p[0], Done = p[1] == "1", Day = d, Text = p[4], Repeat = p.Length > 5 ? p[5] : "", Note = p.Length > 6 ? p[6].Replace("¶", "\n") : "" };
                TimeSpan ts;
                if (p[3].Length == 4 && TimeSpan.TryParseExact(p[3], "hhmm", Inv, out ts)) t.Time = ts;
                list.Add(t);
            }
            return list;
        }

        public static void Save(List<DayTask> list)
        {
            // храним задачи за последние две недели, все будущие и все повторяющиеся (шестое поле — повтор;
            // прежние версии Kapsula его просто не читают)
            var parts = new List<string>();
            foreach (DayTask t in list)
            {
                if (t.Day < DateTime.Today.AddDays(-14) && t.Repeat.Length == 0) continue;
                parts.Add(t.Id + "~" + (t.Done ? "1" : "0") + "~" + t.Day.ToString("yyyyMMdd", Inv) + "~" + (t.Time.HasValue ? new DateTime(t.Time.Value.Ticks).ToString("HHmm", Inv) : "") + "~" + t.Text.Replace("~", "-").Replace("¦", "|")
                    + (t.Repeat.Length > 0 || t.Note.Length > 0 ? "~" + t.Repeat : "")
                    + (t.Note.Length > 0 ? "~" + t.Note.Replace("~", "-").Replace("¦", "|").Replace("\r", "").Replace("\n", "¶") : ""));   // седьмое поле — заметка
            }
            Store.Set("tasks", string.Join("¦", parts.ToArray()));
        }

        // «отчёт, потом в 14:00 созвон; завтра в 9 спортзал» → несколько задач со временем и днём
        public static List<DayTask> Parse(string input, DateTime today)
        {
            var res = new List<DayTask>();
            string[] parts = Regex.Split(input, @"\s*(?:,\s*(?:а\s+)?(?:потом|затем)\s*|;\s*|\s+(?:потом|затем)\s+|\s+и\s+потом\s+)\s*", RegexOptions.IgnoreCase);
            DateTime day = today;
            foreach (string raw in parts)
            {
                string t = " " + raw.Trim() + " ";
                if (t.Trim().Length == 0) continue;
                if (Regex.IsMatch(t, @"\sпослезавтра\s", RegexOptions.IgnoreCase)) { day = today.AddDays(2); t = Regex.Replace(t, @"\sпослезавтра\s", " ", RegexOptions.IgnoreCase); }
                else if (Regex.IsMatch(t, @"\sзавтра\s", RegexOptions.IgnoreCase)) { day = today.AddDays(1); t = Regex.Replace(t, @"\sзавтра\s", " ", RegexOptions.IgnoreCase); }
                else if (Regex.IsMatch(t, @"\sсегодня\s", RegexOptions.IgnoreCase)) { day = today; t = Regex.Replace(t, @"\sсегодня\s", " ", RegexOptions.IgnoreCase); }
                string repeat = "";
                DateTime first;
                if (RepeatPhrase(ref t, today, out repeat, out first)) day = first;   // «каждый понедельник», «по будням», «ежедневно»
                else { DateTime d; if (DatePhrase(ref t, today, out d)) day = d; }
                TimeSpan? time = null;
                // «в 14:00», «в 14.30», «14:30», «в 9», «в 9 утра», «в 7 вечера», «2pm»
                Match m = Regex.Match(t, @"(?:\sв\s*|\s)(\d{1,2})[:.](\d{2})(?=\s)", RegexOptions.IgnoreCase);
                if (!m.Success) m = Regex.Match(t, @"\sв\s*(\d{1,2})()(?:\s*(?:ч|час|часа|часов))?(?:\s+(утра|дня|вечера|ночи))?(?=\s)", RegexOptions.IgnoreCase);
                Match pm = Regex.Match(t, @"\s(\d{1,2})\s*(am|pm)(?=\s)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    int h = int.Parse(m.Groups[1].Value), mi = m.Groups[2].Value.Length > 0 ? int.Parse(m.Groups[2].Value) : 0;
                    string part = m.Groups.Count > 3 ? m.Groups[3].Value.ToLowerInvariant() : "";
                    if ((part == "дня" || part == "вечера") && h < 12) h += 12;
                    if (part == "ночи" && h == 12) h = 0;
                    if (h < 24 && mi < 60) { time = new TimeSpan(h, mi, 0); t = t.Remove(m.Index, m.Length).Insert(m.Index, " "); }
                }
                else if (pm.Success)
                {
                    int h = int.Parse(pm.Groups[1].Value) % 12;
                    if (pm.Groups[2].Value.ToLowerInvariant() == "pm") h += 12;
                    time = new TimeSpan(h, 0, 0); t = t.Remove(pm.Index, pm.Length).Insert(pm.Index, " ");
                }
                string text = Regex.Replace(t, @"\s+", " ").Trim().Trim(',', '.', ';', '-', ' ');
                if (text.Length == 0) continue;
                text = char.ToUpper(text[0]) + text.Substring(1);
                // «каждый день в 9», а сейчас уже вечер — первый раз завтра
                DateTime taskDay = day;
                if (repeat.Length > 0 && taskDay == DateTime.Today && time.HasValue && time.Value < DateTime.Now.TimeOfDay) taskDay = NextDay(repeat, taskDay.AddDays(1));
                res.Add(new DayTask { Text = text, Day = taskDay, Time = time, Repeat = repeat });
                if (repeat.Length > 0) day = today;   // следующая часть фразы — снова на этот день, а не на повтор
            }
            return res;
        }

        // Дата словами: «в пятницу», «во вт», «в следующую среду», «через 3 дня», «через неделю», «15 ноября»,
        // «1 янв 2027», «на 20.11», «25.12.2026», «на выходных». Найденное убирается из текста задачи.
        // Дни недели и «через …» — от дня, на который добавляем (в календаре можно выбрать другой день).
        static readonly string[] Weekdays = { "понедельник[уа]?|пн", "вторник[уа]?|вт", "сред[аеуы]|ср", "четверг[уа]?|чт", "пятниц[аеуы]|пт", "суббот[аеуы]|сб", "воскресень[еяю]|вс" };
        const string Before = @"(?:(?:в|во|на|к|ко|до)\s+)?";   // «в пятницу», «к пятнице», «до 15 ноября»
        static readonly string[] Months = { "янв", "фев", "мар", "апр", "ма[яй]", "июн", "июл", "авг", "сен", "окт", "ноя", "дек" };
        const string End = @"(?=[\s,.!?;])";

        static bool DatePhrase(ref string t, DateTime today, out DateTime day)
        {
            day = today;
            Match m;
            // «когда-нибудь», «без даты», «когда будет время» — в список «Когда-нибудь»
            m = Regex.Match(t, @"(?<=\s)(?:когда-нибудь|когда\s+нибудь|без\s+даты|когда\s+будет\s+время)" + End, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                day = Someday;
                t = t.Remove(m.Index, m.Length).Insert(m.Index, " ");
                return true;
            }
            // день недели: ближайший после этого дня; «следующий» — уже на той неделе
            for (int i = 0; i < 7; i++)
            {
                m = Regex.Match(t, @"(?<=\s)((?:в|во|на|к|ко|до)\s+)?(?:(следующ\w*)\s+)?(" + Weekdays[i] + ")" + End, RegexOptions.IgnoreCase);
                // «среда», «пятница» без предлога посреди фразы — просто слово («настроить среду разработки»)
                if (!m.Success || !m.Groups[1].Success && !m.Groups[2].Success && m.Groups[3].Length > 2 && m.Index > 1) continue;
                int dow = ((int)today.DayOfWeek + 6) % 7;   // понедельник — 0
                int ahead = (i - dow + 7) % 7;
                if (ahead == 0) ahead = 7;                   // «в среду» в среду — следующая среда
                if (m.Groups[2].Success && dow + ahead <= 6) ahead += 7;
                day = today.AddDays(ahead);
                t = t.Remove(m.Index, m.Length).Insert(m.Index, " ");
                return true;
            }
            // «через 3 дня», «через неделю», «через 2 недели», «через месяц»
            m = Regex.Match(t, @"\sчерез\s+(?:(\d{1,3}|пару)\s+)?(день|дня|дней|неделю|недели|недель|месяц|месяца|месяцев)" + End, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int n = !m.Groups[1].Success ? 1 : m.Groups[1].Value.ToLowerInvariant() == "пару" ? 2 : int.Parse(m.Groups[1].Value);
                string unit = m.Groups[2].Value.ToLowerInvariant();
                day = unit.StartsWith("д") ? today.AddDays(n) : unit.StartsWith("н") ? today.AddDays(7 * n) : today.AddMonths(n);
                t = t.Remove(m.Index, m.Length).Insert(m.Index, " ");
                return true;
            }
            // «15 ноября», «1 янв 2027», «20-го декабря»: прошедшее в этом году — значит, в следующем
            m = Regex.Match(t, @"(?<=\s)" + Before + @"(\d{1,2})(?:-?го)?\s+(янв|фев|мар|апр|ма[яй]|июн|июл|авг|сен|окт|ноя|дек)[а-яё]*\.?(?:\s+(\d{4}))?" + End, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string mon = m.Groups[2].Value.ToLowerInvariant();
                int month = 0;
                for (int i = 0; i < 12; i++) if (Regex.IsMatch(mon, "^(?:" + Months[i] + ")$")) month = i + 1;
                int year = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : today.Year;
                DateTime d;
                if (month > 0 && MakeDay(year, month, int.Parse(m.Groups[1].Value), out d))
                {
                    if (!m.Groups[3].Success && d < today) MakeDay(year + 1, month, d.Day, out d);
                    day = d;
                    t = t.Remove(m.Index, m.Length).Insert(m.Index, " ");
                    return true;
                }
            }
            // «25.12.2026», «25.12.26» и «на 20.11» (без «на» «14.30» — это время)
            m = Regex.Match(t, @"(?<=\s)" + Before + @"(\d{1,2})\.(\d{1,2})\.(\d{4}|\d{2})" + End, RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(t, @"(?<=\s)(?:на|к|до)\s+(\d{1,2})\.(\d{1,2})()" + End, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int year = m.Groups[3].Value.Length == 4 ? int.Parse(m.Groups[3].Value) : m.Groups[3].Value.Length == 2 ? 2000 + int.Parse(m.Groups[3].Value) : today.Year;
                DateTime d;
                if (MakeDay(year, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value), out d))
                {
                    if (m.Groups[3].Value.Length == 0 && d < today) MakeDay(year + 1, d.Month, d.Day, out d);
                    day = d;
                    t = t.Remove(m.Index, m.Length).Insert(m.Index, " ");
                    return true;
                }
            }
            // «на выходных», «в выходные» — ближайшая суббота (в выходные — этот же день)
            m = Regex.Match(t, @"\s(?:на\s+выходных|в\s+выходные)" + End, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int dow = ((int)today.DayOfWeek + 6) % 7;
                day = dow >= 5 ? today : today.AddDays(5 - dow);
                t = t.Remove(m.Index, m.Length).Insert(m.Index, " ");
                return true;
            }
            return false;
        }

        // Повтор словами: «каждый день», «ежедневно», «по будням», «по выходным», «каждую неделю», «каждый понедельник»,
        // «каждую пятницу», «по понедельникам и средам», «каждый месяц», «каждое 15-е число», «каждый месяц 1-го».
        static readonly string[] WeekdaysPlural = { "понедельник(?:ам|у|а)?|пн", "вторник(?:ам|у|а)?|вт", "сред(?:ам|у|а|ы)|ср", "четверг(?:ам|у|а)?|чт", "пятниц(?:ам|у|а|ы)|пт", "суббот(?:ам|у|а|ы)|сб", "воскресень(?:ям|е|я)|вс" };

        static int WeekdayIndex(string word)
        {
            for (int i = 0; i < 7; i++) if (Regex.IsMatch(word, "^(?:" + WeekdaysPlural[i] + ")$", RegexOptions.IgnoreCase)) return i;
            return -1;
        }

        static bool RepeatPhrase(ref string t, DateTime today, out string repeat, out DateTime first)
        {
            repeat = ""; first = today;
            Match m = Regex.Match(t, @"(?<=\s)(?:каждый\s+день|ежедневно|каждое\s+утро|каждый\s+вечер)" + End, RegexOptions.IgnoreCase);
            if (m.Success) repeat = "d";
            if (!m.Success) { m = Regex.Match(t, @"(?<=\s)(?:по\s+будням|в\s+будни|каждый\s+будний\s+день)" + End, RegexOptions.IgnoreCase); if (m.Success) repeat = "wd"; }
            if (!m.Success) { m = Regex.Match(t, @"(?<=\s)(?:по\s+выходным|каждые\s+выходные|в\s+выходные\s+дни)" + End, RegexOptions.IgnoreCase); if (m.Success) repeat = "we"; }
            if (!m.Success)
            {
                // «каждый понедельник», «каждую пятницу», «по средам», «по понедельникам и пятницам», «по пн, ср»
                string wd = "(?:" + string.Join("|", WeekdaysPlural) + ")";
                m = Regex.Match(t, @"(?<=\s)(?:кажд(?:ый|ую|ое|ые)|по)\s+(" + wd + @"(?:\s*(?:,|и)\s*" + wd + ")*)" + End, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var days = new List<int>();
                    foreach (string w in Regex.Split(m.Groups[1].Value, @"\s*(?:,|\sи\s)\s*"))
                    {
                        int i = WeekdayIndex(w.Trim());
                        if (i >= 0 && !days.Contains(i)) days.Add(i);
                    }
                    days.Sort();
                    if (days.Count == 0) m = Match.Empty;
                    else
                    {
                        var s = new List<string>();
                        foreach (int i in days) s.Add(i.ToString());
                        repeat = "w:" + string.Join(",", s.ToArray());
                    }
                }
            }
            if (!m.Success) { m = Regex.Match(t, @"(?<=\s)(?:каждую\s+неделю|еженедельно)" + End, RegexOptions.IgnoreCase); if (m.Success) repeat = "w:" + (((int)today.DayOfWeek + 6) % 7); }
            if (!m.Success)
            {
                // «каждое 15-е число», «каждый месяц 1-го», «ежемесячно», «каждый месяц»
                m = Regex.Match(t, @"(?<=\s)(?:кажд(?:ое|ый)\s+(\d{1,2})(?:-?(?:е|го))?(?:\s+числ[оа])?|(?:каждый\s+месяц|ежемесячно)(?:\s+(\d{1,2})(?:-?(?:го|е))?(?:\s+числа)?)?)" + End, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string n = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : today.Day.ToString();
                    int dom = int.Parse(n);
                    if (dom < 1 || dom > 31) m = Match.Empty; else repeat = "m:" + dom;
                }
            }
            if (!m.Success || repeat.Length == 0) { repeat = ""; return false; }
            t = t.Remove(m.Index, m.Length).Insert(m.Index, " ");
            first = NextDay(repeat, today);
            return true;
        }

        // Ближайший день повтора, начиная с from (включительно)
        public static DateTime NextDay(string repeat, DateTime from)
        {
            from = from.Date;
            if (repeat.StartsWith("m:"))
            {
                int dom; if (!int.TryParse(repeat.Substring(2), out dom)) return from;
                for (int k = 0; k < 3; k++)
                {
                    DateTime month = new DateTime(from.Year, from.Month, 1).AddMonths(k);
                    DateTime d = month.AddDays(Math.Min(dom, DateTime.DaysInMonth(month.Year, month.Month)) - 1);   // 31-го в коротком месяце — в последний день
                    if (d >= from) return d;
                }
                return from;
            }
            for (int k = 0; k < 8; k++)
            {
                DateTime d = from.AddDays(k);
                int dow = ((int)d.DayOfWeek + 6) % 7;
                if (repeat == "d" || repeat == "wd" && dow < 5 || repeat == "we" && dow >= 5) return d;
                if (repeat.StartsWith("w:") && Array.IndexOf(repeat.Substring(2).Split(','), dow.ToString()) >= 0) return d;
            }
            return from;
        }

        // «каждый день», «по будням», «каждый понедельник», «по пн, ср», «каждое 15-е»
        public static string RepeatLabel(string repeat)
        {
            if (repeat == "d") return "каждый день";
            if (repeat == "wd") return "по будням";
            if (repeat == "we") return "по выходным";
            if (repeat.StartsWith("m:")) return "каждое " + repeat.Substring(2) + "-е число";
            if (repeat.StartsWith("w:"))
            {
                string[] full = { "каждый понедельник", "каждый вторник", "каждую среду", "каждый четверг", "каждую пятницу", "каждую субботу", "каждое воскресенье" };
                string[] shortNames = { "пн", "вт", "ср", "чт", "пт", "сб", "вс" };
                string[] ds = repeat.Substring(2).Split(',');
                int one;
                if (ds.Length == 1 && int.TryParse(ds[0], out one) && one >= 0 && one < 7) return full[one];
                var s = new List<string>();
                foreach (string x in ds) { int i; if (int.TryParse(x, out i) && i >= 0 && i < 7) s.Add(shortNames[i]); }
                return "по " + string.Join(", ", s.ToArray());
            }
            return "";
        }

        // Шаги в заметке к задаче: «- билеты», «• паспорт», «[ ] вода» — не сделано; «✓ зарядка», «+ …», «[x] …» — сделано
        public static bool StepLine(string line, out bool done, out string text)
        {
            done = false; text = "";
            string s = (line ?? "").Trim();
            Match m = Regex.Match(s, @"^(?:✓|\+|\[[xхXХ✓]\])\s*(.+)$");
            if (m.Success) { done = true; text = m.Groups[1].Value.Trim(); return text.Length > 0; }
            m = Regex.Match(s, @"^(?:[-•*–]|\[\s?\])\s+(.+)$");
            if (m.Success) { text = m.Groups[1].Value.Trim(); return text.Length > 0; }
            return false;
        }

        // Отметить шаг или снять отметку — строка заметки переписывается: «- билеты» ⇄ «✓ билеты»
        public static string StepToggle(string line)
        {
            bool done; string text;
            if (!StepLine(line, out done, out text)) return line;
            string indent = line.Substring(0, line.Length - line.TrimStart().Length);
            return indent + (done ? "- " : "✓ ") + text;
        }

        // Сколько шагов в заметке и сколько сделано («1/3» у строки задачи)
        public static int StepCount(string note, out int done)
        {
            done = 0;
            int total = 0;
            foreach (string line in (note ?? "").Replace("\r", "").Split('\n'))
            {
                bool d; string t;
                if (!StepLine(line, out d, out t)) continue;
                total++; if (d) done++;
            }
            return total;
        }

        static bool MakeDay(int year, int month, int dayOfMonth, out DateTime d)
        {
            d = DateTime.MinValue;
            if (year < 2000 || year > 2100 || month < 1 || month > 12 || dayOfMonth < 1 || dayOfMonth > DateTime.DaysInMonth(year, month)) return false;
            d = new DateTime(year, month, dayOfMonth);
            return true;
        }
    }

    partial class IslandWindow
    {
        // ---- «Сегодня»: календарь месяца слева, план выбранного дня справа
        DateTime todayDay = DateTime.Today;                                   // выбранный день
        DateTime calMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        TextBlock calTitle, dayTitle, dayDate, dayInfo, dayHint;
        UniformGrid calGrid;
        StackPanel todayList;
        TextBox todayBox;

        FrameworkElement BuildTodayPage()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(214) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ---- месяц
            var cal = new DockPanel { Margin = new Thickness(12, 10, 12, 10) };
            var head = new Grid { Height = 26 };
            calTitle = new TextBlock { Foreground = Fg, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            head.Children.Add(calTitle);
            var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var prev = IconBtn(((char)0xE76B).ToString(), 24, delegate { calMonth = calMonth.AddMonths(-1); RenderToday(); });
            var next = IconBtn(((char)0xE76C).ToString(), 24, delegate { calMonth = calMonth.AddMonths(1); RenderToday(); });
            ((TextBlock)prev.Child).FontSize = 10; ((TextBlock)next.Child).FontSize = 10;
            prev.ToolTip = "Предыдущий месяц"; next.ToolTip = "Следующий месяц";
            // «Неделя»: семь дней колонками на всю вкладку
            var weekBtn = SmallPill("Неделя", delegate { SetTodayWeek(true); });
            weekBtn.ToolTip = "Семь дней рядом: задачи и события, перетаскивание между днями";
            weekBtn.Margin = new Thickness(0, 0, 4, 0);
            nav.Children.Add(weekBtn);
            nav.Children.Add(prev); nav.Children.Add(next);
            head.Children.Add(nav);
            DockPanel.SetDock(head, Dock.Top); cal.Children.Add(head);
            var wd = new UniformGrid { Columns = 7, Rows = 1, Margin = new Thickness(0, 6, 0, 2) };
            foreach (string d in new string[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" })
                wd.Children.Add(new TextBlock { Text = d, Foreground = Faint, FontSize = 9.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            DockPanel.SetDock(wd, Dock.Top); cal.Children.Add(wd);
            calGrid = new UniformGrid { Columns = 7, Rows = 6 };
            cal.Children.Add(calGrid);
            var calCard = new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Child = cal };

            // ---- день
            var day = new DockPanel();
            var dhead = new Grid { Margin = new Thickness(4, 2, 4, 6) };
            var titles = new StackPanel();
            dayTitle = new TextBlock { Foreground = Fg, FontSize = 19, FontWeight = FontWeights.SemiBold };
            dayDate = new TextBlock { Foreground = Dim, FontSize = 12 };
            titles.Children.Add(dayTitle); titles.Children.Add(dayDate);
            dhead.Children.Add(titles);
            dayInfo = new TextBlock { Foreground = Dim, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 3, 0, 0), ToolTip = "Второй часовой пояс меняется в настройках" };
            dhead.Children.Add(dayInfo);
            DockPanel.SetDock(dhead, Dock.Top); day.Children.Add(dhead);

            var input = new Grid { Height = 34, Margin = new Thickness(0, 6, 0, 0) };
            dayHint = new TextBlock { Foreground = Faint, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(2, 0, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis };
            todayBox = MakeTextBox(false);
            todayBox.TextChanged += delegate { dayHint.Visibility = todayBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            todayBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; AddTasksFromText(todayBox.Text); todayBox.Text = ""; } };
            var field = new Grid(); field.Children.Add(dayHint); field.Children.Add(todayBox);
            input.Children.Add(new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17), Padding = new Thickness(14, 0, 14, 0), Child = field });
            DockPanel.SetDock(input, Dock.Bottom); day.Children.Add(input);

            todayList = new StackPanel();
            day.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = todayList });
            // конструктор: месяц и день можно поменять местами или спрятать один
            var tdb = PageBoard(PToday, "today", 12, 1, 10);
            tdb.Add("month", "Месяц", calCard, 0, 0, 5, 1, false);
            tdb.Add("day", "День", day, 5, 0, 7, 1, false);
            GC.KeepAlive(g);
            // поверх доски — вид «Неделя»; видно что-то одно
            var host = new Grid();
            host.Children.Add(todayBoardRoot = FinishBoard(tdb));
            host.Children.Add(weekView = BuildWeekView());
            todayWeek = Store.Get("todayView") == "week";
            ApplyTodayMode(false);
            return host;
        }

        // Маленькая кнопка-пилюля в шапке («Неделя», «Месяц», «Сегодня»)
        Border SmallPill(string text, Action click)
        {
            var b = Btn(text, false, 0, 22, click);
            ((TextBlock)b.Child).FontSize = 11;
            b.Padding = new Thickness(9, 0, 9, 1);
            b.VerticalAlignment = VerticalAlignment.Center;
            return b;
        }

        // ---- «Неделя»: семь дней колонками — задачи и события рядом, задачу можно перетащить на другой день
        bool todayWeek;                 // вид «Неделя» вместо «месяц и день»
        DateTime weekStart = WeekOf(DateTime.Today), weekSetOn = DateTime.Today;   // понедельник показанной недели; листали — в этот день
        FrameworkElement todayBoardRoot, weekView;
        UniformGrid weekCols;
        TextBlock weekTitle, weekRange, weekHint;
        Border weekNow;
        TextBox weekBox;

        static DateTime WeekOf(DateTime d) { d = d.Date; return d.AddDays(-(((int)d.DayOfWeek + 6) % 7)); }

        // Будущий раз повторяющейся задачи приходится на этот день? (ближайший раз — сама задача, прошедшие не в счёт)
        static bool RepeatsOn(DayTask t, DateTime d, DateTime today)
        {
            return t.Repeat.Length > 0 && !t.Done && t.Day != Tasks.Someday && d > t.Day && d > today && Tasks.NextDay(t.Repeat, d) == d;
        }

        FrameworkElement BuildWeekView()
        {
            var v = new DockPanel { Width = PageW, Height = PageH };
            // шапка: «Эта неделя · 9–15 ноября», справа «Сегодня», стрелки и «Месяц»
            var head = new Grid { Height = 28, Margin = new Thickness(4, 0, 0, 6) };
            var titles = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            weekTitle = new TextBlock { Foreground = Fg, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            weekRange = new TextBlock { Foreground = Dim, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 1, 0, 0) };
            titles.Children.Add(weekTitle); titles.Children.Add(weekRange);
            head.Children.Add(titles);
            var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            weekNow = SmallPill("Сегодня", delegate { weekStart = WeekOf(DateTime.Today); weekSetOn = DateTime.Today; RenderToday(); });
            weekNow.ToolTip = "Вернуться к этой неделе";
            weekNow.Margin = new Thickness(0, 0, 4, 0);
            var prev = IconBtn(((char)0xE76B).ToString(), 24, delegate { weekStart = weekStart.AddDays(-7); weekSetOn = DateTime.Today; RenderToday(); });
            var next = IconBtn(((char)0xE76C).ToString(), 24, delegate { weekStart = weekStart.AddDays(7); weekSetOn = DateTime.Today; RenderToday(); });
            ((TextBlock)prev.Child).FontSize = 10; ((TextBlock)next.Child).FontSize = 10;
            prev.ToolTip = "Предыдущая неделя"; next.ToolTip = "Следующая неделя";
            var month = SmallPill("Месяц", delegate { SetTodayWeek(false); });
            month.ToolTip = "Календарь месяца и план дня";
            month.Margin = new Thickness(6, 0, 0, 0);
            nav.Children.Add(weekNow); nav.Children.Add(prev); nav.Children.Add(next); nav.Children.Add(month);
            head.Children.Add(nav);
            DockPanel.SetDock(head, Dock.Top); v.Children.Add(head);
            // внизу — добавить задачу: «в чт созвон» ложится на четверг показанной недели
            var input = new Grid { Height = 32, Margin = new Thickness(0, 6, 0, 0) };
            weekHint = new TextBlock { Foreground = Faint, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(2, 0, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis };
            weekBox = MakeTextBox(false);
            weekBox.TextChanged += delegate { weekHint.Visibility = weekBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            weekBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; AddWeekTasks(weekBox.Text); weekBox.Text = ""; } };
            var field = new Grid(); field.Children.Add(weekHint); field.Children.Add(weekBox);
            input.Children.Add(new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 0, 14, 0), Child = field });
            DockPanel.SetDock(input, Dock.Bottom); v.Children.Add(input);
            weekCols = new UniformGrid { Columns = 7, Rows = 1 };
            v.Children.Add(weekCols);
            return v;
        }

        void SetTodayWeek(bool on)
        {
            if (on == todayWeek) return;
            if (on)
            {
                weekStart = WeekOf(todayDay == Tasks.Someday ? DateTime.Today : todayDay); weekSetOn = DateTime.Today;
                Board b = BoardFor(PToday);
                if (b != null && b.Editing) EditPage(PToday, false);   // правка раскладки — только в виде «месяц и день»
            }
            todayWeek = on;
            Store.Set("todayView", on ? "week" : "");
            RenderToday();
            ApplyTodayMode(true);
        }

        // Показать один из видов; при переключении новый проявляется и чуть приподнимается
        void ApplyTodayMode(bool animate)
        {
            if (todayBoardRoot == null || weekView == null) return;
            FrameworkElement show = todayWeek ? weekView : todayBoardRoot, hide = todayWeek ? todayBoardRoot : weekView;
            hide.Visibility = Visibility.Collapsed;
            show.Visibility = Visibility.Visible;
            if (!animate || ReducedMotion) return;
            var tt = new TranslateTransform(0, 8);
            show.RenderTransform = tt;
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            show.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }

        void AddWeekTasks(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            DateTime today = DateTime.Today;
            DateTime from = weekStart == WeekOf(today) ? today : weekStart;   // «в пятницу» — пятница показанной недели
            List<DayTask> all = Tasks.Load();
            List<DayTask> fresh = Tasks.Parse(text, from);
            all.AddRange(fresh);
            Tasks.Save(all);
            if (fresh.Count > 0 && fresh[0].Day != Tasks.Someday) { weekStart = WeekOf(fresh[0].Day); weekSetOn = today; movedTo = fresh[0].Day; movedAt = Environment.TickCount; }
            Toast(((char)0xE73E).ToString(), fresh.Count == 1 ? "Задача добавлена" : "Добавлено задач: " + fresh.Count, null, -1, accent);
            RenderToday();
        }

        // «9–15 ноября», «27 октября – 2 ноября», «29 декабря 2026 – 4 января 2027»
        static string WeekRange(DateTime start, CultureInfo ru)
        {
            DateTime end = start.AddDays(6);
            bool thisYear = start.Year == DateTime.Today.Year && end.Year == DateTime.Today.Year;
            if (start.Month == end.Month) return start.Day + "–" + end.ToString("d MMMM", ru) + (thisYear ? "" : " " + end.Year);
            if (start.Year == end.Year) return start.ToString("d MMMM", ru) + " – " + end.ToString("d MMMM", ru) + (thisYear ? "" : " " + end.Year);
            return start.ToString("d MMMM yyyy", ru) + " – " + end.ToString("d MMMM yyyy", ru);
        }

        void RenderWeek(List<DayTask> all, CultureInfo ru)
        {
            DateTime today = DateTime.Today, now = DateTime.Now;
            if (weekSetOn != today) { weekStart = WeekOf(today); weekSetOn = today; }   // листали вчера — сегодня снова эта неделя
            DateTime cur = WeekOf(today);
            weekTitle.Text = weekStart == cur ? "Эта неделя" : weekStart == cur.AddDays(7) ? "Следующая неделя" : weekStart == cur.AddDays(-7) ? "Прошлая неделя" : "Неделя";
            weekRange.Text = WeekRange(weekStart, ru);
            weekNow.Visibility = weekStart == cur ? Visibility.Collapsed : Visibility.Visible;
            weekHint.Text = weekStart == cur ? "Добавить: «в чт в 19 кино, в сб спортзал»" : "Добавить на эту неделю: «в пт отчёт»";
            weekCols.Children.Clear();
            for (int i = 0; i < 7; i++) weekCols.Children.Add(WeekColumn(weekStart.AddDays(i), i, all, ru, now));
        }

        FrameworkElement WeekColumn(DateTime d, int index, List<DayTask> all, CultureInfo ru, DateTime now)
        {
            DateTime today = now.Date;
            bool isToday = d == today, past = d < today, weekend = d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;
            var dock = new DockPanel();
            // шапка дня: «пн» и число (сегодня — в акцентном кружке); щелчок открывает этот день
            var dayHead = new StackPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Открыть этот день", Margin = new Thickness(0, 1, 0, 5) };
            string dn = ru.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek);
            dayHead.Children.Add(new TextBlock { Text = dn, Foreground = isToday ? accent : weekend || past ? Faint : Dim, FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            var num = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = isToday ? accent : Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0),
                Child = new TextBlock { Text = d.Day.ToString(), Foreground = isToday ? (Brush)Brushes.White : past ? Faint : Fg, FontSize = 13.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            dayHead.Children.Add(num);
            DateTime dd = d;
            dayHead.MouseEnter += delegate { if (!isToday) num.Background = ChipHover; };
            dayHead.MouseLeave += delegate { if (!isToday) num.Background = Brushes.Transparent; };
            dayHead.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true; todayDay = dd; calMonth = new DateTime(dd.Year, dd.Month, 1);
                SetTodayWeek(false);
            };
            FrameworkElement headEl = dayHead;
            if (isToday)
            {
                // на шапке сегодняшнего дня сидит зверёк (попутчик острова садится на его место); всё сделано — радуется
                var seat = new PetView(1.2, Ink);
                bool clear = all.Exists(delegate(DayTask t) { return t.Done && t.Day == dd; }) && !all.Exists(delegate(DayTask t) { return !t.Done && t.Day <= dd; });
                seat.Mood(clear ? "happy" : "idle");
                seat.Root.HorizontalAlignment = HorizontalAlignment.Right;
                seat.Root.VerticalAlignment = VerticalAlignment.Top;
                seat.Root.Margin = new Thickness(0, 1, -2, 0);
                var hg = new Grid();
                hg.Children.Add(dayHead); hg.Children.Add(seat.Root);
                headEl = hg;
            }
            DockPanel.SetDock(headEl, Dock.Top); dock.Children.Add(headEl);

            var items = new StackPanel();
            foreach (CalEvent ev in EventsOn(d)) items.Children.Add(WeekEvent(ev, now));
            // задачи дня: открытые по времени, сделанные внизу; у сегодняшнего дня сверху — просроченные
            // (на этой неделе они только там, а не ещё и в своём прошедшем дне)
            bool curWeek = weekStart == WeekOf(today);
            List<DayTask> list = all.FindAll(delegate(DayTask t) { return t.Day == dd && !(curWeek && t.Day < today && !t.Done); });
            list.Sort(delegate(DayTask a, DayTask b)
            {
                if (a.Done != b.Done) return a.Done ? 1 : -1;
                TimeSpan ta = a.Time ?? TimeSpan.MaxValue, tb = b.Time ?? TimeSpan.MaxValue;
                return ta.CompareTo(tb);
            });
            if (isToday) list.InsertRange(0, all.FindAll(delegate(DayTask t) { return t.Day < today && !t.Done; }));
            foreach (DayTask t in list) items.Children.Add(WeekTask(t, false));
            // повторяющиеся — бледно и в следующие свои дни: видно, что впереди (отметить можно в свой день)
            foreach (DayTask t in all)
                if (RepeatsOn(t, dd, today)) items.Children.Add(WeekTask(t, true));
            dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = items });

            var col = new Border { Background = Card, BorderBrush = isToday ? accent : Line, BorderThickness = new Thickness(isToday ? 1.4 : 1), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(2, 4, 2, 2), Margin = new Thickness(index == 0 ? 0 : 1.5, 0, index == 6 ? 0 : 1.5, 0), Child = dock };
            // сюда можно перетащить задачу — она переедет на этот день
            Brush ring = col.BorderBrush;
            Thickness ringW = col.BorderThickness;
            col.AllowDrop = true;
            col.DragEnter += delegate(object s, DragEventArgs e) { if (e.Data.GetDataPresent(TaskFormat)) { col.BorderBrush = accent; col.BorderThickness = new Thickness(2); } };
            col.DragOver += delegate(object s, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(TaskFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            col.DragLeave += delegate { col.BorderBrush = ring; col.BorderThickness = ringW; };
            col.Drop += delegate(object s, DragEventArgs e)
            {
                e.Handled = true;
                col.BorderBrush = ring; col.BorderThickness = ringW;
                string id = e.Data.GetData(TaskFormat) as string;
                if (id != null) TaskMove(id, dd);
            };
            var accentSolid = accent as SolidColorBrush;
            if (dd == movedTo && unchecked(Environment.TickCount - movedAt) < 400 && accentSolid != null)
            {
                // задачу только что перенесли (или добавили) сюда — колонка коротко вспыхивает
                var flash = new SolidColorBrush(accentSolid.Color) { Opacity = 0.35 };
                var glow = new Border { CornerRadius = new CornerRadius(14), Background = flash, IsHitTestVisible = false, Margin = col.Margin };
                var cell = new Grid();
                cell.Children.Add(col); cell.Children.Add(glow);
                flash.BeginAnimation(Brush.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.35, 0, TimeSpan.FromMilliseconds(900)));
                return cell;
            }
            return col;
        }

        // Событие календаря в колонке недели: время и название, подложка цвета акцента
        Border WeekEvent(CalEvent ev, DateTime now)
        {
            bool past = !ev.AllDay && ev.Start < now;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new Border { Width = 2.5, CornerRadius = new CornerRadius(1.5), Background = accent, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 1) });
            var texts = new StackPanel();
            texts.Children.Add(new TextBlock { Text = ev.AllDay ? "весь день" : ev.Start.ToString("H:mm"), Foreground = Dim, FontSize = 9.5, FontWeight = FontWeights.SemiBold });
            texts.Children.Add(new TextBlock { Text = ev.Subject, Foreground = Fg, FontSize = 10.5, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 13, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = 26 });
            Grid.SetColumn(texts, 1); g.Children.Add(texts);
            var accentSolid = accent as SolidColorBrush;
            Brush tint = accentSolid != null ? new SolidColorBrush(accentSolid.Color) { Opacity = 0.14 } : (Brush)Chip;
            return new Border { CornerRadius = new CornerRadius(8), Background = tint, Padding = new Thickness(4, 3, 2, 4), Margin = new Thickness(0, 0, 0, 3), Child = g, Opacity = past ? 0.5 : 1,
                ToolTip = ev.Subject + (ev.Location.Length > 0 ? " · " + ev.Location : "") };
        }

        // Задача в колонке недели: кружок и текст в две-три строки, под ним время. Щелчок — отметить,
        // повели с зажатой кнопкой — перетащить на другой день, правый клик — удалить.
        // Будущий раз повторяющейся (ghost) — бледный, с пунктирным кружком, без действий.
        Border WeekTask(DayTask task, bool ghost)
        {
            bool overdue = !ghost && IsOverdue(task);
            bool done = task.Done && !ghost;
            // колонка узкая (около 56 точек на текст): кружок — в своей узкой полосе слева, текст мельче, чем в списке дня
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dot = new Ellipse { Width = 7, Height = 7, StrokeThickness = 1.2, Stroke = done ? accent : overdue ? ClaudeOrange : Dim, Fill = done ? (Brush)accent : Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3.5, 0, 0) };
            if (ghost) dot.StrokeDashArray = new DoubleCollection { 2, 1.6 };
            g.Children.Add(dot);
            var texts = new StackPanel();
            texts.Children.Add(new TextBlock { Text = task.Text, Foreground = done ? Faint : Fg, FontSize = 10.5, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                LineHeight = 13.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = 42, TextDecorations = done ? TextDecorations.Strikethrough : null });   // до трёх строк (с запасом на округление)
            Grid.SetColumn(texts, 1); g.Children.Add(texts);
            if (overdue || task.Time.HasValue || task.Repeat.Length > 0)
            {
                var meta = new TextBlock { Foreground = overdue ? ClaudeOrange : Faint, FontSize = 9.5, Margin = new Thickness(0, 1, 0, 0) };
                if (overdue) meta.Inlines.Add(new System.Windows.Documents.Run("просрочено"));
                else if (task.Time.HasValue) meta.Inlines.Add(new System.Windows.Documents.Run(new DateTime(task.Time.Value.Ticks).ToString("H:mm")));
                if (task.Repeat.Length > 0) meta.Inlines.Add(new System.Windows.Documents.Run((meta.Inlines.Count > 0 ? "  " : "") + "") { FontFamily = Icons, FontSize = 8.5 });
                texts.Children.Add(meta);
            }
            string repeatTip = task.Repeat.Length > 0 ? "Повтор: " + Tasks.RepeatLabel(task.Repeat) + ". " : "";
            var b = new Border { CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Padding = new Thickness(2, 3, 1, 4), Margin = new Thickness(0, 0, 0, 2), Child = g };
            if (ghost)
            {
                b.Opacity = 0.42;
                b.ToolTip = repeatTip + "Отметить можно в свой день";
                return b;
            }
            b.Cursor = Cursors.Hand;
            b.ToolTip = repeatTip + (task.Note.Length > 0 ? "Есть заметка. " : "") + "Клик — отметить, перетащите на другой день — перенести, правый клик — удалить";
            b.MouseEnter += delegate { b.Background = ChipHover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            bool pressed = false;
            Point pressAt = new Point();
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; pressed = true; pressAt = e.GetPosition(b); b.CaptureMouse(); };
            b.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
                Point p = e.GetPosition(b);
                if (Math.Abs(p.X - pressAt.X) < 6 && Math.Abs(p.Y - pressAt.Y) < 6) return;
                pressed = false;
                b.ReleaseMouseCapture();
                b.Opacity = 0.45;   // задача «в пути»
                try { DragDrop.DoDragDrop(b, new DataObject(TaskFormat, task.Id), DragDropEffects.Move); }
                catch (Exception ex) { Store.LogError("перенос задачи", ex); }
                finally { b.Opacity = 1; }
            };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (!pressed) return;
                pressed = false; e.Handled = true;
                b.ReleaseMouseCapture();
                TaskToggle(task.Id);
            };
            b.LostMouseCapture += delegate { pressed = false; };
            b.MouseRightButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                List<DayTask> cur = Tasks.Load();
                cur.RemoveAll(delegate(DayTask y) { return y.Id == task.Id; });
                Tasks.Save(cur);
                RenderToday();
            };
            return b;
        }
        void AddTasksFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            List<DayTask> all = Tasks.Load();
            List<DayTask> fresh = Tasks.Parse(text, todayDay);   // без «завтра» и т. п. — на выбранный в календаре день
            all.AddRange(fresh);
            Tasks.Save(all);
            if (fresh.Count > 0) todayDay = fresh[0].Day;
            Toast(((char)0xE73E).ToString(), fresh.Count == 1 ? "Задача добавлена" : "Добавлено задач: " + fresh.Count, null, -1, accent);
            RenderToday();
        }

        static string WorldClockLabel(out DateTime time)
        {
            string id = Store.Get("worldClock");
            if (id == "") id = "Eastern Standard Time";
            string label = Store.Get("worldClockName");
            if (label == "") label = "Нью-Йорк";
            try { time = TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.FindSystemTimeZoneById(id)); }
            catch { time = DateTime.UtcNow; label = "UTC"; }
            return label;
        }

        // События календаря Windows на день (уже загруженные; читаются с запасом на месяц назад и три вперёд)
        static List<CalEvent> EventsOn(DateTime d)
        {
            var res = Calendar.Events().FindAll(delegate(CalEvent e) { return e.Start.Date == d.Date; });
            res.Sort(delegate(CalEvent a, CalEvent b) { if (a.AllDay != b.AllDay) return a.AllDay ? -1 : 1; return a.Start.CompareTo(b.Start); });
            return res;
        }

        void RenderToday()
        {
            if (calGrid == null) return;
            if (taskNoteBox != null && taskNoteBox.Tag is string) TaskNoteSet((string)taskNoteBox.Tag, taskNoteBox.Text);   // недописанная заметка не теряется
            Calendar.Refresh(false);
            var ru = new CultureInfo("ru-RU");
            DateTime now = DateTime.Now, today = DateTime.Today;
            List<DayTask> all = Tasks.Load();
            if (todayWeek) { RenderWeek(all, ru); return; }   // вид «Неделя» — месяц и день спрятаны
            List<CalEvent> evAll = Calendar.Events();

            // ---- месяц
            string mn = ru.DateTimeFormat.GetMonthName(calMonth.Month);
            calTitle.Text = char.ToUpper(mn[0]) + mn.Substring(1) + (calMonth.Year != today.Year ? " " + calMonth.Year : "");
            calGrid.Children.Clear();
            DateTime first = calMonth.AddDays(-(((int)calMonth.DayOfWeek + 6) % 7));
            for (int i = 0; i < 42; i++)
            {
                DateTime d = first.AddDays(i);
                bool inMonth = d.Month == calMonth.Month, isToday = d == today, sel = d == todayDay;
                bool hasTask = all.Exists(delegate(DayTask t) { return t.Day == d && !t.Done; });
                bool hasEv = evAll.Exists(delegate(CalEvent e) { return e.Start.Date == d; });
                var cell = new Grid { Height = 25 };
                var circle = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                    Background = isToday ? (Brush)accent : Brushes.Transparent, BorderBrush = sel && !isToday ? Fg : null, BorderThickness = new Thickness(sel && !isToday ? 1.4 : 0),
                    Child = new TextBlock { Text = d.Day.ToString(), FontSize = 11, FontWeight = isToday || sel ? FontWeights.Bold : FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        Foreground = isToday ? (Brush)Brushes.White : !inMonth ? Faint : (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday ? Dim : Fg) } };
                cell.Children.Add(circle);
                // точки под числом: синяя — событие календаря, оранжевая — открытая задача
                var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -1) };
                if (hasEv) dots.Children.Add(new Ellipse { Width = 3, Height = 3, Fill = isToday ? Fg : (Brush)accent, Margin = new Thickness(0.8, 0, 0.8, 0) });
                if (hasTask) dots.Children.Add(new Ellipse { Width = 3, Height = 3, Fill = ClaudeOrange, Margin = new Thickness(0.8, 0, 0.8, 0) });
                cell.Children.Add(dots);
                var hit = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = cell, Opacity = inMonth ? 1 : 0.55 };
                DateTime dd = d;
                hit.MouseEnter += delegate { if (dd != today) circle.Background = ChipHover; };
                hit.MouseLeave += delegate { if (dd != today) circle.Background = Brushes.Transparent; };
                hit.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true; todayDay = dd;
                    if (dd.Month != calMonth.Month || dd.Year != calMonth.Year) calMonth = new DateTime(dd.Year, dd.Month, 1);
                    RenderToday();
                };
                // сюда можно перетащить задачу — она переедет на этот день
                Brush ringWas = circle.BorderBrush;
                Thickness ringWidth = circle.BorderThickness;
                hit.AllowDrop = true;
                hit.DragEnter += delegate(object s, DragEventArgs e) { if (e.Data.GetDataPresent(TaskFormat)) { circle.BorderBrush = accent; circle.BorderThickness = new Thickness(2); } };
                hit.DragOver += delegate(object s, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(TaskFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
                hit.DragLeave += delegate { circle.BorderBrush = ringWas; circle.BorderThickness = ringWidth; };
                hit.Drop += delegate(object s, DragEventArgs e)
                {
                    e.Handled = true;
                    circle.BorderBrush = ringWas; circle.BorderThickness = ringWidth;
                    string id = e.Data.GetData(TaskFormat) as string;
                    if (id != null) TaskMove(id, dd);
                };
                var accentSolid = accent as SolidColorBrush;
                if (dd == movedTo && unchecked(Environment.TickCount - movedAt) < 400 && !isToday && accentSolid != null)
                {
                    // задачу только что перенесли сюда — число коротко вспыхивает
                    var flash = new SolidColorBrush(accentSolid.Color) { Opacity = 0.5 };
                    circle.Background = flash;
                    flash.BeginAnimation(Brush.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.5, 0, TimeSpan.FromMilliseconds(900)));
                }
                calGrid.Children.Add(hit);
            }

            // ---- день
            string dn = ru.DateTimeFormat.GetDayName(todayDay.DayOfWeek);
            dayTitle.Text = todayDay == today ? "Сегодня" : todayDay == today.AddDays(1) ? "Завтра" : todayDay == today.AddDays(-1) ? "Вчера" : char.ToUpper(dn[0]) + dn.Substring(1);
            dayDate.Text = (todayDay == today || todayDay == today.AddDays(1) || todayDay == today.AddDays(-1) ? dn + ", " : "") + todayDay.ToString("d MMMM", ru) + (todayDay.Year != today.Year ? " " + todayDay.Year : "");
            DateTime other;
            string city = WorldClockLabel(out other);
            dayInfo.Inlines.Clear();
            if (weather != null && weather.Error == null && todayDay == today) dayInfo.Inlines.Add(new System.Windows.Documents.Run(Weather.Temp(weather.Temp) + " · ") { Foreground = Fg, FontWeight = FontWeights.SemiBold });
            dayInfo.Inlines.Add(new System.Windows.Documents.Run(city + " ") { Foreground = Dim });
            dayInfo.Inlines.Add(new System.Windows.Documents.Run(other.ToString("H:mm")) { Foreground = Fg, FontWeight = FontWeights.SemiBold });
            dayHint.Text = todayDay == today ? "Добавить: «в 14:00 созвон, в пятницу отчёт»" : "Добавить на " + todayDay.ToString("d MMMM", ru) + ": «в 9 спорт»";

            todayList.Children.Clear();
            // события календаря Windows
            foreach (CalEvent ev in EventsOn(todayDay))
            {
                bool past = !ev.AllDay && ev.Start < now;
                var row = new Grid { Height = 32, Margin = new Thickness(0, 0, 0, 2), Opacity = past ? 0.5 : 1 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new Border { Width = 3.5, CornerRadius = new CornerRadius(2), Background = accent, Margin = new Thickness(6, 6, 0, 6), HorizontalAlignment = HorizontalAlignment.Left });
                var time = new TextBlock { Text = ev.AllDay ? "весь день" : ev.Start.ToString("H:mm"), Foreground = Dim, FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(time, 1); row.Children.Add(time);
                var subj = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                subj.Children.Add(new TextBlock { Text = ev.Subject, Foreground = Fg, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                if (ev.Location.Length > 0) subj.Children.Add(new TextBlock { Text = ev.Location, Foreground = Faint, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(subj, 2); row.Children.Add(subj);
                if (!ev.AllDay && ev.Start > now && ev.Start < now.AddHours(12))
                {
                    var inTxt = new TextBlock { Text = Calendar.In(ev.Start, now), Foreground = accent, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                    Grid.SetColumn(inTxt, 2); row.Children.Add(inTxt);
                }
                todayList.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Card, Child = row, Margin = new Thickness(0, 0, 0, 4) });
            }

            // задачи выбранного дня (и просроченные — сегодня, сверху)
            List<DayTask> list = all.FindAll(delegate(DayTask t) { return t.Day == todayDay; });
            list.Sort(delegate(DayTask a, DayTask b)
            {
                if (a.Done != b.Done) return a.Done ? 1 : -1;
                TimeSpan ta = a.Time ?? TimeSpan.MaxValue, tb = b.Time ?? TimeSpan.MaxValue;
                return ta.CompareTo(tb);
            });
            if (todayDay == today) list.InsertRange(0, all.FindAll(delegate(DayTask t) { return t.Day < today && !t.Done; }));
            // «Скоро»: открытые задачи на ближайшую неделю — видно, что впереди, не листая календарь
            List<DayTask> soon = todayDay != today ? new List<DayTask>() : all.FindAll(delegate(DayTask t) { return !t.Done && t.Day > today && t.Day <= today.AddDays(7); });
            soon.Sort(delegate(DayTask a, DayTask b)
            {
                if (a.Day != b.Day) return a.Day.CompareTo(b.Day);
                TimeSpan ta = a.Time ?? TimeSpan.MaxValue, tb = b.Time ?? TimeSpan.MaxValue;
                return ta.CompareTo(tb);
            });
            bool more = soon.Count > 0 || todayDay == today && all.Exists(delegate(DayTask t) { return !t.Done && t.Day == Tasks.Someday; });   // ниже будут «Скоро» или «Когда-нибудь»
            if (list.Count == 0 && todayList.Children.Count == 0)
            {
                var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, more ? 8 : 26, 0, more ? 4 : 0) };
                if (!more) empty.Children.Add(new TextBlock { Text = ((char)0xE787).ToString(), FontFamily = Icons, FontSize = 22, Foreground = Faint, HorizontalAlignment = HorizontalAlignment.Center });
                empty.Children.Add(new TextBlock { Text = todayDay == today ? "Сегодня ничего не запланировано" : "Свободный день", Foreground = Dim, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
                todayList.Children.Add(empty);
            }
            foreach (DayTask tk in list)
            {
                todayList.Children.Add(TaskRow(tk, false));
                if (tk.Id == taskOpenId) todayList.Children.Add(TaskNoteEditor(tk));   // раскрытая заметка — прямо под строкой
            }
            if (soon.Count > 0) todayList.Children.Add(new TextBlock { Text = "Скоро", Foreground = Faint, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 10, 0, 2) });
            DateTime shown = DateTime.MinValue;
            foreach (DayTask tk in soon)
            {
                if (tk.Day != shown)
                {
                    // день — заголовком; щелчок открывает его в календаре
                    shown = tk.Day;
                    DateTime dd = tk.Day;
                    string dname = ru.DateTimeFormat.GetAbbreviatedDayName(dd.DayOfWeek);
                    string label = dd == today.AddDays(1) ? "Завтра" : char.ToUpper(dname[0]) + dname.Substring(1) + ", " + dd.ToString("d MMMM", ru);
                    var head = new TextBlock { Text = label, Foreground = Dim, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 4, 0, 0), Cursor = Cursors.Hand, ToolTip = "Открыть этот день" };
                    head.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                    {
                        e.Handled = true; todayDay = dd;
                        if (dd.Month != calMonth.Month || dd.Year != calMonth.Year) calMonth = new DateTime(dd.Year, dd.Month, 1);
                        RenderToday();
                    };
                    todayList.Children.Add(head);
                }
                todayList.Children.Add(TaskRow(tk, true));
                if (tk.Id == taskOpenId) todayList.Children.Add(TaskNoteEditor(tk));
            }
            // «Когда-нибудь»: задачи без дня — их можно перетащить на число в календаре, когда придёт время
            if (todayDay != today) return;
            List<DayTask> someday = all.FindAll(delegate(DayTask t) { return !t.Done && t.Day == Tasks.Someday; });
            if (someday.Count == 0) return;
            todayList.Children.Add(new TextBlock { Text = "Когда-нибудь", Foreground = Faint, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 10, 0, 2), ToolTip = "Задачи без дня — перетащите на число в календаре, когда придёт время" });
            foreach (DayTask tk in someday)
            {
                todayList.Children.Add(TaskRow(tk, true));
                if (tk.Id == taskOpenId) todayList.Children.Add(TaskNoteEditor(tk));
            }
        }

        // ---- заметка к задаче: стрелочка справа у строки раскрывает поле прямо под ней; пишется — сохраняется само
        string taskOpenId;
        public TextBox taskNoteBox;   // открытое поле заметки (для проверок)
        DispatcherTimer taskNoteSave;

        FrameworkElement TaskNoteEditor(DayTask task)
        {
            string id = task.Id;
            var hint = new TextBlock { Text = "Заметка к задаче: что взять, кому позвонить, ссылка…", Foreground = Faint, FontSize = 12, IsHitTestVisible = false, Margin = new Thickness(2, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            var box = taskNoteBox = MakeTextBox(true);
            box.FontSize = 12.5; box.MinHeight = 38; box.MaxHeight = 110;
            box.Text = task.Note;
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            box.TextChanged += delegate
            {
                hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (taskNoteSave == null)
                {
                    taskNoteSave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                    taskNoteSave.Tick += delegate { taskNoteSave.Stop(); if (taskNoteBox != null && taskNoteBox.Tag is string) TaskNoteSet((string)taskNoteBox.Tag, taskNoteBox.Text); };
                }
                taskNoteSave.Stop(); taskNoteSave.Start();   // сохраняем, когда вы остановились
            };
            box.Tag = id;
            box.LostKeyboardFocus += delegate { TaskNoteSet(id, box.Text); };
            box.PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; TaskNoteSet(id, box.Text); taskOpenId = null; RenderToday(); } };
            var field = new Grid(); field.Children.Add(hint); field.Children.Add(box);
            // шаги из заметки («- билеты») — пунктами с отметкой над полем; щелчок переписывает строку в заметке
            var steps = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            Action renderSteps = delegate
            {
                steps.Children.Clear();
                string[] lines = box.Text.Replace("\r", "").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    bool done; string text;
                    if (!Tasks.StepLine(lines[i], out done, out text)) continue;
                    int li = i;
                    var row = new Grid { Height = 22, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = done ? "Снять отметку" : "Отметить шаг" };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var mark = new Grid { Width = 13, Height = 13, HorizontalAlignment = HorizontalAlignment.Left };
                    mark.Children.Add(new Ellipse { Stroke = done ? accent : Dim, StrokeThickness = 1.4, Fill = done ? (Brush)accent : Brushes.Transparent });
                    if (done) mark.Children.Add(new TextBlock { Text = ((char)0xE73E).ToString(), FontFamily = Icons, FontSize = 7.5, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(mark);
                    var tx = new TextBlock { Text = text, Foreground = done ? Faint : Fg, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextDecorations = done ? TextDecorations.Strikethrough : null };
                    Grid.SetColumn(tx, 1); row.Children.Add(tx);
                    row.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                    {
                        e.Handled = true;
                        string[] cur = box.Text.Replace("\r", "").Split('\n');
                        if (li < cur.Length) { cur[li] = Tasks.StepToggle(cur[li]); box.Text = string.Join("\n", cur); }   // поле само сохранит и перерисует шаги
                        bool nowDone; string t2;
                        if (li < cur.Length && Tasks.StepLine(cur[li], out nowDone, out t2) && nowDone) Happy(700);
                    };
                    steps.Children.Add(row);
                }
                steps.Visibility = steps.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                // есть шаги — сам текст заметки под ними приглушён, пока в нём не пишут: главное — список
                box.Foreground = steps.Children.Count > 0 && !box.IsKeyboardFocusWithin ? Faint : Fg;
            };
            box.TextChanged += delegate { renderSteps(); };
            box.GotKeyboardFocus += delegate { box.Foreground = Fg; };
            box.LostKeyboardFocus += delegate { renderSteps(); };
            renderSteps();
            var stack = new StackPanel();
            stack.Children.Add(steps);
            stack.Children.Add(field);
            return new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(30, 0, 8, 6), Child = stack };
        }

        void TaskNoteSet(string id, string note)
        {
            note = (note ?? "").Trim();
            List<DayTask> cur = Tasks.Load();
            DayTask x = cur.Find(delegate(DayTask y) { return y.Id == id; });
            if (x == null || x.Note == note) return;
            x.Note = note;
            Tasks.Save(cur);   // без перерисовки: поле, в котором вы пишете, остаётся на месте
        }

        void TaskNoteToggle(string id)
        {
            if (taskOpenId != null && taskNoteBox != null && taskNoteBox.Tag is string) TaskNoteSet((string)taskNoteBox.Tag, taskNoteBox.Text);
            taskOpenId = taskOpenId == id ? null : id;
            taskNoteBox = null;
            RenderToday();
        }

        // Строка задачи: кружок, текст, справа время или «Просрочено». Клик — отметить, правый клик — удалить.
        // В «Скоро» — строка пониже и помельче.
        Border TaskRow(DayTask task, bool small)
        {
            var row = new Grid { Height = small ? 26 : 30 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });   // стрелочка: заметка к задаче
            double bs = small ? 14 : 16;
            var box = new Grid { Width = bs, Height = bs, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(small ? 9 : 8, 0, 0, 0) };
            box.Children.Add(new Ellipse { Stroke = task.Done ? accent : Dim, StrokeThickness = 1.6, Fill = task.Done ? (Brush)accent : Brushes.Transparent });
            if (task.Done) box.Children.Add(new TextBlock { Text = ((char)0xE73E).ToString(), FontFamily = Icons, FontSize = 9, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(box);
            var txt = new TextBlock { Text = task.Text, Foreground = task.Done ? Faint : Fg, FontSize = small ? 12 : 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextDecorations = task.Done ? TextDecorations.Strikethrough : null };
            Grid.SetColumn(txt, 1); row.Children.Add(txt);
            string right = IsOverdue(task) ? "Просрочено" : task.Time.HasValue ? new DateTime(task.Time.Value.Ticks).ToString("H:mm") : "";
            var r = new TextBlock { Foreground = IsOverdue(task) ? ClaudeOrange : Dim, FontSize = small ? 11 : 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
            if (task.Note.Length > 0)
            {
                // есть заметка — значок; есть шаги — сколько сделано: «1/3»
                int stepsDone, steps = Tasks.StepCount(task.Note, out stepsDone);
                r.Inlines.Add(new System.Windows.Documents.Run("") { FontFamily = Icons, FontSize = small ? 9.5 : 10.5, Foreground = Faint });
                if (steps > 0) r.Inlines.Add(new System.Windows.Documents.Run(" " + stepsDone + "/" + steps) { Foreground = stepsDone == steps ? (Brush)accent : Faint, FontSize = small ? 10.5 : 11 });
                if (task.Repeat.Length > 0 || right.Length > 0) r.Inlines.Add(new System.Windows.Documents.Run("  "));
            }
            if (task.Repeat.Length > 0)
            {
                // повторяющаяся: значок «повтор» перед временем
                r.Inlines.Add(new System.Windows.Documents.Run("") { FontFamily = Icons, FontSize = small ? 9.5 : 10.5 });
                if (right.Length > 0) r.Inlines.Add(new System.Windows.Documents.Run("  " + right));
            }
            else if (task.Note.Length > 0) r.Inlines.Add(new System.Windows.Documents.Run(right));
            else r.Text = right;
            Grid.SetColumn(r, 2); row.Children.Add(r);
            // стрелочка: раскрыть заметку под строкой (видна при наведении, у раскрытой — всегда)
            bool open = task.Id == taskOpenId;
            var chev = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = open ? "Свернуть заметку" : "Заметка к задаче", Opacity = open ? 1 : 0,
                Child = new TextBlock { Text = open ? "" : "", FontFamily = Icons, FontSize = 9, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            string tid = task.Id;
            chev.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; TaskNoteToggle(tid); };
            Grid.SetColumn(chev, 3); row.Children.Add(chev);
            string tip = (task.Repeat.Length > 0 ? "Повтор: " + Tasks.RepeatLabel(task.Repeat) + ". " : "") + "Клик — отметить, перетащите на число в календаре — перенести, правый клик — удалить";
            var b = new Border { CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = row, ToolTip = tip };
            b.MouseEnter += delegate { b.Background = ChipHover; chev.Opacity = 1; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; if (!open) chev.Opacity = 0; };
            // щелчок — отметить; повели с зажатой кнопкой — перетаскивание на число в календаре
            bool pressed = false;
            Point pressAt = new Point();
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; pressed = true; pressAt = e.GetPosition(b); b.CaptureMouse(); };
            b.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
                Point p = e.GetPosition(b);
                if (Math.Abs(p.X - pressAt.X) < 6 && Math.Abs(p.Y - pressAt.Y) < 6) return;
                pressed = false;
                b.ReleaseMouseCapture();
                b.Opacity = 0.45;   // строка «в пути»
                try { DragDrop.DoDragDrop(b, new DataObject(TaskFormat, task.Id), DragDropEffects.Move); }
                catch (Exception ex) { Store.LogError("перенос задачи", ex); }
                finally { b.Opacity = 1; }
            };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (!pressed) return;
                pressed = false; e.Handled = true;
                b.ReleaseMouseCapture();
                TaskToggle(task.Id);
            };
            b.LostMouseCapture += delegate { pressed = false; };
            b.MouseRightButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                List<DayTask> cur = Tasks.Load();
                cur.RemoveAll(delegate(DayTask y) { return y.Id == task.Id; });
                Tasks.Save(cur);
                RenderToday();
            };
            return b;
        }

        // Перенос задачи на другой день (перетащили на число в календаре). Время и повтор остаются как были.
        const string TaskFormat = "KapsulaTask";
        DateTime movedTo = DateTime.MinValue;
        int movedAt;

        public void TaskMove(string id, DateTime day)
        {
            List<DayTask> cur = Tasks.Load();
            DayTask x = cur.Find(delegate(DayTask y) { return y.Id == id; });
            if (x == null || x.Day == day.Date) return;
            x.Day = day.Date;
            Tasks.Save(cur);
            movedTo = day.Date; movedAt = Environment.TickCount;
            Happy(900);
            RenderToday();
        }

        void TaskToggle(string id)
        {
            List<DayTask> cur = Tasks.Load();
            DayTask x = cur.Find(delegate(DayTask y) { return y.Id == id; });
            if (x != null && x.Repeat.Length > 0 && !x.Done)
            {
                // повторяющаяся: отметили — переезжает на следующий раз (пропущенные прошлые — не в счёт, сегодняшний — в счёт)
                DateTime next = Tasks.NextDay(x.Repeat, x.Day.AddDays(1));
                if (next < DateTime.Today) next = Tasks.NextDay(x.Repeat, DateTime.Today);
                bool wasToday = x.Day <= DateTime.Today;
                x.Day = next;
                Tasks.Save(cur); Happy(1500); HabitAdd(1, 1);   // выполненные задачи — к бабочке
                var ru = new CultureInfo("ru-RU");
                Toast("", "Следующий раз — " + (next == DateTime.Today.AddDays(1) ? "завтра" : next == DateTime.Today ? "сегодня" : next.ToString("ddd, d MMMM", ru)), null, -1, accent);
                if (wasToday) PartyCheck(cur, true);   // сегодняшний раз сделан — может, это была последняя задача дня (надпись — после «Следующий раз»)
            }
            else if (x != null)
            {
                x.Done = !x.Done;
                if (x.Done && (x.Day < DateTime.Today || x.Day == Tasks.Someday)) x.Day = DateTime.Today;   // просроченную или «когда-нибудь» сделали сегодня — она и остаётся в сегодняшних, отмеченной
                Tasks.Save(cur); if (x.Done) Happy(1500); HabitAdd(1, x.Done ? 1 : -1);   // выполненные задачи — к бабочке
                if (x.Done && x.Day == DateTime.Today) PartyCheck(cur, false);   // последняя задача дня — праздник
            }
            RenderToday();
        }

        // ---- «живая активность»: скоро встреча из календаря или задача со временем — обратный отсчёт в капсуле
        // («Созвон · через 12 мин»). За 15 минут, а под музыку — за 5, чтобы не вытеснять трек раньше времени.
        class SoonItem { public string Title, Glyph; public DateTime At; }
        SoonItem soonNow;
        string soonKey = "";

        SoonItem SoonFind(int maxMin)
        {
            DateTime now = DateTime.Now;
            SoonItem best = null;
            if (Store.Get("calendar") != "0")
                foreach (CalEvent e in Calendar.Events())
                {
                    if (e.AllDay) continue;
                    double m = (e.Start - now).TotalMinutes;
                    if (m < -1 || m > maxMin) continue;
                    if (best == null || e.Start < best.At) best = new SoonItem { Title = e.Subject, At = e.Start, Glyph = ((char)0xE787).ToString() };
                }
            foreach (DayTask t in TasksCached())
            {
                if (t.Done || !t.Time.HasValue || t.Day != now.Date) continue;
                DateTime at = t.Day + t.Time.Value;
                double m = (at - now).TotalMinutes;
                if (m < -1 || m > maxMin) continue;
                if (best == null || at < best.At) best = new SoonItem { Title = t.Text, At = at, Glyph = ((char)0xE73E).ToString() };
            }
            return best;
        }

        static string SoonWhen(SoonItem s)
        {
            int min = (int)Math.Ceiling((s.At - DateTime.Now).TotalMinutes);
            return min <= 0 ? "сейчас" : "через " + min + " мин";
        }

        // справа в капсуле — коротко: «12 мин», «сейчас»
        static string SoonLeft(SoonItem s)
        {
            int min = (int)Math.Ceiling((s.At - DateTime.Now).TotalMinutes);
            return min <= 0 ? "сейчас" : min + " мин";
        }

        // Пример в настройках (и для проверок): «Созвон с командой · через 12 мин» на несколько секунд
        int soonDemoUntil;

        public void SoonDemo()
        {
            soonDemoUntil = Environment.TickCount + 4000; if (soonDemoUntil == 0) soonDemoUntil = 1;
            soonNow = new SoonItem { Title = "Созвон с командой", At = DateTime.Now.AddMinutes(12), Glyph = ((char)0xE787).ToString() };
            soonKey = "";
            if (state == 0) { RenderCompact(); SetTargets(); }
        }

        // Раз в две секунды: появилось, сменилось или прошло — перерисовать капсулу
        void SoonTick()
        {
            if (Until(soonDemoUntil)) return;   // идёт пример — не сбивать
            bool playing = media.Has && media.Playing;
            SoonItem s = Store.Get("soonPill") == "0" || StartingUp() ? null : SoonFind(playing ? 5 : 15);
            string key = s == null ? "" : s.Title + "|" + SoonWhen(s);
            soonNow = s;
            if (key == soonKey) return;
            soonKey = key;
            if (state == 0 && !toastOn) { RenderCompact(); SetTargets(); }
        }

        // Задачи, разобранные из настроек, — заново только когда они изменились (проверки идут каждые две секунды)
        List<DayTask> TasksCached()
        {
            string raw = Store.Get("tasks");
            if (!ReferenceEquals(raw, tasksSeenRaw) && raw != tasksSeenRaw) { tasksSeenRaw = raw; tasksSeen = Tasks.Load(); }
            return tasksSeen;
        }

        // ---- задача со временем: в свой час зверёк приносит её карточку с колокольчиком, как напоминание
        HashSet<string> tasksRung;
        string tasksSeenRaw;
        List<DayTask> tasksSeen = new List<DayTask>();

        void TaskRemindTick()
        {
            if (StartingUp() || state == 3) return;   // поверх другой карточки не показываем — подождём
            DateTime now = DateTime.Now;
            if (tasksRung == null) tasksRung = new HashSet<string>(Store.Get("tasksRung").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            foreach (DayTask t in TasksCached())   // разбираем заново, только если задачи изменились
            {
                if (t.Done || !t.Time.HasValue || t.Day != now.Date) continue;
                DateTime at = t.Day + t.Time.Value;
                string key = t.Id + "@" + at.ToString("yyyyMMddHHmm");
                if (now < at || now > at.AddMinutes(15) || tasksRung.Contains(key)) continue;   // опоздали больше чем на четверть часа — уже не новость
                tasksRung.Add(key);
                var keep = new List<string>();
                foreach (string k in tasksRung) if (k.Contains("@" + now.ToString("yyyyMMdd"))) keep.Add(k);   // помним только сегодняшние
                Store.Set("tasksRung", string.Join(",", keep.ToArray()));
                string id = t.Id;
                System.Media.SystemSounds.Exclamation.Play();
                cardCarryNext = "bell";   // как напоминание: зверёк приносит колокольчик и звонит в него
                string noteLine = t.Note.Length > 0 ? t.Note.Split('\n')[0].Trim() : "";
                if (noteLine.Length > 60) noteLine = noteLine.Substring(0, 59) + "…";
                ShowCardEx(null, ((char)0xE73E).ToString(), "Задача · " + new DateTime(t.Time.Value.Ticks).ToString("H:mm"), t.Text + (noteLine.Length > 0 ? " — " + noteLine : ""), delegate { SetState(2); ShowPage(PToday); }, "Готово", delegate { TaskToggle(id); }, true);
                cardPage = PToday;
                cardUntil = Environment.TickCount + 60000;   // ждёт ответа, а не исчезает через пару секунд
                return;
            }
        }
        static bool IsOverdue(DayTask t)
        {
            if (t.Done) return false;
            if (t.Day < DateTime.Today) return true;
            return t.Day == DateTime.Today && t.Time.HasValue && DateTime.Today + t.Time.Value < DateTime.Now;
        }

        // ---- утреннее приветствие: один раз за утро, при первом движении мыши или клавиатуры
        void MorningTick()
        {
            DateTime now = DateTime.Now;
            string today = now.ToString("yyyyMMdd");
            if (Store.Get("morningCard") == "0" || now.Hour < 5 || now.Hour >= 12 || Store.Get("greetDay") == today) return;
            if (StartingUp() || Native.IdleMs() > 3000 || state != 0 || hiddenFs || QuietNow) return;
            Store.Set("greetDay", today);
            List<DayTask> list = Tasks.Load().FindAll(delegate(DayTask t) { return !t.Done && t.Day <= DateTime.Today; });
            string next = "";
            DayTask first = null;
            foreach (DayTask t in list) if (t.Time.HasValue && (first == null || t.Time < first.Time)) first = t;
            if (first != null) next = "ближайшее: " + first.Text + " в " + new DateTime(first.Time.Value.Ticks).ToString("H:mm");
            else if (list.Count > 0) next = "первое: " + list[0].Text;
            else next = "задач на сегодня нет — добавьте во вкладке «Сегодня»";
            string head = (now.Hour < 12 ? "Доброе утро" : "Добрый день") + (list.Count > 0 ? " · " + Plural(list.Count, "задача", "задачи", "задач") : "");
            ShowCardEx(null, ((char)0xE706).ToString(), head, next, delegate { SetState(2); ShowPage(PToday); }, "Фокус", delegate { PomodoroStart(); }, true);
            CardPet("happy");
            cardPage = PToday;   // раскрыть карточку — сразу во вкладку «Сегодня»
            cardUntil = Environment.TickCount + 9000;
        }

        // ---- задачи из строки команд острова: «задача в пятницу в 10 отчёт» — сразу видно, на какой день она ляжет;
        // «задачи» (или «дела») — открытые задачи на сегодня: щелчок отмечает, а строка остаётся открытой
        static readonly string[] TaskWords = { "добавь задачу ", "задача ", "задачу ", "todo " };

        void FillTaskCmd(string q, string pq)
        {
            foreach (string k in TaskWords)
            {
                if (!pq.StartsWith(k) || q.Length <= k.Length + 1) continue;
                List<DayTask> fresh = Tasks.Parse(q.Substring(k.Length).Trim(), DateTime.Today);
                if (fresh.Count == 0) return;
                string sub = TaskWhen(fresh[0]) + (fresh.Count > 1 ? " · и ещё " + Plural(fresh.Count - 1, "задача", "задачи", "задач") : "") + " · Enter — добавить";
                string title = fresh[0].Text.Length > 50 ? fresh[0].Text.Substring(0, 49) + "…" : fresh[0].Text;
                AddCmd("Задача: " + title, sub, ((char)0xE73E).ToString(), null, delegate { AddParsedTasks(fresh); });
                return;
            }
        }

        static bool TaskListWord(string pq) { return pq == "задачи" || pq == "дела" || pq == "мои задачи" || pq.StartsWith("задачи на") || pq == "todo"; }

        void FillTaskList()
        {
            DateTime today = DateTime.Today;
            List<DayTask> list = Tasks.Load().FindAll(delegate(DayTask t) { return !t.Done && t.Day <= today || t.Done && t.Day == today; });
            list.Sort(delegate(DayTask a, DayTask b)
            {
                if (a.Done != b.Done) return a.Done ? 1 : -1;
                if (a.Day != b.Day) return a.Day.CompareTo(b.Day);   // просроченные — сверху
                TimeSpan ta = a.Time ?? TimeSpan.MaxValue, tb = b.Time ?? TimeSpan.MaxValue;
                return ta.CompareTo(tb);
            });
            if (list.Count == 0)
            {
                AddCmd("На сегодня задач нет", "добавьте: «задача в 15:00 позвонить маме»", ((char)0xE73E).ToString(), null, null);
                return;
            }
            bool hinted = false;
            foreach (DayTask t in list)
            {
                string id = t.Id;
                int before = cmdItems.Count;
                string sub = t.Done ? "сделано" : (IsOverdue(t) ? "просрочено" : t.Time.HasValue ? "в " + new DateTime(t.Time.Value.Ticks).ToString("H:mm") : "сегодня")
                    + (t.Repeat.Length > 0 ? " · " + Tasks.RepeatLabel(t.Repeat) : "") + (t.Note.Length > 0 ? StepsSub(t.Note) : "") + (!hinted ? (t.Done ? " · щелчок — вернуть" : " · щелчок — отметить") : "");
                hinted = true;
                AddCmd((t.Done ? "✓  " : "○  ") + t.Text, sub, ((char)0xE73E).ToString(), null, delegate { TaskToggle(id); });
                if (cmdItems.Count > before) cmdItems[cmdItems.Count - 1].Stay = true;   // отметили — список остаётся открытым
            }
        }

        static string StepsSub(string note)
        {
            int done, total = Tasks.StepCount(note, out done);
            return total > 0 ? " · шаги " + done + "/" + total : " · есть заметка";
        }

        // «сегодня в 14:00», «завтра», «пт, 20 ноября в 10:00», «каждый понедельник в 10:00»
        static string TaskWhen(DayTask t)
        {
            var ru = new CultureInfo("ru-RU");
            string time = t.Time.HasValue ? " в " + new DateTime(t.Time.Value.Ticks).ToString("H:mm") : "";
            if (t.Repeat.Length > 0) return Tasks.RepeatLabel(t.Repeat) + time;
            if (t.Day == Tasks.Someday) return "когда-нибудь";
            DateTime today = DateTime.Today;
            string day = t.Day == today ? "сегодня" : t.Day == today.AddDays(1) ? "завтра" : t.Day == today.AddDays(2) ? "послезавтра" : t.Day.ToString("ddd, d MMMM", ru) + (t.Day.Year != today.Year ? " " + t.Day.Year : "");
            return day + time;
        }

        void AddParsedTasks(List<DayTask> fresh)
        {
            if (fresh.Count == 0) return;
            List<DayTask> all = Tasks.Load();
            all.AddRange(fresh);
            Tasks.Save(all);
            Toast(((char)0xE73E).ToString(), fresh.Count == 1 ? "Задача — " + TaskWhen(fresh[0]) : "Добавлено задач: " + fresh.Count, null, -1, accent);
            RenderToday();
        }

        // ---- «25м отчёт», «фокус 50 разбор», «45 мин код» в строке команд
        void FillFocusCmd(string q, string pq)
        {
            Match m = Regex.Match(q.Trim(), @"^(?:фокус\s+)?(\d{1,3})\s*(?:м|мин|минут|min|m)\b\s*(.*)$", RegexOptions.IgnoreCase);
            int mins; string label;
            if (m.Success) { mins = int.Parse(m.Groups[1].Value); label = m.Groups[2].Value.Trim(); }
            else
            {
                Match f = Regex.Match(q.Trim(), @"^фокус\b\s*(.*)$", RegexOptions.IgnoreCase);
                if (!f.Success) return;
                mins = 25; label = f.Groups[1].Value.Trim();
            }
            if (mins < 1 || mins > 600) return;
            string l2 = label;
            AddCmd("Фокус " + mins + ":00" + (label.Length > 0 ? " · " + label : ""), "таймер в капсуле, по окончании — карточка", ((char)0xE916).ToString(), null, delegate { StartFocus(mins, l2); });
        }

        void StartFocus(int mins, string label)
        {
            timerRinging = false;
            timerTotal = TimeSpan.FromMinutes(mins);
            timerEnd = DateTime.Now + timerTotal;
            focusLabel = label;
            if (ringArc != null) UpdateRing();
            RenderCompact(); SetTargets();
            Toast(((char)0xE916).ToString(), "Фокус " + mins + " мин" + (label.Length > 0 ? ": " + label : ""), null, -1, accent);
        }
        string focusLabel = "";
    }
}
