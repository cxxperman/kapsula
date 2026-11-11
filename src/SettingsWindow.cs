// Kapsula — «остров» для Windows. Часть исходного кода: SettingsWindow.cs
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WF = System.Windows.Forms;
using Windows.Media.Control;

namespace Kapsula
{
    // ============================================================= Настройки
    class SettingsWindow : Window
    {
        IslandWindow island;
        static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x18));
        static readonly Brush RowBg = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x23));
        static readonly Brush Txt = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6));
        static readonly Brush Sub = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96));
        static readonly Brush ChipBg = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x31));
        static readonly Brush OnBg = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6));
        static readonly Brush OnTxt = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x14));
        static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
        static readonly Color[] Palette =
        {
            Colors.Black, Color.FromRgb(0x1C, 0x1C, 0x1E), Color.FromRgb(0x0B, 0x1B, 0x33), Color.FromRgb(0x0C, 0x26, 0x1C),
            Color.FromRgb(0x2A, 0x0E, 0x16), Color.FromRgb(0x1D, 0x12, 0x33), IslandWindow.LightIsland
        };
        List<Border> swatches = new List<Border>();

        public SettingsWindow(IslandWindow owner)
        {
            island = owner;
            Title = "Kapsula — настройки";
            Width = 780; Height = Math.Min(640, SystemParameters.WorkArea.Height - 16);   // на небольших экранах окно не должно уходить за край
            Background = Bg;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            UseLayoutRounding = true;

            // Настройки разложены по разделам: слева список, справа — только выбранный раздел.
            StackPanel sp = Sec("Общее");
            // Версия и обновление — первой строкой, чтобы не искать
            var checkLabel = new TextBlock { Text = "Проверить обновления", Foreground = OnTxt, FontSize = 12.5, FontWeight = FontWeights.SemiBold };
            var checkBtn = new Border { Background = OnBg, CornerRadius = new CornerRadius(13), Padding = new Thickness(14, 6, 14, 7), Cursor = Cursors.Hand, Child = checkLabel };
            var verRow = Row("Kapsula " + Updater.CurrentText, "—", checkBtn, false);
            TextBlock verStatus = ((StackPanel)((Grid)((Border)verRow).Child).Children[0]).Children[1] as TextBlock;
            Action showStatus = delegate
            {
                string s = Updater.StatusText;
                if (!Updater.Configured) s = "Автообновление в этой сборке не настроено.";
                else if (s == "") s = Store.Get("autoUpdate") == "0" ? "Автообновление выключено. Проверить можно кнопкой." : "Обновления проверяются сами раз в час.";
                if (verStatus != null) verStatus.Text = s;
            };
            showStatus();
            var statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            statusTimer.Tick += delegate { showStatus(); };
            statusTimer.Start();
            Closed += delegate { statusTimer.Stop(); };
            checkBtn.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; if (verStatus != null) verStatus.Text = "Проверяю…"; Updater.CheckNow(); };
            sp.Children.Add(verRow);

            sp = Sec("Внешний вид");
            sp.Children.Add(Row("Цвет острова", "Наведите на цвет — остров сверху сразу покажет его. Клик сохраняет.", BuildSwatches(), true));
            sp.Children.Add(Row("Как в Windows", "Светлый остров при светлой теме Windows, тёмный — при тёмной.", Toggle(island.FollowWindows, delegate(bool v) { island.SetFlag("followWin", v); }), false));
            double[] scales = { 0.8, 1, 1.25, 1.5, 1.75, 2 };
            int si = 1;
            for (int i = 0; i < scales.Length; i++) if (Math.Abs(scales[i] - island.Scale) < 0.01) si = i;
            sp.Children.Add(Row("Размер", "Для больших мониторов.", Chips(new string[] { "80%", "100%", "125%", "150%", "175%", "200%" }, si, delegate(int i) { island.ApplyScale(scales[i]); }), true));

            sp.Children.Add(Row("Автоскрытие", "Остров уезжает за край и возвращается, когда курсор у верхней кромки экрана или есть что показать.", Toggle(island.AutoHide, delegate(bool v) { island.SetFlag("autoHide", v); }), false));
            sp.Children.Add(Row("Прятаться в программах", "Если остров закрывает вкладки или кнопки какой-то программы, добавьте её сюда: пока она активна, остров уезжает за край и возвращается, когда курсор у верхней кромки или есть что показать. В играх и видео на весь экран он прячется и так.", HideAppsEditor(), true));
            int ci = Math.Max(0, Array.IndexOf(IslandWindow.CharacterIds, island.Character));
            sp.Children.Add(Row("Персонаж", "Кто живёт в капсуле. Он следит за курсором, задумывается, пока отвечает нейросеть, пугается таймера и радуется, когда что-то получилось.", Chips(IslandWindow.CharacterNames, ci, delegate(int i) { Store.Set("character", IslandWindow.CharacterIds[i]); island.BuildCharacter(); }), true));
            sp.Children.Add(Row("Показывать персонажа", "Если выключить, в капсуле останутся только часы и погода.", Toggle(island.ShowEyes, delegate(bool v) { island.SetFlag("eyes", v); }), false));
            if (island.MonitorCount > 1)
            {
                var names = new string[island.MonitorCount];
                for (int i = 0; i < names.Length; i++) names[i] = "Монитор " + (i + 1);
                Sec("Общее").Children.Add(Row("Монитор", "На каком экране живёт остров.", Chips(names, Math.Max(0, Math.Min(names.Length - 1, island.MonitorIndex)), delegate(int i) { island.SetMonitor(i); }), true));
            }

            sp = Sec("Главная");
            sp.Children.Add(new TextBlock { Foreground = Sub, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 0, 8), Text = "Уберите то, чем не пользуетесь, — остров перестроится и станет меньше." });
            sp.Children.Add(Row("Плеер", null, Toggle(Store.Get("homePlayer") != "0", delegate(bool v) { island.SetFlag("homePlayer", v); island.ApplyHomeLayout(); }), false));
            var tilesOff = new HashSet<string>(Store.Get("homeTilesOff").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            var tilesOn = new bool[IslandWindow.HomeTileIds.Length];
            for (int i = 0; i < tilesOn.Length; i++) tilesOn[i] = !tilesOff.Contains(IslandWindow.HomeTileIds[i]);
            sp.Children.Add(Row("Быстрые действия", "Какие плитки показывать.", MultiChips(IslandWindow.HomeTileNames, tilesOn, delegate(int i, bool v)
            {
                if (v) tilesOff.Remove(IslandWindow.HomeTileIds[i]); else tilesOff.Add(IslandWindow.HomeTileIds[i]);
                Store.Set("homeTilesOff", string.Join(",", new List<string>(tilesOff).ToArray()));
                island.ApplyHomeLayout();
            }), true));
            sp.Children.Add(Row("Громкость", null, Toggle(Store.Get("homeVolume") != "0", delegate(bool v) { island.SetFlag("homeVolume", v); island.ApplyHomeLayout(); }), false));
            sp.Children.Add(Row("Ряд программ", "Программы, закреплённые на панели задач.", Toggle(Store.Get("homeLauncher") != "0", delegate(bool v) { island.SetFlag("homeLauncher", v); island.ApplyHomeLayout(); }), false));

            sp = Sec("Музыка");
            sp.Children.Add(Row("Смена трека поверх игр и видео", "В полноэкранной программе остров спрятан. С этой настройкой он выглядывает на пару секунд с названием нового трека.", Toggle(Store.Get("fsTrack") != "0", delegate(bool v) { island.SetFlag("fsTrack", v); }), false));
            sp.Children.Add(Row("Капсула во время музыки", "Что показывать в свёрнутом виде. «Строка песни» работает, когда для трека нашёлся текст; иначе показывается название.", Chips(new string[] { "Часы и погода", "Название", "Строка песни", "Полоса трека" }, island.PillMode, delegate(int i) { island.SetPillMode(i); }), true));
            sp.Children.Add(Row("Плеер на «Главной»", "«Компактный» — обложка слева. «Крупная обложка» — обложка по центру, название под ней. «В цвет обложки» — весь плеер залит её цветом.", Chips(new string[] { "Компактный", "Крупная обложка", "В цвет обложки" }, island.PlayerStyle, delegate(int i) { island.SetPlayerStyle(i); }), true));
            sp.Children.Add(Row("Мини-плеер при наведении", "Во время музыки сначала открывается мини-плеер, клик по нему — вся панель.", Toggle(island.HoverMini, delegate(bool v) { island.SetFlag("hoverMini", v); }), false));
            sp.Children.Add(Row("Жидкий край", "Нижний край пляшет под звук.", Toggle(island.LiquidOn, delegate(bool v) { island.SetFlag("liquid", v); }), false));
            sp.Children.Add(Row("Ползунок громкости на «Главной»", "«Только плеер» меняет громкость одной программы, в которой играет музыка, — остальной звук не трогает. Переключается и кнопкой слева от ползунка.", Chips(new string[] { "Весь звук", "Только плеер" }, Store.Get("volPlayer") == "1" ? 1 : 0, delegate(int i) { Store.Set("volPlayer", i == 1 ? "1" : "0"); }), true));
            sp.Children.Add(Row("Край как эквалайзер", "Слева бас, справа высокие частоты. Выключено — край пляшет от общей громкости.", Toggle(Store.Get("liquidSpectrum") != "0", delegate(bool v) { Store.Set("liquidSpectrum", v ? "1" : "0"); }), false));
            sp.Children.Add(Row("Тексты песен", "Загружаются с lrclib.net для играющего трека.", Toggle(island.LyricsEnabled, delegate(bool v) { island.SetFlag("lyrics", v); }), false));

            double[] offs = { -1, -0.5, 0, 0.5, 1 };
            int oi = 2;
            for (int i = 0; i < offs.Length; i++) if (Math.Abs(offs[i] - island.LyricOffset) < 0.01) oi = i;
            sp.Children.Add(Row("Сдвиг текста песни", "Если строки отстают от звука — сдвиньте вперёд, если спешат — назад.", Chips(new string[] { "−1 с", "−0,5 с", "Без сдвига", "+0,5 с", "+1 с" }, oi, delegate(int i) { island.SetLyricOffset(offs[i]); }), true));

            sp = Sec("Чат с Claude");
            sp.Children.Add(Row("Ключ Claude API", "Нужен свой ключ: console.anthropic.com → API Keys. Он хранится только на этом компьютере, зашифрованный Windows, и отправляется только на api.anthropic.com.", BuildKeyEditor(), true));
            sp.Children.Add(Row("Модель", "Opus 5.5 — умнее; Sonnet 5.5 — быстрее и дешевле.", Chips(new string[] { "Opus 5.5", "Sonnet 5.5" }, ClaudeChat.Model() == "claude-sonnet-5-5" ? 1 : 0, delegate(int i) { island.SetClaudeModel(i == 1 ? "claude-sonnet-5-5" : "claude-opus-5-5"); }), true));

            sp = Sec("Steam и Discord");
            if (DiscordBridge.VencordFound)
            {
                var dRow = Row("Discord через Vencord", "—", Toggle(Store.Get("discordVencord") != "0", delegate(bool v) { island.SetFlag("discordVencord", v); if (v) DiscordBridge.Start(); else DiscordBridge.Stop(); }), false);
                var dText = ((StackPanel)((Grid)((Border)dRow).Child).Children[0]).Children[1] as TextBlock;
                Action dShow = delegate
                {
                    string st = Store.Get("discordVencord") == "0" ? "Выключено."
                        : DiscordBridge.Error != null ? "Не работает: " + DiscordBridge.Error + "."
                        : DiscordBridge.Clients > 0 ? "Подключено: сообщения и звонки Discord появляются на острове."
                        : "Ждёт уведомлений: Discord подключается сам, когда приходит сообщение. Если карточек нет — в Vencord откройте Настройки → Плагины и включите XSOverlay.";
                    if (dText != null) dText.Text = "Kapsula только принимает уведомления; в Discord ничего не меняется. " + st;
                };
                dShow();
                var dTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                dTimer.Tick += delegate { dShow(); };
                dTimer.Start();
                Closed += delegate { dTimer.Stop(); };
                sp.Children.Add(dRow);
            }
            else
            {
                sp.Children.Add(Row("Discord через Vencord", "Сообщения и звонки Discord на острове. Нужен Vencord — неофициальная модификация Discord; ставить её или нет, решаете вы.", LinkButton("Сайт Vencord", "https://vencord.dev/"), false));
            }
            if (Steam.Found)
                sp.Children.Add(Row("Steam", "Игры в командной строке, карточка «игра готова» после загрузки, время игровой сессии. Данные берутся из папки Steam на этом компьютере — без входа в аккаунт.", Toggle(Store.Get("steam") != "0", delegate(bool v) { island.SetFlag("steam", v); }), false));

            if (Steam.Found && !SteamBridge.MillenniumFound)
                sp.Children.Add(Row("Уведомления Steam", "Сообщения друзей, «друг в игре» и другие уведомления Steam на острове. Нужен Millennium — неофициальная модификация клиента Steam; ставить её или нет, решаете вы.", LinkButton("Сайт Millennium", "https://steambrew.app/"), false));
            else if (Steam.Found)
            {
                var sLabel = new TextBlock { Foreground = Txt, FontSize = 12.5, FontWeight = FontWeights.SemiBold };
                var sBtn = new Border { Background = ChipBg, CornerRadius = new CornerRadius(13), Padding = new Thickness(14, 6, 14, 7), Cursor = Cursors.Hand, Child = sLabel };
                var sRow = Row("Уведомления Steam", "—", sBtn, false);
                var sText = ((StackPanel)((Grid)((Border)sRow).Child).Children[0]).Children[1] as TextBlock;
                string sNote = "";
                Action sShow = delegate
                {
                    bool have = SteamBridge.PluginInstalled;
                    sLabel.Text = have ? "Удалить" : "Подключить";
                    string st = !have ? "Kapsula положит свой плагин в папку Millennium. В интернет ничего не уходит."
                        : SteamBridge.Error != null ? "Не работает: " + SteamBridge.Error + "."
                        : SteamBridge.Clients > 0 ? "Подключено (плагин " + SteamBridge.InstalledVersion + ")."
                        : "Плагин установлен, но Steam не подключился. В Steam откройте Millennium → Плагины, включите Kapsula и перезапустите Steam.";
                    if (sText != null) sText.Text = "Сообщения друзей, «друг в игре» и другие уведомления Steam — карточками на острове. " + st + sNote;
                };
                sBtn.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (SteamBridge.PluginInstalled) sNote = SteamBridge.RemovePlugin() ? " Плагин удалён; он выгрузится при следующем запуске Steam." : " Не удалось удалить папку плагина.";
                    else sNote = SteamBridge.InstallPlugin() ? "" : " Не удалось записать в папку Millennium.";
                    sShow();
                };
                sShow();
                var sTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                sTimer.Tick += delegate { sShow(); };
                sTimer.Start();
                Closed += delegate { sTimer.Stop(); };
                sp.Children.Add(sRow);
                sp.Children.Add(Row("Не дублировать уведомления Steam", "Когда уведомление показал остров, окошко Steam справа снизу не появляется (звук остаётся). В игре Steam показывает уведомления как обычно.", Toggle(Store.Get("steamHideToasts") != "0", delegate(bool v) { island.SetFlag("steamHideToasts", v); SteamBridge.SendConfig(); }), false));
            }

            Sec("Внешний вид").Children.Add(Row("Секунды в часах", null, Toggle(island.ShowSeconds, delegate(bool v) { island.SetFlag("seconds", v); }), false));
            sp = Sec("Запись и снимки");
            string ra = Store.Get("recAudio");
            sp.Children.Add(Row("Звук в записи экрана", "Звук компьютера — то, что играет в колонках или наушниках: видео, игра, звонок. Микрофон добавляется поверх, если выбран третий вариант.", Chips(new string[] { "Без звука", "Звук компьютера", "Компьютер и микрофон" }, ra == "0" ? 0 : (ra == "2" ? 2 : 1), delegate(int i) { Store.Set("recAudio", i.ToString()); }), true));
            sp.Children.Add(Row("Показывать остров на скриншотах", "По умолчанию он скрыт от снимков, записи экрана и трансляций.", Toggle(island.ShowInCapture, delegate(bool v) { island.SetFlag("showInCapture", v); }), false));
            sp = Sec("Общее");
            sp.Children.Add(Row("Запускать вместе с Windows", null, Toggle(Autostart.IsOn(), delegate(bool v) { Autostart.Set(v); }), false));
            sp.Children.Add(Row("Автообновление", "Kapsula сама скачивает новые версии с GitHub и сверяет контрольную сумму файла. Сейчас установлена версия " + Updater.CurrentText + ".", Toggle(Store.Get("autoUpdate") != "0", delegate(bool v) { island.SetFlag("autoUpdate", v); }), false));

            sp = Sec("Уведомления");
            sp.Children.Add(Row("Показывать смену раскладки", "Короткая подсказка «Раскладка: RU» при переключении языка.", Toggle(island.ShowLayout, delegate(bool v) { island.SetFlag("layout", v); }), false));
            sp.Children.Add(Row("Уведомления Windows на острове", "Новые уведомления других программ показываются карточкой. Нигде не сохраняются.", Toggle(island.ShowNotifications, delegate(bool v) { island.SetFlag("notifications", v); }), false));

            sp = Sec("Клавиши");
            sp.Children.Add(new TextBlock { Foreground = Sub, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 0, 8), Text = "Нажмите на сочетание и введите новое. Backspace отключает, правый клик возвращает исходное. Красным отмечены сочетания, занятые другой программой." });
            foreach (Hot hk in island.Hots) sp.Children.Add(Row(hk.Name, null, HotChip(hk), false));
            PreviewKeyDown += OnCaptureKey;
            Closed += delegate { if (capturing != null) { capturing = null; island.SuspendHotkeys(false); } };
            BuildLayout();
        }

        // ---- разделы настроек
        static readonly string[] SectionOrder = { "Общее", "Внешний вид", "Главная", "Музыка", "Уведомления", "Steam и Discord", "Запись и снимки", "Чат с Claude", "Клавиши" };
        static readonly string[] SectionGlyphs = { "", "", "", "", "", "", "", "", "" };
        static readonly string[] SectionHints =
        {
            "Версия, обновления, запуск",
            "Цвет, размер, персонаж",
            "Что показывать на главной вкладке",
            "Плеер, тексты песен, жидкий край",
            "Что и когда показывает остров",
            "Игры, сообщения и уведомления",
            "Звук в записи, скрытие острова",
            "Ключ и модель",
            "Сочетания клавиш"
        };
        Dictionary<string, StackPanel> sections = new Dictionary<string, StackPanel>();
        List<Border> navItems = new List<Border>();
        TextBlock paneTitle, paneHint;
        string currentSection;

        // Панель раздела по названию: строки настроек добавляются в неё
        StackPanel Sec(string name)
        {
            StackPanel p;
            if (!sections.TryGetValue(name, out p)) { p = new StackPanel(); sections[name] = p; }
            return p;
        }

        void BuildLayout()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var nav = new StackPanel { Margin = new Thickness(12, 16, 8, 12) };
            nav.Children.Add(new TextBlock { Text = "Kapsula", Foreground = Txt, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 2, 0, 0) });
            nav.Children.Add(new TextBlock { Text = "версия " + Updater.CurrentText, Foreground = Sub, FontSize = 11.5, Margin = new Thickness(12, 1, 0, 14) });
            for (int i = 0; i < SectionOrder.Length; i++)
            {
                string name = SectionOrder[i];
                if (!sections.ContainsKey(name) || sections[name].Children.Count == 0) continue;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = SectionGlyphs[i], FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, Foreground = Txt, Width = 26, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = name, Foreground = Txt, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var item = new Border { Height = 38, CornerRadius = new CornerRadius(11), Padding = new Thickness(12, 0, 10, 0), Margin = new Thickness(0, 0, 0, 3), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = row, Tag = name };
                item.MouseLeftButtonDown += delegate { ShowSection(name); };
                item.MouseEnter += delegate { if ((string)item.Tag != currentSection) item.Background = ChipBg; };
                item.MouseLeave += delegate { if ((string)item.Tag != currentSection) item.Background = Brushes.Transparent; };
                navItems.Add(item);
                nav.Children.Add(item);
            }
            root.Children.Add(new Border { Background = RowBg, Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = nav } });

            var pane = new DockPanel { Margin = new Thickness(22, 16, 6, 0) };
            var head = new StackPanel { Margin = new Thickness(4, 0, 0, 12) };
            paneTitle = new TextBlock { Foreground = Txt, FontSize = 20, FontWeight = FontWeights.SemiBold };
            paneHint = new TextBlock { Foreground = Sub, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
            head.Children.Add(paneTitle); head.Children.Add(paneHint);
            DockPanel.SetDock(head, Dock.Top);
            pane.Children.Add(head);
            FrameworkElement pv = BuildPreview();
            DockPanel.SetDock(pv, Dock.Top);
            pane.Children.Add(pv);
            scroller =new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 16, 16) };
            pane.Children.Add(scroller);
            Grid.SetColumn(pane, 1);
            root.Children.Add(pane);
            Content = root;

            string last = Store.Get("settingsTab");
            ShowSection(sections.ContainsKey(last) && sections[last].Children.Count > 0 ? last : "Общее");
        }

        // Превью: живая копия острова прямо в окне настроек. В разделе «Главная» показывает главную панель,
        // в «Музыке» — мини-плеер, в остальных — сам остров как он сейчас выглядит на экране.
        const double PvH = 150, PvHomeH = 210;
        FrameworkElement pvIsland, pvHome, pvMini;
        Border pvHomeBox, pvBox;
        TextBlock pvCaption;
        DispatcherTimer pvTimer;

        FrameworkElement BuildPreview()
        {
            var bg = new LinearGradientBrush(Color.FromRgb(0x3A, 0x45, 0x63), Color.FromRgb(0x27, 0x2C, 0x40), 90);
            var grid = new Grid { ClipToBounds = true };

            var mirror = new VisualBrush(island.PreviewRoot) { Stretch = Stretch.None, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(IslandWindow.PreviewBaseW / 2 - 260, 0, 520, PvH) };
            pvIsland = new Rectangle { Fill = mirror, Width = 520, Height = PvH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            grid.Children.Add(pvIsland);

            pvHomeBox = new Border { Width = IslandWindow.PreviewEW, Height = island.PreviewHomeH, Background = island.PreviewInk, CornerRadius = new CornerRadius(IslandWindow.PreviewER), ClipToBounds = true,
                Child = new Rectangle { Width = IslandWindow.PreviewEW, Height = IslandWindow.PreviewEH, VerticalAlignment = VerticalAlignment.Top, Fill = new VisualBrush(island.PreviewHome) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, IslandWindow.PreviewEW, IslandWindow.PreviewEH) } } };
            pvHome = new Viewbox { Height = PvHomeH - 16, Child = pvHomeBox, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            grid.Children.Add(pvHome);

            var miniBox = new Border { Width = IslandWindow.PreviewMW, Height = IslandWindow.PreviewMH, Background = island.PreviewInk, CornerRadius = new CornerRadius(IslandWindow.PreviewMR), ClipToBounds = true,
                Child = new Rectangle { Fill = new VisualBrush(island.PreviewMini) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, IslandWindow.PreviewMW, IslandWindow.PreviewMH) } } };
            pvMini = new Viewbox { Height = PvH - 40, Child = miniBox, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            grid.Children.Add(pvMini);

            pvCaption = new TextBlock { Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), FontSize = 11, Margin = new Thickness(12, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
            grid.Children.Add(pvCaption);

            pvTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            pvTimer.Tick += delegate { UpdatePreview(); };
            pvTimer.Start();
            Closed += delegate { pvTimer.Stop(); };
            pvBox = new Border { Height = PvH, CornerRadius = new CornerRadius(14), Background = bg, Margin = new Thickness(0, 0, 16, 12), Child = grid, ClipToBounds = true };
            return pvBox;
        }

        void UpdatePreview()
        {
            if (pvIsland == null) return;
            int kind = currentSection == "Главная" ? 1 : (currentSection == "Музыка" && island.PreviewHasMedia ? 2 : 0);
            pvIsland.Visibility = kind == 0 ? Visibility.Visible : Visibility.Collapsed;
            pvHome.Visibility = kind == 1 ? Visibility.Visible : Visibility.Collapsed;
            pvMini.Visibility = kind == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (kind == 1) { island.PreviewRefresh(); pvHomeBox.Height = island.PreviewHomeH; }
            pvBox.Height = kind == 1 ? PvHomeH : PvH;
            pvCaption.Text = kind == 1 ? "Главная" : kind == 2 ? "Мини-плеер" : "Остров сейчас";
        }

        public void ShowSection(string name)
        {
            if (!sections.ContainsKey(name)) return;
            currentSection = name;
            UpdatePreview();
            int idx = Array.IndexOf(SectionOrder, name);
            paneTitle.Text = name;
            paneHint.Text = idx >= 0 ? SectionHints[idx] : "";
            scroller.Content = sections[name];
            scroller.ScrollToTop();
            foreach (Border b in navItems) b.Background = (string)b.Tag == name ? ChipBg : Brushes.Transparent;
            Store.Set("settingsTab", name);
        }

        // Кнопка, открывающая страницу в браузере. Сама Kapsula ничего оттуда не скачивает и не ставит.
        FrameworkElement LinkButton(string text, string url)
        {
            var b = new Border { Background = ChipBg, CornerRadius = new CornerRadius(13), Padding = new Thickness(14, 6, 14, 7), Cursor = Cursors.Hand, Child = new TextBlock { Text = text, Foreground = Txt, FontSize = 12.5, FontWeight = FontWeights.SemiBold } };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; try { System.Diagnostics.Process.Start(url); } catch { } };
            return b;
        }

        FrameworkElement Header(string text)
        {
            return new TextBlock { Text = text, Foreground = Sub, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 12, 0, 8) };
        }

        FrameworkElement Row(string title, string desc, FrameworkElement control, bool below)
        {
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = title, Foreground = Txt, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(desc)) texts.Children.Add(new TextBlock { Text = desc, Foreground = Sub, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            FrameworkElement body;
            if (below)
            {
                var col = new StackPanel();
                col.Children.Add(texts);
                control.Margin = new Thickness(0, 10, 0, 0);
                control.HorizontalAlignment = HorizontalAlignment.Left;
                col.Children.Add(control);
                body = col;
            }
            else
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(texts);
                control.VerticalAlignment = VerticalAlignment.Center;
                control.Margin = new Thickness(16, 0, 0, 0);
                Grid.SetColumn(control, 1);
                g.Children.Add(control);
                body = g;
            }
            return new Border { Background = RowBg, CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8), Child = body };
        }

        FrameworkElement Toggle(bool on, Action<bool> changed)
        {
            bool state = on;
            var knob = new Ellipse { Width = 18, Height = 18, Fill = Brushes.White, Margin = new Thickness(3), HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            var b = new Border { Width = 44, Height = 24, CornerRadius = new CornerRadius(12), Background = state ? Green : ChipBg, Child = knob, Cursor = Cursors.Hand };
            b.MouseLeftButtonDown += delegate
            {
                state = !state;
                knob.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                b.Background = state ? Green : ChipBg;
                changed(state);
            };
            return b;
        }

        FrameworkElement Chips(string[] labels, int selected, Action<int> pick)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var chips = new List<Border>();
            Action<int> paint = delegate(int sel)
            {
                for (int i = 0; i < chips.Count; i++)
                {
                    chips[i].Background = i == sel ? OnBg : ChipBg;
                    ((TextBlock)chips[i].Child).Foreground = i == sel ? OnTxt : Txt;
                }
            };
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var c = new Border { Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 0, 14, 1), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand, Child = new TextBlock { Text = labels[i], FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } };
                c.MouseLeftButtonDown += delegate { paint(idx); pick(idx); };
                chips.Add(c);
                row.Children.Add(c);
            }
            paint(selected);
            return row;
        }

        // Несколько переключателей в ряд(ы): каждый включается и выключается отдельно
        FrameworkElement MultiChips(string[] labels, bool[] on, Action<int, bool> changed)
        {
            var wrap = new WrapPanel { MaxWidth = 470 };
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                bool state = on[i];
                var tb = new TextBlock { Text = labels[i], FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                var c = new Border { Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 0, 14, 1), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, Child = tb };
                Action paint = delegate { c.Background = state ? OnBg : ChipBg; tb.Foreground = state ? OnTxt : Txt; };
                c.MouseLeftButtonDown += delegate { state = !state; paint(); changed(idx, state); };
                paint();
                wrap.Children.Add(c);
            }
            return wrap;
        }

        // Список программ, в которых остров прячется: выбранные — плашками (клик убирает), «Добавить» — из открытых сейчас.
        FrameworkElement HideAppsEditor()
        {
            var wrap = new WrapPanel { MaxWidth = 470 };
            Action fill = null;
            fill = delegate
            {
                wrap.Children.Clear();
                List<string> apps = IslandWindow.HideApps();
                foreach (string a in apps)
                {
                    string name = a;
                    var tb = new TextBlock { Text = name + "  ✕", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = OnTxt, VerticalAlignment = VerticalAlignment.Center };
                    var c = new Border { Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 0, 12, 1), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, Background = OnBg, Child = tb, ToolTip = "Убрать из списка" };
                    c.MouseLeftButtonDown += delegate { List<string> cur = IslandWindow.HideApps(); cur.RemoveAll(delegate(string x) { return string.Equals(x, name, StringComparison.OrdinalIgnoreCase); }); IslandWindow.SetHideApps(cur); fill(); };
                    wrap.Children.Add(c);
                }
                var addText = new TextBlock { Text = "+ Добавить программу", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Txt, VerticalAlignment = VerticalAlignment.Center };
                var add = new Border { Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 0, 14, 1), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, Background = ChipBg, Child = addText };
                add.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    // Программы, у которых сейчас открыто окно. Названия окон никуда не сохраняются — только имя программы.
                    var menu = new ContextMenu();
                    var seen = new List<string>(IslandWindow.HideApps());
                    seen.Add("Kapsula");
                    try
                    {
                        foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcesses())
                        {
                            string pn, title;
                            try { if (p.MainWindowHandle == IntPtr.Zero) continue; pn = p.ProcessName; title = p.MainWindowTitle ?? ""; } catch { continue; }
                            if (title.Length == 0 || seen.Exists(delegate(string x) { return string.Equals(x, pn, StringComparison.OrdinalIgnoreCase); })) continue;
                            seen.Add(pn);
                            string shown = title.Length > 46 ? title.Substring(0, 45) + "…" : title;
                            var mi = new MenuItem { Header = pn + "  —  " + shown };
                            mi.Click += delegate { List<string> cur = IslandWindow.HideApps(); cur.Add(pn); IslandWindow.SetHideApps(cur); fill(); };
                            menu.Items.Add(mi);
                        }
                    }
                    catch { }
                    if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "Нет открытых программ", IsEnabled = false });
                    menu.PlacementTarget = add;
                    menu.Placement = PlacementMode.Bottom;
                    menu.IsOpen = true;
                };
                wrap.Children.Add(add);
            };
            fill();
            return wrap;
        }

        ScrollViewer scroller;
        public void ScrollToBottom() { Loaded += delegate { scroller.ScrollToEnd(); }; }

        // ---- редактор горячих клавиш
        Hot capturing;
        TextBlock capturingLabel;
        Dictionary<Hot, TextBlock> hotLabels = new Dictionary<Hot, TextBlock>();
        static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x62));

        void PaintHot(Hot h)
        {
            TextBlock tb = hotLabels[h];
            tb.Text = IslandWindow.HotText(h.Mods, h.Vk) + (h.Ok ? "" : " — занято");
            tb.Foreground = h.Ok ? (h.Vk == 0 ? Sub : Txt) : Red;
        }

        FrameworkElement HotChip(Hot h)
        {
            var tb = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            hotLabels[h] = tb;
            var chip = new Border { Height = 30, MinWidth = 120, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 0, 14, 1), Background = ChipBg, Cursor = Cursors.Hand, Child = tb };
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            chip.MouseLeftButtonDown += delegate
            {
                if (capturing != null) PaintHot(capturing);
                capturing = h; capturingLabel = tb;
                tb.Text = "Нажмите сочетание…"; tb.Foreground = Sub;
                // На время ввода снимаем все сочетания острова, иначе нажатие сработало бы как команда.
                island.SuspendHotkeys(true);
                Focus();
            };
            chip.MouseRightButtonDown += delegate
            {
                if (capturing != null) { capturing = null; island.SuspendHotkeys(false); }
                island.SetHotkey(h, h.DefMods, h.DefVk);
                PaintHot(h);
            };
            PaintHot(h);
            return chip;
        }

        void OnCaptureKey(object sender, KeyEventArgs e)
        {
            if (capturing == null) return;
            e.Handled = true;
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift || k == Key.LWin || k == Key.RWin) return;
            Hot h = capturing;
            if (k == Key.Escape) { capturing = null; island.SuspendHotkeys(false); PaintHot(h); return; }
            uint mods = 0;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= 0x1;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= 0x2;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= 0x4;
            if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) mods |= 0x8;
            capturing = null;
            island.SuspendHotkeys(false);
            if (k == Key.Back || k == Key.Delete) island.SetHotkey(h, 0, 0);
            else if ((mods & 0xB) == 0) { capturingLabel.Text = "Нужен Ctrl, Alt или Win"; capturingLabel.Foreground = Red; return; }
            else island.SetHotkey(h, mods, (uint)KeyInterop.VirtualKeyFromKey(k));
            PaintHot(h);
        }

        // Поле для ключа: вводит его сам пользователь; обратно ключ никогда не показывается.
        FrameworkElement BuildKeyEditor()
        {
            var col = new StackPanel();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var pw = new PasswordBox { Width = 250, Height = 30, Background = ChipBg, Foreground = Txt, BorderThickness = new Thickness(0), Padding = new Thickness(10, 5, 10, 0), CaretBrush = Txt, FontSize = 13 };
            row.Children.Add(pw);
            var status = new TextBlock { Foreground = Sub, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            Action paint = delegate
            {
                status.Text = Secrets.Saved ? "Ключ сохранён." : (Secrets.GetKey() != "" ? "Используется ключ из переменной среды ANTHROPIC_API_KEY." : "Ключ не задан — чат не работает.");
            };
            var save = new Border { Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 0, 14, 1), Margin = new Thickness(8, 0, 0, 0), Background = OnBg, Cursor = Cursors.Hand, Child = new TextBlock { Text = "Сохранить", Foreground = OnTxt, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } };
            save.MouseLeftButtonDown += delegate
            {
                if (pw.Password.Trim().Length == 0) return;
                island.SaveClaudeKey(pw.Password);
                pw.Clear();
                paint();
            };
            row.Children.Add(save);
            var del = new Border { Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 0, 14, 1), Margin = new Thickness(6, 0, 0, 0), Background = ChipBg, Cursor = Cursors.Hand, Child = new TextBlock { Text = "Удалить", Foreground = Txt, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } };
            del.MouseLeftButtonDown += delegate { island.SaveClaudeKey(""); pw.Clear(); paint(); };
            row.Children.Add(del);
            col.Children.Add(row);
            col.Children.Add(status);
            paint();
            return col;
        }

        void PaintSwatches()
        {
            foreach (Border r in swatches)
                r.BorderBrush = r.Tag is Color && (Color)r.Tag == island.IslandColor ? Brushes.White : Brushes.Transparent;
        }

        FrameworkElement BuildSwatches()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (Color pc in Palette)
            {
                Color c = pc;
                var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(c), BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x50)), BorderThickness = new Thickness(1) };
                var ring = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Child = dot, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0), Tag = c };
                // Наведение показывает цвет на самом острове, уход курсора возвращает сохранённый.
                ring.MouseEnter += delegate { island.ApplyTheme(c); };
                ring.MouseLeave += delegate { island.ApplyEffectiveTheme(); };
                ring.MouseLeftButtonDown += delegate { island.SetColor(c); PaintSwatches(); };
                swatches.Add(ring);
                row.Children.Add(ring);
            }
            var custom = new Border { Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 0, 14, 1), Margin = new Thickness(8, 0, 0, 0), Background = ChipBg, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "Свой…", Foreground = Txt, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } };
            custom.MouseLeftButtonDown += delegate
            {
                using (var dlg = new WF.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(island.IslandColor.R, island.IslandColor.G, island.IslandColor.B) })
                {
                    if (dlg.ShowDialog() == WF.DialogResult.OK) { island.SetColor(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B)); PaintSwatches(); }
                }
            };
            row.Children.Add(custom);
            PaintSwatches();
            return row;
        }
    }
}
