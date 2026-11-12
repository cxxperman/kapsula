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
    // ============================================================= Первый запуск
    // Короткое окно, которое один раз объясняет новому человеку, что это за капсула вверху экрана и что с ней делать.
    class WelcomeWindow : Window
    {
        static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x18));
        static readonly Brush RowBg = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x23));
        static readonly Brush Txt = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6));
        static readonly Brush Sub = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96));
        static readonly Brush ChipBg = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x31));

        public WelcomeWindow(IslandWindow island)
        {
            Title = "Kapsula";
            Icon = PetView.WindowIcon();
            Width = 520; SizeToContent = SizeToContent.Height;
            Background = Bg;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            UseLayoutRounding = true;

            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            var hello = new StackPanel { Orientation = Orientation.Horizontal };
            var greeter = new PetView(3.2, Bg);   // зверёк встречает нового человека
            greeter.Mood("happy");
            greeter.Root.Margin = new Thickness(0, 0, 4, 0);
            hello.Children.Add(greeter.Root);
            hello.Children.Add(new TextBlock { Text = "Kapsula запущена", Foreground = Txt, FontSize = 21, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(hello);
            sp.Children.Add(new TextBlock { Text = "Это капсула у верхнего края экрана. Вот с чего начать:", Foreground = Sub, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 14) });
            string[][] tips =
            {
                new string[] { "E8B0", "Наведите курсор на капсулу", "Она раскроется в панель: музыка, буфер обмена, заметки, погода и другое. Уберите курсор — свернётся." },
                new string[] { "E721", "Строка команд", "Запуск программ, поиск файлов, калькулятор, таймеры. Открывается сочетанием клавиш: " + island.HotkeyText("cmd") + "." },
                new string[] { "E8D6", "Музыка", "Включите что-нибудь в плеере или браузере — капсула покажет трек, обложку и текст песни." },
                new string[] { "E722", "Снимки и запись экрана", "Плитки на главной вкладке: область, весь экран, запись, распознавание текста." },
                new string[] { "E713", "Настройки", "Значок шестерёнки в панели или правый клик по значку Kapsula возле часов. Там же — выход." }
            };
            foreach (string[] t in tips)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = ((char)Convert.ToInt32(t[0], 16)).ToString(), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 17, Foreground = Txt, VerticalAlignment = VerticalAlignment.Center });
                var texts = new StackPanel();
                texts.Children.Add(new TextBlock { Text = t[1], Foreground = Txt, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
                texts.Children.Add(new TextBlock { Text = t[2], Foreground = Sub, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
                Grid.SetColumn(texts, 1); row.Children.Add(texts);
                sp.Children.Add(new Border { Background = RowBg, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 11, 14, 12), Margin = new Thickness(0, 0, 0, 8), Child = row });
            }
            sp.Children.Add(new TextBlock { Text = "Kapsula обновляется сама и ничего о вас не собирает. Это ранняя версия: что-то может работать не так.", Foreground = Sub, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 14) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(Button("Открыть настройки", false, delegate { Close(); island.OpenSettings(); }));
            buttons.Children.Add(Button("Понятно", true, delegate { Close(); }));
            sp.Children.Add(buttons);
            Content = sp;
        }

        static FrameworkElement Button(string text, bool main, Action click)
        {
            var b = new Border { Background = main ? Txt : ChipBg, CornerRadius = new CornerRadius(15), Padding = new Thickness(18, 7, 18, 8), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, Foreground = main ? Bg : Txt, FontSize = 13, FontWeight = FontWeights.SemiBold } };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }
    }

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
            Icon = PetView.WindowIcon();
            Width = 840; Height = Math.Min(700, SystemParameters.WorkArea.Height - 16);   // на небольших экранах окно не должно уходить за край
            Background = Bg;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            UseLayoutRounding = true;

            // Полоса прокрутки под тёмное окно: тонкая, без стрелок.
            try
            {
                var sbStyle = (Style)System.Windows.Markup.XamlReader.Parse(
                    "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ScrollBar'>" +
                    "<Setter Property='Width' Value='10'/><Setter Property='MinWidth' Value='10'/><Setter Property='Background' Value='Transparent'/>" +
                    "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Grid Background='Transparent'>" +
                    "<Track Name='PART_Track' IsDirectionReversed='True'>" +
                    "<Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton>" +
                    "<Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton>" +
                    "<Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border CornerRadius='3' Background='#55555E' Margin='2,2,2,2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
                    "</Track></Grid></ControlTemplate></Setter.Value></Setter></Style>");
                Resources.Add(typeof(ScrollBar), sbStyle);
            }
            catch { }

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
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            sp.Children.Add(Row("Размер", "Потяните — остров меняется сразу. Для больших мониторов удобнее покрупнее.", SliderCtl(0.8, 2, 0.05, island.Scale,
                delegate(double v) { return Math.Round(v * 100) + "%"; },
                delegate(double v) { island.PreviewScale(v.ToString("0.##", inv)); },
                delegate(double v) { island.ApplyScale(v); }), true));
            sp.Children.Add(Row("Матовое стекло", "Раскрытая панель и строка команд становятся полупрозрачными, а то, что за ними, размывается. Капсула и карточки остаются чёрными.", Toggle(island.GlassOn, delegate(bool v) { island.SetGlass(v); }), false));
            sp.Children.Add(Row("Прозрачность фона", "Фон острова становится полупрозрачным, текст и значки остаются плотными.", SliderCtl(0.5, 1, 0.05, island.IslandOpacity,
                delegate(double v) { return v >= 0.995 ? "нет" : Math.Round((1 - v) * 100) + "%"; },
                delegate(double v) { island.ApplyOpacity(v, false); },
                delegate(double v) { island.ApplyOpacity(v, true); }), true));

            sp.Children.Add(Row("Автоскрытие", "Остров уезжает за край и возвращается, когда курсор у верхней кромки экрана или есть что показать.", Toggle(island.AutoHide, delegate(bool v) { island.SetFlag("autoHide", v); }), false));
            sp.Children.Add(Row("Показывать в играх", "Остров остаётся на месте поверх игр и видео на весь экран, а не прячется за край. Работает в оконном режиме без рамки; в «эксклюзивном» полноэкранном режиме игра сама закрывает собой все окна. Переключается и из меню значка у часов.", Toggle(Store.Get("showInGames") == "1", delegate(bool v) { island.SetFlag("showInGames", v); }), false));
            sp.Children.Add(Row("Прятаться в программах", "Если остров закрывает вкладки или кнопки какой-то программы, добавьте её сюда: пока она активна, остров уезжает за край и возвращается, когда курсор у верхней кромки или есть что показать. В играх и видео на весь экран он прячется и так.", HideAppsEditor(), true));
            sp.Children.Add(Row("Зверёк в капсуле", "Следит за курсором, задумывается, пока отвечает нейросеть, пугается таймера, радуется удаче и показывает, чем занят Claude Code. Если выключить, останутся только часы и погода.", Toggle(island.ShowEyes, delegate(bool v) { island.SetFlag("eyes", v); }), false));
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
            sp.Children.Add(Row("Недавно скопированное", "Три последние записи буфера обмена. Клик вставляет запись в активное окно.", Toggle(Store.Get("homeClips") == "1", delegate(bool v) { Store.Set("homeClips", v ? "1" : "0"); island.ApplyHomeLayout(); }), false));

            sp = Sec("Музыка");
            sp.Children.Add(Row("Смена трека поверх игр и видео", "В полноэкранной программе остров спрятан. С этой настройкой он выглядывает на пару секунд с названием нового трека.", Toggle(Store.Get("fsTrack") != "0", delegate(bool v) { island.SetFlag("fsTrack", v); }), false));
            sp.Children.Add(Row("Капсула во время музыки", "Что показывать в свёрнутом виде. «Строка песни» работает, когда для трека нашёлся текст; иначе показывается название.", Chips(new string[] { "Часы и погода", "Название", "Строка песни", "Полоса трека" }, island.PillMode, delegate(int i) { island.SetPillMode(i); }), true));
            sp.Children.Add(Row("Плеер на «Главной»", "«Компактный» — обложка слева. «Крупная обложка» — обложка по центру, название под ней. «В цвет обложки» — весь плеер залит её цветом.", Chips(new string[] { "Компактный", "Крупная обложка", "В цвет обложки" }, island.PlayerStyle, delegate(int i) { island.SetPlayerStyle(i); }), true));
            sp.Children.Add(Row("Мини-плеер при наведении", "Во время музыки сначала открывается мини-плеер, клик по нему — вся панель.", Toggle(island.HoverMini, delegate(bool v) { island.SetFlag("hoverMini", v); }), false));
            sp.Children.Add(Row("Жидкий край", "Нижний край пляшет под звук.", Toggle(island.LiquidOn, delegate(bool v) { island.SetFlag("liquid", v); }), false));
            sp.Children.Add(Row("Ползунок громкости на «Главной»", "«Только плеер» меняет громкость одной программы, в которой играет музыка, — остальной звук не трогает. Переключается и кнопкой слева от ползунка.", Chips(new string[] { "Весь звук", "Только плеер" }, Store.Get("volPlayer") == "1" ? 1 : 0, delegate(int i) { Store.Set("volPlayer", i == 1 ? "1" : "0"); }), true));
            sp.Children.Add(Row("Заряд AirPods и Beats", "Когда наушники появляются рядом, остров показывает их заряд и предупреждает на 20% и 10%. Наушники сами сообщают заряд по Bluetooth — Kapsula только слушает. Команда «наушники» в строке острова покажет заряд в любой момент.", Toggle(Earbuds.Enabled, delegate(bool v) { Store.Set("earbuds", v ? "1" : "0"); if (v) Earbuds.Start(); else Earbuds.Stop(); }), false));
            sp.Children.Add(Row("Пауза, когда сняли наушники", "Музыка встаёт на паузу, когда вы снимаете AirPods, и продолжается, когда надеваете обратно. Проба: на некоторых моделях может срабатывать неточно.", Toggle(Store.Get("earbudsPause") == "1", delegate(bool v) { Store.Set("earbudsPause", v ? "1" : "0"); }), false));
            sp.Children.Add(Row("Край как эквалайзер", "Слева бас, справа высокие частоты. Выключено — край пляшет от общей громкости.", Toggle(Store.Get("liquidSpectrum") != "0", delegate(bool v) { Store.Set("liquidSpectrum", v ? "1" : "0"); }), false));
            sp.Children.Add(Row("Тексты песен", "Загружаются с lrclib.net для играющего трека.", Toggle(island.LyricsEnabled, delegate(bool v) { island.SetFlag("lyrics", v); }), false));

            double[] offs = { -1, -0.5, 0, 0.5, 1 };
            int oi = 2;
            for (int i = 0; i < offs.Length; i++) if (Math.Abs(offs[i] - island.LyricOffset) < 0.01) oi = i;
            sp.Children.Add(Row("Сдвиг текста песни", "Если строки отстают от звука — сдвиньте вперёд, если спешат — назад.", Chips(new string[] { "−1 с", "−0,5 с", "Без сдвига", "+0,5 с", "+1 с" }, oi, delegate(int i) { island.SetLyricOffset(offs[i]); }), true));

            sp = Sec("Чат с Claude");
            sp.Children.Add(Row("Кто отвечает в чате", "Duck.ai открывается прямо во вкладке «Чат»: без ключа и без входа, нейросеть выбирается на самой странице, переписка хранится в ней же. Claude и Gemini отвечают по вашему ключу; кнопкам «Перевести», «Исправить», «Сократить», «Объяснить» ключ нужен в любом случае. К Claude Code это не относится.", Chips(new string[] { "Duck.ai", "Claude", "Gemini" }, Ai.Duck ? 0 : Ai.Gemini ? 2 : 1, delegate(int i) { Store.Set("aiProvider", i == 0 ? "duck" : i == 2 ? "gemini" : "claude"); }), true));
            sp.Children.Add(Row("Ключ Gemini API", "Ключ создаётся в Google AI Studio под вашим аккаунтом Google; у Gemini есть бесплатный уровень с ограничением числа запросов. Ключ хранится только на этом компьютере, зашифрованный Windows, и отправляется только на generativelanguage.googleapis.com.", BuildGeminiKeyEditor(), true));
            sp.Children.Add(Row("Модель Gemini", "Flash-Lite — самая быстрая, на бесплатном уровне отвечает стабильнее всех. Flash умнее, но часто перегружена; если она занята, Kapsula сама попробует другую модель.", Chips(GeminiChat.ModelNames, Array.IndexOf(GeminiChat.Models, GeminiChat.Model()), delegate(int i) { Store.Set("geminiModel", GeminiChat.Models[i]); }), true));
            sp.Children.Add(Row("Ключ Claude API", "Нужен свой ключ: console.anthropic.com → API Keys. Он хранится только на этом компьютере, зашифрованный Windows, и отправляется только на api.anthropic.com.", BuildKeyEditor(), true));
            sp.Children.Add(Row("Модель", "Opus 5.5 — умнее; Sonnet 5.5 — быстрее и дешевле.", Chips(new string[] { "Opus 5.5", "Sonnet 5.5" }, ClaudeChat.Model() == "claude-sonnet-5-5" ? 1 : 0, delegate(int i) { island.SetClaudeModel(i == 1 ? "claude-sonnet-5-5" : "claude-opus-5-5"); }), true));

            if (ClaudeBridge.ClaudeCodeFound)
            {
                var cLabel = new TextBlock { Foreground = Txt, FontSize = 12.5, FontWeight = FontWeights.SemiBold };
                var cBtn = new Border { Background = ChipBg, CornerRadius = new CornerRadius(13), Padding = new Thickness(14, 6, 14, 7), Cursor = Cursors.Hand, Child = cLabel };
                var cRow = Row("Claude Code на острове", "—", cBtn, false);
                var cText = ((StackPanel)((Grid)((Border)cRow).Child).Children[0]).Children[1] as TextBlock;
                string cNote = "";
                Action cShow = delegate
                {
                    bool have = ClaudeBridge.Installed && ClaudeBridge.Enabled;
                    bool old = have && !ClaudeBridge.UpToDate;
                    cLabel.Text = old ? "Обновить" : have ? "Отключить" : "Подключить";
                    string st = !have ? "Kapsula допишет свои строки в настройки Claude Code (прежний файл сохранится рядом). В интернет ничего не уходит."
                        : old ? "Подключение устарело: нажмите «Обновить», чтобы заработали новые возможности."
                        : ClaudeBridge.Error != null ? "Не работает: " + ClaudeBridge.Error + "."
                        : "Подключено. Новые сессии Claude Code подхватят это сразу, уже открытые — после перезапуска.";
                    if (cText != null) cText.Text = "Пока вы в другой программе, остров показывает запросы разрешений и вопросы Claude Code с кнопками, сообщает, что он закончил, и даёт ответить ему. Остановить его можно из значка Kapsula у часов или командой «стоп claude». Не ответили за полминуты — Claude Code спросит сам, как обычно. " + st + cNote;
                };
                cBtn.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (ClaudeBridge.Installed && ClaudeBridge.Enabled && !ClaudeBridge.UpToDate)
                    {
                        string err = ClaudeBridge.Install();
                        cNote = err == null ? "" : " Не удалось: " + err + ".";
                    }
                    else if (ClaudeBridge.Installed && ClaudeBridge.Enabled)
                    {
                        string err = ClaudeBridge.Remove();
                        Store.Set("claudeCode", "0"); ClaudeBridge.Stop();
                        cNote = err == null ? "" : " Не удалось убрать строки из настроек: " + err + ".";
                    }
                    else
                    {
                        string err = ClaudeBridge.Install();
                        if (err == null) { Store.Set("claudeCode", "1"); ClaudeBridge.Start(); cNote = ""; }
                        else cNote = " Не удалось: " + err + ".";
                    }
                    cShow();
                };
                cShow();
                sp.Children.Add(cRow);
                sp.Children.Add(Row("Звук, когда Claude Code ждёт вас", "Системный звук вместе с карточкой разрешения или вопроса — чтобы не пропустить, если вы отвернулись от экрана.", Toggle(Store.Get("claudeSound") != "0", delegate(bool v) { Store.Set("claudeSound", v ? "1" : "0"); }), false));
                sp.Children.Add(Row("Показывать, что делает Claude Code", "Короткая надпись на острове: какую команду он выполняет или какой файл правит. Только пока вы в другой программе.", Toggle(Store.Get("claudeActivity") != "0", delegate(bool v) { Store.Set("claudeActivity", v ? "1" : "0"); }), false));
            }

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
            sp.Children.Add(Row("Напоминать о событиях календаря", "За десять минут до события из календаря Windows появится карточка. Список ближайших событий — по команде «календарь» в строке острова.", Toggle(Store.Get("calendar") != "0", delegate(bool v) { Store.Set("calendar", v ? "1" : "0"); }), false));
            sp.Children.Add(Row("Счётчик непрочитанного", "Красный кружок с числом сообщений из Discord и Steam, пришедших, пока вы в них не смотрели. Сбрасывается, когда вы открываете программу.", Toggle(Store.Get("unread") != "0", delegate(bool v) { Store.Set("unread", v ? "1" : "0"); }), false));

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

        // Цвета значков разделов, в порядке SectionOrder
        static readonly Color[] SectionColors =
        {
            Color.FromRgb(0x8E, 0x8E, 0x93), Color.FromRgb(0x5E, 0x5C, 0xE6), Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0xFF, 0x37, 0x5F), Color.FromRgb(0xFF, 0x45, 0x3A),
            Color.FromRgb(0x30, 0xB0, 0xC7), Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0xE0, 0x7B, 0x45), Color.FromRgb(0x63, 0x63, 0x66)
        };
        static readonly Brush Divider = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x33));
        static readonly Brush RowHover = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x2A));

        // Строки настроек, идущие подряд, собираются в одну карточку: скруглены только её внешние углы,
        // между строками — тонкая линия. Так раздел читается как несколько понятных групп, а не россыпь плашек.
        static void GroupRows(StackPanel p)
        {
            var kids = new List<UIElement>();
            foreach (UIElement k in p.Children) kids.Add(k);
            for (int i = 0; i < kids.Count; i++)
            {
                var b = kids[i] as Border;
                if (b == null || (b.Tag as string) != "row") continue;
                bool first = i == 0 || !((kids[i - 1] is Border) && ((Border)kids[i - 1]).Tag as string == "row");
                bool last = i == kids.Count - 1 || !((kids[i + 1] is Border) && ((Border)kids[i + 1]).Tag as string == "row");
                b.CornerRadius = new CornerRadius(first ? 16 : 0, first ? 16 : 0, last ? 16 : 0, last ? 16 : 0);
                b.Margin = new Thickness(0, 0, 0, last ? 16 : 0);
                b.BorderBrush = Divider;
                b.BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1);
            }
        }

        void BuildLayout()
        {
            foreach (StackPanel sec in sections.Values) GroupRows(sec);
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var nav = new StackPanel { Margin = new Thickness(12, 20, 10, 12) };
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 2, 0, 0) };
            var navPet = new PetView(2.0, RowBg);   // зверёк рядом с названием: моргает и поглядывает по сторонам
            navPet.Root.Margin = new Thickness(0, 0, 2, 0);
            brand.Children.Add(navPet.Root);
            brand.Children.Add(new TextBlock { Text = "Kapsula", Foreground = Txt, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            nav.Children.Add(brand);
            nav.Children.Add(new TextBlock { Text = "версия " + Updater.CurrentText, Foreground = Sub, FontSize = 11.5, Margin = new Thickness(12, 1, 0, 14) });
            for (int i = 0; i < SectionOrder.Length; i++)
            {
                string name = SectionOrder[i];
                if (!sections.ContainsKey(name) || sections[name].Children.Count == 0) continue;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                // значок раздела — белый на цветном скруглённом квадрате, у каждого раздела свой цвет
                Color tint = SectionColors[i % SectionColors.Length];
                row.Children.Add(new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(7.5), Margin = new Thickness(0, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center,
                    Background = new LinearGradientBrush(Color.FromRgb((byte)Math.Min(255, tint.R + 22), (byte)Math.Min(255, tint.G + 22), (byte)Math.Min(255, tint.B + 22)), tint, 90),
                    Child = new TextBlock { Text = SectionGlyphs[i], FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13.5, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                });
                row.Children.Add(new TextBlock { Text = name, Foreground = Txt, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var item = new Border { Height = 40, CornerRadius = new CornerRadius(11), Padding = new Thickness(9, 0, 10, 0), Margin = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = row, Tag = name };
                item.MouseLeftButtonDown += delegate { ShowSection(name); };
                item.MouseEnter += delegate { if ((string)item.Tag != currentSection) item.Background = ChipBg; };
                item.MouseLeave += delegate { if ((string)item.Tag != currentSection) item.Background = Brushes.Transparent; };
                navItems.Add(item);
                nav.Children.Add(item);
            }
            root.Children.Add(new Border { Background = RowBg, Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = nav } });

            var pane = new DockPanel { Margin = new Thickness(28, 22, 10, 0) };
            var head = new StackPanel { Margin = new Thickness(4, 0, 0, 12) };
            paneTitle = new TextBlock { Foreground = Txt, FontSize = 25, FontWeight = FontWeights.Bold };
            paneHint = new TextBlock { Foreground = Sub, FontSize = 12.5, Margin = new Thickness(0, 3, 0, 0) };
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
                Child = new Rectangle { Width = IslandWindow.PreviewEW, Height = IslandWindow.PreviewEH + 60, VerticalAlignment = VerticalAlignment.Top, Fill = new VisualBrush(island.PreviewHome) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, IslandWindow.PreviewEW, IslandWindow.PreviewEH + 60) } } };
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
            pvBox = new Border { Height = PvH, CornerRadius = new CornerRadius(18), Background = bg, Margin = new Thickness(0, 0, 16, 18), Child = grid, ClipToBounds = true, BorderBrush = Divider, BorderThickness = new Thickness(1) };
            return pvBox;
        }

        int hoverKind = -1;

        // Что показать в превью для строки настроек: 1 — главная панель, 2 — мини-плеер, 0 — сам остров, -1 — как у раздела
        static int PreviewKindFor(string title)
        {
            string t = title ?? "";
            if (t.Contains("Главн")) return 1;
            if (t.StartsWith("Мини-плеер") || t.Contains("екст")) return 2;
            if (t.StartsWith("Капсула") || t.Contains("край") || t.Contains("Край") || t.StartsWith("Смена трека") || t.StartsWith("Зверёк") || t.StartsWith("Секунды")) return 0;
            return -1;
        }

        void UpdatePreview()
        {
            if (pvIsland == null) return;
            int kind = hoverKind >= 0 ? hoverKind : currentSection == "Главная" ? 1 : currentSection == "Музыка" ? 2 : 0;
            if (kind == 2 && !island.PreviewHasMedia) kind = 0;   // ничего не играет — мини-плеер пуст, показываем сам остров
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
            texts.Children.Add(new TextBlock { Text = title, Foreground = Txt, FontSize = 14, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(desc)) texts.Children.Add(new TextBlock { Text = desc, Foreground = Sub, FontSize = 12, LineHeight = 17, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
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
            var rowBox = new Border { Background = RowBg, CornerRadius = new CornerRadius(16), Padding = new Thickness(18, 13, 18, 14), Margin = new Thickness(0, 0, 0, 8), Child = body, Tag = "row" };
            rowBox.MouseEnter += delegate { rowBox.Background = RowHover; };
            rowBox.MouseLeave += delegate { rowBox.Background = RowBg; };
            // Превью показывает то, что меняет строка под курсором: главную панель, мини-плеер или сам остров.
            int kind = PreviewKindFor(title);
            if (kind >= 0)
            {
                rowBox.MouseEnter += delegate { hoverKind = kind; UpdatePreview(); };
                rowBox.MouseLeave += delegate { if (hoverKind == kind) { hoverKind = -1; UpdatePreview(); } };
            }
            return rowBox;
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

        // Ползунок: live вызывается при каждом движении (без записи в настройки), done — когда отпустили.
        FrameworkElement SliderCtl(double min, double max, double step, double value, Func<double, string> fmt, Action<double> live, Action<double> done)
        {
            const double W = 300, T = 18;
            double cur = Math.Max(min, Math.Min(max, value));
            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = ChipBg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(T / 2, 0, T / 2, 0) };
            var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = OnBg, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(T / 2, 0, 0, 0) };
            var thumb = new Ellipse { Width = T, Height = T, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var area = new Grid { Width = W, Height = 28, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            area.Children.Add(track); area.Children.Add(fill); area.Children.Add(thumb);
            var label = new TextBlock { Foreground = Txt, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 1), Width = 56 };
            Action paint = delegate
            {
                double f = (cur - min) / (max - min);
                fill.Width = f * (W - T);
                thumb.Margin = new Thickness(f * (W - T), 0, 0, 0);
                label.Text = fmt(cur);
            };
            Action<MouseEventArgs> move = delegate(MouseEventArgs e)
            {
                double f = Math.Max(0, Math.Min(1, (e.GetPosition(area).X - T / 2) / (W - T)));
                double v = min + Math.Round(f * (max - min) / step) * step;
                v = Math.Max(min, Math.Min(max, v));
                if (Math.Abs(v - cur) < 1e-9) return;
                cur = v; paint(); live(cur);
            };
            area.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { area.CaptureMouse(); move(e); e.Handled = true; };
            area.MouseMove += delegate(object s, MouseEventArgs e) { if (area.IsMouseCaptured) move(e); };
            area.MouseLeftButtonUp += delegate { if (area.IsMouseCaptured) { area.ReleaseMouseCapture(); done(cur); } };
            paint();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(area); row.Children.Add(label);
            return row;
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
        // Поле для ключа Gemini: ключ вводит сам пользователь, хранится зашифрованным на этом компьютере
        FrameworkElement BuildGeminiKeyEditor()
        {
            var col = new StackPanel();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var pw = new PasswordBox { Width = 250, Height = 30, Background = ChipBg, Foreground = Txt, BorderThickness = new Thickness(0), Padding = new Thickness(10, 5, 10, 0), CaretBrush = Txt, FontSize = 13 };
            row.Children.Add(pw);
            var status = new TextBlock { Foreground = Sub, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            Action paint = delegate
            {
                status.Text = Secrets.IsSaved("geminiKey") ? "Ключ сохранён." : (Secrets.GetKey("geminiKey", "GEMINI_API_KEY") != "" ? "Используется ключ из переменной среды GEMINI_API_KEY." : "Ключ не задан.");
            };
            var save = new Border { Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 0, 14, 1), Margin = new Thickness(8, 0, 0, 0), Background = OnBg, Cursor = Cursors.Hand, Child = new TextBlock { Text = "Сохранить", Foreground = OnTxt, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } };
            save.MouseLeftButtonDown += delegate { if (pw.Password.Trim().Length == 0) return; Secrets.SetKey("geminiKey", pw.Password); pw.Clear(); paint(); };
            row.Children.Add(save);
            var del = new Border { Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 0, 14, 1), Margin = new Thickness(6, 0, 0, 0), Background = ChipBg, Cursor = Cursors.Hand, Child = new TextBlock { Text = "Удалить", Foreground = Txt, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } };
            del.MouseLeftButtonDown += delegate { Secrets.SetKey("geminiKey", ""); pw.Clear(); paint(); };
            row.Children.Add(del);
            var link = LinkButton("Получить ключ в Google AI Studio", "https://aistudio.google.com/apikey");
            link.Margin = new Thickness(0, 10, 0, 0); link.HorizontalAlignment = HorizontalAlignment.Left;
            col.Children.Add(row);
            col.Children.Add(status);
            col.Children.Add(link);
            paint();
            return col;
        }

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
