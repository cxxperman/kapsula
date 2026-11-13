// Kapsula — «остров» для Windows. Часть исходного кода: Extras.cs
// Вкладка уведомлений, дека ярлыков с горячими клавишами, режим звонка, быстрые настройки (яркость, питание),
// умные карточки буфера, свои сайты во вкладке чата и экономия процессора в покое.
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
using WF = System.Windows.Forms;

namespace Kapsula
{
    partial class IslandWindow
    {
        System.Windows.Threading.DispatcherTimer pollTimer;

        void InitExtras()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                RegisterDeckHotkeys();
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

        // каждые полсекунды
        void ExtrasTick()
        {
            CallTick();
            OverlaysTick();
            // В покое, когда курсор далеко от острова и ничего не движется, опрашиваем мышь реже — меньше нагрузки
            if (pollTimer != null)
            {
                Native.POINT pt;
                bool near = Native.GetCursorPos(out pt) && pt.Y / dpi < Top + 160;
                bool idle = state == 0 && !frameOn && !toastOn && !near && !timerRinging;
                var want = TimeSpan.FromMilliseconds(idle ? 120 : 33);
                if (pollTimer.Interval != want) pollTimer.Interval = want;
            }
        }

        // ================================================================ Уведомления
        StackPanel notifList;

        FrameworkElement BuildNotifPage()
        {
            var dock = new DockPanel();
            var foot = new Grid { Height = 28, Margin = new Thickness(0, 6, 0, 0) };
            notifCount = new TextBlock { Foreground = Faint, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            foot.Children.Add(notifCount);
            var clear = Btn("Очистить всё", false, 0, 28, delegate { seenNotes.Clear(); RenderNotifPage(); });
            clear.HorizontalAlignment = HorizontalAlignment.Right;
            foot.Children.Add(clear);
            DockPanel.SetDock(foot, Dock.Bottom);
            dock.Children.Add(foot);
            notifList = new StackPanel();
            var nsv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = notifList };
            var ntb = PageBoard(PNotif, "notif", 1, 9, 8);
            ntb.Add("list", "Список", nsv, 0, 0, 1, 8, false);
            ntb.Add("foot", "Счётчик и «Очистить всё»", foot, 0, 8, 1, 1, false);
            GC.KeepAlive(dock);
            return FinishBoard(ntb);
        }
        TextBlock notifCount;

