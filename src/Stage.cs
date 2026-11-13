// Kapsula — «остров» для Windows. Часть исходного кода: Stage.cs
//  • Мини-плеер «только текст песни»: когда для трека нашёлся текст, строки идут крупно во всю ширину,
//    сверху — маленькая обложка и название, снизу — тонкая полоса времени. Переключается кнопкой в самом
//    мини-плеере и в настройках.
//  • Экран яркости: меняется яркость экрана — остров показывает её полоской, как громкость. У ноутбуков
//    Windows сама сообщает о каждой смене; у внешних мониторов яркость меняется колёсиком над островом
//    с зажатым Ctrl (если монитор позволяет программам её менять).
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Kapsula
{
    partial class IslandWindow
    {
        // ================================================================ мини-плеер «только текст песни»
        public bool MiniLyricsOnly;
        Grid miniPlayer;            // обычный мини-плеер: обложка, полоса, кнопки, громкость
        Grid stageHost;
        LyricView stageLyrics;
        Border stageCover, stageBtn, lyricsBtn;
        TextBlock stageTitle;
        Bar stageProgress;
        const double StageW = 540;

        // Режим включён и текст есть (или новый ещё ищется — место под него держим, чтобы мини-плеер не прыгал)
        bool LyricsStage() { return MiniLyricsOnly && (lyrics != null || lyricsPending); }

        void BuildStage()
        {
            MiniLyricsOnly = Store.Get("miniLyricsOnly") == "1";
            stageHost = new Grid { Margin = new Thickness(20, 12, 20, 14), Visibility = Visibility.Collapsed };
            stageHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            stageHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            stageHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            stageCover = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = Chip, VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(stageCover);
            stageTitle = new TextBlock { Foreground = Dim, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(9, 0, 8, 1) };
            Grid.SetColumn(stageTitle, 1); head.Children.Add(stageTitle);
            stageBtn = MiniToggle(false);
            Grid.SetColumn(stageBtn, 2); head.Children.Add(stageBtn);
            stageHost.Children.Add(head);
            stageLyrics = new LyricView(StageW - 40, MH - 12 - 14 - 24 - 6, true, 0.26, Fg, 19, true);
            stageLyrics.Root.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(stageLyrics.Root, 1); stageHost.Children.Add(stageLyrics.Root);
            stageProgress = new Bar(StageW - 40, 3, accent, 6);
            stageProgress.Root.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetRow(stageProgress.Root, 2); stageHost.Children.Add(stageProgress.Root);
            miniHost.Children.Add(stageHost);
            // кнопка «только текст» — в углу колонки текста обычного мини-плеера
            lyricsBtn = MiniToggle(true);
            lyricsBtn.HorizontalAlignment = HorizontalAlignment.Right; lyricsBtn.VerticalAlignment = VerticalAlignment.Top;
            lyricsBtn.Margin = new Thickness(0, 7, 10, 0);
            lyricsBtn.Visibility = Visibility.Collapsed;
            miniHost.Children.Add(lyricsBtn);
        }

        Border MiniToggle(bool toStage)
        {
            var tb = new TextBlock { Text = ((char)(toStage ? 0xE8D2 : 0xE8D6)).ToString(), FontFamily = Icons, FontSize = 12, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Brushes.Transparent, Child = tb, Cursor = Cursors.Hand, ToolTip = toStage ? "Только текст песни" : "Обычный мини-плеер" };
            b.MouseEnter += delegate { b.Background = ChipHover; tb.Foreground = Fg; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; tb.Foreground = Dim; };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; SetMiniLyricsOnly(toStage); };   // клик не раскрывает панель
            Fx.Press(b, 0.88);
            return b;
        }

        public void SetMiniLyricsOnly(bool on)
        {
            MiniLyricsOnly = on;
            Store.Set("miniLyricsOnly", on ? "1" : "0");
            ApplyMiniMode(true);
        }

        void ApplyMiniMode(bool animate)
        {
            if (stageHost == null || miniPlayer == null) return;
            bool stage = LyricsStage(), hasLyr = lyrics != null;
            bool was = stageHost.Visibility == Visibility.Visible;
            miniPlayer.Visibility = stage ? Visibility.Collapsed : Visibility.Visible;
            stageHost.Visibility = stage ? Visibility.Visible : Visibility.Collapsed;
            miniLyrics.Root.Visibility = !stage && hasLyr ? Visibility.Visible : Visibility.Collapsed;
            lyricsBtn.Visibility = !stage && hasLyr ? Visibility.Visible : Visibility.Collapsed;
            if (animate && was != stage && !ReducedMotion)
            {
                // переход: новый вид проявляется и чуть всплывает
                FrameworkElement show = stage ? (FrameworkElement)stageHost : miniPlayer;
                var rise = new TranslateTransform(0, 8);
                show.RenderTransform = rise;
                rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = SlideEase });
                show.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = SlideEase });
                if (!stage && hasLyr) miniLyrics.Root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));
            }
            double w = MiniW();
            if (Math.Abs(miniHost.Width - w) > 0.5) { miniHost.Width = w; if (state == 1) SetTargets(); }
            if (stage && hasLyr) { try { stageLyrics.Root.UpdateLayout(); } catch { } UpdateLyrics(); }
        }

        // Название и обложка в шапке — вместе с остальным плеером
        void StageMedia(Brush cover)
        {
            if (stageTitle == null) return;
            stageTitle.Text = string.IsNullOrEmpty(media.Artist) ? media.Title : media.Title + " — " + media.Artist;
            if (cover != null) stageCover.Background = cover;
        }

        // ================================================================ шрифт острова
        // Готовые варианты — из шрифтов, которые есть в каждой Windows; «Другой…» — любой установленный.
        // Если в шрифте нет нужных букв (кириллицы, цифр), их подставляет Segoe UI.
        public static readonly string[] FontNames = { "Как в Windows", "Строгий", "С засечками", "Моноширинный", "Рукописный", "Другой…" };
        static readonly string[] FontFaces = { "", "Bahnschrift", "Georgia", "Cascadia Mono, Consolas", "Segoe Print" };
        const string FontDefault = "Segoe UI Variable Display, Segoe UI";

        static FontFamily FontFor(string face)
        {
            face = (face ?? "").Trim();
            return new FontFamily(face.Length > 0 ? face + ", Segoe UI" : FontDefault);
        }

        public string IslandFont { get { return Store.Get("font"); } }

        // Какой вариант выбран: 0…4 — готовые, 5 — свой
        public int FontChoice()
        {
            string f = IslandFont;
            int i = Array.IndexOf(FontFaces, f);
            return i >= 0 ? i : FontNames.Length - 1;
        }

        public string FontFace(int i) { return i >= 0 && i < FontFaces.Length ? FontFaces[i] : null; }

        public void ApplyFont(string face, bool save)
        {
            if (face == null) return;
            if (save) Store.Set("font", face.Trim());
            FontFamily = FontFor(face);
            RenderCompact();
            SetTargets();   // ширина надписей в капсуле могла поменяться
        }

        // ================================================================ экран яркости
        public bool BrightHud = true;
        System.Management.ManagementEventWatcher brightWatch;
        bool toastBright;                 // текущая надпись в капсуле — про яркость: зверёк щурится или таращит глаза
        int brightCache = -2;             // -2 — ещё не спрашивали, -1 — монитор не даёт менять яркость
        int brightTarget = -1, brightHintAt;
        bool brightBusy;
        readonly object brightLock = new object();

        void StartBrightnessWatch()
        {
            BrightHud = Store.Get("brightHud") != "0";
            if (!BrightHud || brightWatch != null) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var w = new System.Management.ManagementEventWatcher(new System.Management.ManagementScope(@"root\WMI"), new System.Management.WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                    w.EventArrived += delegate(object s, System.Management.EventArrivedEventArgs e)
                    {
                        int v;
                        try { v = Convert.ToInt32(e.NewEvent.Properties["Brightness"].Value); } catch { return; }
                        Dispatcher.BeginInvoke(new Action(delegate { BrightnessShown(v); }));
                    };
                    w.Start();
                    brightWatch = w;
                }
                catch { }   // настольный монитор: Windows о яркости не сообщает — остаётся колёсико с Ctrl
            });
        }

        void StopBrightnessWatch()
        {
            var w = brightWatch;
            brightWatch = null;
            if (w != null) try { w.Stop(); w.Dispose(); } catch { }
        }

        // Показать яркость в капсуле: значок солнца, полоса и проценты
        void BrightnessShown(int v)
        {
            brightCache = v;
            if (!BrightHud || state == 2 || state == 4) return;   // в панели яркость и так видна ползунком
            Toast(((char)0xE706).ToString(), null, v + "%", Math.Max(0, Math.Min(1, v / 100.0)), Amber);
            toastBright = true;
        }

        // Колёсико над островом с зажатым Ctrl: яркость на 5% за щелчок
        void BrightnessWheel(int step)
        {
            if (brightCache == -2) brightCache = Brightness.Get();   // первый раз спрашиваем монитор — это бывает небыстро
            if (brightCache < 0)
            {
                if (unchecked(Environment.TickCount - brightHintAt) > 4000)
                {
                    brightHintAt = Environment.TickCount;
                    Toast(((char)0xE706).ToString(), "Этот монитор не даёт менять яркость", null, -1, Dim);
                }
                return;
            }
            int v = Math.Max(0, Math.Min(100, brightCache + step));
            BrightnessShown(v);
            // монитор отвечает медленно: применяем только последнее значение, промежуточные шаги пропускаем
            lock (brightLock)
            {
                brightTarget = v;
                if (brightBusy) return;
                brightBusy = true;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                while (true)
                {
                    int t;
                    lock (brightLock) t = brightTarget;
                    Brightness.SetNow(t);
                    lock (brightLock) { if (t == brightTarget) { brightBusy = false; return; } }
                }
            });
        }
    }
}
