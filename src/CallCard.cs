// Kapsula — «остров» для Windows. Часть исходного кода: CallCard.cs
// Карточка звонка на «Главной»: как голосовая панель самого Discord, но в духе iPhone. Сверху — канал, сервер и
// зелёный таймер. Ниже — кто в канале: аватарки, у говорящего зелёное кольцо (как в Discord), у того, кто выключил
// микрофон или звук, — значок. Потом круглые кнопки: микрофон, полный мут, демка (экран или окно — меню, как окно
// выбора в Discord), настройки звука и красная «отключиться»; включённая кнопка становится белой, а значок —
// красным и перечёркнутым, как в Discord. Когда вы показываете экран или включили камеру — у таймера метки «LIVE»
// и камеры. Если играет музыка, внизу тонкая строка плеера: обложка, трек, «назад / пауза / вперёд».
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WF = System.Windows.Forms;

namespace Kapsula
{
    partial class IslandWindow
    {
        static readonly SolidColorBrush AppleGreen = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
        static readonly SolidColorBrush AppleRed = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        static readonly SolidColorBrush Secondary = new SolidColorBrush(Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF));
        static readonly SolidColorBrush Hairline = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
        static readonly SolidColorBrush BadgeBack = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E));
        // нет аватарки — буква на цветном круге, как в контактах iPhone
        static readonly Brush[] FaceTones = { new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)), new SolidColorBrush(Color.FromRgb(0x30, 0xB0, 0x50)), new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)), new SolidColorBrush(Color.FromRgb(0xBF, 0x5A, 0xF2)), new SolidColorBrush(Color.FromRgb(0xFF, 0x37, 0x5F)), new SolidColorBrush(Color.FromRgb(0x40, 0xA8, 0xD8)), new SolidColorBrush(Color.FromRgb(0x5E, 0x5C, 0xE6)) };

        const int Faces = 6;   // аватарок в ряд, остальные — «+N»
        TextBlock callTitle, callMusicTitle, callMusicArtist, callMore;
        readonly Border[] callKeys = new Border[5];   // микрофон, полный мут, устройства, отключиться, демка
        readonly TextBlock[] callKeyGlyphs = new TextBlock[5];
        readonly FrameworkElement[] keyStrike = new FrameworkElement[2];   // красная косая черта на микрофоне и наушниках
        readonly Grid[] faceSlots = new Grid[Faces];
        readonly Ellipse[] faceRings = new Ellipse[Faces], facePics = new Ellipse[Faces];
        readonly TextBlock[] faceLetters = new TextBlock[Faces], faceBadgeGlyphs = new TextBlock[Faces];
        readonly Border[] faceBadges = new Border[Faces];
        StackPanel callFaces;
        PetView callPet;   // зверёк в наушниках на карточке звонка
        Border callLive, callCam, callMusicArt;
        FrameworkElement callMusicRow, callMusicPlay, callMusicPause;
        Rectangle callDivider;

        void EnsureCallCard()
        {
            if (homeCallCard != null || homeGrid == null) return;
            var stack = new StackPanel { Margin = new Thickness(16, 12, 16, 12), VerticalAlignment = VerticalAlignment.Center };

            // 1. Канал, сервер, таймер
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            callTitle = new TextBlock { Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            callAppText = new TextBlock { Foreground = Secondary, FontSize = 12, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            names.Children.Add(callTitle); names.Children.Add(callAppText);
            head.Children.Add(names);
            // справа — метки «LIVE» (вы показываете экран) и камеры, как в Discord, и зелёный таймер; метки одной
            // высоты: красная «LIVE» и зелёная камера (зелёный — как точка камеры на iPhone)
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            callLive = new Border { Height = 18, CornerRadius = new CornerRadius(9), Background = AppleRed, Padding = new Thickness(7, 0, 7, 0), Margin = new Thickness(0, 2, 6, 0), VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, ToolTip = "Вы показываете экран",
                Child = new TextBlock { Text = "LIVE", Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) } };
            callCam = new Border { Height = 18, CornerRadius = new CornerRadius(9), Background = AppleGreen, Padding = new Thickness(7, 0, 7, 0), Margin = new Thickness(0, 2, 6, 0), VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, ToolTip = "Камера включена",
                Child = new TextBlock { Text = "", FontFamily = Icons, FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            callTimeText = new TextBlock { Foreground = AppleGreen, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
            System.Windows.Documents.Typography.SetNumeralAlignment(callTimeText, FontNumeralAlignment.Tabular);   // цифры не прыгают
            right.Children.Add(callLive); right.Children.Add(callCam); right.Children.Add(callTimeText);
            Grid.SetColumn(right, 1); head.Children.Add(right);
            stack.Children.Add(head);

            // 2. Кто в канале
            callFaces = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-2, 9, 0, 0), Visibility = Visibility.Collapsed };
            for (int i = 0; i < Faces; i++) callFaces.Children.Add(FaceSlot(i));
            callMore = new TextBlock { Foreground = Secondary, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            callFaces.Children.Add(callMore);
            // справа от собеседников — зверёк в наушниках: слушает разговор (попутчик острова садится на его место)
            var facesRow = new Grid();
            facesRow.Children.Add(callFaces);
            callPet = new PetView(1.6, Ink);
            callPet.OwnOutfit = "phones";
            callPet.Root.HorizontalAlignment = HorizontalAlignment.Right;
            callPet.Root.VerticalAlignment = VerticalAlignment.Bottom;
            callPet.Root.Margin = new Thickness(0, 9, 0, 2);
            callPet.Root.Visibility = Visibility.Collapsed;
            facesRow.Children.Add(callPet.Root);
            stack.Children.Add(facesRow);

            // 3. Кнопки, как в голосовой панели Discord: микрофон, звук, демка, настройки; справа — красная «отключиться»
            var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string[] glyphs = { "\uE720", "\uE7F6", "\uE713", "\uE778", "\uE7F4" };
            string[] tips = { "Микрофон", "Полный мут: не слышать никого", "Микрофон и динамики", "Отключиться", "Показать экран (демка)" };
            for (int i = 0; i < 5; i++)
            {
                int k = i;
                var g = callKeyGlyphs[i] = new TextBlock { Text = glyphs[i], FontFamily = Icons, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                UIElement face = i < 2 ? Strike(g, 18, 1.8, Brushes.White, out keyStrike[i]) : (UIElement)g;   // микрофон и наушники перечёркиваются, как в Discord
                var key = callKeys[i] = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = i == 3 ? Glass.Tinted(Color.FromRgb(0xFF, 0x45, 0x3A)) : (Brush)Glass.Fill, BorderBrush = Glass.Rim, BorderThickness = new Thickness(1), Child = face, Cursor = Cursors.Hand, ToolTip = tips[i], Margin = new Thickness(0, 0, i == 2 || i == 3 ? 0 : 10, 0) };
                key.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; CallKey(k); };
                Fx.Press(key, 0.9);
            }
            left.Children.Add(callKeys[0]); left.Children.Add(callKeys[1]); left.Children.Add(callKeys[4]); left.Children.Add(callKeys[2]);
            row.Children.Add(left);
            Grid.SetColumn(callKeys[3], 1); row.Children.Add(callKeys[3]);
            stack.Children.Add(row);

            // 4. Музыка — тонкой строкой под чертой
            callDivider = new Rectangle { Height = 1, Fill = Hairline, Margin = new Thickness(0, 11, 0, 7), Visibility = Visibility.Collapsed };
            stack.Children.Add(callDivider);
            var mg = new Grid { Visibility = Visibility.Collapsed };
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            callMusicArt = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(7), Background = Chip };
            mg.Children.Add(callMusicArt);
            var mt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
            callMusicTitle = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            callMusicArtist = new TextBlock { Foreground = Secondary, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
            mt.Children.Add(callMusicTitle); mt.Children.Add(callMusicArtist);
            Grid.SetColumn(mt, 1); mg.Children.Add(mt);
            var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ctl.Children.Add(MusicKey(MediaShape("prev", 12, Brushes.White), 0xB1));
            callMusicPlay = MediaShape("play", 14, Brushes.White);
            callMusicPause = MediaShape("pause", 14, Brushes.White);
            var pp = new Grid();
            pp.Children.Add(callMusicPlay); pp.Children.Add(callMusicPause);
            ctl.Children.Add(MusicKey(pp, 0xB3));
            ctl.Children.Add(MusicKey(MediaShape("next", 12, Brushes.White), 0xB0));
            Grid.SetColumn(ctl, 2); mg.Children.Add(ctl);
            callMusicRow = mg;
            stack.Children.Add(mg);

            homeCallCard = new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Child = stack, Visibility = Visibility.Collapsed };
            Grid.SetRow(homeCallCard, 0);
            homeGrid.Children.Add(homeCallCard);
            PaintMute();
        }

        // Аватарка участника: картинка (или буква на цветном круге), зелёное кольцо, пока говорит, значок мута
        FrameworkElement FaceSlot(int i)
        {
            var g = faceSlots[i] = new Grid { Width = 34, Height = 34, Margin = new Thickness(0, 0, 2, 0), Visibility = Visibility.Collapsed };
            faceRings[i] = new Ellipse { Stroke = AppleGreen, StrokeThickness = 2, Opacity = 0 };
            facePics[i] = new Ellipse { Width = 28, Height = 28 };
            faceLetters[i] = new TextBlock { Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            faceBadgeGlyphs[i] = new TextBlock { FontFamily = Icons, FontSize = 7.5, Foreground = AppleRed, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            FrameworkElement strike;
            faceBadges[i] = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = BadgeBack, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Child = Strike(faceBadgeGlyphs[i], 10, 1.1, BadgeBack, out strike), Visibility = Visibility.Collapsed };
            strike.Visibility = Visibility.Visible;   // значок появляется, только когда микрофон или звук выключен
            g.Children.Add(faceRings[i]); g.Children.Add(facePics[i]); g.Children.Add(faceLetters[i]); g.Children.Add(faceBadges[i]);
            return g;
        }

        // Значок с косой красной чертой сверху вниз — как перечёркнутые микрофон и наушники в Discord и на iPhone.
        // Под чертой — прорезь цвета фона, чтобы черта не сливалась со значком. Черта видна, когда strike показан.
        Grid Strike(TextBlock glyph, double box, double thick, Brush back, out FrameworkElement strike)
        {
            var g = new Grid { Width = box, Height = box, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(glyph);
            double a = box * 0.12, b = box * 0.88;
            var lines = new Grid { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            lines.Children.Add(new Line { X1 = a, Y1 = a, X2 = b, Y2 = b, Stroke = back, StrokeThickness = thick + 2.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            lines.Children.Add(new Line { X1 = a, Y1 = a, X2 = b, Y2 = b, Stroke = AppleRed, StrokeThickness = thick, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            g.Children.Add(lines);
            strike = lines;
            return g;
        }

        Border MusicKey(FrameworkElement shape, byte command)
        {
            var b = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = shape };
            shape.HorizontalAlignment = HorizontalAlignment.Center; shape.VerticalAlignment = VerticalAlignment.Center;
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; watcher.Command(command); };
            Fx.Press(b, 0.86);
            return b;
        }

        void CallKey(int k)
        {
            bool live = DiscordLive;
            if (k == 0)
            {
                if (live) DiscordCommand("mute");   // микрофон самого Discord, а не весь микрофон Windows
                else { Audio.SetMicMuted(!Audio.MicMuted()); PaintMute(); RenderCompact(); }
            }
            else if (k == 1 && live) DiscordCommand("deaf");
            else if (k == 2) CallDevices(callKeys[2]);
            else if (k == 3 && live) DiscordCommand("leave");
            else if (k == 4 && live)
            {
                if (dvStream) DiscordCommand("unstream");   // идёт демка — остановить
                else if (dvSourcesWanted == 0) StreamPick();
            }
        }

        // Кнопка демки: Discord присылает, что можно показать (экраны и окна), — и у кнопки открывается меню, как
        // окно выбора в самом Discord. Пока ждём ответа, кнопка притухает; молчит — значит, плагин в Discord старый
        void StreamPick()
        {
            dvSourcesWanted = Environment.TickCount + 4000;
            if (dvSourcesWanted == 0) dvSourcesWanted = 1;
            DiscordVoiceServer.Command("sources");
            PaintMute();
            ClaudeAfter(4200, delegate
            {
                if (dvSourcesWanted == 0 || unchecked(Environment.TickCount - dvSourcesWanted) < 0) return;
                dvSourcesWanted = 0;
                PaintMute();
                CallNotice("Discord не ответил — обновите плагин Kapsula");
            });
        }

        void StreamMenu(object[] list)
        {
            if (homeCallCard == null || !DiscordLive || !dvInCall) return;
            var screens = new List<KeyValuePair<string, string>>();
            var windows = new List<KeyValuePair<string, string>>();
            foreach (object x in list)
            {
                var s = x as Dictionary<string, object>;
                if (s == null) continue;
                string id = DText(s, "id"), name = DText(s, "name");
                if (id.Length == 0) continue;
                if (id.StartsWith("screen:")) screens.Add(new KeyValuePair<string, string>(id, name));
                else if (windows.Count < 20) windows.Add(new KeyValuePair<string, string>(id, name.Length > 0 ? name : "Окно"));
            }
            if (screens.Count + windows.Count == 0) { CallNotice("Discord не нашёл, что показать"); return; }
            if (TestRider != null)
            {
                // проверка: меню не открываем (оно забирает клавиатуру) — берём первый экран
                WalkLog("демка: экранов " + screens.Count + ", окон " + windows.Count);
                DiscordCommand("stream:" + (screens.Count > 0 ? screens[0].Key : windows[0].Key));
                return;
            }
            var menu = new WF.ContextMenuStrip();
            menu.Items.Add(new WF.ToolStripMenuItem("Показать в Discord") { Enabled = false });
            for (int i = 0; i < screens.Count; i++)
            {
                string id = screens[i].Key;
                var it = new WF.ToolStripMenuItem(screens.Count == 1 ? "Весь экран" : "Экран " + (i + 1));
                it.Click += delegate { DiscordCommand("stream:" + id); };
                menu.Items.Add(it);
            }
            if (windows.Count > 0)
            {
                menu.Items.Add(new WF.ToolStripSeparator());
                foreach (KeyValuePair<string, string> w in windows)
                {
                    string id = w.Key;
                    string label = w.Value.Length > 46 ? w.Value.Substring(0, 45) + "…" : w.Value;
                    var it = new WF.ToolStripMenuItem(label.Replace("&", "&&"));
                    it.Click += delegate { DiscordCommand("stream:" + id); };
                    menu.Items.Add(it);
                }
            }
            DarkMenuRenderer.Apply(menu, 300, true);
            Point at = callKeys[4].PointToScreen(new Point(0, callKeys[4].ActualHeight + 4));
            menu.Show(new System.Drawing.Point((int)at.X, (int)at.Y));
        }

        // Что-то не вышло — сказать прямо в карточке (красной строкой на три с половиной секунды) и надписью в капсуле
        string callNote;
        int callNoteUntil;

        void CallNotice(string text)
        {
            callNote = text; callNoteUntil = Environment.TickCount + 3500;
            PaintMute();
            ClaudeAfter(3600, delegate { PaintMute(); });
            if (state == 0) Toast("", text, null, -1, null);
        }

        // Настройки звука: микрофон и динамики в одном меню
        void CallDevices(FrameworkElement anchor)
        {
            var menu = new WF.ContextMenuStrip();
            for (int flow = 1; flow >= 0; flow--)
            {
                if (flow == 0) menu.Items.Add(new WF.ToolStripSeparator());
                menu.Items.Add(new WF.ToolStripMenuItem(flow == 1 ? "Микрофон" : "Звук") { Enabled = false });
                bool any = false;
                foreach (AudioOutDevice dev in AudioOut.List(flow))
                {
                    AudioOutDevice d = dev;
                    var it = new WF.ToolStripMenuItem(d.Name) { Checked = d.Default };
                    it.Click += delegate { AudioOut.SetDefault(d.Id); };
                    menu.Items.Add(it);
                    any = true;
                }
                if (!any) menu.Items.Add(new WF.ToolStripMenuItem("устройств нет") { Enabled = false });
            }
            DarkMenuRenderer.Apply(menu, 300, true);
            Point at = anchor.PointToScreen(new Point(0, anchor.ActualHeight + 4));
            menu.Show(new System.Drawing.Point((int)at.X, (int)at.Y));
        }

        // Включённая кнопка — белая с красным значком: так и на iPhone видно, и в Discord привычно
        void CallKeyState(int i, bool on, string tip)
        {
            callKeys[i].Background = on ? Brushes.White : (Brush)Glass.Fill;
            callKeyGlyphs[i].Foreground = on ? AppleRed : Brushes.White;
            if (i < keyStrike.Length) keyStrike[i].Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            callKeys[i].ToolTip = tip;
        }

        // Обновить карточку: звонок, кто говорит, кнопки, музыку (раз в полсекунды, при каждом сообщении Discord и смене трека)
        void PaintMute()
        {
            if (homeCallCard == null) return;
            bool live = DiscordLive;
            bool deaf = live && dvDeaf;
            bool muted = live ? dvMute || dvDeaf : Audio.MicMuted();
            CallKeyState(0, muted, muted ?"Микрофон выключен — нажмите, чтобы включить" : "Вас слышно — нажмите, чтобы выключить микрофон");
            CallKeyState(1, deaf, deaf ?"Полный мут — нажмите, чтобы вернуть звук" : "Полный мут: не слышать никого");
            callKeys[1].Visibility = live ? Visibility.Visible : Visibility.Collapsed;   // полный мут и «отключиться» — только для звонка Discord
            callKeys[3].Visibility = live ? Visibility.Visible : Visibility.Collapsed;
            callKeys[4].Visibility = live ? Visibility.Visible : Visibility.Collapsed;   // и демка
            CallKeyState(4, live && dvStream, dvStream ? "Идёт демка — нажмите, чтобы остановить" : "Показать экран (демка)");
            callKeys[4].Opacity = dvSourcesWanted != 0 ? 0.5 : 1;   // ждём от Discord, что можно показать
            callTitle.Text = live ? DvCallName("Личный звонок") : "Звонок";
            callAppText.Text = live ? (dvGuild.Length > 0 ? dvGuild : "Discord") + (dvUsers > 1 ? " · " + Plural(dvUsers, "участник", "участника", "участников") : "")
                : (callApp ?? "") + (muted ? " · микрофон выключен" : "");
            bool note = callNote != null && unchecked(Environment.TickCount - callNoteUntil) < 0;   // что-то не вышло — красной строкой
            if (note) callAppText.Text = callNote;
            callAppText.Foreground = note ? AppleRed : Secondary;
            if (callTimeText != null && callApp != null) callTimeText.Text = CallTime();
            callLive.Visibility = live && dvStream ? Visibility.Visible : Visibility.Collapsed;
            callCam.Visibility = live && dvVideo ? Visibility.Visible : Visibility.Collapsed;

            // кто в канале
            List<DvPerson> people = live ? dvPeople : null;
            int n = people == null ? 0 : people.Count;
            callFaces.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (callPet != null)
            {
                callPet.Root.Visibility = n > 0 && PetProps() ? Visibility.Visible : Visibility.Collapsed;
                callPet.Mood(dvSpeaking && !dvMute ? "dance" : "idle");   // вы говорите — кивает в наушниках
            }
            for (int i = 0; i < Faces; i++)
            {
                if (i >= n) { faceSlots[i].Visibility = Visibility.Collapsed; continue; }
                DvPerson p = people[i];
                bool pm = p.Me ? dvMute : p.Mute, pd = p.Me ? dvDeaf : p.Deaf;   // своё состояние — сразу, не дожидаясь Discord
                faceSlots[i].Visibility = Visibility.Visible;
                Brush pic = FaceImage(p.Avatar);
                Brush fill = pic ?? FaceTones[(int)((uint)p.Id.GetHashCode() % (uint)FaceTones.Length)];
                if (facePics[i].Fill != fill) facePics[i].Fill = fill;
                faceLetters[i].Text = pic != null || p.Name.Length == 0 ? "" : p.Name.Substring(0, 1).ToUpper();
                faceRings[i].Opacity = p.Speaking && !pm ? 1 : 0;   // говорит — зелёное кольцо, как в Discord
                faceBadges[i].Visibility = pm || pd ? Visibility.Visible : Visibility.Collapsed;
                faceBadgeGlyphs[i].Text = pd ? "\uE7F6" : "\uE720";   // \u043F\u0435\u0440\u0435\u0447\u0451\u0440\u043A\u043D\u0443\u0442\u044B\u0435 \u043D\u0430\u0443\u0448\u043D\u0438\u043A\u0438 \u0438\u043B\u0438 \u043C\u0438\u043A\u0440\u043E\u0444\u043E\u043D
                faceSlots[i].ToolTip = (p.Name.Length > 0 ? p.Name : "Участник") + (p.Me ? " (вы)" : "") + (pd ? " · полный мут" : pm ? " · микрофон выключен" : p.Speaking ? " · говорит" : "") + (p.Stream ? " · показывает экран" : "");
            }
            callMore.Text = n > Faces ? "+" + (n - Faces) : "";

            bool music = media.Has;
            callDivider.Visibility = music ? Visibility.Visible : Visibility.Collapsed;
            callMusicRow.Visibility = music ? Visibility.Visible : Visibility.Collapsed;
            if (music)
            {
                if (callMusicTitle.Text != media.Title) callMusicTitle.Text = media.Title ?? "";
                if (callMusicArtist.Text != media.Artist) callMusicArtist.Text = media.Artist ?? "";
                Brush art = coverBrush != null ? (Brush)coverBrush : Chip;
                if (callMusicArt.Background != art) callMusicArt.Background = art;
                callMusicPlay.Visibility = media.Playing ? Visibility.Collapsed : Visibility.Visible;
                callMusicPause.Visibility = media.Playing ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // Аватарки — только с сервера картинок Discord, в памяти, пока Kapsula открыта
        readonly Dictionary<string, Brush> faceCache = new Dictionary<string, Brush>();
        readonly HashSet<string> faceLoading = new HashSet<string>();

        Brush FaceImage(string url) { return FaceImage(url, 64); }

        Brush FaceImage(string url, int px)
        {
            if (string.IsNullOrEmpty(url)) return null;
            Brush img;
            if (faceCache.TryGetValue(url, out img)) return img;
            if (!url.StartsWith("https://cdn.discordapp.com/") && !url.StartsWith("https://media.discordapp.net/")) return null;
            if (!faceLoading.Add(url)) return null;
            ThreadPool.QueueUserWorkItem(delegate
            {
                byte[] data = null;
                try
                {
                    System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
                    using (var wc = new System.Net.WebClient()) { wc.Headers["User-Agent"] = "Kapsula"; data = wc.DownloadData(url); }
                }
                catch { }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    faceLoading.Remove(url);
                    Brush bi = null;
                    if (data != null)
                        try
                        {
                            var b = new BitmapImage();
                            b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.DecodePixelWidth = px; b.StreamSource = new System.IO.MemoryStream(data); b.EndInit(); b.Freeze();
                            var brush = new ImageBrush(b) { Stretch = Stretch.UniformToFill };
                            brush.Freeze();
                            bi = brush;
                        }
                        catch { }
                    if (faceCache.Count > 200) faceCache.Clear();
                    faceCache[url] = bi;   // не вышло — запоминаем пустоту: будет буква на цветном круге, без новых попыток
                    PaintMute();
                    RingFaceReady(url);
                }));
            });
            return null;
        }
    }
}