        void RenderNotifPage()
        {
            if (notifList == null) return;
            notifList.Children.Clear();
            notifCount.Text = seenNotes.Count == 0 ? "Хранятся до закрытия Kapsula" : Plural(seenNotes.Count, "уведомление", "уведомления", "уведомлений") + " · хранятся до закрытия Kapsula";
            if (seenNotes.Count == 0)
            {
                notifList.Children.Add(PetSpot("sleep", 50, 12, "notif"));
                notifList.Children.Add(new TextBlock { Text = "Уведомлений пока не было", Foreground = Dim, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
                return;
            }
            for (int i = seenNotes.Count - 1; i >= 0; i--)
            {
                Seen s = seenNotes[i];
                var row = new Grid { Height = 46 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                ImageSource ic = null;
                try { ic = SourceIcon(s.Source); } catch { }
                FrameworkElement icon = ic != null ? (FrameworkElement)new Image { Source = ic, Width = 26, Height = 26 } :
                    new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = Chip, Child = new TextBlock { Text = ((char)0xEA8F).ToString(), FontFamily = Icons, FontSize = 13, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(icon);
                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                texts.Children.Add(new TextBlock { Text = s.Title, Foreground = Fg, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                if (s.Text.Length > 0) texts.Children.Add(new TextBlock { Text = s.Text, Foreground = Dim, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(texts, 1); row.Children.Add(texts);
                TimeSpan ago = DateTime.Now - s.At;
                var tm = new TextBlock { Text = ago.TotalMinutes < 1 ? "сейчас" : ago.TotalMinutes < 60 ? (int)ago.TotalMinutes + " мин" : s.At.ToString("H:mm"), Foreground = Faint, FontSize = 11, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 8, 10, 0) };
                Grid.SetColumn(tm, 2); row.Children.Add(tm);
                Seen copy = s;
                var b = new Border { CornerRadius = new CornerRadius(12), Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 5), Child = row, Cursor = Cursors.Hand, ToolTip = "Клик — скопировать текст" };
                b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e) { e.Handled = true; CopyResult(copy.Title + (copy.Text.Length > 0 ? " — " + copy.Text : "")); };
                notifList.Children.Add(b);
            }
        }

        // ================================================================ Дека
        // Пронумерованные ярлыки: программы и действия. Ctrl+Alt+1…9 запускают нужное место.
        const int DeckHotBase = 900;
        class DeckItem { public string Kind = "app", Value = "", Name = ""; }
        WrapPanel deckWrap;

        static readonly string[][] DeckActions =
        {
            new string[] { "snip", "Снимок области", "" }, new string[] { "full", "Весь экран", "" }, new string[] { "rec", "Запись экрана", "" },
            new string[] { "focus25", "Фокус 25", "" }, new string[] { "focus50", "Фокус 50", "" }, new string[] { "lock", "Блокировка", "" },
            new string[] { "downloads", "Загрузки", "" }, new string[] { "cmd", "Команда", "" }, new string[] { "mute", "Микрофон", "" },
            new string[] { "phonecam", "Камера телефона", "" }
        };

        List<DeckItem> DeckLoad()
        {
            var list = new List<DeckItem>();
            string raw = Store.Get("deck");
            if (raw == "" && Store.Get("deckInit") != "1") raw = "act~snip~¦act~focus50~¦act~downloads~¦act~lock~";
            foreach (string part in raw.Split(new char[] { '¦' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = part.Split('~');
                if (p.Length >= 2) list.Add(new DeckItem { Kind = p[0], Value = p[1], Name = p.Length > 2 ? p[2] : "" });
            }
            return list;
        }

        void DeckSave(List<DeckItem> list)
        {
            var parts = new List<string>();
            foreach (DeckItem d in list) parts.Add(d.Kind + "~" + d.Value.Replace("~", "") + "~" + d.Name.Replace("~", ""));
            Store.Set("deck", string.Join("¦", parts.ToArray()));
            Store.Set("deckInit", "1");
            RegisterDeckHotkeys();
        }

        public void ReloadDeckHotkeys() { RegisterDeckHotkeys(); }
        public void RefreshTabs() { if (page == PDeck && !DeckOn) ShowPage(PHome); else ShowPage(page); }

        void RegisterDeckHotkeys()
        {
            if (hwnd == IntPtr.Zero) return;
            for (int i = 0; i < 9; i++) Native.UnregisterHotKey(hwnd, DeckHotBase + i);
            if (Store.Get("deckHotkeys") == "0" || !DeckOn) return;
            int n = DeckLoad().Count;
            for (int i = 0; i < n && i < 9; i++) Native.RegisterHotKey(hwnd, DeckHotBase + i, 0x1 | 0x2 | 0x4000, (uint)(0x31 + i));   // Ctrl+Alt+цифра
        }

        FrameworkElement BuildDeckPage()
        {
            var dock = new DockPanel();
            var foot = new TextBlock { Text = "Перетащите сюда программу или файл · Ctrl+Alt+цифра запускает место · правый клик — убрать", Foreground = Faint, FontSize = 11.5, Margin = new Thickness(4, 6, 0, 0) };
            DockPanel.SetDock(foot, Dock.Bottom);
            dock.Children.Add(foot);
            deckWrap = new WrapPanel();
            var host = new Grid { Background = Brushes.Transparent, AllowDrop = true };
            host.Children.Add(deckWrap);
            host.Drop += delegate(object o, DragEventArgs e)
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                List<DeckItem> list = DeckLoad();
                foreach (string f in files)
                {
                    if (list.Count >= 9) break;
                    list.Add(new DeckItem { Kind = "app", Value = f, Name = System.IO.Path.GetFileNameWithoutExtension(f) });
                    Toast(((char)0xF0E2).ToString(), "Добавлено в деку · Ctrl+Alt+" + list.Count, null, -1, accent);
                }
                DeckSave(list);
                RenderDeck();
            };
            dock.Children.Add(host);
            return dock;
        }

        void RenderDeck()
        {
            if (deckWrap == null) return;
            deckWrap.Children.Clear();
            List<DeckItem> list = DeckLoad();
            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                DeckItem d = list[i];
                FrameworkElement icon;
                string name = d.Name;
                if (d.Kind == "act")
                {
                    string[] a = Array.Find(DeckActions, delegate(string[] x) { return x[0] == d.Value; });
                    if (a == null) continue;
                    if (name.Length == 0) name = a[1];
                    icon = new TextBlock { Text = a[2], FontFamily = Icons, FontSize = 18, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                }
                else
                {
                    ImageSource src = null;
                    try { src = Shell.IconFor(d.Value); } catch { }
                    icon = src != null ? (FrameworkElement)new Image { Source = src, Width = 28, Height = 28 } : new TextBlock { Text = ((char)0xE8A5).ToString(), FontFamily = Icons, FontSize = 18, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                }
                var g = new Grid { Width = 84, Height = 80 };
                var sq = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(14), Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), Child = icon };
                g.Children.Add(sq);
                g.Children.Add(new TextBlock { Text = (i + 1).ToString(), Foreground = Faint, FontSize = 9.5, FontWeight = FontWeights.Bold, Margin = new Thickness(14, 0, 0, 0) });
                g.Children.Add(new TextBlock { Text = name, Foreground = Dim, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 80, Margin = new Thickness(0, 0, 0, 6) });
                var b = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand, Child = g, ToolTip = name + " · Ctrl+Alt+" + (i + 1) };
                b.MouseEnter += delegate { b.Background = ChipHover; };
                b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
                b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e) { e.Handled = true; RunDeckSlot(idx); };
                b.MouseRightButtonDown += delegate(object o, MouseButtonEventArgs e) { e.Handled = true; List<DeckItem> cur = DeckLoad(); if (idx < cur.Count) { cur.RemoveAt(idx); DeckSave(cur); RenderDeck(); } };
                Fx.Press(b, 0.94);
                deckWrap.Children.Add(b);
            }
            if (list.Count < 9)
            {
                var add = new Grid { Width = 84, Height = 80 };
                add.Children.Add(new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(14), BorderBrush = Line, BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0),
                    Child = new TextBlock { Text = ((char)0xE710).ToString(), FontFamily = Icons, FontSize = 14, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
                add.Children.Add(new TextBlock { Text = "Добавить", Foreground = Faint, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) });
                var ab = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand, Child = add };
                ab.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e) { e.Handled = true; DeckAddMenu(ab); };
                deckWrap.Children.Add(ab);
            }
        }

        void DeckAddMenu(FrameworkElement anchor)
        {
            var menu = new WF.ContextMenuStrip();
            foreach (string[] a in DeckActions)
            {
                string id = a[0], nm = a[1];
                menu.Items.Add(nm, null, delegate { List<DeckItem> l = DeckLoad(); if (l.Count < 9) { l.Add(new DeckItem { Kind = "act", Value = id }); DeckSave(l); RenderDeck(); } });
            }
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Программа или файл…", null, delegate
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Что положить в деку", Filter = "Программы и ярлыки|*.exe;*.lnk;*.url|Все файлы|*.*" };
                if (dlg.ShowDialog() == true) { List<DeckItem> l = DeckLoad(); if (l.Count < 9) { l.Add(new DeckItem { Kind = "app", Value = dlg.FileName, Name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName) }); DeckSave(l); RenderDeck(); } }
            });
            DarkMenuRenderer.Apply(menu, 240, false);
            Point at = anchor.PointToScreen(new Point(0, anchor.ActualHeight + 4));
            menu.Show(new System.Drawing.Point((int)at.X, (int)at.Y));
        }

        public void RunDeckSlot(int i)
        {
            List<DeckItem> list = DeckLoad();
            if (i < 0 || i >= list.Count) return;
            DeckItem d = list[i];
            if (d.Kind == "app")
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(d.Value) { UseShellExecute = true }); }
                catch { Toast(((char)0xF0E2).ToString(), "Не удалось открыть: " + d.Name, null, -1, null); }
                return;
            }
            switch (d.Value)
            {
                case "snip": StartSnip(false); break;
                case "full": FullShot(); break;
                case "rec": ToggleRecord(false); break;
                case "focus25": StartFocus(25, ""); break;
                case "focus50": StartFocus(50, ""); break;
                case "lock": Native.LockWorkStation(); break;
                case "downloads": try { System.Diagnostics.Process.Start(DownloadsDir()); } catch { } break;
                case "cmd": ToggleCommand(); break;
                case "phonecam": OpenPhoneCam(); break;
                case "mute": Audio.SetMicMuted(!Audio.MicMuted()); Toast(((char)0xE720).ToString(), Audio.MicMuted() ? "Микрофон выключен" : "Микрофон включён", null, -1, Audio.MicMuted() ? RecRed : (Brush)accent); break;
            }
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
        string CallTime() { TimeSpan t = DateTime.Now - callStart; return (int)t.TotalMinutes + ":" + t.Seconds.ToString("00"); }

        static string CallAppNow()
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
                                foreach (string a in CallApps) if ((exe == a || exe.StartsWith(a)) && AppVolume.SessionActive(exe)) return Pretty(exe);
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
                                foreach (string a in CallApps) if (low.Contains(a) && AppVolume.SessionActive(a)) return Pretty(a);
                            }
                        }
                }
            }
            catch { }
            return null;
        }

        static string Pretty(string exe)
        {
            switch (exe)
            {
                case "msedge": return "Edge"; case "chrome": return "Chrome"; case "ms-teams": case "teams": return "Teams";
                default: return exe.Length > 0 ? char.ToUpper(exe[0]) + exe.Substring(1) : exe;
            }
        }

        int callTickN;
        // Звонок = программа для звонков держит микрофон и звук, и в разговоре реально говорят (вы или собеседник).
        // Просто сидеть в голосовом канале Discord — не звонок: карточка появится, когда кто-то заговорит,
        // и уйдёт после 45 секунд тишины.
        string callCandidate;
        int callVoiceAt;
        void CallTick()
        {
            callTickN++;
            if (DiscordLive) { DiscordCallTick(); return; }   // плагин в Discord сообщает всё сам — догадки по микрофону не нужны
            int now = Environment.TickCount;
            if (callTest) callCandidate = "Discord";
            else if (Store.Get("callMode") != "1") callCandidate = null;   // альфа: выключено, пока не включат в настройках
            else if (callTickN % 4 == 0) callCandidate = CallAppNow();
            if (callCandidate != null)
            {
                double pk = callTest ? (callTickN % 6 < 3 ? 0.3 : 0) : Math.Max(AppVolume.SessionPeak(callCandidate, 0), AppVolume.SessionPeak(callCandidate, 1));
                if (pk > 0.08) callVoiceAt = now;   // тишина в канале ~0.01, речь — от 0.1
            }
            bool talking = callCandidate != null && callVoiceAt != 0 && unchecked(now - callVoiceAt) < 45000;
            string app = talking ? callCandidate : null;
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

        // ================================================================ Быстрые настройки
        Border powerChips;

        FrameworkElement BuildQuickControls()
        {
            var g = new Grid { Height = 36, Margin = new Thickness(0, 6, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // яркость: ползунок; если монитор не даёт её менять — подпись
            var bright = new Grid { Margin = new Thickness(14, 0, 14, 0) };
            bright.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            bright.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bright.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            bright.Children.Add(new TextBlock { Text = ((char)0xE706).ToString(), FontFamily = Icons, FontSize = 13, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center });
            var bv = new TextBlock { Foreground = Dim, FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(bv, 2); bright.Children.Add(bv);
            var bar = new Bar(150, 5, Fg, 22);
            bar.Root.Cursor = Cursors.Hand; bar.Root.HorizontalAlignment = HorizontalAlignment.Stretch;
            int cur = Brightness.Get();
            if (cur < 0) { bv.Text = "—"; bar.Root.Opacity = 0.35; bar.Root.ToolTip = "Этот монитор не даёт менять яркость программам"; }
            else { bar.Set(cur / 100.0); bv.Text = cur + "%"; }
            Action<Point> setFrom = delegate(Point p) { if (cur < 0) return; double v = bar.ValueAt(p); bar.Set(v); bv.Text = Math.Round(v * 100) + "%"; Brightness.Set((int)Math.Round(v * 100)); };
            bar.Root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { bar.Root.CaptureMouse(); setFrom(e.GetPosition(bar.Root)); };
            bar.Root.MouseMove += delegate(object s, MouseEventArgs e) { if (bar.Root.IsMouseCaptured) setFrom(e.GetPosition(bar.Root)); };
            bar.Root.MouseLeftButtonUp += delegate { bar.Root.ReleaseMouseCapture(); };
            bar.Root.SizeChanged += delegate { bar.Resize(Math.Max(40, bar.Root.ActualWidth)); };
            Grid.SetColumn(bar.Root, 1); bright.Children.Add(bar.Root);
            g.Children.Add(new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Child = bright });
            // режим питания
            var chips = new StackPanel { Orientation = Orientation.Horizontal };
            string[] names = { "Экономия", "Баланс", "Мощность" };
            string active = Power.Active();
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                bool on = Power.Ids[i] == active;
                var c = new Border { Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(11, 0, 11, 1), Background = on ? TabOn : Brushes.Transparent, Cursor = Cursors.Hand,
                    Child = new TextBlock { Text = names[i], FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = on ? Fg : Dim, VerticalAlignment = VerticalAlignment.Center } };
                c.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (!Power.Set(idx)) Toast(((char)0xE945).ToString(), "Этой схемы питания нет на компьютере", null, -1, null);
                    else Toast(((char)0xE945).ToString(), "Питание: " + names[idx], null, -1, accent);
                    var parent = (Panel)g.Parent; int at = parent.Children.IndexOf(g); parent.Children.RemoveAt(at); parent.Children.Insert(at, BuildQuickControls());
                };
                chips.Children.Add(c);
            }
            powerChips = new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(3), Child = chips, ToolTip = "Схема питания Windows" };
            Grid.SetColumn(powerChips, 2); g.Children.Add(powerChips);
            // ночной свет и заряд батареи
            var extra = new StackPanel { Orientation = Orientation.Horizontal };
            var night = IconBtn(((char)0xE708).ToString(), 32, delegate { try { System.Diagnostics.Process.Start("ms-settings:nightlight"); } catch { } });
            night.ToolTip = "Ночной свет — параметры Windows";
            extra.Children.Add(night);
            var ps = WF.SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus != WF.BatteryChargeStatus.NoSystemBattery && ps.BatteryLifePercent <= 1)
                extra.Children.Add(new TextBlock { Text = (int)Math.Round(ps.BatteryLifePercent * 100) + "%" + (ps.PowerLineStatus == WF.PowerLineStatus.Online ? " ⚡" : ""), Foreground = Fg, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0), ToolTip = "Заряд батареи" });
            var extraCard = new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(2), Child = extra };
            Grid.SetColumn(extraCard, 4); g.Children.Add(extraCard);
            return g;
        }

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
                var c = new Border { Height = 24, CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 0, 11, 1), Background = on ? TabOn : Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 2, 0),
                    Child = new TextBlock { Text = names[i], FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = on ? Fg : Dim, VerticalAlignment = VerticalAlignment.Center } };
                c.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; clipFilter = idx; RenderClips(); };
                clipFilterRow.Children.Add(c);
            }
        }

        // Заголовок записи: метка типа (ссылка, цвет с образцом, код) и сам текст
        FrameworkElement ClipTitle(ClipItem it, string one)
        {
            int k = ClipKind(it);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (k == 3)
            {
                Color c;
                if (TryParseCssColor(it.Text.Trim(), out c))
                    sp.Children.Add(new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(c), BorderBrush = Line, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
            }
            else if (k == 2 || k == 4)
            {
                var badge = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(k == 2 ? Color.FromArgb(0x40, 0x4D, 0xA3, 0xFF) : Color.FromArgb(0x40, 0xAF, 0x52, 0xDE)),
                    Child = new TextBlock { Text = k == 2 ? "Ссылка" : "Код", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(k == 2 ? Color.FromRgb(0x7F, 0xBE, 0xFF) : Color.FromRgb(0xD0, 0x9C, 0xFF)) } };
                sp.Children.Add(badge);
            }
            sp.Children.Add(new TextBlock { Text = one, Foreground = Fg, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontFamily = k == 4 ? new FontFamily("Cascadia Mono, Consolas") : FontFamily });
            return sp;
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
            if (name.Length > 16) name = name.Substring(0, 15) + "…";
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
            parts.RemoveAt(i);
            Store.Set("webSites", string.Join("¦", parts.ToArray()));
            int sel = WebIndex();
            if (sel == i + 3) { Store.Set("webChat", "0"); if (duck != null) duck.Go(WebUrls[0]); }
            else if (sel > i + 3) Store.Set("webChat", (sel - 1).ToString());
            RenderWebRow();
        }

        // Маленькое тёмное окно с полями ввода; null — отмена
        string[] PromptFields(string title, string[] hints)
        {
            bool wasPinned = pinned;
            SetPinned(true);
            var win = new Window { Title = "Kapsula", Width = 380, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, ShowInTaskbar = false,
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
        readonly List<Border> rateBtns = new List<Border>(), favBtns = new List<Border>();
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
            rateBtns.Add(b);
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

    // ---- схемы питания Windows
    static class Power
    {
        // экономия, сбалансированная, высокая производительность
        public static readonly string[] Ids = { "a1841308-3541-4fab-bc81-f71556f20b4a", "381b4222-f694-41f0-9685-ff5bb260df2e", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" };

        static string Run(string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("powercfg", args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
                using (var p = System.Diagnostics.Process.Start(psi)) { string s = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000); return s; }
            }
            catch { return ""; }
        }

        public static string Active()
        {
            Match m = Regex.Match(Run("/getactivescheme"), @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
            return m.Success ? m.Value.ToLowerInvariant() : "";
        }

        public static bool Set(int i)
        {
            Run("/setactive " + Ids[i]);
            return Active() == Ids[i];
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

        static PHYSICAL_MONITOR[] Monitors()
        {
            IntPtr mon = MonitorFromPoint(new POINT(), 1);
            uint n;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(mon, out n) || n == 0) return null;
            var arr = new PHYSICAL_MONITOR[n];
            return GetPhysicalMonitorsFromHMONITOR(mon, n, arr) ? arr : null;
        }

        // 0…100, -1 — менять нельзя
        public static int Get()
        {
            try
            {
                var s = new System.Management.ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (System.Management.ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            try
            {
                var arr = Monitors();
                if (arr == null) return -1;
                try
                {
                    uint min, cur, max;
                    if (GetMonitorBrightness(arr[0].h, out min, out cur, out max) && max > min) return (int)Math.Round((cur - min) * 100.0 / (max - min));
                }
                finally { DestroyPhysicalMonitors((uint)arr.Length, arr); }
            }
            catch { }
            return -1;
        }

        public static void Set(int v)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { SetNow(v); });
        }

        // То же, но сразу, в текущем потоке (монитор по DDC/CI отвечает десятки миллисекунд — не из потока окна)
        public static void SetNow(int v)
        {
            v = Math.Max(0, Math.Min(100, v));
            try
            {
                var s = new System.Management.ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (System.Management.ManagementObject o in s.Get()) { o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)v }); return; }
            }
            catch { }
            try
            {
                var arr = Monitors();
                if (arr == null) return;
                try
                {
                    uint min, cur, max;
                    foreach (var m in arr) if (GetMonitorBrightness(m.h, out min, out cur, out max)) SetMonitorBrightness(m.h, (uint)(min + (max - min) * v / 100));
                }
                finally { DestroyPhysicalMonitors((uint)arr.Length, arr); }
            }
            catch { }
        }
    }
}

