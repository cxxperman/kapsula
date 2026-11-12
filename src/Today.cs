// Kapsula — «остров» для Windows. Часть исходного кода: Today.cs
// Вкладка «Сегодня»: дата и неделя, ближайшее событие, погода и вторые часы, задачи со временем.
// Задачи пишутся обычной фразой: «отчёт, потом в 14:00 созвон» — две задачи. Плюс утреннее приветствие и «25м …» в строке.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
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
        Grid todayHead;
        StackPanel todayWeek, todayList;
        TextBlock todayLeft;
        Path todayRing;
        DateTime todayDay = DateTime.Today;
        TextBox todayBox;

        FrameworkElement BuildTodayPage()
        {
            var dock = new DockPanel();
            // строка ввода внизу
            var input = new Grid { Height = 34, Margin = new Thickness(0, 6, 0, 0) };
            var hint = new TextBlock { Text = "Добавить: «отчёт, потом в 14:00 созвон», «завтра в 9 спорт»", Foreground = Faint, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(2, 0, 0, 1) };
            todayBox = MakeTextBox(false);
            todayBox.TextChanged += delegate { hint.Visibility = todayBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            todayBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; AddTasksFromText(todayBox.Text); todayBox.Text = ""; } };
            var field = new Grid(); field.Children.Add(hint); field.Children.Add(todayBox);
            input.Children.Add(new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17), Padding = new Thickness(14, 0, 14, 0), Child = field });
            DockPanel.SetDock(input, Dock.Bottom);
            dock.Children.Add(input);

            // шапка: дата и неделя · ближайшее событие · погода и вторые часы
            todayHead = new Grid { Height = 64, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(todayHead, Dock.Top);
            dock.Children.Add(todayHead);

            // лента недели и кольцо «осталось»
            var weekRow = new Grid { Height = 40, Margin = new Thickness(0, 0, 0, 4) };
            todayWeek = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            weekRow.Children.Add(todayWeek);
            var ringBox = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            todayLeft = new TextBlock { Foreground = Fg, FontSize = 12, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var ringGrid = new Grid { Width = 26, Height = 26 };
            ringGrid.Children.Add(new Ellipse { Stroke = Bar.TrackBrush, StrokeThickness = 3 });
            todayRing = new Path { Stroke = accent, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            ringGrid.Children.Add(todayRing);
            ringBox.Children.Add(todayLeft); ringBox.Children.Add(ringGrid);
            weekRow.Children.Add(ringBox);
            DockPanel.SetDock(weekRow, Dock.Top);
            dock.Children.Add(weekRow);

            todayList = new StackPanel();
            dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = todayList });
            return dock;
        }

        void AddTasksFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            List<DayTask> all = Tasks.Load();
            List<DayTask> fresh = Tasks.Parse(text, DateTime.Today);
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

        void RenderToday()
        {
            if (todayHead == null) return;
            var ru = new CultureInfo("ru-RU");
            DateTime now = DateTime.Now;
            todayHead.Children.Clear();
            todayHead.ColumnDefinitions.Clear();
            todayHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            todayHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            todayHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            todayHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            todayHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

            // 1) дата
            int week = ru.Calendar.GetWeekOfYear(now, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            var date = new Grid { Margin = new Thickness(12, 8, 10, 8) };
            var dn = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            dn.Children.Add(new TextBlock { Text = now.Day.ToString(), Foreground = Fg, FontSize = 30, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var dw = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
            dw.Children.Add(new TextBlock { Text = ru.DateTimeFormat.GetAbbreviatedDayName(now.DayOfWeek).ToUpper(), Foreground = accent, FontSize = 10.5, FontWeight = FontWeights.Bold });
            dw.Children.Add(new TextBlock { Text = ru.DateTimeFormat.GetAbbreviatedMonthName(now.Month).TrimEnd('.'), Foreground = Dim, FontSize = 11 });
            dn.Children.Add(dw);
            date.Children.Add(dn);
            date.Children.Add(new TextBlock { Text = "неделя " + week, Foreground = Faint, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
            todayHead.Children.Add(new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Child = date });

            // 2) ближайшее событие календаря или задача со временем
            string evTitle = null; DateTime evAt = DateTime.MinValue;
            try
            {
                foreach (CalEvent e in Calendar.Events())
                    if (!e.AllDay && e.Start > now && (evTitle == null || e.Start < evAt)) { evTitle = e.Subject; evAt = e.Start; }
            }
            catch { }
            foreach (DayTask t in Tasks.Load())
                if (!t.Done && t.Day == DateTime.Today && t.Time.HasValue && DateTime.Today + t.Time.Value > now && (evTitle == null || DateTime.Today + t.Time.Value < evAt)) { evTitle = t.Text; evAt = DateTime.Today + t.Time.Value; }
            var ev = new StackPanel { Margin = new Thickness(12, 8, 12, 8), VerticalAlignment = VerticalAlignment.Center };
            if (evTitle != null)
            {
                TimeSpan left = evAt - now;
                string inTxt = left.TotalMinutes < 60 ? "через " + Math.Max(1, (int)left.TotalMinutes) + " мин" : left.TotalHours < 24 ? "в " + evAt.ToString("H:mm") : evAt.ToString("d MMM, H:mm", ru);
                var row = new Grid();
                row.Children.Add(new TextBlock { Text = evTitle, Foreground = Fg, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 70, 0) });
                row.Children.Add(new TextBlock { Text = inTxt, Foreground = accent, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right });
                ev.Children.Add(new TextBlock { Text = "БЛИЖАЙШЕЕ", Foreground = Faint, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                ev.Children.Add(row);
                // полоса: сколько осталось из последних двух часов
                double f = Math.Max(0, Math.Min(1, 1 - left.TotalMinutes / 120.0));
                var track = new Grid { Height = 4, Margin = new Thickness(0, 6, 0, 0) };
                track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Bar.TrackBrush });
                var fill = new Border { CornerRadius = new CornerRadius(2), Background = accent, HorizontalAlignment = HorizontalAlignment.Left };
                track.SizeChanged += delegate { fill.Width = Math.Max(3, track.ActualWidth * f); };
                track.Children.Add(fill);
                ev.Children.Add(track);
            }
            else
            {
                ev.Children.Add(new TextBlock { Text = "БЛИЖАЙШЕЕ", Foreground = Faint, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                ev.Children.Add(new TextBlock { Text = "Больше ничего на сегодня", Foreground = Dim, FontSize = 12.5 });
            }
            var evCard = new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Child = ev };
            Grid.SetColumn(evCard, 2); todayHead.Children.Add(evCard);

            // 3) погода и вторые часы
            DateTime other;
            string city = WorldClockLabel(out other);
            var wx = new Grid { Margin = new Thickness(12, 8, 12, 8) };
            var top = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            if (weather != null && weather.Error == null)
            {
                top.Children.Add(WeatherArt.Icon(weather.Code, weather.IsDay, 20));
                top.Children.Add(new TextBlock { Text = " " + Weather.Temp(weather.Temp), Foreground = Fg, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            }
            else top.Children.Add(new TextBlock { Text = "Погода не выбрана", Foreground = Dim, FontSize = 11.5 });
            wx.Children.Add(top);
            var clock = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Bottom, ToolTip = "Второй часовой пояс меняется в настройках" };
            clock.Inlines.Add(new System.Windows.Documents.Run(city + " ") { Foreground = Dim });
            clock.Inlines.Add(new System.Windows.Documents.Run(other.ToString("H:mm")) { Foreground = Fg, FontWeight = FontWeights.SemiBold });
            wx.Children.Add(clock);
            var wxCard = new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Child = wx };
            Grid.SetColumn(wxCard, 4); todayHead.Children.Add(wxCard);

            // лента недели: понедельник…воскресенье, точка под днём с задачами
            List<DayTask> all = Tasks.Load();
            todayWeek.Children.Clear();
            DateTime mon = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
            for (int i = 0; i < 7; i++)
            {
                DateTime d = mon.AddDays(i);
                bool sel = d == todayDay, isToday = d == DateTime.Today;
                bool has = all.Exists(delegate(DayTask t) { return t.Day == d && !t.Done; });
                var sp = new StackPanel { Width = 34 };
                sp.Children.Add(new TextBlock { Text = ru.DateTimeFormat.GetShortestDayName(d.DayOfWeek), Foreground = Faint, FontSize = 9.5, HorizontalAlignment = HorizontalAlignment.Center });
                var num = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = sel ? (Brush)Fg : Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock { Text = d.Day.ToString(), Foreground = sel ? (Brush)Ink : isToday ? (Brush)accent : Fg, FontSize = 11.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                sp.Children.Add(num);
                sp.Children.Add(new Ellipse { Width = 3, Height = 3, Fill = has ? (Brush)accent : Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0) });
                var hit = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = sp };
                DateTime day = d;
                hit.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; todayDay = day; RenderToday(); };
                todayWeek.Children.Add(hit);
            }

            // задачи выбранного дня
            List<DayTask> list = all.FindAll(delegate(DayTask t) { return t.Day == todayDay; });
            list.Sort(delegate(DayTask a, DayTask b)
            {
                if (a.Done != b.Done) return a.Done ? 1 : -1;
                TimeSpan ta = a.Time ?? TimeSpan.MaxValue, tb = b.Time ?? TimeSpan.MaxValue;
                return ta.CompareTo(tb);
            });
            // просроченные со вчера и раньше — тоже сегодня, сверху
            if (todayDay == DateTime.Today) list.InsertRange(0, all.FindAll(delegate(DayTask t) { return t.Day < DateTime.Today && !t.Done; }));
            int left2 = 0, overdue = 0;
            foreach (DayTask t in list) if (!t.Done) { left2++; if (IsOverdue(t)) overdue++; }
            todayLeft.Inlines.Clear();
            todayLeft.Inlines.Add(new System.Windows.Documents.Run(left2 == 0 ? "всё сделано" : "осталось " + left2) { Foreground = Fg });
            if (overdue > 0) todayLeft.Inlines.Add(new System.Windows.Documents.Run("\n" + overdue + " просрочено") { Foreground = ClaudeOrange, FontSize = 10.5, FontWeight = FontWeights.Normal });
            int total = list.Count;
            double frac = total == 0 ? 0 : (total - left2) / (double)total;
            todayRing.Data = frac <= 0.001 ? null : ArcGeometry(13, 13, 11.5, 0, Math.Min(359.9, frac * 360));

            todayList.Children.Clear();
            if (list.Count == 0)
            {
                todayList.Children.Add(new TextBlock { Text = todayDay == DateTime.Today ? "На сегодня задач нет — напишите их строкой ниже" : "На этот день задач нет", Foreground = Dim, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) });
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
                    if (x != null) { x.Done = !x.Done; Tasks.Save(cur); if (x.Done) Happy(1500); }
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
