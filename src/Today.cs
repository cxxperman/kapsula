// Kapsula — «остров» для Windows. Часть исходного кода: Today.cs
// Вкладка «Сегодня»: дата и неделя, ближайшее событие, погода и вторые часы, задачи со временем.
// Задачи пишутся обычной фразой: «отчёт, потом в 14:00 созвон» — две задачи. Плюс утреннее приветствие и «25м …» в строке.
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

namespace Kapsula
{
    class DayTask { public string Id = Guid.NewGuid().ToString("N").Substring(0, 8), Text = ""; public bool Done; public DateTime Day = DateTime.Today; public TimeSpan? Time; }

    static class Tasks
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static List<DayTask> Load()
        {
            var list = new List<DayTask>();
            foreach (string item in Store.Get("tasks").Split(new char[] { '¦' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = item.Split('~');
                if (p.Length < 5) continue;
                DateTime d;
                if (!DateTime.TryParseExact(p[2], "yyyyMMdd", Inv, DateTimeStyles.None, out d)) continue;
                var t = new DayTask { Id = p[0], Done = p[1] == "1", Day = d, Text = p[4] };
                TimeSpan ts;
                if (p[3].Length == 4 && TimeSpan.TryParseExact(p[3], "hhmm", Inv, out ts)) t.Time = ts;
                list.Add(t);
            }
            return list;
        }

        public static void Save(List<DayTask> list)
        {
            // храним задачи за последние две недели и все будущие
            var parts = new List<string>();
            foreach (DayTask t in list)
            {
                if (t.Day < DateTime.Today.AddDays(-14)) continue;
                parts.Add(t.Id + "~" + (t.Done ? "1" : "0") + "~" + t.Day.ToString("yyyyMMdd", Inv) + "~" + (t.Time.HasValue ? new DateTime(t.Time.Value.Ticks).ToString("HHmm", Inv) : "") + "~" + t.Text.Replace("~", "-").Replace("¦", "|"));
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
                res.Add(new DayTask { Text = text, Day = day, Time = time });
            }
            return res;
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
            return FinishBoard(tdb);
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
            Calendar.Refresh(false);
            var ru = new CultureInfo("ru-RU");
            DateTime now = DateTime.Now, today = DateTime.Today;
            List<DayTask> all = Tasks.Load();
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
            dayHint.Text = todayDay == today ? "Добавить: «отчёт, потом в 14:00 созвон»" : "Добавить на " + todayDay.ToString("d MMMM", ru) + ": «в 9 спорт»";

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
            if (list.Count == 0 && todayList.Children.Count == 0)
            {
                var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) };
                empty.Children.Add(new TextBlock { Text = ((char)0xE787).ToString(), FontFamily = Icons, FontSize = 22, Foreground = Faint, HorizontalAlignment = HorizontalAlignment.Center });
                empty.Children.Add(new TextBlock { Text = todayDay == today ? "Сегодня ничего не запланировано" : "Свободный день", Foreground = Dim, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
                todayList.Children.Add(empty);
                return;
            }
            foreach (DayTask tk in list)
            {
                DayTask task = tk;
                var row = new Grid { Height = 30 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var box = new Grid { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 0, 0, 0) };
                box.Children.Add(new Ellipse { Stroke = task.Done ? accent : Dim, StrokeThickness = 1.6, Fill = task.Done ? (Brush)accent : Brushes.Transparent });
                if (task.Done) box.Children.Add(new TextBlock { Text = ((char)0xE73E).ToString(), FontFamily = Icons, FontSize = 9, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(box);
                var txt = new TextBlock { Text = task.Text, Foreground = task.Done ? Faint : Fg, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextDecorations = task.Done ? TextDecorations.Strikethrough : null };
                Grid.SetColumn(txt, 1); row.Children.Add(txt);
                string right = IsOverdue(task) ? "Просрочено" : task.Time.HasValue ? new DateTime(task.Time.Value.Ticks).ToString("H:mm") : "";
                var r = new TextBlock { Text = right, Foreground = IsOverdue(task) ? ClaudeOrange : Dim, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) };
                Grid.SetColumn(r, 2); row.Children.Add(r);
                var b = new Border { CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = row, ToolTip = "Клик — отметить, правый клик — удалить" };
                b.MouseEnter += delegate { b.Background = ChipHover; };
                b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
                b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    List<DayTask> cur = Tasks.Load();
                    DayTask x = cur.Find(delegate(DayTask y) { return y.Id == task.Id; });
                    if (x != null) { x.Done = !x.Done; Tasks.Save(cur); if (x.Done) Happy(1500); HabitAdd(1, x.Done ? 1 : -1); }   // выполненные задачи — к бабочке
                    RenderToday();
                };
                b.MouseRightButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    List<DayTask> cur = Tasks.Load();
                    cur.RemoveAll(delegate(DayTask y) { return y.Id == task.Id; });
                    Tasks.Save(cur);
                    RenderToday();
                };
                todayList.Children.Add(b);
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
