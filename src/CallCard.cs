// Kapsula — «остров» для Windows. Часть исходного кода: CallCard.cs
// Карточка звонка на «Главной» — в духе iPhone. Сверху звонок: зелёный кружок с трубкой (вокруг него расходится
// волна, когда в разговоре звучит голос), канал и сервер, зелёный таймер. Под ним круглые кнопки с подписями:
// микрофон, звук, устройства и красная «положить трубку»; включённая кнопка становится белой, как на iPhone.
// Если играет музыка, внизу компактный плеер — обложка, трек, «назад / пауза / вперёд», — а фон всей карточки
// становится мягким размытым отсветом обложки. Звонок и музыка в одной карточке, ничего не прячется.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WF = System.Windows.Forms;

namespace Kapsula
{
    partial class IslandWindow
    {
        static readonly SolidColorBrush AppleGreen = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
        static readonly SolidColorBrush AppleRed = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        static readonly SolidColorBrush GlassKey = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        static readonly SolidColorBrush Secondary = new SolidColorBrush(Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF));
        static readonly SolidColorBrush KeyDark = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E));
        static readonly Brush CallGlow = new RadialGradientBrush(Color.FromArgb(0x66, 0x30, 0xD1, 0x58), Color.FromArgb(0x00, 0x30, 0xD1, 0x58))
            { Center = new Point(0.12, 0.18), GradientOrigin = new Point(0.12, 0.18), RadiusX = 0.85, RadiusY = 1.3 };

        Rectangle callBackdrop;
        Ellipse callRing;
        ScaleTransform callRingScale;
        bool callRinging;
        TextBlock callTitle, callMusicTitle, callMusicArtist;
        readonly Border[] callKeys = new Border[4];
        readonly TextBlock[] callKeyGlyphs = new TextBlock[4];
        readonly FrameworkElement[] callKeyCells = new FrameworkElement[4];
        Border callMusic, callMusicArt;
        FrameworkElement callMusicPlay, callMusicPause;

        void EnsureCallCard()
        {
            if (homeCallCard != null || homeGrid == null) return;
            var root = new Grid();
            root.SizeChanged += delegate(object s, SizeChangedEventArgs e) { root.Clip = new RectangleGeometry(new Rect(e.NewSize), 21, 21); };
            // фон: размытая обложка, а без музыки — зелёный отсвет звонка; сверху — затемнение, чтобы читался текст
            callBackdrop = new Rectangle { Fill = CallGlow, Opacity = 0.6, Margin = new Thickness(-36), IsHitTestVisible = false, CacheMode = new BitmapCache() };
            root.Children.Add(callBackdrop);
            root.Children.Add(new Rectangle { IsHitTestVisible = false, Fill = new LinearGradientBrush(Color.FromArgb(0x30, 0, 0, 0), Color.FromArgb(0x9A, 0, 0, 0), 90) });

            var stack = new StackPanel { Margin = new Thickness(16, 12, 16, 12), VerticalAlignment = VerticalAlignment.Center };

            // 1. Звонок: значок с волной, канал и сервер, таймер
            var head = new Grid { Height = 42 };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var badge = new Grid { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center };
            callRingScale = new ScaleTransform(1, 1, 20, 20);
            callRing = new Ellipse { Stroke = AppleGreen, StrokeThickness = 2, Opacity = 0, RenderTransform = callRingScale, IsHitTestVisible = false };
            badge.Children.Add(callRing);
            badge.Children.Add(new Ellipse { Fill = AppleGreen });
            badge.Children.Add(new TextBlock { Text = ((char)0xE717).ToString(), FontFamily = Icons, FontSize = 17, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(badge);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 10, 0) };
            callTitle = new TextBlock { Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            callAppText = new TextBlock { Foreground = Secondary, FontSize = 12, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            names.Children.Add(callTitle); names.Children.Add(callAppText);
            Grid.SetColumn(names, 1); head.Children.Add(names);
            callTimeText = new TextBlock { Foreground = AppleGreen, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Documents.Typography.SetNumeralAlignment(callTimeText, FontNumeralAlignment.Tabular);   // цифры не прыгают, пока идёт время
            Grid.SetColumn(callTimeText, 2); head.Children.Add(callTimeText);
            stack.Children.Add(head);

            // 2. Круглые кнопки с подписями
            var keys = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            string[] glyphs = { "", "", "", "" };
            string[] labels = { "Микрофон", "Звук", "Устройства", "Выйти" };
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                var glyph = callKeyGlyphs[i] = new TextBlock { Text = glyphs[i], FontFamily = Icons, FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var key = callKeys[i] = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), Background = i == 3 ? (Brush)AppleRed : GlassKey, Child = glyph, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center };
                key.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; CallKey(k); };
                Fx.Press(key, 0.9);
                var cell = new StackPanel { Width = 72 };
                cell.Children.Add(key);
                cell.Children.Add(new TextBlock { Text = labels[i], Foreground = Secondary, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                callKeyCells[i] = cell;
                keys.Children.Add(cell);
            }
            callKeys[3].ToolTip = "Выйти из звонка";
            callKeys[2].ToolTip = "Микрофон и динамики";
            stack.Children.Add(keys);

            // 3. Музыка — компактный плеер внизу
            callMusic = new Border { CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)), Padding = new Thickness(6), Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            var mg = new Grid();
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            callMusicArt = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = Chip };
            mg.Children.Add(callMusicArt);
            var mt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
            callMusicTitle = new TextBlock { Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            callMusicArtist = new TextBlock { Foreground = Secondary, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            mt.Children.Add(callMusicTitle); mt.Children.Add(callMusicArtist);
            Grid.SetColumn(mt, 1); mg.Children.Add(mt);
            var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ctl.Children.Add(MusicKey(MediaShape("prev", 13, Brushes.White), 0xB1));
            callMusicPlay = MediaShape("play", 15, Brushes.White);
            callMusicPause = MediaShape("pause", 15, Brushes.White);
            var pp = new Grid();
            pp.Children.Add(callMusicPlay); pp.Children.Add(callMusicPause);
            ctl.Children.Add(MusicKey(pp, 0xB3));
            ctl.Children.Add(MusicKey(MediaShape("next", 13, Brushes.White), 0xB0));
            Grid.SetColumn(ctl, 2); mg.Children.Add(ctl);
            callMusic.Child = mg;
            stack.Children.Add(callMusic);

            root.Children.Add(stack);
            homeCallCard = new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Child = root, Visibility = Visibility.Collapsed };
            Grid.SetRow(homeCallCard, 0);
            homeGrid.Children.Add(homeCallCard);
            PaintMute();
        }

        Border MusicKey(FrameworkElement shape, byte command)
        {
            var b = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = shape };
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
        }

        // «Устройства»: микрофон и динамики в одном меню
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

        // Кнопка-переключатель: включённая — белая с тёмным значком, как на iPhone
        void CallKeyState(int i, bool on, string glyph, string tip)
        {
            callKeys[i].Background = on ? Brushes.White : GlassKey;
            callKeyGlyphs[i].Foreground = on ? KeyDark : Brushes.White;
            if (callKeyGlyphs[i].Text != glyph) callKeyGlyphs[i].Text = glyph;
            callKeys[i].ToolTip = tip;
        }

        // Обновить карточку: звонок, кнопки, музыку, волну вокруг значка (раз в полсекунды и при смене трека)
        void PaintMute()
        {
            if (homeCallCard == null) return;
            bool live = DiscordLive;
            bool deaf = live && dvDeaf;
            bool muted = live ? dvMute || dvDeaf : Audio.MicMuted();
            CallKeyState(0, muted, muted ? "" : "", muted ? "Микрофон выключен — нажмите, чтобы включить" : "Вас слышно — нажмите, чтобы выключить микрофон");
            CallKeyState(1, deaf, deaf ? "" : "", deaf ? "Полный мут — нажмите, чтобы вернуть звук" : "Полный мут: не слышать никого");
            callKeyCells[1].Visibility = live ? Visibility.Visible : Visibility.Collapsed;   // полный мут и выход — только для звонка Discord
            callKeyCells[3].Visibility = live ? Visibility.Visible : Visibility.Collapsed;
            callTitle.Text = live ? (dvChannel.Length > 0 ? dvChannel : "Личный звонок") : "Звонок";
            callAppText.Text = live ? (dvGuild.Length > 0 ? dvGuild + " · " : "") + (dvUsers > 1 ? Plural(dvUsers, "участник", "участника", "участников") : "Discord")
                : (callApp ?? "") + (muted ? " · микрофон выключен" : "");
            if (callTimeText != null && callApp != null) callTimeText.Text = CallTime();

            bool music = media.Has;
            callMusic.Visibility = music ? Visibility.Visible : Visibility.Collapsed;
            if (music)
            {
                if (callMusicTitle.Text != media.Title) callMusicTitle.Text = media.Title ?? "";
                if (callMusicArtist.Text != media.Artist) callMusicArtist.Text = media.Artist ?? "";
                Brush art = coverBrush != null ? (Brush)coverBrush : Chip;
                if (callMusicArt.Background != art) callMusicArt.Background = art;
                callMusicPlay.Visibility = media.Playing ? Visibility.Collapsed : Visibility.Visible;
                callMusicPause.Visibility = media.Playing ? Visibility.Visible : Visibility.Collapsed;
            }
            Brush back = music && coverBrush != null ? (Brush)coverBrush : CallGlow;
            if (callBackdrop.Fill != back)
            {
                callBackdrop.Fill = back;
                // обложку размываем в мягкий отсвет; зелёному отсвету размытие не нужно
                callBackdrop.Effect = back == CallGlow ? null : new BlurEffect { Radius = 44, RenderingBias = RenderingBias.Performance };
                callBackdrop.Opacity = back == CallGlow ? 0.75 : 0.6;
            }

            // волна вокруг значка звонка — пока в разговоре звучит голос
            bool voice = callApp != null && callVoiceAt != 0 && unchecked(Environment.TickCount - callVoiceAt) < 900 && !ReducedMotion;
            if (voice != callRinging)
            {
                callRinging = voice;
                if (voice)
                {
                    var dur = new Duration(TimeSpan.FromMilliseconds(1100));
                    var grow = new DoubleAnimation(1, 1.55, dur) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    callRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                    callRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                    callRing.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.85, 0, dur) { RepeatBehavior = RepeatBehavior.Forever });
                }
                else
                {
                    callRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    callRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    callRing.BeginAnimation(UIElement.OpacityProperty, null);
                    callRing.Opacity = 0;
                }
            }
        }
    }
}
