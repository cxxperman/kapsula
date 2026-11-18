// Kapsula — «остров» для Windows. Часть исходного кода: Extras.cs
// Вкладка уведомлений, режим звонка, яркость монитора,
// умные карточки буфера и свои сайты во вкладке чата.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Kapsula
{
    partial class IslandWindow
    {
        void InitExtras()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                StartOverlays();
                if (Store.Get("phoneRemote") == "1") Phone.Start(this);
                if (PhoneCam.Installed) PhoneCam.Start(this);
            }), DispatcherPriority.ApplicationIdle);
        }

        // Вход в «телефон как веб-камера» отовсюду: если камера стоит — сразу QR-код, иначе раздел настроек с кнопкой установки
        string openSection;
        public void OpenPhoneCam()
        {
            if (PhoneCam.Installed && PhoneCam.UpToDate) { PhoneCam.Start(this); PhoneCam.ShowQr(); return; }
            openSection = "Телефон";
            OpenSettings();
        }

        Border camBtn;

        // ---- QR-код прямо в острове: панель поверх вкладок. Остров не сворачивается, пока она открыта,
        // а когда телефон подключился, панель закрывается сама.
        Border qrPanel;
        Image qrImage;
        TextBlock qrTitle;
        StackPanel qrLines;
        bool qrWasPinned;

        public void ShowQrInIsland(string url, string title, string[] lines)
        {
            if (qrPanel == null)
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                qrImage = new Image { Width = 226, Height = 226 };
                RenderOptions.SetBitmapScalingMode(qrImage, BitmapScalingMode.NearestNeighbor);
                g.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(20), Padding = new Thickness(10), Child = qrImage, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
                var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                qrTitle = new TextBlock { Foreground = Fg, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                right.Children.Add(qrTitle);
                qrLines = new StackPanel();
                right.Children.Add(qrLines);
                var close = Btn("Закрыть", false, 0, 30, delegate { HideQrInIsland(); });
                close.HorizontalAlignment = HorizontalAlignment.Left; close.Margin = new Thickness(0, 14, 0, 0);
                right.Children.Add(close);
                Grid.SetColumn(right, 1);
                g.Children.Add(right);
                qrPanel = new Border { Background = Ink, Padding = new Thickness(26, 22, 22, 22), Child = g, Visibility = Visibility.Collapsed };
                expandedHost.Children.Add(qrPanel);
            }
            qrImage.Source = QrMake.Render(url, 8);
            qrTitle.Text = title;
            qrLines.Children.Clear();
            foreach (string l in lines)
                qrLines.Children.Add(new TextBlock { Text = l, Foreground = Dim, FontSize = 12, LineHeight = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            if (qrPanel.Visibility != Visibility.Visible) qrWasPinned = pinned;
            qrPanel.Visibility = Visibility.Visible;
            qrPanel.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            SetPinned(true);
            if (state != 2) SetState(2);
        }

        public void HideQrInIsland()
        {
            if (qrPanel == null || qrPanel.Visibility != Visibility.Visible) return;
            qrPanel.Visibility = Visibility.Collapsed;
            SetPinned(qrWasPinned);
        }

        // телефон начал или перестал работать камерой
        public void PhoneCamChanged(bool on)
        {
            if (on) HideQrInIsland();
            if (camBtn != null)
            {
                var gl = camBtn.Child as TextBlock;
                if (gl != null) gl.Foreground = on ? (Brush)ClaudeGreen : Fg;
                camBtn.ToolTip = on ? "Телефон работает камерой · нажмите, чтобы показать QR-код" : "Телефон как веб-камера";
            }
            Toast(((char)0xE714).ToString(), on ? "Телефон работает камерой · Kapsula Camera" : "Камера телефона отключена", null, -1, on ? (Brush)ClaudeGreen : null);
        }

        // каждые полсекунды (частоту опроса мыши здесь не трогаем — её меняет только CalmCheck в Rider.cs)
        void ExtrasTick()
        {
            CallTick();
            OverlaysTick();
        }

        // ================================================================ Уведомления
        StackPanel notifList;

        FrameworkElement BuildNotifPage()
        {
            var foot = new Grid { Height = 26, Margin = new Thickness(0, 6, 0, 0) };
            notifCount = new TextBlock { Foreground = Faint, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            foot.Children.Add(notifCount);
            var clear = notifClearBtn = Btn("Очистить всё", false, 0, 26, delegate { seenNotes.Clear(); RenderNotifPage(); });   // ряд конструктора — 26 точек: кнопка выше срезалась бы снизу
            clear.HorizontalAlignment = HorizontalAlignment.Right;
            foot.Children.Add(clear);
            notifList = new StackPanel();
            var nsv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = notifList };
            // уведомлений больше, чем помещается, — край списка мягко тает, а не режет карточку посреди букв
            nsv.ScrollChanged += delegate { FadeScrollEdges(nsv, true, 16); };
            var ntb = PageBoard(PNotif, "notif", 1, 9, 8);
            ntb.Add("list", "Список", nsv, 0, 0, 1, 8, false);
            ntb.Add("foot", "Счётчик и «Очистить всё»", foot, 0, 8, 1, 1, false);
            return FinishBoard(ntb);
        }
        TextBlock notifCount;
        Border notifClearBtn;   // «Очистить всё» — только когда есть что стирать

        void RenderNotifPage()
        {
            if (notifList == null) return;
            if (notifClearBtn != null) notifClearBtn.Visibility = seenNotes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;   // пустой истории стирать нечего
            notifList.Children.Clear();
            notifCount.Text = seenNotes.Count == 0 ? "История хранится до закрытия kapsula.txt" : Plural(seenNotes.Count, "уведомление", "уведомления", "уведомлений") + " · история хранится до закрытия kapsula.txt";
            if (seenNotes.Count == 0)
            {
                notifList.Children.Add(PetSpot("sleep", 50, 12, "notif"));
                notifList.Children.Add(new TextBlock { Text = "Уведомлений пока не было", Foreground = Dim, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
                return;
            }
            for (int i = seenNotes.Count - 1; i >= 0; i--)
            {
                Seen s = seenNotes[i];
                // строка растёт под текст: имя — до двух строк, сообщение — до трёх; целиком — в подсказке
                var row = new Grid { MinHeight = 46 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                ImageSource ic = null;
                try { ic = SourceIcon((s.Source ?? "").ToLowerInvariant()); } catch { }   // в истории — «Discord», «Steam», а значки ищутся по «discord», «steam»
                FrameworkElement icon = ic != null ? (FrameworkElement)new Image { Source = ic, Width = 26, Height = 26 } :
                    new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = Glass.Fill, Child = new TextBlock { Text = ((char)0xEA8F).ToString(), FontFamily = Icons, FontSize = 13, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new Thickness(0, 9, 0, 0);
                row.Children.Add(icon);
                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 7, 0, 8) };
                // без заголовка — хотя бы откуда пришло, а не пустая строка; текст из одних пробелов — без пустой второй строки
                bool hasTitle = s.Title.Trim().Length > 0, hasText = s.Text.Trim().Length > 0;
                string head = hasTitle ? s.Title : string.IsNullOrEmpty(s.Source) ? "Уведомление" : s.Source;
                var title = new TextBlock { Text = head, Foreground = Fg, FontSize = 12.5, FontWeight = FontWeights.SemiBold };
                Ui.Lines(title, 2);
                texts.Children.Add(title);
                if (hasText)
                {
                    // сообщения из Discord и Telegram бывают с переводами строк — здесь они сливаются в одну строку текста
                    var body = new TextBlock { Text = Regex.Replace(s.Text, @"\s+", " ").Trim(), Foreground = Dim, FontSize = 11.5 };
                    Ui.Lines(body, 3);
                    texts.Children.Add(body);
                }
                Grid.SetColumn(texts, 1); row.Children.Add(texts);
                var tm = new TextBlock { Text = NotifAgo(s.At), Foreground = Faint, FontSize = 11, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 8, 10, 0) };
                Grid.SetColumn(tm, 2); row.Children.Add(tm);
                string full = s.Text.Length > 600 ? s.Text.Substring(0, char.IsHighSurrogate(s.Text[599]) ? 599 : 600) + "…" : s.Text;   // в подсказке — до 600 знаков, остальное даст «скопировать»
                string clip = hasTitle && hasText ? s.Title + " — " + s.Text : hasText ? s.Text : head;   // без висящего « — », когда чего-то нет
                var b = new Border { CornerRadius = Ui.Row, Background = Glass.Fill, BorderBrush = Glass.Rim, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 5), Child = row, Cursor = Cursors.Hand,
                    ToolTip = head + (hasText ? "\n" + full : "") + "\n\nКлик — скопировать текст" };
                b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e) { e.Handled = true; CopyResult(clip); };
                notifList.Children.Add(b);
            }
        }

        // Когда пришло уведомление: «сейчас», «5 мин назад», сегодня — «14:05», «вчера, 14:05», раньше — «12 ноября, 14:05».
        // История живёт до закрытия Kapsula, а это бывает и сутки: одно «14:05» выдавало бы вчерашнее за сегодняшнее.
        // Месяц — словом, как в календаре и задачах: короткие названия .NET без точки и не в том падеже («12 май»)
        static string NotifAgo(DateTime at)
        {
            var ru = new System.Globalization.CultureInfo("ru-RU");
            TimeSpan ago = DateTime.Now - at;
            if (ago.TotalMinutes < 1) return "сейчас";
            if (ago.TotalMinutes < 60) return (int)ago.TotalMinutes + " мин назад";
            if (at.Date == DateTime.Today) return at.ToString("H:mm", ru);
            if (at.Date == DateTime.Today.AddDays(-1)) return "вчера, " + at.ToString("H:mm", ru);
            return at.ToString("d MMMM, H:mm", ru);
        }

        // ================================================================ Звонок
        // Звонок — это когда программа для звонков одновременно держит микрофон и сама выводит звук (голосовой поток).
        // Одного микрофона мало: Discord, например, держит его открытым и вне звонка. Тогда на «Главной» большая кнопка микрофона.
        static readonly string[] CallApps = { "discord", "zoom", "teams", "ms-teams", "telegram", "skype", "slack", "whatsapp", "viber", "webex", "chrome", "msedge", "firefox", "opera", "yandex" };
        string callApp;
        DateTime callStart;
        Border homeCallCard;
        TextBlock callTimeText, callAppText;

        bool CallVisible() { return callApp != null && (DiscordLive || Store.Get("callMode") == "1" || callTest); }   // плагин в Discord — звонок точно, без «альфы»
        bool callTest = Environment.GetEnvironmentVariable("KAPSULA_CALLTEST") == "1";   // проверка: «звонок» в Discord с голосом через раз
        bool callMuted;   // микрофон выключен — запоминается раз в полсекунды, зверёк в капсуле показывает это
        string CallTime() { return CallClock(DateTime.Now - callStart); }

        // Время звонка, как на iPhone: «0:07», «12:34», а после часа — «1:05:23»
        static string CallClock(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t.TotalHours >= 1) return (int)t.TotalHours + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
            return t.Minutes + ":" + t.Seconds.ToString("00");
        }

        // skipDiscord — плагин Discord на связи: о звонке Discord он скажет сам, а открытый им микрофон — ещё не звонок
        static string CallAppNow(bool skipDiscord)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone\NonPackaged"))
                {
                    if (k != null)
                        foreach (string n in k.GetSubKeyNames())
                            using (var s = k.OpenSubKey(n))
                            {
                                object v = s == null ? null : s.GetValue("LastUsedTimeStop");
                                if (!(v is long) || (long)v != 0) continue;
                                string exe = n.Substring(n.LastIndexOf('#') + 1).ToLowerInvariant().Replace(".exe", "");
                                foreach (string a in CallApps) if (!(skipDiscord && a == "discord") && (exe == a || exe.StartsWith(a)) && AppVolume.SessionActive(exe)) return exe;
                            }
                }
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone"))
                {
                    if (k != null)
                        foreach (string n in k.GetSubKeyNames())
                        {
                            if (n == "NonPackaged") continue;
                            using (var s = k.OpenSubKey(n))
                            {
                                object v = s == null ? null : s.GetValue("LastUsedTimeStop");
                                if (!(v is long) || (long)v != 0) continue;
                                string low = n.ToLowerInvariant();
                                foreach (string a in CallApps) if (!(skipDiscord && a == "discord") && low.Contains(a) && AppVolume.SessionActive(a)) return a;
                            }
                        }
                }
            }
            catch { }
            return null;
        }

        // Название программы для карточки звонка: «WhatsApp», а не «Whatsapp»; «Яндекс» по-русски, как в списке плееров
        // (MediaWatcher.DisplayName). Звук ищется не по нему, а по имени процесса (callCandidate)
        static string Pretty(string exe)
        {
            switch (exe)
            {
                case "msedge": return "Edge"; case "chrome": return "Chrome"; case "ms-teams": case "teams": return "Teams";
                case "whatsapp": return "WhatsApp"; case "yandex": return "Яндекс";
                default: return exe.Length > 0 ? char.ToUpper(exe[0]) + exe.Substring(1) : exe;
            }
        }

        int callTickN;
        // Звонок = программа для звонков держит микрофон и звук, и в разговоре реально говорят (вы или собеседник).
        // Просто сидеть в голосовом канале Discord — не звонок: карточка появится, когда кто-то заговорит,
        // и уйдёт после 45 секунд тишины.
        string callCandidate;   // имя процесса («yandex», «msedge»): по нему ищется звук; в карточке — Pretty(callCandidate)
        int callVoiceAt;
        void CallTick()
        {
            callTickN++;
            RingTick();   // входящий звонок: показать после другой карточки, убрать, если Discord закрыли
            if (DiscordLive && (dvInCall || callApp == "Discord")) { DiscordCallTick(); return; }   // звонок Discord — по словам плагина; вне голоса Discord догадка по микрофону ищет Zoom, Teams, Telegram и браузер
            int now = Environment.TickCount;
            if (callTest) callCandidate = DiscordLive ? null : "Discord";   // проверочный «звонок» — только без плагина, иначе плагин тут же снимал бы его
            else if (Store.Get("callMode") != "1") callCandidate = null;   // альфа: выключено, пока не включат в настройках
            else if (callTickN % 4 == 0) callCandidate = CallAppNow(DiscordLive);
            if (callCandidate != null)
            {
                double pk = callTest ? (callTickN % 6 < 3 ? 0.3 : 0) : Math.Max(AppVolume.SessionPeak(callCandidate, 0), AppVolume.SessionPeak(callCandidate, 1));
                if (pk > 0.08) callVoiceAt = now;   // тишина в канале ~0.01, речь — от 0.1
            }
            bool talking = callCandidate != null && callVoiceAt != 0 && unchecked(now - callVoiceAt) < 45000;
            string app = talking ? Pretty(callCandidate) : null;
            if (app != callApp)
            {
                bool started = callApp == null && app != null;
                callApp = app;
                if (started) callStart = DateTime.Now;
                EnsureCallCard();
                ApplyHomeLayout();
                RenderCompact(); SetTargets();
            }
            if (callApp != null)
            {
                callMuted = Audio.MicMuted();
                if (callTimeText != null) callTimeText.Text = CallTime();
                PaintMute();
                if (state == 0) RenderCompact();
            }
        }
        // Карточка звонка на «Главной» (EnsureCallCard, PaintMute) — в CallCard.cs

        // ================================================================ Умный буфер
        StackPanel clipFilterRow;
        int clipFilter;   // 0 всё, 1 текст, 2 ссылки, 3 цвета, 4 код, 5 картинки, 6 закреплённые

        static int ClipKind(ClipItem it)
        {
            if (it.Image != null) return 5;
            string t = (it.Text ?? "").Trim();
            if (Regex.IsMatch(t, @"^(https?://|www\.)\S+$", RegexOptions.IgnoreCase)) return 2;
            if (Regex.IsMatch(t, @"^(#[0-9a-f]{3}([0-9a-f]{3})?([0-9a-f]{2})?|rgba?\(\s*\d+\s*,\s*\d+\s*,\s*\d+.*\))$", RegexOptions.IgnoreCase)) return 3;
            int codeHints = 0;
            foreach (string h in new string[] { ";\n", "{", "}", "=>", "()", "def ", "function", "const ", "let ", "var ", "return ", "import ", "</", "public " }) if (t.Contains(h)) codeHints++;
            if (codeHints >= 2) return 4;
            return 1;
        }

        bool ClipPasses(ClipItem it)
        {
            if (clipFilter == 0) return true;
            if (clipFilter == 6) return it.Pinned;
            int k = ClipKind(it);
            return k == clipFilter;
        }

        void RenderClipFilters()
        {
            if (clipFilterRow == null) return;
            clipFilterRow.Children.Clear();
            string[] names = { "Всё", "Текст", "Ссылки", "Цвета", "Код", "Картинки", "Закреплённые" };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                bool on = clipFilter == i;
                var c = new Border { Height = 24, CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 0, 11, 1), Background = on ? (Brush)Glass.Strong : Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 2, 0),
                    Child = new TextBlock { Text = names[i], FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = on ? Fg : Dim, VerticalAlignment = VerticalAlignment.Center } };
                Ui.Seg(c, on);
                c.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; clipFilter = idx; RenderClips(); };
                clipFilterRow.Children.Add(c);
            }
        }

        // Заголовок записи: метка типа (ссылка, цвет с образцом, код) и сам текст — до двух строк, дальше «…».
        // Сетка, а не горизонтальная StackPanel: в StackPanel у текста бесконечная ширина, и он обрезался посреди буквы без «…».
        // text — сама надпись: по ней строка решает, нужна ли подсказка с полным текстом
        FrameworkElement ClipTitle(ClipItem it, string one, out TextBlock text)
        {
            int k = ClipKind(it);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (k == 3)
            {
                Color c;
                if (TryParseCssColor(it.Text.Trim(), out c))
                    g.Children.Add(new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(c), BorderBrush = Line, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 7, 0) });
            }
            else if (k == 2 || k == 4)
            {
                // метка — у первой строки текста
                var badge = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(0, 2, 7, 0), VerticalAlignment = VerticalAlignment.Top,
                    Background = new SolidColorBrush(k == 2 ? Color.FromArgb(0x40, 0x4D, 0xA3, 0xFF) : Color.FromArgb(0x40, 0xAF, 0x52, 0xDE)),
                    Child = new TextBlock { Text = k == 2 ? "Ссылка" : "Код", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(k == 2 ? Color.FromRgb(0x7F, 0xBE, 0xFF) : Color.FromRgb(0xD0, 0x9C, 0xFF)) } };
                g.Children.Add(badge);
            }
            var tb = new TextBlock { Text = one, Foreground = Fg, FontSize = 12.5, FontFamily = k == 4 ? new FontFamily("Cascadia Mono, Consolas") : FontFamily };
            Ui.Lines(tb, 2);
            Grid.SetColumn(tb, 1); g.Children.Add(tb);
            text = tb;
            return g;
        }

        static bool TryParseCssColor(string s, out Color c)
        {
            c = Colors.Transparent;
            try
            {
                if (s.StartsWith("#"))
                {
                    string h = s.Substring(1);
                    if (h.Length == 3) h = "" + h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
                    if (h.Length >= 6) { c = Color.FromRgb(Convert.ToByte(h.Substring(0, 2), 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16)); return true; }
                }
                Match m = Regex.Match(s, @"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)", RegexOptions.IgnoreCase);
                if (m.Success) { c = Color.FromRgb((byte)Math.Min(255, int.Parse(m.Groups[1].Value)), (byte)Math.Min(255, int.Parse(m.Groups[2].Value)), (byte)Math.Min(255, int.Parse(m.Groups[3].Value))); return true; }
            }
            catch { }
            return false;
        }

        // ================================================================ Свои сайты во вкладке чата
        void AddWebSite()
        {
            string[] r = PromptFields("Свой сайт во вкладке чата", new string[] { "Название (например, Perplexity)", "Адрес (https://…)" });
            if (r == null) return;
            string name = r[0].Trim().Replace("~", "").Replace("¦", ""), url = r[1].Trim().Replace("~", "").Replace("¦", "");
            if (url.Length == 0) return;
            if (!url.StartsWith("http://") && !url.StartsWith("https://")) url = "https://" + url;
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) { Toast(((char)0xE774).ToString(), "Не похоже на адрес сайта", null, -1, null); return; }
            if (name.Length == 0) name = u.Host.StartsWith("www.") ? u.Host.Substring(4) : u.Host;
            // имя хранится целиком (без «…»): его показывают и в предложениях («Открываю …»); короче его делает
            // только сама плашка над чатом. 60 знаков — просто разумный предел
            if (name.Length > 60) name = name.Substring(0, char.IsHighSurrogate(name[59]) ? 59 : 60).TrimEnd();
            string cur = Store.Get("webSites");
            Store.Set("webSites", (cur.Length > 0 ? cur + "¦" : "") + name + "~" + url);
            Store.Set("webChat", (WebUrls.Length - 1).ToString());
            if (duck != null) duck.Go(url);
            RenderWebRow();
        }

        void RemoveWebSite(int i)
        {
            var parts = new List<string>(Store.Get("webSites").Split(new char[] { '¦' }, StringSplitOptions.RemoveEmptyEntries));
            if (i < 0 || i >= parts.Count) return;
            // выбранный сайт читаем до того, как список стал короче: потом номер последнего уже вне списка,
            // WebIndex() принял бы его за Duck.ai — и браузер остался бы на убранном сайте
            int sel; if (!int.TryParse(Store.Get("webChat"), out sel)) sel = 0;
            parts.RemoveAt(i);
            Store.Set("webSites", string.Join("¦", parts.ToArray()));
            if (sel == i + 3) { Store.Set("webChat", "0"); if (duck != null) duck.Go(WebUrls[0]); }
            else if (sel > i + 3) Store.Set("webChat", (sel - 1).ToString());
            RenderWebRow();
        }

        // Маленькое тёмное окно с полями ввода; null — отмена
        string[] PromptFields(string title, string[] hints)
        {
            bool wasPinned = pinned;
            SetPinned(true);
            var win = new Window { Title = "kapsula.txt", Width = 380, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, ShowInTaskbar = false,
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x19)) };
            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
            var boxes = new TextBox[hints.Length];
            for (int i = 0; i < hints.Length; i++)
            {
                sp.Children.Add(new TextBlock { Text = hints[i], Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x93)), FontSize = 12, Margin = new Thickness(0, 4, 0, 4) });
                boxes[i] = new TextBox { FontSize = 13.5, Padding = new Thickness(8, 6, 8, 6), Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x27)), Foreground = Brushes.White, CaretBrush = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x3A)) };
                sp.Children.Add(boxes[i]);
            }
            bool ok = false;
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = Btn("Отмена", false, 0, 30, delegate { win.Close(); });
            cancel.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(cancel);
            row.Children.Add(Btn("Добавить", false, 0, 30, delegate { ok = true; win.Close(); }));
            sp.Children.Add(row);
            win.Content = sp;
            win.KeyDown += delegate(object o, KeyEventArgs e) { if (e.Key == Key.Enter) { ok = true; win.Close(); } else if (e.Key == Key.Escape) win.Close(); };
            win.Loaded += delegate { boxes[0].Focus(); };
            win.ShowDialog();
            SetPinned(wasPinned);
            if (!ok) return null;
            var res = new string[hints.Length];
            for (int i = 0; i < hints.Length; i++) res[i] = boxes[i].Text;
            return res;
        }
        // ================================================================ «В избранное» и «Не интересует» (Cider)
        readonly List<Border> favBtns = new List<Border>();
        readonly List<FrameworkElement[]> favStars = new List<FrameworkElement[]>();   // [контурная, залитая]
        static readonly Brush StarYellow = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x33));
        readonly List<Border> banBtns = new List<Border>();
        bool? favNow;
        string favKey;
        string rateMode = "";          // "cider" — через API Cider, "uia" — кнопками самого плеера, "" — нет
        bool uiaHasBan;
        int ratePlayerSeq;

        void ShowRate(bool fav, bool ban)
        {
            foreach (Border b in favBtns) b.Visibility = fav ? Visibility.Visible : Visibility.Collapsed;
            foreach (Border b in banBtns) b.Visibility = ban ? Visibility.Visible : Visibility.Collapsed;
        }

        Border FavButton(double size)
        {
            var outline = MediaShape("star", size * 0.5, Fg);
            var filled = MediaShape("starfill", size * 0.5, StarYellow);
            filled.Visibility = Visibility.Collapsed;
            var g = new Grid(); g.Children.Add(outline); g.Children.Add(filled);
            var b = RateBtn(size, g, "В избранное", delegate { ToggleFavorite(); });
            favStars.Add(new FrameworkElement[] { outline, filled });
            favBtns.Add(b);
            return b;
        }

        Border BanButton(double size)
        {
            var b = RateBtn(size, MediaShape("ban", size * 0.46, Dim), "Не интересует — предлагать меньше такого и дальше", delegate { NotInterested(); });
            banBtns.Add(b);
            return b;
        }

        Border RateBtn(double size, FrameworkElement child, string tip, Action click)
        {
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Brushes.Transparent, Child = child, Cursor = Cursors.Hand, ToolTip = tip, Visibility = Visibility.Collapsed, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            Fx.Press(b, 0.86);
            HoverFollows(b);
            return b;
        }

        // Кнопки видны, только когда играющий плеер умеет оценки: Cider — через его API, остальные (браузеры, Spotify,
        // Яндекс Музыка) — если в их окне нашлись кнопки «Нравится» / «Не нравится» (альфа, включается в настройках)
        void UpdateRateButtons()
        {
            bool test = Environment.GetEnvironmentVariable("KAPSULA_RATETEST") == "1";
            string mode = !media.Has ? "" : (Cider.IsCider(media.Source) || test) ? (Store.Get("ciderRate") != "0" ? "cider" : "") : UiaRate.Enabled ? "uia" : "";
            string key = mode + "|" + media.Source + "|" + media.Title + "|" + media.Artist;
            if (mode == "") { rateMode = ""; favKey = null; ShowRate(false, false); return; }
            if (mode == "cider") ShowRate(true, true);
            else if (rateMode != "uia" || key != favKey) ShowRate(false, false);   // до проверки окна плеера кнопок не показываем
            else ShowRate(true, uiaHasBan);
            if (key == favKey && mode == rateMode) return;
            rateMode = mode; favKey = key; favNow = null; PaintStar();
            if (mode == "cider")
            {
                if (test) return;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    int st; bool? f = Cider.IsFavorite(out st);
                    Dispatcher.BeginInvoke(new Action(delegate { if (favKey == key) { favNow = f; PaintStar(); } }));
                });
                return;
            }
            ProbeUia(key, 700);
        }

        // Ищем кнопки в окне плеера в фоне: сайту нужно мгновение, чтобы обновить полосу плеера, — пробуем дважды
        void ProbeUia(string key, int delay)
        {
            string src = media.Source, title = media.Title;
            int seq = ++ratePlayerSeq;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                UiaRate.Found f = null;
                for (int i = 0; i < 2 && f == null; i++)
                {
                    System.Threading.Thread.Sleep(i == 0 ? delay : 1500);
                    if (seq != ratePlayerSeq) return;
                    try { f = UiaRate.Find(src, title); } catch (Exception ex) { Store.LogError("оценки", ex); }
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (favKey != key || rateMode != "uia") return;
                    uiaHasBan = f != null && f.Dislike != null;
                    ShowRate(f != null && f.Like != null, uiaHasBan);
                    favNow = f != null ? f.Liked : null; PaintStar();
                }));
            });
        }
        void PaintStar()
        {
            foreach (FrameworkElement[] s in favStars)
            {
                s[0].Visibility = favNow == true ? Visibility.Collapsed : Visibility.Visible;
                s[1].Visibility = favNow == true ? Visibility.Visible : Visibility.Collapsed;
            }
            foreach (Border b in favBtns) b.ToolTip = favNow == true ? "Убрать из избранного" : "В избранное";
        }

        void ToggleFavorite()
        {
            if (rateMode == "uia") { UiaPress(true); return; }
            bool want = favNow != true;
            favNow = want; PaintStar();   // сразу, не дожидаясь Cider
            string key = favKey;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                int st; bool ok = Cider.SetFavorite(want, out st);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (ok) { Toast(((char)0xE734).ToString(), want ? "В избранном" : "Убрано из избранного", null, -1, want ? StarYellow : null); if (want) Happy(1200); return; }
                    if (favKey == key) { favNow = !want; PaintStar(); }
                    CiderFailed(st);
                }));
            });
        }

        void NotInterested()
        {
            if (rateMode == "uia") { UiaPress(false); return; }
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                int st; bool ok = Cider.Dislike(out st);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (ok) { trackDir = 1; trackDirAt = Environment.TickCount; Toast(((char)0xE8D8).ToString(), "Учту: такого меньше", null, -1, null); return; }
                    CiderFailed(st);
                }));
            });
        }

        // Нажимаем кнопку в самом плеере (ищем её заново — страница могла перестроиться), потом перечитываем состояние
        void UiaPress(bool like)
        {
            string src = media.Source, title = media.Title, key = favKey;
            bool? was = favNow;
            if (like) { favNow = was != true; PaintStar(); }
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                UiaRate.Found f = null; bool ok = false;
                try { f = UiaRate.Find(src, title); ok = like ? UiaRate.PressLike(f) : UiaRate.PressDislike(f); } catch (Exception ex) { Store.LogError("оценки", ex); }
                bool? after = null;
                if (ok && like)
                {
                    System.Threading.Thread.Sleep(600);
                    try { var f2 = UiaRate.FindIn(f.Window); if (f2 != null) after = f2.Liked; } catch { }
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!ok)
                    {
                        if (favKey == key) { favNow = was; PaintStar(); }
                        Toast(((char)0xE7BA).ToString(), "Не вижу кнопку в плеере — откройте его вкладку", null, -1, null);
                        return;
                    }
                    if (like)
                    {
                        bool nowLiked = after ?? (was != true);
                        if (favKey == key) { favNow = after ?? favNow; PaintStar(); }
                        Toast(((char)0xE734).ToString(), nowLiked ? "В избранном" : "Убрано из избранного", null, -1, nowLiked ? StarYellow : null);
                        if (nowLiked) Happy(1200);
                        return;
                    }
                    trackDir = 1; trackDirAt = Environment.TickCount;
                    Toast(((char)0xE8D8).ToString(), "Учту: такого меньше", null, -1, null);
                    // Сам плеер не переключил трек — переключаем сами
                    string t0 = title;
                    var tm = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
                    tm.Tick += delegate { tm.Stop(); if (media.Has && media.Title == t0) watcher.Command(0xB0); };
                    tm.Start();
                }));
            });
        }

        void CiderFailed(int status)
        {
            if (status == 401 || status == 403)
            {
                Toast(((char)0xE72E).ToString(), Cider.Token.Length == 0 ? "Нужен ключ Cider — настройки, «Музыка»" : "Cider не принял ключ — проверьте его в настройках", null, -1, ClaudeOrange);
                if (Cider.Token.Length == 0) { openSection = "Музыка"; OpenSettings(); }
            }
            else Toast(((char)0xE7BA).ToString(), "Cider не ответил", null, -1, null);
        }
    }

    // ---- яркость: у ноутбуков — через WMI, у внешних мониторов — по DDC/CI (если монитор это поддерживает)
    static class Brightness
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PHYSICAL_MONITOR { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string desc; }
        [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr mon, out uint n);
        [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr mon, uint n, [Out] PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint n, PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll")] static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
        [DllImport("dxva2.dll")] static extern bool SetMonitorBrightness(IntPtr h, uint v);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        // Физические мониторы за HMONITOR (IntPtr.Zero — главный экран)
        static PHYSICAL_MONITOR[] Monitors(IntPtr mon)
        {
            if (mon == IntPtr.Zero) mon = MonitorFromPoint(new POINT(), 1);
            uint n;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(mon, out n) || n == 0) return null;
            var arr = new PHYSICAL_MONITOR[n];
            return GetPhysicalMonitorsFromHMONITOR(mon, n, arr) ? arr : null;
        }

        // Яркость монитора mon (HMONITOR того, на котором остров): 0…100, -1 — менять нельзя. Сначала по DDC/CI у него
        // самого; так он не умеет (экран ноутбука) — через Windows (WMI)
        public static int Get(IntPtr mon)
        {
            try
            {
                var arr = Monitors(mon);
                if (arr != null)
                {
                    try
                    {
                        uint min, cur, max;
                        if (GetMonitorBrightness(arr[0].h, out min, out cur, out max) && max > min) return (int)Math.Round((cur - min) * 100.0 / (max - min));
                    }
                    finally { DestroyPhysicalMonitors((uint)arr.Length, arr); }
                }
            }
            catch { }
            try
            {
                var s = new System.Management.ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (System.Management.ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return -1;
        }

        // Поставить яркость монитору mon сразу, в текущем потоке (по DDC/CI он отвечает десятки миллисекунд — не из потока окна)
        public static void SetNow(IntPtr mon, int v)
        {
            v = Math.Max(0, Math.Min(100, v));
            try
            {
                var arr = Monitors(mon);
                if (arr != null)
                {
                    bool done = false;
                    try
                    {
                        uint min, cur, max;
                        foreach (var m in arr) if (GetMonitorBrightness(m.h, out min, out cur, out max) && SetMonitorBrightness(m.h, (uint)(min + (max - min) * v / 100))) done = true;
                    }
                    finally { DestroyPhysicalMonitors((uint)arr.Length, arr); }
                    if (done) return;
                }
            }
            catch { }
            try
            {
                var s = new System.Management.ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (System.Management.ManagementObject o in s.Get()) { o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)v }); return; }
            }
            catch { }
        }
    }
}

