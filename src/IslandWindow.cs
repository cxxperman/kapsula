// Kapsula — «остров» для Windows. Часть исходного кода: IslandWindow.cs
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
    // ============================================================ Главное окно
    class IslandWindow : Window
    {
        // Размеры (в независимых от DPI пикселях)
        const double CH = 34, CR = 15, Ear = 10;
        const double MW = 440, MH = 138, MR = 28;
        const double EW = 584, EH = 370, ER = 34;
        const double CardW = 390, CardH = 80, CmdW = 580;
        const int PHome = 0, PClip = 1, PShelf = 2, PText = 3, PChat = 4, PNote = 5, PWeather = 6, PSystem = 7;

        // Экономный режим отрисовки жидкого края
        double liquidAt, appliedAt;
        bool liquidLive;

        // Доработки: сдвиг текста песен, автоскрытие, раскладка, глаза, уведомления, монитор
        public double LyricOffset;
        public bool AutoHide, ShowLayout = true, ShowEyes = true, ShowNotifications;
        public int MonitorIndex;
        int revealUntil;
        bool autoHidden;
        int lastLayout;
        Canvas eyes;
        Shape[] pupils, whites;
        Path[] smiles;
        ScaleTransform[] lids;
        double pupilW, pupilH;
        string character;
        int happyUntil;
        public string TestCharacter, TestMood;   // ключи --character и --mood: для снимков

        public static readonly string[] CharacterIds = { "eyes", "cat", "robot", "dots" };
        public static readonly string[] CharacterNames = { "Глаза", "Кот", "Робот", "Точки" };
        public string Character { get { string c = TestCharacter ?? Store.Get("character"); return Array.IndexOf(CharacterIds, c) >= 0 ? c : "eyes"; } }

        // Персонаж ненадолго радуется: глаза превращаются в дужки
        public void Happy(int ms) { happyUntil = Environment.TickCount + ms; }

        // Персонаж в капсуле собирается из простых фигур, у каждого свои глаза и детали.
        public void BuildCharacter()
        {
            if (eyes == null) return;
            eyes.Children.Clear();
            character = Character;
            bool cat = character == "cat", robot = character == "robot", dots = character == "dots";
            pupilW = cat ? 3 : robot ? 4.5 : dots ? 6 : 5;
            pupilH = cat ? 9 : robot ? 4.5 : dots ? 6 : 5;
            whites = new Shape[2]; pupils = new Shape[2]; smiles = new Path[2]; lids = new ScaleTransform[2];
            for (int i = 0; i < 2; i++)
            {
                lids[i] = new ScaleTransform(1, 1);
                Shape white = robot ? (Shape)new Rectangle { RadiusX = 2.5, RadiusY = 2.5 } : new Ellipse();
                white.Width = 11; white.Height = 13;
                white.Fill = dots ? Brushes.Transparent : Fg;
                white.RenderTransformOrigin = new Point(0.5, 0.5); white.RenderTransform = lids[i];
                Canvas.SetLeft(white, i * 14); Canvas.SetTop(white, 0.5);
                whites[i] = white; eyes.Children.Add(white);
                Shape pupil = robot ? (Shape)new Rectangle { RadiusX = 1, RadiusY = 1 } : new Ellipse();
                pupil.Width = pupilW; pupil.Height = pupilH;
                pupil.Fill = dots ? Fg : Ink;
                pupils[i] = pupil; eyes.Children.Add(pupil);
                var smile = new Path { Stroke = Fg, StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Data = Geometry.Parse("M 1,9 Q 5.5,1 10,9"), Visibility = Visibility.Collapsed };
                Canvas.SetLeft(smile, i * 14); Canvas.SetTop(smile, 1);
                smiles[i] = smile; eyes.Children.Add(smile);
            }
            if (cat)
            {
                // уши
                var earL = new Polygon { Fill = Fg, Points = new PointCollection { new Point(0.5, 1.5), new Point(2.5, -5), new Point(7.5, 0) } };
                var earR = new Polygon { Fill = Fg, Points = new PointCollection { new Point(17.5, 0), new Point(22.5, -5), new Point(24.5, 1.5) } };
                eyes.Children.Insert(0, earL); eyes.Children.Insert(0, earR);
            }
            if (robot)
            {
                // антенна с огоньком
                eyes.Children.Add(new Line { X1 = 12.5, Y1 = -1, X2 = 12.5, Y2 = -5, Stroke = Fg, StrokeThickness = 1.5 });
                var lamp = new Ellipse { Width = 5.5, Height = 5.5, Fill = Amber };
                Canvas.SetLeft(lamp, 9.75); Canvas.SetTop(lamp, -10);
                eyes.Children.Add(lamp);
            }
        }
        int blinkAt, blinkUntil;

        // ИИ-чат и диктовка
        List<ChatMsg> chatHistory = new List<ChatMsg>();
        string pendingImage;
        Border attachChip;
        StackPanel chatList, chatEmpty;
        ScrollViewer chatScroll;
        TextBox chatBox;
        TextBlock chatModelLabel;
        bool chatBusy, dictating;
        int chatGen;
        IntPtr prevFg = IntPtr.Zero;
        public bool TestChat, TestEyes;
        const double Pitch = 22;
        static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(0xFF, 0xC2, 0x3D));
        const double PageW = 548, PageH = 300;

        // Кисти темы общие для всего интерфейса: ApplyTheme меняет их цвет, и остров перекрашивается целиком.
        static readonly SolidColorBrush Ink = new SolidColorBrush(Colors.Black);
        static readonly SolidColorBrush Fg = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6));
        static readonly SolidColorBrush Dim = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96));
        static readonly SolidColorBrush Faint = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x62));
        static readonly SolidColorBrush Card = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x17));
        static readonly SolidColorBrush Chip = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1E));
        static readonly SolidColorBrush ChipHover = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2F));
        static readonly SolidColorBrush TabOn = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2A));

        // Настройки вида и поведения
        const double BaseW = EW + 120, BaseH = EH + 90;
        public static readonly Color LightIsland = Color.FromRgb(0xF4, 0xF4, 0xF6);
        public Color IslandColor = Colors.Black, DarkColor = Colors.Black;
        public bool FollowWindows, PillTitle, HoverMini = true, LiquidOn = true, ShowSeconds, ShowInCapture;
        public double Scale = 1;
        public string TestColor;
        public bool TestSettings, TestSettingsTop;
        ScaleTransform scaleT = new ScaleTransform(1, 1);
        SettingsWindow settingsWin;
        bool lastWinDark;
        Color rawAccent = DefaultAccent;

        static bool TryHex(string s, out Color c)
        {
            c = Colors.Black;
            if (s == null) return false;
            s = s.Trim().TrimStart('#');
            int v;
            if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out v)) return false;
            c = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }

        static string Hex(Color c) { return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        public static bool IsLight(Color c) { return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255 > 0.6; }
        static Color Mix(Color a, Color b, double t) { return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t)); }

        void LoadPrefs()
        {
            Color c;
            if (TryHex(Store.Get("color"), out c)) IslandColor = c;
            if (TryHex(Store.Get("darkColor"), out c)) DarkColor = c;
            FollowWindows = Store.Get("followWin") == "1";
            PillTitle = Store.Get("pillTitle") == "1";
            HoverMini = Store.Get("hoverMini") != "0";
            LiquidOn = Store.Get("liquid") != "0";
            ShowSeconds = Store.Get("seconds") == "1";
            AutoHide = Store.Get("autoHide") == "1";
            ShowLayout = Store.Get("layout") != "0";
            ShowEyes = Store.Get("eyes") != "0";
            ShowNotifications = Store.Get("notifications") == "1";
            int mi;
            MonitorIndex = int.TryParse(Store.Get("monitor"), out mi) ? mi : -1;
            double lo;
            if (double.TryParse(Store.Get("lyricOffset"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lo)) LyricOffset = lo;
            ShowInCapture = Store.Get("showInCapture") == "1";
            double s;
            if (double.TryParse(Store.Get("scale"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s) && s >= 0.8 && s <= 2) Scale = s;
        }

        // Все оттенки выводятся из цвета острова, поэтому подходит любой: светлый фон даёт тёмный текст и наоборот.
        public void ApplyTheme(Color island)
        {
            bool light = IsLight(island);
            Color text = light ? Color.FromRgb(0x11, 0x11, 0x14) : Color.FromRgb(0xF4, 0xF4, 0xF6);
            Ink.Color = island;
            Fg.Color = text;
            Dim.Color = Mix(text, island, 0.42);
            Faint.Color = Mix(text, island, 0.64);
            Card.Color = Mix(island, text, light ? 0.06 : 0.085);
            Chip.Color = Mix(island, text, light ? 0.09 : 0.115);
            ChipHover.Color = Mix(island, text, 0.18);
            TabOn.Color = Mix(island, text, 0.16);
            Bar.TrackBrush.Color = Mix(island, text, 0.2);
            Tile.Off.Color = Chip.Color; Tile.Hover.Color = ChipHover.Color;
            Tile.On.Color = text; Tile.Light.Color = text; Tile.Dark.Color = island;
            if (glowTop != null) UpdateAccent(rawAccent);
        }

        public void ApplyEffectiveTheme()
        {
            ApplyTheme(FollowWindows ? (IsDarkTheme() ? DarkColor : LightIsland) : IslandColor);
        }

        public void SetColor(Color c)
        {
            IslandColor = c;
            if (!IsLight(c)) { DarkColor = c; Store.Set("darkColor", Hex(c)); }
            Store.Set("color", Hex(c));
            ApplyEffectiveTheme();
        }

        public void ToggleLightDark()
        {
            if (FollowWindows) SetFlag("followWin", false);
            bool toLight = !IsLight(IslandColor);
            SetColor(toLight ? LightIsland : DarkColor);
            Toast("", toLight ? "Светлая тема" : "Тёмная тема", null, -1, null);
        }

        public void ApplyScale(double s)
        {
            Scale = Math.Max(0.8, Math.Min(2, s));
            scaleT.ScaleX = Scale; scaleT.ScaleY = Scale;
            Width = BaseW * Scale;
            Height = BaseH * Scale;
            PlaceWindow();
            Store.Set("scale", Scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        }

        void ApplyCapture()
        {
            if (hwnd != IntPtr.Zero) Native.SetWindowDisplayAffinity(hwnd, Capturable || ShowInCapture ? 0u : 0x11u);
        }

        public void SetFlag(string key, bool v)
        {
            Store.Set(key, v ? "1" : "0");
            switch (key)
            {
                case "followWin": FollowWindows = v; lastWinDark = IsDarkTheme(); ApplyEffectiveTheme(); break;
                case "pillTitle": PillTitle = v; RenderCompact(); SetTargets(); break;
                case "hoverMini": HoverMini = v; break;
                case "liquid": LiquidOn = v; Wake(); break;
                case "seconds": ShowSeconds = v; RenderCompact(); SetTargets(); break;
                case "showInCapture": ShowInCapture = v; ApplyCapture(); break;
                case "autoHide": AutoHide = v; revealUntil = Environment.TickCount + 2500; SetTargets(); break;
                case "layout": ShowLayout = v; break;
                case "eyes": ShowEyes = v; RenderCompact(); break;
                case "notifications": ShowNotifications = v; if (v) StartNotifications(); else notifStop = true; break;
                case "lyrics": Store.Set("lyrics", v ? "on" : "off"); lyricsKey = null; SetLyrics(null); RenderMedia(); break;
            }
        }

        public bool LyricsEnabled { get { return LyricsOn(); } }

        // Проверочные режимы: цвет и размер только на этот запуск, в настройки не пишутся.
        public void PreviewOnly(string hex)
        {
            Color c;
            if (TryHex(hex, out c)) { FollowWindows = false; IslandColor = c; ApplyTheme(c); }
        }

        public void PreviewScale(string s)
        {
            double v;
            if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return;
            Scale = Math.Max(0.8, Math.Min(2, v));
            scaleT.ScaleX = Scale; scaleT.ScaleY = Scale;
            Width = BaseW * Scale; Height = BaseH * Scale;
            PlaceWindow();
        }

        public void OpenSettings()
        {
            if (state != 0) SetState(0);
            if (settingsWin != null) { settingsWin.Activate(); return; }
            settingsWin = new SettingsWindow(this);
            settingsWin.Closed += delegate { settingsWin = null; ApplyEffectiveTheme(); };
            if (TestSettings) { if (!TestSettingsTop) settingsWin.ScrollToBottom(); settingsWin.Topmost = true; settingsWin.Height = 1280; settingsWin.WindowStartupLocation = WindowStartupLocation.Manual; settingsWin.Top = 60; settingsWin.Left = (SystemParameters.PrimaryScreenWidth - 560) / 2; }
            settingsWin.Show();
            settingsWin.Activate();
        }
        static readonly Color DefaultAccent = Color.FromRgb(0x4C, 0xD9, 0x7B);
        static readonly FontFamily Icons = new FontFamily("Segoe MDL2 Assets");
        static readonly FontFamily Symbols = new FontFamily("Segoe UI Symbol");

        public bool Capturable, TestLyrics, TestCard, TestNote, TestRec;
        public bool Demo;   // ключ --demo: выдуманный трек и стандартные программы Windows вместо настоящих — для снимков в описание
        public string TestCmd;
        public int ForceOpen = -1; // проверочные режимы: 0..5 — вкладка, -2 — мини-плеер, -3 — капсула без реакции на курсор

        // Геометрия и физика
        Grid root, clipHost, compactHost, expandedHost;
        Path shape;
        TranslateTransform slide = new TranslateTransform();
        ScaleTransform expandScale = new ScaleTransform(1, 1);
        Spring sw = new Spring(190, 250, 23), sh = new Spring(CH, 250, 23), sr = new Spring(CR, 250, 26), sy = new Spring(0, 200, 26);
        bool frameOn, hiddenFs, snipActive, fsPeeking;
        int fsPeekUntil;
        public bool TestFs;   // ключ --fstest: в демо-режиме через 4 с «переключает трек»
        int state; // 0 — капсула, 1 — мини-плеер, 2 — панель, 3 — карточка события, 4 — командная строка
        bool expanded { get { return state == 2; } }
        Spring[] so = { new Spring(1, 340, 37), new Spring(0, 340, 37), new Spring(0, 340, 37), new Spring(0, 340, 37), new Spring(0, 340, 37) };
        Grid[] layers;
        ScaleTransform[] layerScale;

        // Карточка события
        Grid cardHost;
        Border cardThumb;
        TextBlock cardGlyph, cardTitle, cardSub;
        Action cardAction;
        int cardUntil;

        // Командная строка
        Grid cmdHost;
        TextBox cmdBox;
        StackPanel cmdList;
        List<CmdItem> cmdItems = new List<CmdItem>();
        int cmdSel;
        List<KeyValuePair<string, string>> apps = new List<KeyValuePair<string, string>>();

        // Загрузки
        string dlDir, dlActive;
        Dictionary<string, long> dlSizes = new Dictionary<string, long>();
        long dlSize;
        double dlSpeed;
        int dlSeenAt, dlPollAt;

        // Запись экрана
        Tile recTile;
        bool recBusy;
        static readonly Brush RecRed = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));

        // Центр управления
        Tile netTile, btTile, vpnTile;
        ControlState control = new ControlState();
        bool controlBusy;
        StackPanel sysMain, btList;
        DockPanel btPanel;
        Border btSwitch;
        TextBlock btSwitchLabel;
        public bool TestBt;

        // Карточка: кнопка справа
        Border cardBtn;
        TextBlock cardBtnLabel;
        Action cardBtnAction;

        // Погода: новая раскладка
        Grid weatherGrid, wHours, wDays;
        GradientStop skyA, skyB;
        TextBlock wCity, wSymbol, wTemp, wDesc, wFeels, wHiLo, wWind, wWindArrow, wWindSub, wHum, wPres, wUv, wUvSub, wRain, wSun;
        RotateTransform wWindRot = new RotateTransform(0);
        int weatherAnimAt;

        // Заметки, кольцо таймера, напоминания
        RichTextBox noteRich;
        Border[] folderChips;
        int noteFolder;
        bool noteLoading;
        Path ringArc;
        TextBlock ringText, startLabel;
        TimeSpan timerTotal = TimeSpan.FromMinutes(5);
        int timerPreset = 5;
        Border[] presetChips;
        TextBox remBox;
        StackPanel remList;
        List<Reminder> reminders = new List<Reminder>();

        // Полка и поиск по буферу
        List<string> shelf = new List<string>();
        WrapPanel shelfPanel;
        TextBlock shelfEmpty;
        bool selfDrag;
        string dragArmed;
        Point dragFrom;
        TextBox clipSearch;
        ClipItem pendingSelf;
        int pendingSelfAt;

        TextBlock[] tabGlyphs;
        StackPanel launcherRow;
        Tile lyrTile, armedTile;
        string armedLabel;
        int armedAt;
        double lastFrame, dpi = 1;

        // Мини-плеер
        Grid miniHost;
        ScaleTransform miniScale = new ScaleTransform(1, 1);
        Border nCover;
        TextBlock nTitle, nArtist, nClock, nPos, nRem, nPlayGlyph;
        Bar nProgress;

        // Тексты песен
        List<LyricLine> lyrics;
        string lyricsKey;
        LyricView miniLyrics, homeLyrics;
        double lyricsDur;
        int lyricsReq;

        // Жидкий край
        Path edge;
        Border bottomGlow, coverGlow;
        GradientStop glowTop, glowBottom, coverGlowA, coverGlowB;
        double[] amp = new double[81], noise = new double[81], nextRoll = new double[81];
        double level, gainMax = 0.05, liquidClock, glowAmt, edgeAmt;
        Random rnd = new Random();
        int enterAt, leaveAt;
        IntPtr hwnd;

        // Свёрнутый вид
        Ellipse micDot;
        Border cCover;
        TextBlock cGlyph, cText, cRight;
        Bar cBar;
        StackPanel eq;
        string toastGlyph, toastText, toastRight;
        double toastValue = -1;
        Brush toastBrush;
        int toastUntil;
        bool toastOn;

        // Вкладки
        FrameworkElement[] pages;
        Border[] tabs;
        TextBlock[] tabLabels;
        int page;

        // Главная
        Grid playerBody;
        TextBlock playerEmpty, mTitle, mArtist, mPlayGlyph, mPos, mDur, volText;
        Border mCover;
        Bar mProgress, volBar;
        Grid progressRow;

        // Буфер и текст
        StackPanel clipList;
        List<ClipItem> clips = new List<ClipItem>();
        TextBlock textPreview;

        // Заметка, погода, управление
        TextBox noteBox, cityBox;
        DispatcherTimer noteSave;
        StackPanel weatherBody;
        WeatherData weather;
        bool weatherBusy;
        int weatherAt;
        TextBlock cpuText, ramText, timerText;
        Bar cpuBar, ramBar;
        Tile themeTile, micTile, autoTile, sleepTile;
        int sleepArmedAt;

        // Состояние
        MediaInfo media = new MediaInfo();
        MediaWatcher watcher = new MediaWatcher();
        byte[] shownCover;
        ImageBrush coverBrush;
        SolidColorBrush accent = new SolidColorBrush(DefaultAccent);
        DateTime timerEnd = DateTime.MinValue;
        bool timerRinging, micInUse, capsLast, firstPoll = true;
        double lastVol = -1, cpu;
        ulong lastIdle, lastTotal;
        int tick, pollN;
        WF.NotifyIcon tray;

        public IslandWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            LoadPrefs();
            scaleT.ScaleX = Scale; scaleT.ScaleY = Scale;
            Width = BaseW * Scale;
            Height = BaseH * Scale;
            PlaceWindow();
            Top = 0;
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            LoadShelf();
            BuildUi();
            lastWinDark = IsDarkTheme();
            ApplyEffectiveTheme();
            LoadNote();
            RenderShelf();
            AllowDrop = true;
            DragEnter += OnFileDragOver;
            DragOver += OnFileDragOver;
            Drop += OnFileDrop;

            SourceInitialized += OnSourceInitialized;
            Closed += OnClosed;
            Deactivated += delegate { if (state == 4 && ForceOpen == -1) SetState(0); SetNoActivate(true); };
            PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) { if (state == 4) SetState(0); Keyboard.ClearFocus(); SetNoActivate(true); } };
            IndexApps();
            PreviewMouseWheel += OnWheel;

            var poll = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(33) };
            poll.Tick += delegate { OnPoll(); };
            poll.Start();

            var slow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            slow.Tick += delegate { OnTick(); };
            slow.Start();

            watcher.Updated += delegate(MediaInfo m) { Dispatcher.BeginInvoke(new Action(delegate { if (Demo) return; media = m; RenderMedia(); })); };
            watcher.Preferred = Store.Get("player");
            watcher.Start();
            DiscordBridge.Notified += delegate(string t, string x, byte[] i) { Dispatcher.BeginInvoke(new Action(delegate { DiscordNotify(t, x, i); })); };
            DiscordBridge.Start();
            SteamBridge.Received += delegate(Dictionary<string, object> d)
            {
                object type;
                d.TryGetValue("type", out type);
                if ((type as string) == "hello")
                {
                    Dispatcher.BeginInvoke(new Action(delegate { Toast("", "Steam подключён к Kapsula", null, -1, accent); }));
                    SteamBridge.SendConfig();
                    // Проверочный режим: просим Steam показать его собственные образцы уведомлений.
                    if (TestSteam) SteamBridge.Send("{\"cmd\":\"test\",\"names\":[\"TestFriendMessage\",\"TestFriendOnline\",\"TestFriendIngame\",\"TestAchievement:550\",\"TestDownloadComplete:570\",\"TestKapsulaInvite\"]}");
                }
                else if ((type as string) == "toast")
                {
                    Func<string, string> get = delegate(string k) { object v; return d.TryGetValue(k, out v) && v != null ? v.ToString() : ""; };
                    string title = DiscordBridge.Clean(get("title"), 80), body = DiscordBridge.Clean(get("body"), 300), kind = SteamBridge.KindName(get("kind"));
                    string url = get("url");
                    // Уведомление приходит уже в фоновом потоке — значок можно скачать прямо здесь.
                    byte[] icon = SteamBridge.FetchIcon(get("icon"));
                    string game = DiscordBridge.Clean(get("game"), 80);
                    if (body == "" && game != "" && get("kind").ToLowerInvariant().Contains("game")) body = "Играет в " + game;
                    if (title == "" && game != "") title = game;   // «загрузка завершена», достижение без названия
                    if (title == "" && body == "") { title = "Steam"; body = kind; }
                    else if (title == "") title = kind;
                    else if (body == "") body = game != "" && game != title ? kind + " · " + game : kind;
                    object inv; bool invite = d.TryGetValue("invite", out inv) && inv is bool && (bool)inv && SteamBridge.InviteUrlAllowed(url);
                    string steamid = get("steamid"), appid = get("appid");
                    if (icon == null) icon = Steam.IconBytes(appid);   // «загрузка завершена» и подобные: значок игры с диска
                    bool hasFriend = System.Text.RegularExpressions.Regex.IsMatch(steamid, "^\\d{5,20}$");
                    bool message = hasFriend && !invite && get("kind").ToLowerInvariant() == "friend message";
                    // Куда ведёт клик: чат с этим другом, страница игры в библиотеке, иначе — то, что предложил сам Steam.
                    Action open;
                    if (hasFriend) open = delegate { SteamBridge.Send("{\"cmd\":\"openChat\",\"steamid\":\"" + steamid + "\"}"); };
                    else if (System.Text.RegularExpressions.Regex.IsMatch(appid, "^\\d{1,10}$")) open = delegate { try { System.Diagnostics.Process.Start("steam://nav/games/details/" + appid); } catch { } };
                    else { string target = SteamBridge.SteamUrlAllowed(url) ? url : "steam://open/main"; open = delegate { try { System.Diagnostics.Process.Start(target); } catch { } }; }
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (invite) SteamInvite(title, body, icon, url, steamid);
                        else SteamNotify(title, body, icon, open, message ? steamid : null);
                    }));
                }
                else if ((type as string) == "download")
                {
                    Func<string, string> get = delegate(string k) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : ""; };
                    string appid = get("appid"), name = DiscordBridge.Clean(get("name"), 60);
                    double pct, rate;
                    double.TryParse(get("percent"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pct);
                    double.TryParse(get("rate"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rate);
                    object pz; bool paused = d.TryGetValue("paused", out pz) && pz is bool && (bool)pz;
                    byte[] icon = appid != "" && appid != "0" && appid != steamDlApp ? Steam.IconBytes(appid) : null;
                    Dispatcher.BeginInvoke(new Action(delegate { SteamDownload(appid, name, pct, rate, paused, icon); }));
                }
                else if ((type as string) == "replied")
                {
                    object ok; bool sent = d.TryGetValue("ok", out ok) && ok is bool && (bool)ok;
                    Dispatcher.BeginInvoke(new Action(delegate { Toast("", sent ? "Отправлено" : "Не удалось отправить", null, -1, sent ? (Brush)accent : Dim); }));
                }
            };
            SteamBridge.Start();

            BuildTray();
            RenderMedia();
            RenderClips();
            OnTick();
            ApplyFrame();
            if (Store.Get("cityQuery") != "") RefreshWeather(Store.Get("cityQuery"));
        }

        // ------------------------------------------------------ каркас
        void BuildUi()
        {
            // Интерфейс рисуется в базовом размере и масштабируется целиком — так работает настройка «Размер».
            root = new Grid { RenderTransform = slide, Width = BaseW, Height = BaseH, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, LayoutTransform = scaleT };
            shape = new Path { Fill = Ink, SnapsToDevicePixels = false };
            root.Children.Add(shape);

            clipHost = new Grid();
            Color ac = accent.Color;
            glowTop = new GradientStop(Color.FromArgb(0, ac.R, ac.G, ac.B), 0);
            glowBottom = new GradientStop(Color.FromArgb(0x66, ac.R, ac.G, ac.B), 1);
            var gb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            gb.GradientStops.Add(glowTop); gb.GradientStops.Add(glowBottom);
            bottomGlow = new Border { Height = 70, VerticalAlignment = VerticalAlignment.Top, Background = gb, IsHitTestVisible = false, Opacity = 0 };
            clipHost.Children.Add(bottomGlow);

            expandedHost = new Grid { Width = EW, Height = EH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, RenderTransform = expandScale, RenderTransformOrigin = new Point(0.5, 0), Opacity = 0, IsHitTestVisible = false };
            miniHost = new Grid { Width = MW, Height = MH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, RenderTransform = miniScale, RenderTransformOrigin = new Point(0.5, 0), Opacity = 0, IsHitTestVisible = false };
            compactHost = new Grid { Height = CH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            var cardScale = new ScaleTransform(1, 1);
            var cmdScale = new ScaleTransform(1, 1);
            cardHost = new Grid { Width = CardW, Height = CardH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, RenderTransform = cardScale, RenderTransformOrigin = new Point(0.5, 0), Opacity = 0, IsHitTestVisible = false };
            cmdHost = new Grid { Width = CmdW, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, RenderTransform = cmdScale, RenderTransformOrigin = new Point(0.5, 0), Opacity = 0, IsHitTestVisible = false };
            BuildCompact();
            BuildMini();
            BuildExpanded();
            BuildCard();
            BuildCmd();
            clipHost.Children.Add(expandedHost);
            clipHost.Children.Add(miniHost);
            clipHost.Children.Add(cardHost);
            clipHost.Children.Add(cmdHost);
            clipHost.Children.Add(compactHost);
            layers = new Grid[] { compactHost, miniHost, expandedHost, cardHost, cmdHost };
            layerScale = new ScaleTransform[] { null, miniScale, expandScale, cardScale, cmdScale };
            root.Children.Add(clipHost);
            edge = new Path { Stroke = accent, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false, Opacity = 0 };
            root.Children.Add(edge);
            Content = root;
        }

        Border IconBtn(string glyph, double size, Action click)
        {
            var tb = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 15, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Brushes.Transparent, Child = tb, Cursor = Cursors.Hand };
            b.MouseEnter += delegate { b.Background = ChipHover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        Border RoundPlay(double size, out TextBlock glyph)
        {
            var tb = new TextBlock { Text = "", FontFamily = Icons, FontSize = size * 0.38, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Fg, Child = tb, Cursor = Cursors.Hand, Margin = new Thickness(14, 0, 14, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
            b.MouseEnter += delegate { Pulse(b, 1.08); };
            b.MouseLeave += delegate { Pulse(b, 1); };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; Pulse(b, 0.9); watcher.Command(0xB3); };
            b.MouseLeftButtonUp += delegate { Pulse(b, 1.08); };
            glyph = tb;
            return b;
        }

        static void Pulse(Border b, double to)
        {
            var st = (ScaleTransform)b.RenderTransform;
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        void BuildMini()
        {
            miniHost.Background = Brushes.Transparent;
            miniHost.Cursor = Cursors.Hand;
            miniHost.ToolTip = "Нажмите, чтобы открыть панель";
            miniHost.MouseLeftButtonDown += delegate { SetState(2); };

            // Колонка с текстом песни: появляется справа, когда текст найден
            miniLyrics = new LyricView(196, MH - 30, true, 0.3, Fg, 13, true);
            miniLyrics.Root.HorizontalAlignment = HorizontalAlignment.Right;
            miniLyrics.Root.VerticalAlignment = VerticalAlignment.Top;
            miniLyrics.Root.Margin = new Thickness(0, 8, 20, 0);
            miniLyrics.Root.Visibility = Visibility.Collapsed;
            miniHost.Children.Add(miniLyrics.Root);

            var g = new Grid { Margin = new Thickness(18, 12, 18, 16), Width = MW - 36, HorizontalAlignment = HorizontalAlignment.Left };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nCover = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(11), Background = Chip };
            top.Children.Add(nCover);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nTitle = new TextBlock { Foreground = Fg, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            nArtist = new TextBlock { Foreground = Dim, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            names.Children.Add(nTitle); names.Children.Add(nArtist);
            Grid.SetColumn(names, 2); top.Children.Add(names);
            nClock = new TextBlock { Foreground = Fg, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(nClock, 3); top.Children.Add(nClock);
            g.Children.Add(top);

            var pr = new Grid();
            pr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            pr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            nPos = new TextBlock { Foreground = Dim, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            nRem = new TextBlock { Foreground = Dim, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            nProgress = new Bar(MW - 36 - 76, 4, accent, 10);
            nProgress.Root.HorizontalAlignment = HorizontalAlignment.Center;
            pr.Children.Add(nPos);
            Grid.SetColumn(nProgress.Root, 1); pr.Children.Add(nProgress.Root);
            Grid.SetColumn(nRem, 2); pr.Children.Add(nRem);
            Grid.SetRow(pr, 2);
            g.Children.Add(pr);

            var ctl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            ctl.Children.Add(IconBtn("", 36, delegate { watcher.Command(0xB1); }));
            ctl.Children.Add(RoundPlay(34, out nPlayGlyph));
            ctl.Children.Add(IconBtn("", 36, delegate { watcher.Command(0xB0); }));
            Grid.SetRow(ctl, 4);
            g.Children.Add(ctl);
            miniHost.Children.Add(g);
        }

        // ------------------------------------------------- карточка события
        void BuildCard()
        {
            cardHost.Background = Brushes.Transparent;
            cardHost.Cursor = Cursors.Hand;
            cardHost.MouseLeftButtonDown += delegate { var a = cardAction; hoverBlock = true; SetState(0); if (a != null) a(); };
            var g = new Grid { Margin = new Thickness(18, 12, 18, 16) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var thumb = cardThumbBox = new Grid { Width = 84, Height = 52 };
            cardThumb = new Border { CornerRadius = new CornerRadius(10), Background = Chip };
            cardGlyph = new TextBlock { FontFamily = Icons, FontSize = 20, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            thumb.Children.Add(cardThumb); thumb.Children.Add(cardGlyph);
            // Без рамки: у круглых значков (Steam) она выглядела как лишнее кольцо.
            cardBadge = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -7, -7), Visibility = Visibility.Collapsed };
            thumb.Children.Add(cardBadge);
            g.Children.Add(thumb);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            cardTitle = new TextBlock { Foreground = Fg, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            cardSub = new TextBlock { Foreground = Dim, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            texts.Children.Add(cardTitle); texts.Children.Add(cardSub);
            Grid.SetColumn(texts, 2); g.Children.Add(texts);
            var open = new TextBlock { Text = "", FontFamily = Icons, FontSize = 15, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var openBtn = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = Chip, Child = open, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(openBtn, 3); g.Children.Add(openBtn);
            cardBtn = openBtn; cardBtnLabel = open;
            // Вторая кнопка — «отклонить» (крестик): есть только у карточек с выбором, например у приглашения в игру.
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cardNo = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = Chip, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed,
                Child = new TextBlock { Text = "", FontFamily = Icons, FontSize = 13, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            Grid.SetColumn(cardNo, 4); g.Children.Add(cardNo);
            cardNo.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                var a = cardNoAction;
                hoverBlock = true;
                SetState(0);
                if (a != null) a();
            };
            // У карточки может быть своя кнопка с подписью (например «+10 мин» у напоминания).
            openBtn.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (cardBtnAction == null) return;
                e.Handled = true;
                var a = cardBtnAction;
                hoverBlock = true;
                SetState(0);
                a();
            };
            cardHost.Children.Add(g);
        }

        void ShowCard(ImageSource img, string glyph, string title, string sub, Action act) { ShowCardEx(img, glyph, title, sub, act, null, null, false); }

        Border cardNo;
        Action cardNoAction;
        Grid cardThumbBox;
        Border cardBadge;        // значок программы, от которой пришло уведомление (Discord, Steam…), в углу картинки
        double cardExtra;        // карточки с кнопками шире обычных, чтобы текст не обрезался

        static readonly Dictionary<string, ImageSource> sourceIcons = new Dictionary<string, ImageSource>();

        // Значок программы-источника: берём у её собственного exe-файла на этом компьютере.
        static ImageSource SourceIcon(string source)
        {
            ImageSource hit;
            if (sourceIcons.TryGetValue(source, out hit)) return hit;
            ImageSource res = null;
            try
            {
                string exe = null;
                if (source == "steam") { string d = Steam.Dir(); if (d != null) exe = System.IO.Path.Combine(d, "steam.exe"); }
                else if (source == "discord")
                {
                    string root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
                    if (System.IO.Directory.Exists(root))
                    {
                        string[] apps = System.IO.Directory.GetDirectories(root, "app-*");
                        Array.Sort(apps, StringComparer.OrdinalIgnoreCase);
                        for (int i = apps.Length - 1; i >= 0 && exe == null; i--) { string f = System.IO.Path.Combine(apps[i], "Discord.exe"); if (System.IO.File.Exists(f)) exe = f; }
                    }
                }
                if (exe != null && System.IO.File.Exists(exe)) res = Shell.IconFor(exe);
            }
            catch { }
            sourceIcons[source] = res;
            return res;
        }

        // Круглая кнопка со значком папки нужна карточкам про файлы (снимок, запись, загрузка). У уведомлений её убираем.
        void HideCardButton() { if (state == 3) { cardBtn.Visibility = Visibility.Collapsed; FitCard(); } }

        static double NaturalWidth(FrameworkElement e)
        {
            if (e == null || e.Visibility != Visibility.Visible) return 0;
            e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return e.DesiredSize.Width;
        }

        // Ширина карточки — по содержимому: короткое уведомление не оставляет пустого места, длинное получает больше.
        void FitCard()
        {
            if (cardHost == null || state != 3) return;
            double text = Math.Max(NaturalWidth(cardTitle), NaturalWidth(cardSub));
            text = Math.Max(70, Math.Min(330, Math.Ceiling(text) + 2));
            double buttons = NaturalWidth(cardBtn) + NaturalWidth(cardNo);   // вместе со своими отступами
            double total = 18 + cardThumbBox.Width + 14 + text + buttons + 22;
            total = Math.Max(220, Math.Min(620, total));
            cardExtra = total - CardW;
            cardHost.Width = total;
            SetTargets();
        }

        // Вызывается сразу после показа карточки: ставит в угол картинки значок источника.
        void SetCardBadge(ImageSource icon)
        {
            if (cardBadge == null || state != 3 || icon == null) return;
            cardBadge.Background = new ImageBrush(icon) { Stretch = Stretch.UniformToFill };
            cardBadge.Visibility = Visibility.Visible;
        }

        // Карточка с выбором: кнопка с подписью и крестик. Висит дольше обычной.
        void ShowCardChoice(ImageSource img, string glyph, string title, string sub, string yesText, Action yes, Action no)
        {
            ShowCardEx(img, glyph, title, sub, delegate { }, yesText, yes, false);
            if (state != 3) return;   // панель открыта — показали короткую подсказку вместо карточки
            cardNoAction = no;
            cardNo.Visibility = Visibility.Visible;
            FitCard();
            cardUntil = Environment.TickCount + 15000;
        }

        void ShowCardEx(ImageSource img, string glyph, string title, string sub, Action act, string btnText, Action btnAct, bool force)
        {
            cardNoAction = null;
            if (cardNo != null) cardNo.Visibility = Visibility.Collapsed;
            if (cardBadge != null) cardBadge.Visibility = Visibility.Collapsed;
            cardExtra = btnText != null ? 70 : 0;
            cardHost.Width = CardW + cardExtra;
            cardBtn.Visibility = Visibility.Visible;
            cardBtnAction = btnAct;
            cardBtnLabel.Text = btnText ?? "";
            cardBtnLabel.FontFamily = btnText != null ? FontFamily : Icons;
            cardBtnLabel.FontSize = btnText != null ? 12 : 15;
            cardBtnLabel.FontWeight = btnText != null ? FontWeights.SemiBold : FontWeights.Normal;
            cardBtn.Width = btnText != null ? double.NaN : 36;
            cardBtn.Padding = btnText != null ? new Thickness(12, 0, 12, 1) : new Thickness(0);
            if (force && (state == 2 || state == 4)) SetState(0);
            if (state == 2 || state == 4) { Toast(glyph ?? "", title, null, -1, accent); return; }
            // Аватар и значок достижения — квадратные, как в Steam: так тексту остаётся больше места. Снимки экрана — широкие.
            bool square = img == null;   // карточка со значком вместо картинки — тоже квадратная
            var bs = img as BitmapSource;
            if (bs != null && bs.PixelHeight > 0) { double ratio = (double)bs.PixelWidth / bs.PixelHeight; square = ratio > 0.8 && ratio < 1.25; }
            cardThumbBox.Width = square ? 52 : 84;
            cardThumb.Background = img != null ? (Brush)new ImageBrush(img) { Stretch = Stretch.UniformToFill } : Chip;
            cardGlyph.Text = img != null ? "" : (glyph ?? "");
            cardTitle.Text = title;
            cardSub.Text = sub;
            cardAction = act;
            cardUntil = Environment.TickCount + 4200;
            if (state == 3) SetTargets(); else SetState(3);
            FitCard();
        }

        // Новая версия уже лежит на месте старой. Kapsula перезапустится сама, когда это не помешает; кнопка — для нетерпеливых.
        public void UpdateInstalled(string version)
        {
            ShowCardEx(null, "", "Kapsula обновлена до " + version, "Перезапустится сама, когда вы отойдёте", RestartForUpdate, "Перезапустить", RestartForUpdate, false);
            cardUntil = Environment.TickCount + 12000;
        }

        public void RestartForUpdate()
        {
            if (tray != null) tray.Visible = false;
            Updater.Restart();
        }

        // Перезапуск занимает секунду, но не должен обрывать запись экрана, таймер или открытую панель.
        public bool CanRestartNow(int idleMs)
        {
            if (state == 2 || state == 4) return false;
            if (Recorder.Active || timerRinging || timerEnd > DateTime.Now) return false;
            return Native.IdleMs() >= idleMs;
        }

        public void UpdateToast(string text) { Toast("", text, null, -1, null); }

        public void NotifyUpdated()
        {
            Toast("", "Kapsula обновлена до " + Updater.CurrentText, null, -1, null);
        }

        static void RevealFile(string path)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }

        void Arm(Tile t, string label, Action act)
        {
            int now = Environment.TickCount;
            if (armedTile == t && unchecked(now - armedAt) < 3000) { armedTile = null; t.Label.Text = label; act(); return; }
            if (armedTile != null) armedTile.Label.Text = armedLabel;
            armedTile = t; armedLabel = label; armedAt = now;
            t.Label.Text = "Ещё раз";
        }

        // Обложка для демо-режима: рисуем сами, чтобы в снимках не было чужих картинок.
        static byte[] DemoCover()
        {
            using (var bmp = new System.Drawing.Bitmap(300, 300))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var r = new System.Drawing.Rectangle(0, 0, 300, 300);
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(r, System.Drawing.Color.FromArgb(28, 22, 70), System.Drawing.Color.FromArgb(240, 110, 70), 60f)) g.FillRectangle(br, r);
                using (var sun = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 214, 140))) g.FillEllipse(sun, 150, 60, 110, 110);
                using (var hill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(20, 16, 44)))
                {
                    g.FillEllipse(hill, -120, 190, 380, 240);
                    g.FillEllipse(hill, 120, 215, 320, 200);
                }
                using (var ms = new System.IO.MemoryStream()) { bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); return ms.ToArray(); }
            }
        }

        // ---------------------------------------------------- ряд программ
        void LoadLauncher()
        {
            launcherRow.Children.Clear();
            int n = 0;
            try
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
                string[] items = new string[0];
                if (Demo)
                {
                    string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    var std = new List<string>();
                    foreach (string f in new string[] { @"explorer.exe", @"System32\notepad.exe", @"System32\calc.exe", @"System32\mspaint.exe", @"System32\SnippingTool.exe", @"System32\cmd.exe" })
                        if (System.IO.File.Exists(System.IO.Path.Combine(win, f))) std.Add(System.IO.Path.Combine(win, f));
                    items = std.ToArray();
                }
                else if (System.IO.Directory.Exists(dir)) items = System.IO.Directory.GetFiles(dir, "*.lnk");
                    foreach (string lnk in items)
                    {
                        if (n >= 13) break;
                        ImageSource icon = Shell.IconFor(lnk);
                        if (icon == null) continue;
                        string path = lnk;
                        var img = new Image { Source = icon, Width = 24, Height = 24 };
                        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                        var b = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(11), Background = Brushes.Transparent, Child = img, Cursor = Cursors.Hand, ToolTip = System.IO.Path.GetFileNameWithoutExtension(lnk), Margin = new Thickness(1, 0, 1, 0) };
                        b.MouseEnter += delegate { b.Background = ChipHover; };
                        b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
                        b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; try { System.Diagnostics.Process.Start(path); } catch { } };
                        launcherRow.Children.Add(b);
                        n++;
                    }
            }
            catch { }
            if (n == 0) launcherRow.Children.Add(new TextBlock { Text = "Закрепите программы на панели задач — они появятся здесь", Foreground = Faint, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        }

        // --------------------------------------------------------- загрузки
        static string DownloadsDir()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"))
                {
                    string v = k == null ? null : k.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
                    if (!string.IsNullOrEmpty(v)) return Environment.ExpandEnvironmentVariables(v);
                }
            }
            catch { }
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        static bool IsTempDownload(string f)
        {
            string e = System.IO.Path.GetExtension(f).ToLowerInvariant();
            return e == ".crdownload" || e == ".part" || e == ".opdownload" || e == ".download";
        }

        static long FileLen(string f)
        {
            try { using (var fs = new System.IO.FileStream(f, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete)) return fs.Length; }
            catch { try { return new System.IO.FileInfo(f).Length; } catch { return -1; } }
        }

        static string SizeText(double b)
        {
            if (b >= 1073741824.0) return (b / 1073741824.0).ToString("0.0") + " ГБ";
            if (b >= 1048576.0) return (b / 1048576.0).ToString("0.0") + " МБ";
            return Math.Max(1, Math.Round(b / 1024)) + " КБ";
        }

        // Смотрим только на имена и размеры файлов в папке «Загрузки»: растущий временный файл браузера — это идущая загрузка.
        void PollDownloads()
        {
            try
            {
                if (dlDir == null) dlDir = DownloadsDir();
                if (!System.IO.Directory.Exists(dlDir)) return;
                int now = Environment.TickCount;
                double dt = dlPollAt == 0 ? 1 : Math.Max(0.2, unchecked(now - dlPollAt) / 1000.0);
                dlPollAt = now;
                string grow = null; long growSize = 0, growDelta = 0;
                var seen = new Dictionary<string, long>();
                foreach (string f in System.IO.Directory.GetFiles(dlDir))
                {
                    if (!IsTempDownload(f)) continue;
                    long len = FileLen(f);
                    if (len < 0) continue;
                    seen[f] = len;
                    long prev;
                    if (dlSizes.TryGetValue(f, out prev) && len > prev && len - prev >= growDelta) { grow = f; growSize = len; growDelta = len - prev; }
                }
                dlSizes = seen;
                if (grow != null)
                {
                    dlSpeed = dlActive == grow ? dlSpeed * 0.5 + (growDelta / dt) * 0.5 : growDelta / dt;
                    bool started = dlActive == null;
                    dlActive = grow; dlSize = growSize; dlSeenAt = now;
                    RenderCompact();
                    if (started) SetTargets();
                }
                else if (dlActive != null)
                {
                    if (!System.IO.File.Exists(dlActive))
                    {
                        dlActive = null;
                        string done = NewestFinished();
                        RenderCompact(); SetTargets();
                        if (done != null)
                        {
                            long len = FileLen(done);
                            ShowCard(null, "", "Загружено", System.IO.Path.GetFileName(done) + (len > 0 ? " · " + SizeText(len) : ""), delegate { RevealFile(done); });
                        }
                    }
                    else if (unchecked(now - dlSeenAt) > 12000) { dlActive = null; RenderCompact(); SetTargets(); }
                }
            }
            catch (Exception ex) { LogOnce("загрузки", ex); }
        }

        // Повторяющуюся ошибку пишем в журнал один раз, а не каждую секунду.
        HashSet<string> loggedOnce = new HashSet<string>();
        void LogOnce(string where, Exception ex)
        {
            if (loggedOnce.Add(where + "|" + ex.GetType().Name)) Store.LogError(where, ex);
        }

        string NewestFinished()
        {
            string best = null; DateTime bestT = DateTime.MinValue;
            foreach (string f in System.IO.Directory.GetFiles(dlDir))
            {
                if (IsTempDownload(f)) continue;
                DateTime t = System.IO.File.GetLastWriteTimeUtc(f);
                DateTime c = System.IO.File.GetCreationTimeUtc(f);
                if (c > t) t = c;
                if (t > bestT) { bestT = t; best = f; }
            }
            return best != null && (DateTime.UtcNow - bestT).TotalSeconds < 25 ? best : null;
        }

        // -------------------------------------------------- командная строка
        void BuildCmd()
        {
            var g = new Grid { Margin = new Thickness(20, 8, 20, 0), VerticalAlignment = VerticalAlignment.Top };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 15, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center });
            var hint = cmdHint = new TextBlock { Text = CmdHintDefault, Foreground = Faint, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(2, 0, 0, 1) };
            Grid.SetColumn(hint, 1); top.Children.Add(hint);
            cmdGlyph = top.Children[0] as TextBlock;   // лупа; в режиме ответа другу заменяется значком сообщения
            cmdGlyphDefault = cmdGlyph.Text;
            cmdBox = MakeTextBox(false);
            cmdBox.FontSize = 15;
            cmdBox.TextChanged += delegate { hint.Visibility = cmdBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; UpdateCmd(); };
            cmdBox.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Down) { cmdSel = Math.Min(cmdItems.Count - 1, cmdSel + 1); RenderCmd(); e.Handled = true; }
                else if (e.Key == Key.Up) { cmdSel = Math.Max(0, cmdSel - 1); RenderCmd(); e.Handled = true; }
                else if (e.Key == Key.Enter) { RunCmd(cmdSel); e.Handled = true; }
            };
            Grid.SetColumn(cmdBox, 1); top.Children.Add(cmdBox);
            g.Children.Add(top);
            cmdList = new StackPanel { Margin = new Thickness(-8, 6, -8, 0) };
            Grid.SetRow(cmdList, 1);
            g.Children.Add(cmdList);
            cmdHost.Children.Add(g);
        }

        void IndexApps()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                var list = new List<KeyValuePair<string, string>>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Environment.SpecialFolder sf in new Environment.SpecialFolder[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
                {
                    var files = new List<string>();
                    WalkLnk(Environment.GetFolderPath(sf), files, 0);
                    foreach (string f in files)
                    {
                        string name = System.IO.Path.GetFileNameWithoutExtension(f);
                        string low = name.ToLowerInvariant();
                        if (low.Contains("uninstall") || low.Contains("удал") || low.Contains("readme") || !seen.Add(name)) continue;
                        list.Add(new KeyValuePair<string, string>(name, f));
                    }
                }
                Dispatcher.BeginInvoke(new Action(delegate { apps = list; }));
            });
        }

        // Обход папок по одной: служебные ссылки-папки с запретом доступа пропускаются, а не обрывают весь поиск.
        static void WalkLnk(string dir, List<string> files, int depth)
        {
            if (depth > 6 || string.IsNullOrEmpty(dir)) return;
            try { files.AddRange(System.IO.Directory.GetFiles(dir, "*.lnk")); } catch { return; }
            string[] subs;
            try { subs = System.IO.Directory.GetDirectories(dir); } catch { return; }
            foreach (string s in subs)
            {
                try { if ((System.IO.File.GetAttributes(s) & System.IO.FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                WalkLnk(s, files, depth + 1);
            }
        }

        const string CmdHintDefault = "Программа, 2400 * 15%, 5 миль в км, таймер 10 мин…";
        TextBlock cmdHint, cmdGlyph;
        string cmdGlyphDefault;
        string replySteamId, replyName;   // строка ввода работает как поле ответа другу в Steam
        bool hoverBlock;                  // после клика по карточке остров не раскрывается, пока курсор с него не уйдёт

        // Ответ на сообщение Steam прямо на острове: та же строка ввода, Enter отправляет.
        void StartReply(string steamid, string name)
        {
            if (state == 4) SetState(0);
            replySteamId = steamid; replyName = name;
            cmdHint.Text = "Ответ для " + name + " — Enter отправит";
            cmdGlyph.Text = ""; cmdGlyph.Foreground = accent;
            ToggleCommand();
        }

        void SendSteamReply(string steamid, string text)
        {
            string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(text);
            SteamBridge.Send("{\"cmd\":\"reply\",\"steamid\":\"" + steamid + "\",\"text\":" + json + "}");
        }

        void ToggleCommand()
        {
            if (state == 4) { SetState(0); return; }
            // Запоминаем окно, в котором вы работали, чтобы вернуть ему фокус после закрытия строки.
            prevFg = Native.GetForegroundWindow();
            if (prevFg == hwnd) prevFg = IntPtr.Zero;
            cmdBox.Text = "";
            cmdItems.Clear();
            RenderCmd();
            SetState(4);
            SetNoActivate(false);
            Activate();
            cmdBox.Focus();
            Keyboard.Focus(cmdBox);
        }

        List<string> fileHits = new List<string>();
        string fileHitsFor;
        int fileReq;

        // «C:\Users\имя\Documents\Отчёты\file.docx» → «Documents\Отчёты»: только две последние папки
        static string ShortFolder(string path)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(path) ?? "";
                string[] parts = dir.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length <= 2) return dir;
                return parts[parts.Length - 2] + "\\" + parts[parts.Length - 1];
            }
            catch { return "Файл"; }
        }

        void AddCmd(string title, string sub, string glyph, string iconPath, Action run)
        {
            if (cmdItems.Count >= 6) return;
            cmdItems.Add(new CmdItem { Title = title, Sub = sub, Glyph = glyph, IconPath = iconPath, Run = run });
        }

        void CopyResult(string text)
        {
            try { Clipboard.SetText(text); } catch { }
            Toast("", "Скопировано: " + text, null, -1, accent);
        }

        void UpdateCmd()
        {
            string q = cmdBox.Text.Trim();
            cmdItems.Clear();
            cmdSel = 0;
            if (replySteamId != null)
            {
                // Режим ответа: единственный пункт — отправить набранное
                if (q.Length > 0)
                {
                    string id = replySteamId, text = q;
                    AddCmd("Отправить: " + (q.Length > 60 ? q.Substring(0, 60) + "…" : q), replyName, "", null, delegate { SendSteamReply(id, text); });
                }
                RenderCmd();
                if (state == 4) SetTargets();
                return;
            }
            if (q.Length > 0)
            {
                string r; TimeSpan ts;
                if (dictating)
                {
                    // Режим диктовки: первым пунктом — вставить надиктованное туда, где вы печатали.
                    string spoken = q;
                    AddCmd("Вставить в активное окно", "Enter", "", null, delegate { PasteText(spoken); });
                    AddCmd("Только скопировать", "Буфер", "", null, delegate { CopyResult(spoken); });
                }
                if (Calc.TryCalc(q, out r)) { string v = r; AddCmd("= " + v, "Enter — скопировать", "", null, delegate { CopyResult(v); }); }
                if (Calc.TryConvert(q, out r)) { string v = r; AddCmd(v, "Enter — скопировать", "", null, delegate { CopyResult(v); }); }
                if (Calc.TryTimer(q, out ts)) { TimeSpan t = ts; AddCmd("Запустить таймер на " + Fmt(t), "Таймер", "", null, delegate { AddTimerSpan(t); }); }

                string ql = q.ToLowerInvariant();
                if (ql.Length >= 2)
                {
                    if (HasKey(ql, "блок", "заблок", "lock")) AddCmd("Заблокировать компьютер", "Система", "", null, delegate { Native.LockWorkStation(); });
                    if (HasKey(ql, "тема", "тёмн", "темн", "theme")) AddCmd("Переключить тёмную тему", "Система", "", null, delegate { SetDarkTheme(!IsDarkTheme()); });
                    if (HasKey(ql, "настр", "settings", "цвет остр")) AddCmd("Настройки Kapsula", "Цвет, размер, поведение", "", null, delegate { OpenSettings(); });
                    if (HasKey(ql, "светл", "light")) AddCmd("Светлая / тёмная тема острова", "Ctrl+Alt+T", "", null, delegate { ToggleLightDark(); });
                    if (HasKey(ql, "чат", "claude", "клод", "ии ")) AddCmd("Открыть чат с Claude", "Ctrl+Alt+A", "", null, delegate { OpenChat(); });
                    if (!dictating && HasKey(ql, "диктов", "голос", "voice")) AddCmd("Диктовка: надиктовать и вставить", "Ctrl+Alt+D", "", null, delegate { Dictate(); });
                    if (HasKey(ql, "qr", "кьюар", "куар")) AddCmd("Прочитать QR-код с экрана", "Ctrl+Alt+Q", "", null, delegate { ScanQr(); });
                    if (HasKey(ql, "запис", "record", "видео")) AddCmd(Recorder.Active ? "Остановить запись экрана" : "Записать экран", "Ctrl+Alt+R", "", null, delegate { ToggleRecord(false); });
                    if (HasKey(ql, "скрин", "снимок", "screenshot")) AddCmd("Снимок области", "Ctrl+Alt+S", "", null, delegate { StartSnip(false); });
                    if (HasKey(ql, "пипет", "цвет", "color")) AddCmd("Пипетка", "Ctrl+Alt+C", "", null, delegate { StartSnip(true); });
                }

                // Игры Steam: набрали название — запустили
                if (ql.Length >= 2 && Steam.Enabled)
                {
                    int games = 0;
                    for (int pass = 0; pass < 2 && games < 3; pass++)
                        foreach (SteamGame sg in steamGames)
                        {
                            if (games >= 3) break;
                            string low = sg.Name.ToLowerInvariant();
                            bool starts = low.StartsWith(ql);
                            if (pass == 0 ? !starts : (starts || !low.Contains(ql))) continue;
                            string id = sg.Id;
                            AddCmd(sg.Name, sg.Ready ? "Игра Steam" : "Игра Steam · скачивается", "", null, delegate { Steam.Launch(id); });
                            games++;
                        }
                }

                int found = 0;
                for (int pass = 0; pass < 2 && found < 4; pass++)
                    foreach (var a in apps)
                    {
                        if (found >= 4) break;
                        string low = a.Key.ToLowerInvariant();
                        bool starts = low.StartsWith(ql);
                        if (pass == 0 ? !starts : (starts || !low.Contains(ql))) continue;
                        string path = a.Value;
                        AddCmd(a.Key, "Программа", null, path, delegate { try { System.Diagnostics.Process.Start(path); } catch { } });
                        found++;
                    }

                // Файлы по имени: ищем в фоне через индекс Windows, а когда ответ пришёл — перерисовываем список.
                if (ql.Length >= 3 && !dictating)
                {
                    if (fileHitsFor == q)
                    {
                        int files = 0;
                        foreach (string f in fileHits)
                        {
                            if (files >= 3 || cmdItems.Count >= 5) break;
                            string path = f;
                            AddCmd(System.IO.Path.GetFileName(path), ShortFolder(path), null, path, delegate { try { System.Diagnostics.Process.Start(path); } catch { } });
                            files++;
                        }
                    }
                    else
                    {
                        int req = ++fileReq;
                        string asked = q;
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            List<string> hits = FileSearch.Find(asked, 6);
                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                if (req != fileReq || state != 4 || cmdBox.Text.Trim() != asked) return;
                                fileHitsFor = asked; fileHits = hits;
                                if (hits.Count > 0) { int keep = cmdSel; UpdateCmd(); cmdSel = Math.Min(keep, Math.Max(0, cmdItems.Count - 1)); RenderCmd(); }
                            }));
                        });
                    }
                }

                string query = q;
                AddCmd("Искать в интернете: " + q, "Браузер", "", null, delegate { try { System.Diagnostics.Process.Start("https://www.google.com/search?q=" + Uri.EscapeDataString(query)); } catch { } });
            }
            RenderCmd();
            if (state == 4) SetTargets();
        }

        static bool HasKey(string q, params string[] keys)
        {
            foreach (string k in keys) if (k.StartsWith(q) || q.StartsWith(k)) return true;
            return false;
        }

        void RunCmd(int i)
        {
            if (i < 0 || i >= cmdItems.Count) return;
            Action run = cmdItems[i].Run;
            SetState(0);
            if (run != null) run();
        }

        void RenderCmd()
        {
            cmdList.Children.Clear();
            for (int i = 0; i < cmdItems.Count; i++)
            {
                int idx = i;
                CmdItem it = cmdItems[i];
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                ImageSource icon = it.IconPath != null ? Shell.IconFor(it.IconPath) : null;
                if (icon != null)
                {
                    var img = new Image { Source = icon, Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    row.Children.Add(img);
                }
                else row.Children.Add(new TextBlock { Text = it.Glyph ?? "", FontFamily = Icons, FontSize = 15, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                var title = new TextBlock { Text = it.Title, Foreground = Fg, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 1) };
                Grid.SetColumn(title, 1); row.Children.Add(title);
                var sub = new TextBlock { Text = it.Sub ?? "", Foreground = Faint, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
                Grid.SetColumn(sub, 2); row.Children.Add(sub);
                var b = new Border { Height = 38, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 4), Background = i == cmdSel ? ChipHover : Brushes.Transparent, Child = row, Cursor = Cursors.Hand };
                b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; RunCmd(idx); };
                cmdList.Children.Add(b);
            }
        }

        double MiniW() { return lyrics != null ? MW + 212 : MW; }

        static bool LyricsOn() { return Store.Get("lyrics") != "off"; }

        void SetLyrics(List<LyricLine> list)
        {
            lyrics = list;
            miniLyrics.SetLines(list);
            homeLyrics.SetLines(list);
            miniLyrics.Root.Visibility = list != null ? Visibility.Visible : Visibility.Collapsed;
            homeLyrics.Root.Visibility = list != null ? Visibility.Visible : Visibility.Collapsed;
            miniHost.Width = MiniW();
            if (list != null) UpdateLyrics();
            if (state == 1) SetTargets();
        }

        void FetchLyrics(string key, string title, string artist, double dur)
        {
            int req = ++lyricsReq;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<LyricLine> l = Lyrics.Fetch(title, artist, dur);
                // Применяем только ответ на самый свежий запрос — старый мог искать с неверной длительностью.
                Dispatcher.BeginInvoke(new Action(delegate { if (req == lyricsReq && key == lyricsKey && media.Has) SetLyrics(l); }));
            });
        }

        void UpdateLyrics()
        {
            if (lyrics == null) return;
            // Начинаем движение чуть раньше, чтобы строка доехала к моменту, когда её поют.
            double pos = media.Now() + 0.3 + LyricOffset;
            int idx = -1;
            for (int i = 0; i < lyrics.Count; i++) { if (lyrics[i].T <= pos) idx = i; else break; }
            miniLyrics.Tick(idx);
            homeLyrics.Tick(idx);
        }

        void SetClock(TextBlock tb)
        {
            tb.Inlines.Clear();
            tb.Inlines.Add(new System.Windows.Documents.Run(DateTime.Now.ToString(ShowSeconds ? "HH:mm:ss" : "HH:mm")));
            if (weather != null && weather.Error == null)
            {
                tb.Inlines.Add(new System.Windows.Documents.Run("   " + Weather.Symbol(weather.Code) + " ") { FontFamily = Symbols, Foreground = Amber });
                tb.Inlines.Add(new System.Windows.Documents.Run(Weather.Temp(weather.Temp)));
            }
        }

        void UpdateAccent(Color c)
        {
            rawAccent = c;
            // На светлом острове подсветку затемняем, иначе она теряется на белом.
            if (IsLight(Ink.Color)) c = Mix(c, Colors.Black, 0.42);
            accent.Color = c;
            glowTop.Color = Color.FromArgb(0, c.R, c.G, c.B);
            glowBottom.Color = Color.FromArgb(0x66, c.R, c.G, c.B);
            coverGlowA.Color = Color.FromArgb(0x70, c.R, c.G, c.B);
            coverGlowB.Color = Color.FromArgb(0, c.R, c.G, c.B);
        }

        void BuildCompact()
        {
            var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Ear + 12, 0, 0, 0) };
            micDot = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, ToolTip = "Микрофон используется" };
            cCover = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(6), Background = Chip, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            cGlyph = new TextBlock { FontFamily = Icons, FontSize = 14, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            left.Children.Add(micDot);
            cDlIcon = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Background = Chip, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            left.Children.Add(cDlIcon);
            cDlName = new TextBlock { Foreground = Fg, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), MaxWidth = 104, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
            left.Children.Add(cDlName);
            // «Глаза»: следят за курсором, моргают и закрываются, когда вы отошли от компьютера.
            eyes = new Canvas { Width = 26, Height = 14, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(5, 0, 6, 0) };
            BuildCharacter();
            left.Children.Add(eyes);
            left.Children.Add(cCover);
            left.Children.Add(cGlyph);
            compactHost.Children.Add(left);

            cText = new TextBlock { Foreground = Fg, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            compactHost.Children.Add(cText);
            cBar = new Bar(130, 5, Fg, 12);
            cBar.Root.HorizontalAlignment = HorizontalAlignment.Center;
            cBar.Root.Visibility = Visibility.Collapsed;
            compactHost.Children.Add(cBar.Root);

            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, Ear + 13, 0) };
            cRight = new TextBlock { Foreground = Dim, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            right.Children.Add(cRight);
            eq = new StackPanel { Orientation = Orientation.Horizontal, Height = 14, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            for (int i = 0; i < 4; i++)
                eq.Children.Add(new Rectangle { Width = 2.5, Height = 14, RadiusX = 1.25, RadiusY = 1.25, Fill = accent, Margin = new Thickness(1.25, 0, 1.25, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 0.25) });
            right.Children.Add(eq);
            compactHost.Children.Add(right);
        }

        void BuildExpanded()
        {
            var g = new Grid { Margin = new Thickness(18, 12, 18, 16) };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(PageH) });

            var tabRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            string[] names = { "Главная", "Буфер", "Полка", "Текст", "Чат", "Заметка", "Погода", "Система" };
            // Вкладки — значки; подпись видна только у активной.
            string[] glyphs = { "", "", "", "", "", "", "", "" };
            tabs = new Border[names.Length];
            tabLabels = new TextBlock[names.Length];
            tabGlyphs = new TextBlock[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var gl = new TextBlock { Text = glyphs[i], FontFamily = Icons, FontSize = 14, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center };
                var tb = new TextBlock { Text = names[i], FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 1), Visibility = Visibility.Collapsed };
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(gl); row.Children.Add(tb);
                var b = new Border { Child = row, Padding = new Thickness(12, 0, 12, 0), Height = 30, CornerRadius = new CornerRadius(15), Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(2, 0, 2, 0), ToolTip = names[i] };
                b.MouseLeftButtonDown += delegate { ShowPage(idx); };
                tabs[i] = b; tabLabels[i] = tb; tabGlyphs[i] = gl;
                tabRow.Children.Add(b);
            }
            g.Children.Add(tabRow);

            pages = new FrameworkElement[] { BuildHome(), BuildClipPage(), BuildShelfPage(), BuildTextPage(), BuildChatPage(), BuildNotePage(), BuildWeatherPage(), BuildSystemPage() };
            foreach (var p in pages) { Grid.SetRow(p, 2); g.Children.Add(p); }
            expandedHost.Children.Add(g);
            ShowPage(0);
        }

        void ShowPage(int idx)
        {
            page = idx;
            if (state == 2) SetTargets();   // у «Главной» своя высота
            for (int i = 0; i < pages.Length; i++)
            {
                bool on = i == idx;
                pages[i].Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                tabs[i].Background = on ? TabOn : Brushes.Transparent;
                tabLabels[i].Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                tabGlyphs[i].Foreground = on ? Fg : Dim;
            }
            if (idx != PClip && idx != PNote && idx != PWeather && idx != PChat) { Keyboard.ClearFocus(); }
            if (idx == PChat) RenderChatEmpty();
            if (idx == PShelf) RenderShelf();
            pages[idx].Opacity = 0;
            var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            pages[idx].BeginAnimation(UIElement.OpacityProperty, a);
            if (idx == PText) RenderTextPreview();
            if (idx == PSystem) { RenderSystem(); ShowBt(TestBt); RefreshControl(); }
            if (idx == PNote) UpdateRing();
            if (idx == PWeather && weather != null && weather.Error == null) { weatherAnimAt = Environment.TickCount | 1; AnimateWeather(); }
        }

        Border Btn(string text, bool glyph, double w, double h, Action click)
        {
            var tb = new TextBlock { Text = text, Foreground = Fg, FontSize = glyph ? 14 : 12, FontWeight = glyph ? FontWeights.Normal : FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (glyph) tb.FontFamily = Icons;
            var b = new Border { Height = h, CornerRadius = new CornerRadius(h / 2), Background = Chip, Child = tb, Cursor = Cursors.Hand };
            if (w > 0) b.Width = w; else b.Padding = new Thickness(13, 0, 13, 1);
            b.MouseEnter += delegate { b.Background = ChipHover; };
            b.MouseLeave += delegate { b.Background = Chip; };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        Border srcBtn;
        TextBlock srcLabel;

        void NextPlayer()
        {
            string[] ids = media.Sources;
            if (ids == null || ids.Length < 2) return;
            int i = Array.IndexOf(ids, media.Source);
            string next = ids[(i + 1 + ids.Length) % ids.Length];
            watcher.Preferred = next;
            Store.Set("player", next);
            srcLabel.Text = MediaWatcher.PrettyName(next);
            Toast("", "Плеер: " + MediaWatcher.PrettyName(next), null, -1, accent);
        }

        // ------------------------------------------------------ Главная
        // ---- «Главная» под себя: какие части показывать. Остров перестраивается и уменьшается под то, что осталось.
        Grid homeGrid;
        Border homePlayerCard, homeVolCard, homeLaunchCard;
        UniformGrid homeTilesGrid;
        Tile[] homeTileList;
        TextBlock homeEmpty;
        double homeCut;   // на сколько «Главная» ниже полной высоты
        public static readonly string[] HomeTileIds = { "area", "full", "qr", "pipette", "rec", "dictate", "ocr", "plain" };
        public static readonly string[] HomeTileNames = { "Область", "Весь экран", "QR-код", "Пипетка", "Запись", "Диктовка", "Текст", "Без стиля" };

        public void ApplyHomeLayout()
        {
            if (homeGrid == null) return;
            bool player = Store.Get("homePlayer") != "0", vol = Store.Get("homeVolume") != "0", launch = Store.Get("homeLauncher") != "0";
            var off = new HashSet<string>(Store.Get("homeTilesOff").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            homeTilesGrid.Children.Clear();
            int n = 0;
            for (int i = 0; i < homeTileList.Length; i++)
                if (!off.Contains(HomeTileIds[i])) { homeTilesGrid.Children.Add(homeTileList[i].Root); n++; }
            bool tiles = n > 0;
            homePlayerCard.Visibility = player ? Visibility.Visible : Visibility.Collapsed;
            homeTilesGrid.Visibility = tiles ? Visibility.Visible : Visibility.Collapsed;
            homeVolCard.Visibility = vol ? Visibility.Visible : Visibility.Collapsed;
            homeLaunchCard.Visibility = launch ? Visibility.Visible : Visibility.Collapsed;

            double top;
            if (player && tiles)
            {
                // плеер слева, плитки столбиком справа
                Grid.SetColumnSpan(homePlayerCard, 1);
                Grid.SetColumn(homeTilesGrid, 2); Grid.SetColumnSpan(homeTilesGrid, 1);
                homeTilesGrid.Columns = 2; homeTilesGrid.Rows = Math.Max(1, (n + 1) / 2);
                top = 200;
            }
            else if (tiles)
            {
                // плеера нет — плитки во всю ширину, по четыре в ряд
                Grid.SetColumn(homeTilesGrid, 0); Grid.SetColumnSpan(homeTilesGrid, 3);
                homeTilesGrid.Columns = Math.Min(4, n); homeTilesGrid.Rows = (n + 3) / 4;
                top = homeTilesGrid.Rows * 52;
            }
            else if (player) { Grid.SetColumnSpan(homePlayerCard, 3); top = 200; }
            else top = 0;

            if (mProgress != null) mProgress.Resize(player && !tiles ? 444 : 244);   // плеер во всю ширину — и полоса перемотки длиннее
            bool nothing = top == 0 && !vol && !launch;
            homeEmpty.Visibility = nothing ? Visibility.Visible : Visibility.Collapsed;
            if (nothing) top = 46;
            homeGrid.RowDefinitions[0].Height = new GridLength(top);
            homeGrid.RowDefinitions[1].Height = new GridLength(top > 0 && (vol || launch) ? 10 : 0);
            homeGrid.RowDefinitions[2].Height = new GridLength(vol ? 34 : 0);
            homeGrid.RowDefinitions[3].Height = new GridLength(vol && launch ? 10 : 0);
            homeGrid.RowDefinitions[4].Height = new GridLength(launch ? 46 : 0);
            double total = 0;
            foreach (RowDefinition r in homeGrid.RowDefinitions) total += r.Height.Value;
            homeGrid.Height = total;
            homeCut = Math.Max(0, PageH - total);
            if (state == 2) SetTargets();
        }

        // Высота раскрытого острова: «Главная» может быть ниже остальных вкладок
        double ExpandedH() { return EH - (page == 0 ? homeCut : 0); }

        FrameworkElement BuildHome()
        {
            var g = homeGrid = new Grid { VerticalAlignment = VerticalAlignment.Top };
            homeEmpty = new TextBlock { Text = "«Главная» пуста — включите что-нибудь в настройках", Foreground = Dim, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            Grid.SetColumnSpan(homeEmpty, 3);
            g.Children.Add(homeEmpty);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(188) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });

            // Ряд программ, закреплённых на панели задач
            launcherRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
            var launchCard = homeLaunchCard = new Border { Background = Card, CornerRadius = new CornerRadius(17), Child = launcherRow };
            Grid.SetRow(launchCard, 4); Grid.SetColumnSpan(launchCard, 3);
            g.Children.Add(launchCard);
            LoadLauncher();

            // Плеер
            var card = homePlayerCard = new Border { Background = Card, CornerRadius = new CornerRadius(22), Padding = new Thickness(16) };
            var cardGrid = new Grid();
            // Мягкое свечение в цвет обложки
            coverGlowA = new GradientStop(Color.FromArgb(0x70, DefaultAccent.R, DefaultAccent.G, DefaultAccent.B), 0);
            coverGlowB = new GradientStop(Color.FromArgb(0, DefaultAccent.R, DefaultAccent.G, DefaultAccent.B), 1);
            var rg = new RadialGradientBrush();
            rg.GradientStops.Add(coverGlowA); rg.GradientStops.Add(coverGlowB);
            coverGlow = new Border { Width = 170, Height = 170, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-43, -43, 0, 0), Background = rg, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            cardGrid.Children.Add(coverGlow);
            playerEmpty =new TextBlock { Text = "Сейчас ничего не играет", Foreground = Dim, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            cardGrid.Children.Add(playerEmpty);

            playerBody = new Grid();
            playerBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });
            playerBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            playerBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mCover = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(16), Background = Chip };
            top.Children.Add(mCover);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            mTitle = new TextBlock { Foreground = Fg, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            mArtist = new TextBlock { Foreground = Dim, FontSize = 13, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            homeLyrics = new LyricView(216, 20, false, 0, accent, 12.5, false);
            homeLyrics.Root.HorizontalAlignment = HorizontalAlignment.Left;
            homeLyrics.Root.Margin = new Thickness(0, 7, 0, 0);
            homeLyrics.Root.Visibility = Visibility.Collapsed;
            names.Children.Add(mTitle);
            names.Children.Add(mArtist);
            names.Children.Add(homeLyrics.Root);
            Grid.SetColumn(names, 2);
            top.Children.Add(names);
            playerBody.Children.Add(top);

            progressRow = new Grid { VerticalAlignment = VerticalAlignment.Center };
            progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            mPos = new TextBlock { Foreground = Dim, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            mDur = new TextBlock { Foreground = Dim, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            mProgress = new Bar(244, 4, accent, 10);
            mProgress.Root.HorizontalAlignment = HorizontalAlignment.Center;
            progressRow.Children.Add(mPos);
            Grid.SetColumn(mProgress.Root, 1); progressRow.Children.Add(mProgress.Root);
            Grid.SetColumn(mDur, 2); progressRow.Children.Add(mDur);
            Grid.SetRow(progressRow, 1);
            playerBody.Children.Add(progressRow);

            var ctl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var prev = IconBtn("", 38, delegate { watcher.Command(0xB1); });
            var play = RoundPlay(42, out mPlayGlyph);
            var next = IconBtn("", 38, delegate { watcher.Command(0xB0); });
            prev.VerticalAlignment = VerticalAlignment.Center; next.VerticalAlignment = VerticalAlignment.Center;
            ctl.Children.Add(prev); ctl.Children.Add(play); ctl.Children.Add(next);
            Grid.SetRow(ctl, 2);
            playerBody.Children.Add(ctl);
            cardGrid.Children.Add(playerBody);
            // Если играют сразу несколько программ — кнопка с названием плеера: клик переключает на следующий.
            srcBtn = Btn("", false, 0, 22, delegate { NextPlayer(); });
            srcBtn.HorizontalAlignment = HorizontalAlignment.Right; srcBtn.VerticalAlignment = VerticalAlignment.Top;
            srcBtn.Margin = new Thickness(0, -6, -6, 0); srcBtn.Padding = new Thickness(9, 0, 9, 1);
            srcBtn.Visibility = Visibility.Collapsed;
            srcBtn.ToolTip = "Какой плеер показывать. Клик — следующий";
            srcLabel = (TextBlock)srcBtn.Child; srcLabel.FontSize = 10.5;
            cardGrid.Children.Add(srcBtn);
            card.Child = cardGrid;
            card.SizeChanged += delegate { card.Clip = new RectangleGeometry(new Rect(0, 0, card.ActualWidth, card.ActualHeight), 22, 22); };
            g.Children.Add(card);

            // Быстрые действия
            var tiles = homeTilesGrid = new UniformGrid { Rows = 4, Columns = 2, Margin = new Thickness(-3) };
            recTile = new Tile("", "Запись", delegate { ToggleRecord(false); }).Compact();
            recTile.Root.ToolTip = "Клик — весь экран, правый клик — выбрать область";
            recTile.Root.MouseRightButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; if (!Recorder.Active) ToggleRecord(true); };
            Tile[] ts = {
                new Tile("", "Область", delegate { StartSnip(false); }).Compact(),
                new Tile("", "Весь экран", delegate { FullShot(); }).Compact(),
                new Tile("", "QR-код", delegate { ScanQr(); }).Compact(),
                new Tile("", "Пипетка", delegate { StartSnip(true); }).Compact(),
                recTile,
                new Tile("", "Диктовка", delegate { Dictate(); }).Compact(),
                new Tile("", "Текст", delegate { StartOcr(); }).Compact(),
                new Tile("", "Без стиля", delegate
                {
                    // Убирает оформление у скопированного: в буфере остаётся только текст.
                    try
                    {
                        if (!Clipboard.ContainsText()) { Toast("", "В буфере нет текста", null, -1, null); return; }
                        Clipboard.SetText(Clipboard.GetText());
                        Toast("", "Оформление убрано", null, -1, accent);
                    }
                    catch { }
                }).Compact()
            };
            homeTileList = ts;
            for (int i = 0; i < ts.Length; i++)
            {
                ts[i].Root.Margin = new Thickness(3);
                tiles.Children.Add(ts[i].Root);
            }
            Grid.SetColumn(tiles, 2);
            g.Children.Add(tiles);

            // Громкость
            var volCard = homeVolCard = new Border { Background = Card, CornerRadius = new CornerRadius(17) };
            var vol = new Grid { Margin = new Thickness(16, 0, 16, 0) };
            vol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            vol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            vol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            vol.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 15, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center });
            volBar = new Bar(PageW - 32 - 30 - 44, 5, Fg, 22);
            volBar.Root.Cursor = Cursors.Hand;
            volBar.Root.HorizontalAlignment = HorizontalAlignment.Left;
            volBar.Root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { volBar.Root.CaptureMouse(); SetVolumeFrom(e.GetPosition(volBar.Root)); };
            volBar.Root.MouseMove += delegate(object s, MouseEventArgs e) { if (volBar.Root.IsMouseCaptured) SetVolumeFrom(e.GetPosition(volBar.Root)); };
            volBar.Root.MouseLeftButtonUp += delegate { volBar.Root.ReleaseMouseCapture(); };
            Grid.SetColumn(volBar.Root, 1); vol.Children.Add(volBar.Root);
            volText = new TextBlock { Foreground = Dim, FontSize = 12, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(volText, 2); vol.Children.Add(volText);
            volCard.Child = vol;
            // Значок слева от ползунка переключает, чем он управляет: всей системой или только плеером
            volIcon = vol.Children[0] as TextBlock;
            volIconDefault = volIcon.Text;
            volIcon.Background = Brushes.Transparent; volIcon.Cursor = Cursors.Hand;
            volIcon.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; ToggleVolMode(); };
            SetVolMode(false);
            Grid.SetRow(volCard, 2); Grid.SetColumnSpan(volCard, 3);
            g.Children.Add(volCard);
            ApplyHomeLayout();
            return g;
        }

        // ------------------------------------------------------ Буфер
        FrameworkElement BuildClipPage()
        {
            var dock = new DockPanel();
            var foot = new Grid { Margin = new Thickness(0, 8, 0, 0), Height = 28 };
            foot.Children.Add(new TextBlock { Text = "Клик вставляет в активное окно · хранится только в памяти", Foreground = Faint, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            var clear = Btn("Очистить", false, 0, 28, delegate { clips.RemoveAll(delegate(ClipItem c) { return !c.Pinned; }); RenderClips(); });
            clear.HorizontalAlignment = HorizontalAlignment.Right;
            foot.Children.Add(clear);
            DockPanel.SetDock(foot, Dock.Bottom);
            dock.Children.Add(foot);

            // Поиск по истории
            var search = new Grid { Height = 32, Margin = new Thickness(0, 0, 0, 8) };
            search.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            search.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            search.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 13, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center });
            var searchHint = new TextBlock { Text = "Поиск по истории", Foreground = Faint, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(2, 0, 0, 1) };
            Grid.SetColumn(searchHint, 1); search.Children.Add(searchHint);
            clipSearch = MakeTextBox(false);
            clipSearch.TextChanged += delegate { searchHint.Visibility = clipSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RenderClips(); };
            Grid.SetColumn(clipSearch, 1); search.Children.Add(clipSearch);
            var searchCard = new Border { Background = Card, CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 0, 14, 0), Child = search, Margin = new Thickness(0, 0, 0, 8), Height = 32 };
            search.Margin = new Thickness(0);
            DockPanel.SetDock(searchCard, Dock.Top);
            dock.Children.Add(searchCard);

            clipList = new StackPanel();
            dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = clipList });
            return dock;
        }

        void RenderClips()
        {
            clipList.Children.Clear();
            if (clips.Count == 0)
            {
                clipList.Children.Add(new TextBlock { Text = "Скопируйте что-нибудь — появится здесь", Foreground = Dim, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 84, 0, 0) });
                return;
            }
            string filter = clipSearch == null ? "" : clipSearch.Text.Trim().ToLowerInvariant();
            string filterAlt = TextTools.FixLayout(filter).ToLowerInvariant();
            foreach (ClipItem it in clips)
            {
                ClipItem item = it;
                if (filter.Length > 0)
                {
                    // Ищем и по запросу как есть, и по нему же в другой раскладке («лфз» → «kap»).
                    string low = item.Text.ToLowerInvariant();
                    if (low.IndexOf(filter, StringComparison.Ordinal) < 0 && low.IndexOf(filterAlt, StringComparison.Ordinal) < 0) continue;
                }
                if (item.Image != null) { clipList.Children.Add(ClipImageRow(item)); continue; }
                string one = item.Text.Replace("\r", " ").Replace("\n", " ").Trim();
                if (one.Length > 140) one = one.Substring(0, 140);
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                row.Children.Add(new TextBlock { Text = one, Foreground = Fg, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(14, 0, 6, 1), VerticalAlignment = VerticalAlignment.Center });
                var pin = new TextBlock { Text = "", FontFamily = Icons, FontSize = 12, Foreground = item.Pinned ? accent : Faint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var pinHit = new Border { Background = Brushes.Transparent, Child = pin, ToolTip = item.Pinned ? "Открепить" : "Закрепить" };
                pinHit.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; item.Pinned = !item.Pinned; SortClips(); RenderClips(); };
                Grid.SetColumn(pinHit, 1);
                row.Children.Add(pinHit);
                var b = new Border { Background = Chip, CornerRadius = new CornerRadius(11), Height = 34, Margin = new Thickness(0, 0, 0, 5), Child = row, Cursor = Cursors.Hand };
                b.MouseEnter += delegate { b.Background = ChipHover; };
                b.MouseLeave += delegate { b.Background = Chip; };
                b.MouseLeftButtonDown += delegate
                {
                    try { Clipboard.SetText(item.Text); } catch { return; }
                    if (!IsActive) Native.PasteIntoActiveWindow();
                };
                clipList.Children.Add(b);
            }
            if (clipList.Children.Count == 0)
                clipList.Children.Add(new TextBlock { Text = "Ничего не найдено", Foreground = Dim, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 70, 0, 0) });
        }

        Border ClipImageRow(ClipItem item)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            var thumbBrush = new ImageBrush(item.Image) { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(thumbBrush, BitmapScalingMode.HighQuality);
            row.Children.Add(new Border { Width = 84, Height = 46, CornerRadius = new CornerRadius(8), Background = thumbBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            var label = new TextBlock { Text = item.Text, Foreground = Fg, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var pin = new TextBlock { Text = "", FontFamily = Icons, FontSize = 12, Foreground = item.Pinned ? accent : Faint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var pinHit = new Border { Background = Brushes.Transparent, Child = pin, ToolTip = item.Pinned ? "Открепить" : "Закрепить" };
            pinHit.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; item.Pinned = !item.Pinned; SortClips(); RenderClips(); };
            Grid.SetColumn(pinHit, 2); row.Children.Add(pinHit);
            var b = new Border { Background = Chip, CornerRadius = new CornerRadius(11), Height = 62, Margin = new Thickness(0, 0, 0, 5), Child = row, Cursor = Cursors.Hand };
            b.MouseEnter += delegate { b.Background = ChipHover; };
            b.MouseLeave += delegate { b.Background = Chip; };
            b.MouseLeftButtonDown += delegate
            {
                pendingSelf = item; pendingSelfAt = Environment.TickCount;
                try
                {
                    BitmapSource full = item.Image;
                    if (item.Png != null)
                    {
                        var dec = BitmapDecoder.Create(new System.IO.MemoryStream(item.Png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        full = dec.Frames[0];
                    }
                    Clipboard.SetImage(full);
                }
                catch (Exception ex) { Store.LogError("вставка картинки из буфера", ex); pendingSelf = null; return; }
                if (!IsActive) Native.PasteIntoActiveWindow();
            };
            return b;
        }

        void OnClipboardImage()
        {
            // Картинку положили мы сами по клику из истории — просто поднимаем её наверх.
            if (pendingSelf != null && unchecked(Environment.TickCount - pendingSelfAt) < 1500)
            {
                ClipItem p = pendingSelf; pendingSelf = null;
                clips.Remove(p); clips.Insert(0, p); SortClips(); RenderClips();
                return;
            }
            pendingSelf = null;
            BitmapSource img = Clipboard.GetImage();
            if (img == null || (long)img.PixelWidth * img.PixelHeight > 40000000) return;
            // Копия без альфа-канала: у картинок из буфера он часто пустой, и они выходят прозрачными.
            var copy = new WriteableBitmap(new FormatConvertedBitmap(img, PixelFormats.Bgr24, null, 0));
            copy.Freeze();
            // Windows сообщает об одной скопированной картинке несколько раз — сверяем по отпечатку и не плодим дубли.
            int stride = copy.PixelWidth * 3;
            var px = new byte[stride * copy.PixelHeight];
            copy.CopyPixels(px, stride, 0);
            uint hash = 2166136261;
            for (int i = 0; i < px.Length; i += 61) hash = (hash ^ px[i]) * 16777619;
            string label = "Изображение " + copy.PixelWidth + " × " + copy.PixelHeight;
            ClipItem same = clips.Find(delegate(ClipItem c) { return c.Image != null && c.Hash == hash && c.Text == label; });
            if (same != null) { clips.Remove(same); clips.Insert(0, same); SortClips(); RenderClips(); return; }
            // В истории держим сжатый PNG и маленькую миниатюру, а не несжатую картинку: скриншот 4K — это десятки мегабайт.
            byte[] png;
            using (var ms = new System.IO.MemoryStream())
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(copy));
                enc.Save(ms);
                png = ms.ToArray();
            }
            double k = Math.Min(1, 200.0 / copy.PixelWidth);
            var thumb = new WriteableBitmap(new TransformedBitmap(copy, new ScaleTransform(k, k)));
            thumb.Freeze();
            clips.Insert(0, new ClipItem { Image = thumb, Png = png, Text = label, Hash = hash });
            SortClips();
            int imgs = 0;
            for (int i = 0; i < clips.Count; i++)
                if (clips[i].Image != null && !clips[i].Pinned && ++imgs > 8) { clips.RemoveAt(i); i--; }
            RenderClips();
        }

        // ------------------------------------------------------ Чат
        FrameworkElement BuildChatPage()
        {
            var dock = new DockPanel();
            var top = new Grid { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
            chatModelLabel = new TextBlock { Foreground = Faint, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            top.Children.Add(chatModelLabel);
            var fresh = Btn("Новый чат", false, 0, 28, delegate { chatGen++; chatBusy = false; chatHistory.Clear(); chatList.Children.Clear(); RenderChatEmpty(); });
            fresh.HorizontalAlignment = HorizontalAlignment.Right;
            top.Children.Add(fresh);
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);

            var input = new Grid { Height = 36, Margin = new Thickness(0, 8, 0, 0) };
            input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            attachChip = IconBtn("", 36, delegate { AttachFromClipboard(); });
            attachChip.Background = Chip; attachChip.HorizontalAlignment = HorizontalAlignment.Right;
            attachChip.ToolTip = "Прикрепить картинку из буфера обмена";
            attachChip.MouseLeave += delegate { attachChip.Background = pendingImage != null ? TabOn : Chip; };
            Grid.SetColumn(attachChip, 3); input.Children.Add(attachChip);
            var field = new Grid();
            var hint = new TextBlock { Text = "Спросите что-нибудь…", Foreground = Faint, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(2, 0, 0, 1) };
            chatBox = MakeTextBox(false);
            chatBox.TextChanged += delegate { hint.Visibility = chatBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            chatBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { SendChat(); e.Handled = true; } };
            field.Children.Add(hint); field.Children.Add(chatBox);
            input.Children.Add(new Border { Background = Card, CornerRadius = new CornerRadius(18), Padding = new Thickness(14, 0, 14, 0), Child = field });
            var mic = IconBtn("", 36, delegate { DictateInto(chatBox); });
            mic.Background = Chip; mic.HorizontalAlignment = HorizontalAlignment.Right; mic.ToolTip = "Диктовка Windows (Win+H)";
            mic.MouseLeave += delegate { mic.Background = Chip; };
            Grid.SetColumn(mic, 1); input.Children.Add(mic);
            var send = IconBtn("", 36, delegate { SendChat(); });
            send.Background = Chip; send.HorizontalAlignment = HorizontalAlignment.Right; send.ToolTip = "Отправить (Enter)";
            send.MouseLeave += delegate { send.Background = Chip; };
            Grid.SetColumn(send, 2); input.Children.Add(send);
            DockPanel.SetDock(input, Dock.Bottom);
            dock.Children.Add(input);

            var body = new Grid();
            chatEmpty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            body.Children.Add(chatEmpty);
            chatList = new StackPanel();
            chatScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = chatList };
            body.Children.Add(chatScroll);
            dock.Children.Add(body);
            return dock;
        }

        void RenderChatEmpty()
        {
            bool key = Secrets.GetKey() != "";
            chatModelLabel.Text = key ? "Claude · " + (ClaudeChat.Model() == "claude-sonnet-5-5" ? "Sonnet 5.5" : "Opus 5.5") : "Claude · ключ не задан";
            chatEmpty.Children.Clear();
            chatEmpty.Visibility = chatHistory.Count == 0 && chatList.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (key)
            {
                chatEmpty.Children.Add(new TextBlock { Text = "Спросите о чём угодно — ответит Claude.", Foreground = Dim, FontSize = 13, TextAlignment = TextAlignment.Center });
                chatEmpty.Children.Add(new TextBlock { Text = "Разговор хранится только в памяти и стирается кнопкой «Новый чат».", Foreground = Faint, FontSize = 11.5, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
            }
            else
            {
                chatEmpty.Children.Add(new TextBlock { Text = "Чтобы чат заработал, добавьте свой ключ Claude API.", Foreground = Dim, FontSize = 13, TextAlignment = TextAlignment.Center });
                chatEmpty.Children.Add(new TextBlock { Text = "Ключ вводится в настройках и хранится зашифрованным на этом компьютере.", Foreground = Faint, FontSize = 11.5, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 12) });
                var open = Btn("Открыть настройки", false, 0, 30, delegate { OpenSettings(); });
                open.HorizontalAlignment = HorizontalAlignment.Center;
                chatEmpty.Children.Add(open);
            }
        }

        TextBlock AddBubble(bool user, string text)
        {
            var tb = new TextBlock { Text = text, Foreground = Fg, FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
            var b = new Border
            {
                Background = user ? TabOn : Card, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 7, 12, 8), Child = tb,
                HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left, MaxWidth = user ? 400 : 480,
                Margin = new Thickness(user ? 60 : 0, 0, 0, 8), Cursor = Cursors.Hand, ToolTip = "Нажмите, чтобы скопировать"
            };
            b.MouseLeftButtonDown += delegate { try { Clipboard.SetText(tb.Text); Toast("", "Скопировано", null, -1, accent); } catch { } };
            chatList.Children.Add(b);
            chatEmpty.Visibility = Visibility.Collapsed;
            chatScroll.ScrollToEnd();
            return tb;
        }

        // Вложение: картинка из буфера обмена (например, только что сделанный снимок области).
        void AttachFromClipboard()
        {
            try
            {
                if (pendingImage != null) { SetAttachment(null); return; }
                if (!Clipboard.ContainsImage()) { Toast("", "Сначала скопируйте картинку или сделайте снимок", null, -1, null); return; }
                BitmapSource img = Clipboard.GetImage();
                if (img == null) return;
                BitmapSource src = new FormatConvertedBitmap(img, PixelFormats.Bgr24, null, 0);
                // Длинная сторона не больше 1568 точек: больше модели не нужно, а запрос выходит заметно дороже.
                double k = Math.Min(1, 1568.0 / Math.Max(src.PixelWidth, src.PixelHeight));
                if (k < 1) src = new TransformedBitmap(src, new ScaleTransform(k, k));
                using (var ms = new System.IO.MemoryStream())
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(src));
                    enc.Save(ms);
                    SetAttachment(Convert.ToBase64String(ms.ToArray()));
                }
            }
            catch (Exception ex) { Store.LogError("вложение в чат", ex); Toast("", "Не удалось прикрепить картинку", null, -1, null); }
        }

        void SetAttachment(string b64)
        {
            pendingImage = b64;
            attachChip.Background = b64 != null ? TabOn : Chip;
            attachChip.ToolTip = b64 != null ? "Картинка прикреплена — нажмите, чтобы убрать" : "Прикрепить картинку из буфера обмена";
            ((TextBlock)attachChip.Child).Foreground = b64 != null ? accent : Fg;
        }

        // Действие ИИ над текстом из буфера: результат кладётся обратно в буфер.
        void AiText(string task)
        {
            if (chatBusy) return;
            string key = Secrets.GetKey();
            if (key == "") { Toast("", "Нужен ключ Claude API — добавьте в настройках", null, -1, null); return; }
            string text;
            try { text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch { return; }
            if (text.Trim().Length == 0) { Toast("", "В буфере нет текста", null, -1, null); return; }
            if (text.Length > 60000) { Toast("", "Текст слишком длинный для одного запроса", null, -1, null); return; }
            chatBusy = true;
            textPreview.Text = "Claude работает…";
            textPreview.Foreground = Dim;
            string model = ClaudeChat.Model();
            var msgs = new List<ChatMsg> { new ChatMsg { Role = "user", Text = "Задача: " + task + "\n\nТекст:\n" + text } };
            const string sys = "Ты выполняешь одно преобразование текста. Верни только результат — без пояснений, вступлений, кавычек и разметки. Сохраняй абзацы и переносы строк исходного текста.";
            var sb = new StringBuilder();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = ClaudeChat.Send(key, model, msgs, sys, delegate(string piece) { lock (sb) sb.Append(piece); });
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    chatBusy = false;
                    string result;
                    lock (sb) result = sb.ToString().Trim();
                    if (err != null || result.Length == 0) { Toast("", err ?? "Пустой ответ", null, -1, null); RenderTextPreview(); return; }
                    try { Clipboard.SetText(result); } catch { }
                    RenderTextPreview();
                    Toast("", "Готово — результат в буфере", null, -1, accent);
                }));
            });
        }

        void SendChat()
        {
            string q = chatBox.Text.Trim();
            if (q.Length == 0 || chatBusy) return;
            string key = Secrets.GetKey();
            if (key == "") { RenderChatEmpty(); Toast("", "Сначала добавьте ключ Claude API", null, -1, null); return; }
            chatBox.Text = "";
            string image = pendingImage;
            SetAttachment(null);
            chatHistory.Add(new ChatMsg { Role = "user", Text = q, ImageB64 = image });
            AddBubble(true, image != null ? "[картинка] " + q : q);
            TextBlock answer = AddBubble(false, "Думаю…");
            answer.Foreground = Dim;
            chatBusy = true;
            int gen = chatGen;
            string model = ClaudeChat.Model();
            var snapshot = new List<ChatMsg>(chatHistory);
            var sb = new StringBuilder();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = ClaudeChat.Send(key, model, snapshot, null, delegate(string piece)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (gen != chatGen) return;
                        sb.Append(piece);
                        answer.Foreground = Fg;
                        answer.Text = sb.ToString();
                        chatScroll.ScrollToEnd();
                    }));
                });
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (gen != chatGen) return;
                    chatBusy = false;
                    if (err == null && sb.Length > 0) { chatHistory.Add(new ChatMsg { Role = "assistant", Text = sb.ToString() }); return; }
                    // Неудача: убираем вопрос из истории, чтобы следующий запрос не начинался с двух сообщений пользователя подряд.
                    chatHistory.RemoveAt(chatHistory.Count - 1);
                    answer.Foreground = Dim;
                    answer.Text = (sb.Length > 0 ? sb + "\n\n" : "") + (err ?? "Пустой ответ.");
                    chatScroll.ScrollToEnd();
                }));
            });
        }

        // ----------------------------------------------------- голосовой ввод
        // Используется встроенная диктовка Windows (Win+H): она понимает русский и не требует стороннего ключа.
        static void PressWinH()
        {
            Native.Key(0x5B, false); Native.Key(0x48, false); Native.Key(0x48, true); Native.Key(0x5B, true);
        }

        void DictateInto(TextBox box)
        {
            SetNoActivate(false);
            Activate();
            box.Focus();
            Keyboard.Focus(box);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            t.Tick += delegate { t.Stop(); PressWinH(); };
            t.Start();
        }

        // Диктовка «в никуда»: открывается строка ввода, после Enter текст вставляется в окно, где вы работали.
        void Dictate()
        {
            if (state != 4) ToggleCommand();
            dictating = true;
            UpdateCmd();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            t.Tick += delegate { t.Stop(); if (state == 4) PressWinH(); };
            t.Start();
        }

        void OpenChat()
        {
            ShowPage(PChat);
            if (state != 2) SetState(2);
            SetNoActivate(false);
            Activate();
            chatBox.Focus();
            Keyboard.Focus(chatBox);
        }

        public void SaveClaudeKey(string key)
        {
            Secrets.SetKey(key);
            RenderChatEmpty();
        }

        public void SetClaudeModel(string id)
        {
            Store.Set("claudeModel", id);
            RenderChatEmpty();
        }

        void PasteText(string text)
        {
            try { Clipboard.SetText(text); } catch { return; }
            // Вставляем чуть позже: фокус должен успеть вернуться в прежнее окно.
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            t.Tick += delegate { t.Stop(); Native.PasteIntoActiveWindow(); };
            t.Start();
        }

        // ------------------------------------------------------ Полка
        FrameworkElement BuildShelfPage()
        {
            var dock = new DockPanel();
            var top = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 10) };
            top.Children.Add(new TextBlock { Text = "Файлы ждут здесь, пока вы не перетащите их в нужное окно", Foreground = Faint, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var all = Btn("Перетащить все", false, 0, 28, delegate { if (shelf.Count > 0) DragOut(shelf.ToArray()); });
            all.Margin = new Thickness(0, 0, 6, 0);
            all.ToolTip = "Зажмите и тяните в нужное окно";
            btns.Children.Add(all);
            btns.Children.Add(Btn("Очистить", false, 0, 28, delegate { shelf.Clear(); SaveShelf(); RenderShelf(); }));
            top.Children.Add(btns);
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);

            var body = new Grid();
            shelfEmpty = new TextBlock { Text = "Перетащите файлы на остров — они лягут на полку", Foreground = Dim, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var zone = new Rectangle { Stroke = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40)), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection(new double[] { 5, 4 }), RadiusX = 18, RadiusY = 18, Fill = Brushes.Transparent };
            body.Children.Add(zone);
            body.Children.Add(shelfEmpty);
            shelfPanel = new WrapPanel { Margin = new Thickness(10, 10, 2, 2) };
            body.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = shelfPanel });
            dock.Children.Add(body);
            return dock;
        }

        void LoadShelf()
        {
            foreach (string p in Store.Get("shelf").Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                if (System.IO.File.Exists(p) || System.IO.Directory.Exists(p)) shelf.Add(p);
        }

        void SaveShelf() { Store.Set("shelf", string.Join("|", shelf.ToArray())); }

        void RenderShelf()
        {
            shelfPanel.Children.Clear();
            shelf.RemoveAll(delegate(string p) { return !System.IO.File.Exists(p) && !System.IO.Directory.Exists(p); });
            shelfEmpty.Visibility = shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (string sp in shelf)
            {
                string path = sp;
                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                ImageSource icon = Shell.IconFor(path);
                if (icon != null)
                {
                    var img = new Image { Source = icon, Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    col.Children.Add(img);
                }
                else col.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 28, Foreground = Amber, HorizontalAlignment = HorizontalAlignment.Center });
                col.Children.Add(new TextBlock { Text = System.IO.Path.GetFileName(path), Foreground = Fg, FontSize = 11.5, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 30, Margin = new Thickness(6, 6, 6, 0) });
                var cell = new Grid();
                cell.Children.Add(col);
                var x = new TextBlock { Text = "", FontFamily = Icons, FontSize = 9, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var xHit = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Brushes.Transparent, Child = x, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 3, 0), ToolTip = "Убрать с полки" };
                xHit.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; shelf.Remove(path); SaveShelf(); RenderShelf(); };
                cell.Children.Add(xHit);
                var b = new Border { Width = 98, Height = 96, CornerRadius = new CornerRadius(14), Background = Chip, Margin = new Thickness(0, 0, 8, 8), Child = cell, Cursor = Cursors.Hand, ToolTip = path };
                b.MouseEnter += delegate { b.Background = ChipHover; };
                b.MouseLeave += delegate { b.Background = Chip; };
                b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { dragArmed = path; dragFrom = e.GetPosition(this); };
                b.MouseMove += delegate(object s, MouseEventArgs e)
                {
                    if (e.LeftButton != MouseButtonState.Pressed || dragArmed != path) return;
                    Point p = e.GetPosition(this);
                    if (Math.Abs(p.X - dragFrom.X) + Math.Abs(p.Y - dragFrom.Y) < 6) return;
                    dragArmed = null;
                    DragOut(new string[] { path });
                };
                shelfPanel.Children.Add(b);
            }
        }

        // Вытаскивание файлов с полки в другое окно. После удачного переноса файл с полки уходит.
        void DragOut(string[] paths)
        {
            selfDrag = true;
            try
            {
                var data = new DataObject(DataFormats.FileDrop, paths);
                DragDropEffects eff = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
                if (eff != DragDropEffects.None)
                {
                    foreach (string p in paths) shelf.Remove(p);
                    SaveShelf();
                    RenderShelf();
                }
            }
            catch { }
            finally { selfDrag = false; }
        }

        void OnFileDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (selfDrag || !e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; return; }
            e.Effects = DragDropEffects.Copy;
            if (page != PShelf) ShowPage(PShelf);
            if (state != 2) SetState(2);
        }

        void OnFileDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (selfDrag) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            int added = 0;
            foreach (string f in files)
                if (!shelf.Contains(f)) { shelf.Add(f); added++; }
            SaveShelf();
            RenderShelf();
            if (added > 0) Toast("", added == 1 ? "На полке: " + System.IO.Path.GetFileName(files[0]) : "На полке файлов: " + added, null, -1, accent);
        }

        void SortClips()
        {
            var pinned = clips.FindAll(delegate(ClipItem c) { return c.Pinned; });
            var rest = clips.FindAll(delegate(ClipItem c) { return !c.Pinned; });
            clips.Clear(); clips.AddRange(pinned); clips.AddRange(rest);
        }

        void OnClipboard()
        {
            try
            {
                // Менеджеры паролей помечают так секреты — их не запоминаем.
                if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return;
                if (!Clipboard.ContainsText())
                {
                    if (Clipboard.ContainsImage()) OnClipboardImage();
                    return;
                }
                string t = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(t) || t.Length > 20000) return;
                ClipItem existing = clips.Find(delegate(ClipItem c) { return c.Image == null && c.Text == t; });
                if (existing != null) clips.Remove(existing); else existing = new ClipItem { Text = t };
                clips.Insert(0, existing);
                SortClips();
                int unpinned = 0;
                for (int i = clips.Count - 1; i >= 0; i--) if (!clips[i].Pinned && ++unpinned > 0) { }
                while (clips.Count > 40) { int last = clips.FindLastIndex(delegate(ClipItem c) { return !c.Pinned; }); if (last < 0) break; clips.RemoveAt(last); }
                RenderClips();
                if (page == PText) RenderTextPreview();
            }
            catch { }
        }

        // ------------------------------------------------------ Текст
        FrameworkElement BuildTextPage()
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "Текст из буфера обмена", Foreground = Faint, FontSize = 11.5, Margin = new Thickness(4, 0, 0, 6) });
            textPreview = new TextBlock { Foreground = Fg, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 52, LineHeight = 17 };
            sp.Children.Add(new Border { Background = Card, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10, 14, 10), Height = 74, Child = textPreview });
            sp.Children.Add(new TextBlock { Text = "Преобразовать и положить обратно в буфер", Foreground = Faint, FontSize = 11.5, Margin = new Thickness(4, 12, 0, 6) });
            var wrap = new WrapPanel();
            AddTool(wrap, "Раскладка RU⇄EN", TextTools.FixLayout);
            AddTool(wrap, "ВЕРХНИЙ", delegate(string s) { return s.ToUpper(); });
            AddTool(wrap, "нижний", delegate(string s) { return s.ToLower(); });
            AddTool(wrap, "Как В Заголовке", TextTools.Title);
            AddTool(wrap, "Транслит", TextTools.Translit);
            AddTool(wrap, "Без форматирования", delegate(string s) { return s.Trim(); });
            AddTool(wrap, "Base64 →", TextTools.B64Enc);
            AddTool(wrap, "Base64 ←", TextTools.B64Dec);
            AddTool(wrap, "URL →", Uri.EscapeDataString);
            AddTool(wrap, "URL ←", Uri.UnescapeDataString);
            // Действия ИИ: работают, когда задан ключ Claude API
            AddAi(wrap, "Перевести", "переведи текст: если он на русском — на английский, иначе на русский");
            AddAi(wrap, "Исправить", "исправь орфографию, пунктуацию и опечатки, не меняя смысл и стиль");
            AddAi(wrap, "Сократить", "сократи текст примерно вдвое, сохранив главное");
            AddAi(wrap, "Объяснить", "объясни простыми словами, о чём этот текст, в двух-четырёх предложениях");
            sp.Children.Add(wrap);
            return sp;
        }

        void AddAi(WrapPanel wrap, string name, string task)
        {
            var b = Btn("✦ " + name, false, 0, 30, delegate { AiText(task); });
            b.Margin = new Thickness(0, 0, 6, 6);
            b.ToolTip = "Выполнит Claude; результат попадёт в буфер обмена";
            ((TextBlock)b.Child).Foreground = accent;
            wrap.Children.Add(b);
        }

        void AddTool(WrapPanel wrap, string name, Func<string, string> f)
        {
            var b = Btn(name, false, 0, 30, delegate
            {
                try
                {
                    if (!Clipboard.ContainsText()) { Toast("", "В буфере нет текста", null, -1, null); return; }
                    string r = f(Clipboard.GetText());
                    Clipboard.SetText(r);
                    RenderTextPreview();
                }
                catch { Toast("", "Не получилось преобразовать", null, -1, null); }
            });
            b.Margin = new Thickness(0, 0, 6, 6);
            wrap.Children.Add(b);
        }

        void RenderTextPreview()
        {
            try
            {
                string t = Clipboard.ContainsText() ? Clipboard.GetText() : "";
                if (t.Length > 400) t = t.Substring(0, 400);
                textPreview.Text = t == "" ? "Пусто — скопируйте текст" : t;
                textPreview.Foreground = t == "" ? Dim : Fg;
            }
            catch { }
        }

        // ------------------------------------------------------ Заметка
        TextBox MakeTextBox(bool multiline)
        {
            var tb = new TextBox
            {
                Background = Brushes.Transparent, Foreground = Fg, CaretBrush = Fg, BorderThickness = new Thickness(0),
                FontSize = 13, SelectionBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x6F, 0xD8)),
                AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center
            };
            tb.FocusVisualStyle = null;
            // Окно не забирает фокус, пока вы не кликнули в поле ввода.
            tb.PreviewMouseLeftButtonDown += delegate { SetNoActivate(false); Activate(); tb.Focus(); };
            return tb;
        }

        FrameworkElement BuildNotePage()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) });

            // ---- слева: папки, редактор, панель форматирования
            var left = new DockPanel();
            var folders = new StackPanel { Orientation = Orientation.Horizontal, Height = 28, Margin = new Thickness(0, 0, 0, 8) };
            string[] fn = { "Заметки", "Задачи", "Идеи" };
            folderChips = new Border[fn.Length];
            for (int i = 0; i < fn.Length; i++)
            {
                int idx = i;
                var chip = new Border { Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(13, 0, 13, 1), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = new TextBlock { Text = fn[i], FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center } };
                chip.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; SelectFolder(idx); };
                folderChips[i] = chip;
                folders.Children.Add(chip);
            }
            DockPanel.SetDock(folders, Dock.Top);
            left.Children.Add(folders);

            noteRich = new RichTextBox
            {
                Background = Brushes.Transparent, Foreground = Fg, CaretBrush = Fg, BorderThickness = new Thickness(0), FontSize = 13, Padding = new Thickness(0),
                SelectionBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x6F, 0xD8)), VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, AcceptsTab = false
            };
            noteRich.FocusVisualStyle = null;
            var doc = new System.Windows.Documents.FlowDocument { PagePadding = new Thickness(0), FontSize = 13, FontFamily = FontFamily, Foreground = Fg };
            var ps = new Style(typeof(System.Windows.Documents.Paragraph));
            ps.Setters.Add(new Setter(System.Windows.Documents.Block.MarginProperty, new Thickness(0, 0, 0, 4)));
            doc.Resources.Add(typeof(System.Windows.Documents.Paragraph), ps);
            var ls = new Style(typeof(System.Windows.Documents.List));
            ls.Setters.Add(new Setter(System.Windows.Documents.Block.MarginProperty, new Thickness(0, 0, 0, 4)));
            ls.Setters.Add(new Setter(System.Windows.Documents.Block.PaddingProperty, new Thickness(20, 0, 0, 0)));
            doc.Resources.Add(typeof(System.Windows.Documents.List), ls);
            noteRich.Document = doc;
            // Окно не забирает фокус, пока вы не кликнули в заметку.
            noteRich.PreviewMouseLeftButtonDown += delegate { SetNoActivate(false); Activate(); noteRich.Focus(); };
            noteRich.PreviewMouseLeftButtonUp += ToggleCheckAt;
            noteSave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            noteSave.Tick += delegate { noteSave.Stop(); SaveNote(); };
            noteRich.TextChanged += delegate { if (!noteLoading) { noteSave.Stop(); noteSave.Start(); } };

            var tools = new StackPanel { Orientation = Orientation.Horizontal, Height = 28, Margin = new Thickness(-4, 6, 0, 0) };
            tools.Children.Add(NoteTool("Ж", false, FontWeights.Bold, FontStyles.Normal, "Жирный (Ctrl+B)", delegate { System.Windows.Documents.EditingCommands.ToggleBold.Execute(null, noteRich); }));
            tools.Children.Add(NoteTool("К", false, FontWeights.Normal, FontStyles.Italic, "Курсив (Ctrl+I)", delegate { System.Windows.Documents.EditingCommands.ToggleItalic.Execute(null, noteRich); }));
            var strike = NoteTool("З", false, FontWeights.Normal, FontStyles.Normal, "Зачёркнутый", delegate
            {
                object cur = noteRich.Selection.GetPropertyValue(System.Windows.Documents.Inline.TextDecorationsProperty);
                bool has = cur is TextDecorationCollection && ((TextDecorationCollection)cur).Count > 0;
                noteRich.Selection.ApplyPropertyValue(System.Windows.Documents.Inline.TextDecorationsProperty, has ? new TextDecorationCollection() : TextDecorations.Strikethrough);
            });
            ((TextBlock)strike.Child).TextDecorations = TextDecorations.Strikethrough;
            tools.Children.Add(strike);
            tools.Children.Add(NoteTool("", true, FontWeights.Normal, FontStyles.Normal, "Выделить цветом", delegate
            {
                object cur = noteRich.Selection.GetPropertyValue(System.Windows.Documents.TextElement.BackgroundProperty);
                noteRich.Selection.ApplyPropertyValue(System.Windows.Documents.TextElement.BackgroundProperty, cur is SolidColorBrush ? null : new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xD6, 0x0A)));
            }));
            tools.Children.Add(NoteTool("", true, FontWeights.Normal, FontStyles.Normal, "Маркированный список", delegate { System.Windows.Documents.EditingCommands.ToggleBullets.Execute(null, noteRich); }));
            tools.Children.Add(NoteTool("1.", false, FontWeights.SemiBold, FontStyles.Normal, "Нумерованный список", delegate { System.Windows.Documents.EditingCommands.ToggleNumbering.Execute(null, noteRich); }));
            tools.Children.Add(NoteTool("☑", false, FontWeights.Normal, FontStyles.Normal, "Задача с галочкой — клик по квадрату отмечает", delegate
            {
                var p = noteRich.CaretPosition.GetLineStartPosition(0) ?? noteRich.CaretPosition;
                p.InsertTextInRun("☐ ");
            }));

            var editor = new DockPanel();
            DockPanel.SetDock(tools, Dock.Bottom);
            editor.Children.Add(tools);
            editor.Children.Add(noteRich);
            left.Children.Add(new Border { Background = Card, CornerRadius = new CornerRadius(18), Padding = new Thickness(14, 12, 12, 8), Child = editor });
            g.Children.Add(left);

            // ---- справа: кольцо таймера и напоминания
            var right = new DockPanel();
            var ringRow = new Grid { Height = 92 };
            var ring = new Grid { Width = 92, Height = 92, HorizontalAlignment = HorizontalAlignment.Left };
            ring.Children.Add(new Ellipse { Stroke = Bar.TrackBrush, StrokeThickness = 6, Margin = new Thickness(3) });
            ringArc = new Path { Stroke = accent, StrokeThickness = 6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            ring.Children.Add(ringArc);
            ringText = new TextBlock { Foreground = Fg, FontSize = 17, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ring.Children.Add(ringText);
            ringRow.Children.Add(ring);
            var start = Btn("Старт", false, 88, 34, delegate
            {
                if (timerEnd > DateTime.Now) { timerEnd = DateTime.MinValue; timerRinging = false; }
                else { timerRinging = false; timerTotal = TimeSpan.FromMinutes(timerPreset); timerEnd = DateTime.Now + timerTotal; }
                RenderCompact(); UpdateRing(); SetTargets();
            });
            startLabel = (TextBlock)start.Child;
            start.HorizontalAlignment = HorizontalAlignment.Right;
            start.VerticalAlignment = VerticalAlignment.Center;
            ringRow.Children.Add(start);
            DockPanel.SetDock(ringRow, Dock.Top);
            right.Children.Add(ringRow);

            var presets = new UniformGrid { Rows = 1, Columns = 5, Margin = new Thickness(-2, 8, -2, 0), Height = 26 };
            int[] mins = { 1, 5, 10, 15, 25 };
            presetChips = new Border[mins.Length];
            for (int i = 0; i < mins.Length; i++)
            {
                int m = mins[i];
                var pc = Btn(m.ToString(), false, double.NaN, 26, delegate { timerPreset = m; UpdateRing(); });
                pc.Padding = new Thickness(0); pc.Margin = new Thickness(2, 0, 2, 0); pc.Tag = m;
                presetChips[i] = pc;
                presets.Children.Add(pc);
            }
            DockPanel.SetDock(presets, Dock.Top);
            right.Children.Add(presets);

            var remGrid = new Grid();
            var remHint = new TextBlock { Text = "Напомнить: через 10 мин чай", Foreground = Faint, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(2, 0, 0, 1) };
            remBox = MakeTextBox(false);
            remBox.FontSize = 12;
            remBox.TextChanged += delegate { remHint.Visibility = remBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            remBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { AddReminderPhrase(remBox.Text); e.Handled = true; } };
            remGrid.Children.Add(remHint); remGrid.Children.Add(remBox);
            var remCard = new Border { Background = Card, CornerRadius = new CornerRadius(15), Padding = new Thickness(12, 0, 12, 0), Height = 30, Margin = new Thickness(0, 10, 0, 6), Child = remGrid, ToolTip = "Примеры: «через 10 мин чай», «в 18:30 созвон», «завтра в 9 позвонить», «каждый день в 23:00 таблетки»" };
            DockPanel.SetDock(remCard, Dock.Top);
            right.Children.Add(remCard);
            remList = new StackPanel();
            right.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = remList });
            Grid.SetColumn(right, 2);
            g.Children.Add(right);
            return g;
        }

        Border NoteTool(string text, bool mdl2, FontWeight weight, FontStyle style, string tip, Action act)
        {
            var tb = new TextBlock { Text = text, Foreground = Fg, FontSize = mdl2 ? 13 : 13.5, FontWeight = weight, FontStyle = style, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (mdl2) tb.FontFamily = Icons;
            var b = new Border { Width = 30, Height = 28, CornerRadius = new CornerRadius(9), Background = Brushes.Transparent, Child = tb, Cursor = Cursors.Hand, ToolTip = tip, Margin = new Thickness(0, 0, 2, 0) };
            b.MouseEnter += delegate { b.Background = ChipHover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            // Кнопка не забирает фокус, поэтому выделение в заметке сохраняется.
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; try { act(); } catch { } };
            return b;
        }

        // Клик по квадратику задачи ставит или снимает галочку.
        void ToggleCheckAt(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var tp = noteRich.GetPositionFromPoint(e.GetPosition(noteRich), true);
                if (tp == null) return;
                System.Windows.Documents.TextPointer a = null, b = null;
                string f = tp.GetTextInRun(System.Windows.Documents.LogicalDirection.Forward);
                string k = tp.GetTextInRun(System.Windows.Documents.LogicalDirection.Backward);
                char c = '\0';
                if (f.Length > 0 && (f[0] == '☐' || f[0] == '☑')) { a = tp; b = tp.GetPositionAtOffset(1); c = f[0]; }
                else if (k.Length > 0 && (k[k.Length - 1] == '☐' || k[k.Length - 1] == '☑')) { a = tp.GetPositionAtOffset(-1); b = tp; c = k[k.Length - 1]; }
                if (a == null || b == null) return;
                new System.Windows.Documents.TextRange(a, b).Text = c == '☐' ? "☑" : "☐";
            }
            catch { }
        }

        string NoteFile(int folder) { return System.IO.Path.Combine(Store.Dir, "note-" + folder + ".xamlpkg"); }

        void SaveNote()
        {
            try
            {
                var range = new System.Windows.Documents.TextRange(noteRich.Document.ContentStart, noteRich.Document.ContentEnd);
                using (var fs = System.IO.File.Create(NoteFile(noteFolder))) range.Save(fs, DataFormats.XamlPackage);
            }
            catch (Exception ex) { Store.LogError("сохранение заметки", ex); }
        }

        void LoadNoteDoc()
        {
            noteLoading = true;
            try
            {
                var range = new System.Windows.Documents.TextRange(noteRich.Document.ContentStart, noteRich.Document.ContentEnd);
                string f = NoteFile(noteFolder);
                if (System.IO.File.Exists(f)) { using (var fs = System.IO.File.OpenRead(f)) range.Load(fs, DataFormats.XamlPackage); }
                else if (noteFolder == 0 && System.IO.File.Exists(Store.NotePath)) range.Text = System.IO.File.ReadAllText(Store.NotePath, Encoding.UTF8); // заметка из прежней версии
                else range.Text = "";
            }
            catch (Exception ex) { Store.LogError("загрузка заметки", ex); }
            noteLoading = false;
            noteSave.Stop();
            for (int i = 0; i < folderChips.Length; i++)
            {
                folderChips[i].Background = i == noteFolder ? TabOn : Brushes.Transparent;
                ((TextBlock)folderChips[i].Child).Foreground = i == noteFolder ? Fg : Dim;
            }
        }

        void SelectFolder(int idx)
        {
            if (idx == noteFolder) return;
            noteSave.Stop();
            SaveNote();
            noteFolder = idx;
            Store.Set("noteFolder", idx.ToString());
            LoadNoteDoc();
        }

        void LoadNote()
        {
            int f;
            if (int.TryParse(Store.Get("noteFolder"), out f) && f >= 0 && f < folderChips.Length) noteFolder = f;
            LoadNoteDoc();
            LoadReminders();
            RenderReminders();
            UpdateRing();
        }

        // ---------------------------------------------------- кольцо таймера
        void UpdateRing()
        {
            bool running = timerEnd > DateTime.Now;
            double frac = running && timerTotal.TotalSeconds > 0 ? Math.Max(0, Math.Min(1, (timerEnd - DateTime.Now).TotalSeconds / timerTotal.TotalSeconds)) : 1;
            ringText.Text = running ? Fmt(timerEnd - DateTime.Now) : timerPreset + ":00";
            startLabel.Text = running ? "Стоп" : "Старт";
            ringArc.Opacity = running ? 1 : 0.35;
            double ang = Math.Min(359.9, 360 * frac) * Math.PI / 180, r = 40, c = 46;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c, c - r), false, false);
                ctx.ArcTo(new Point(c + r * Math.Sin(ang), c - r * Math.Cos(ang)), new Size(r, r), 0, ang > Math.PI, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            ringArc.Data = g;
            foreach (Border pc in presetChips) pc.Background = (int)pc.Tag == timerPreset && !running ? TabOn : Chip;
        }

        // ------------------------------------------------------ напоминания
        void LoadReminders()
        {
            foreach (string item in Store.Get("reminders").Split(new char[] { '¦' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = item.Split('~');
                long ticks;
                if (p.Length < 3 || !long.TryParse(p[0], out ticks)) continue;
                reminders.Add(new Reminder { At = new DateTime(ticks), Daily = p[1] == "D", Text = p[2] });
            }
        }

        void SaveReminders()
        {
            var parts = new List<string>();
            foreach (Reminder r in reminders) parts.Add(r.At.Ticks + "~" + (r.Daily ? "D" : "0") + "~" + r.Text.Replace("~", "-").Replace("¦", "|"));
            Store.Set("reminders", string.Join("¦", parts.ToArray()));
        }

        void AddReminderPhrase(string phrase)
        {
            DateTime at; string text; bool daily;
            if (!Calc.TryReminder(phrase, DateTime.Now, out at, out text, out daily))
            {
                Toast("", "Не понял время: «через 10 мин …»", null, -1, null);
                return;
            }
            reminders.Add(new Reminder { At = at, Text = text, Daily = daily });
            reminders.Sort(delegate(Reminder a, Reminder b) { return a.At.CompareTo(b.At); });
            SaveReminders();
            RenderReminders();
            remBox.Text = "";
        }

        void RenderReminders()
        {
            remList.Children.Clear();
            foreach (Reminder rr in reminders)
            {
                Reminder r = rr;
                var row = new Grid { Height = 30 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                string when = r.Daily ? "ежедн. " + r.At.ToString("HH:mm") : (r.At.Date == DateTime.Today ? r.At.ToString("HH:mm") : r.At.ToString("dd.MM HH:mm"));
                row.Children.Add(new TextBlock { Text = when, Foreground = accent, FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) });
                var t = new TextBlock { Text = r.Text, Foreground = Fg, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(t, 1); row.Children.Add(t);
                var x = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Удалить", Child = new TextBlock { Text = "", FontFamily = Icons, FontSize = 8.5, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                x.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; reminders.Remove(r); SaveReminders(); RenderReminders(); };
                Grid.SetColumn(x, 2); row.Children.Add(x);
                remList.Children.Add(new Border { Background = Chip, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 4), Child = row, ToolTip = r.Text });
            }
        }

        void CheckReminders()
        {
            DateTime now = DateTime.Now;
            Reminder due = reminders.Find(delegate(Reminder r) { return r.At <= now; });
            if (due == null) return;
            if (due.Daily) { while (due.At <= now) due.At = due.At.AddDays(1); }
            else reminders.Remove(due);
            reminders.Sort(delegate(Reminder a, Reminder b) { return a.At.CompareTo(b.At); });
            SaveReminders();
            RenderReminders();
            string text = due.Text;
            System.Media.SystemSounds.Exclamation.Play();
            ShowCardEx(null, "", "Напоминание", text, delegate { }, "+10 мин", delegate
            {
                reminders.Add(new Reminder { At = DateTime.Now.AddMinutes(10), Text = text });
                reminders.Sort(delegate(Reminder a, Reminder b) { return a.At.CompareTo(b.At); });
                SaveReminders();
                RenderReminders();
            }, true);
            cardUntil = Environment.TickCount + 60000; // ждёт ответа, а не исчезает через пару секунд
        }

        // ------------------------------------------------------ Погода
        FrameworkElement BuildWeatherPage()
        {
            var dock = new DockPanel();
            var top = new Grid { Height = 32 };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cityBox = MakeTextBox(false);
            cityBox.Text = Store.Get("cityQuery");
            cityBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) RefreshWeather(cityBox.Text.Trim()); };
            top.Children.Add(new Border { Background = Card, CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 0, 14, 0), Child = cityBox });
            var go = Btn("Показать", false, 0, 32, delegate { RefreshWeather(cityBox.Text.Trim()); });
            Grid.SetColumn(go, 2);
            top.Children.Add(go);
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);
            var body = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            weatherBody = new StackPanel();
            body.Children.Add(weatherBody);

            weatherGrid = new Grid { Visibility = Visibility.Collapsed };
            weatherGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(168) });
            weatherGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            weatherGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Карточка с небом: цвет зависит от погоды и времени суток
            skyA = new GradientStop(Color.FromRgb(0x2A, 0x7D, 0xE8), 0);
            skyB = new GradientStop(Color.FromRgb(0x6C, 0xC4, 0xF2), 1);
            var skyBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.35, 1) };
            skyBrush.GradientStops.Add(skyA); skyBrush.GradientStops.Add(skyB);
            var sky = new Grid { Margin = new Thickness(16, 14, 14, 14) };
            var skyTop = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            wCity = new TextBlock { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            wSymbol = new TextBlock { FontFamily = Symbols, FontSize = 30, Foreground = Brushes.White, Margin = new Thickness(-2, 8, 0, 0) };
            wTemp = new TextBlock { Foreground = Brushes.White, FontSize = 46, FontWeight = FontWeights.Light, Margin = new Thickness(-2, -4, 0, 0) };
            wDesc = new TextBlock { Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            skyTop.Children.Add(wCity); skyTop.Children.Add(wSymbol); skyTop.Children.Add(wTemp); skyTop.Children.Add(wDesc);
            sky.Children.Add(skyTop);
            var skyBottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.88 };
            wFeels = new TextBlock { Foreground = Brushes.White, FontSize = 11.5 };
            wHiLo = new TextBlock { Foreground = Brushes.White, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) };
            skyBottom.Children.Add(wFeels); skyBottom.Children.Add(wHiLo);
            sky.Children.Add(skyBottom);
            weatherGrid.Children.Add(new Border { Background = skyBrush, CornerRadius = new CornerRadius(22), Child = sky });

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            wHours = new Grid();
            right.Children.Add(wHours);
            var tiles = new UniformGrid { Rows = 2, Columns = 3, Margin = new Thickness(-3) };
            TextBlock dummy;
            BuildWeatherGauges();
            tiles.Children.Add(WTile("Ветер", out wWind, out wWindSub, false, gCompass));
            tiles.Children.Add(WTile("Влажность", out wHum, out dummy, false, gHum.Root));
            tiles.Children.Add(WTile("Давление", out wPres, out dummy, false, gPressure));
            tiles.Children.Add(WTile("УФ-индекс", out wUv, out wUvSub, false, gUv));
            tiles.Children.Add(WTile("Осадки", out wRain, out dummy, false, gRain.Root));
            tiles.Children.Add(WTile("Восход · закат", out wSun, out dummy, false, gSun));
            wSun.FontSize = 11; wWind.FontSize = 13;
            Grid.SetRow(tiles, 2); right.Children.Add(tiles);
            wDays = new Grid();
            Grid.SetRow(wDays, 4); right.Children.Add(wDays);
            Grid.SetColumn(right, 2);
            weatherGrid.Children.Add(right);
            body.Children.Add(weatherGrid);
            dock.Children.Add(body);
            RenderWeather();
            return dock;
        }

        // ---- «живая» погода: маленькие приборы на плитках, которые оживают вместе с цифрами
        class Ring
        {
            public Grid Root;
            Path arc;
            public Ring(Brush color)
            {
                Root = new Grid { Width = 30, Height = 30 };
                Root.Children.Add(new Ellipse { Stroke = Bar.TrackBrush, StrokeThickness = 3.5, Margin = new Thickness(1.75) });
                arc = new Path { Stroke = color, StrokeThickness = 3.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                Root.Children.Add(arc);
            }
            public void Set(double f)
            {
                if (double.IsNaN(f) || f < 0) f = 0; if (f > 0.999) f = 0.999;
                arc.Data = f < 0.004 ? null : ArcGeometry(15, 15, 13.25, 0, f * 360);
            }
        }

        // Дуга окружности: углы в градусах по часовой стрелке от «12 часов»
        static Geometry ArcGeometry(double cx, double cy, double r, double startDeg, double sweepDeg)
        {
            double a0 = (startDeg - 90) * Math.PI / 180, a1 = (startDeg + sweepDeg - 90) * Math.PI / 180;
            var fig = new PathFigure { StartPoint = new Point(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0)), IsClosed = false };
            fig.Segments.Add(new ArcSegment(new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1)), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            return g;
        }

        Ring gHum, gRain;
        Canvas gCompass, gPressure, gUv, gSun;
        RotateTransform gNeedle = new RotateTransform(0, 15, 15), gPresNeedle = new RotateTransform(0, 15, 17);
        Ellipse gUvDot, gSunDot;

        void BuildWeatherGauges()
        {
            gHum = new Ring(accent);
            gRain = new Ring(new SolidColorBrush(Color.FromRgb(0x4D, 0xA3, 0xFF)));

            // Компас: стрелка показывает, куда дует ветер
            gCompass = new Canvas { Width = 30, Height = 30 };
            var rim = new Ellipse { Width = 28, Height = 28, Stroke = Bar.TrackBrush, StrokeThickness = 2 };
            Canvas.SetLeft(rim, 1); Canvas.SetTop(rim, 1);
            gCompass.Children.Add(rim);
            gCompass.Children.Add(new Polygon { Fill = accent, Points = new PointCollection { new Point(15, 4), new Point(18.5, 17), new Point(15, 15), new Point(11.5, 17) }, RenderTransform = gNeedle });
            gCompass.Children.Add(new Polygon { Fill = Faint, Points = new PointCollection { new Point(15, 26), new Point(17.5, 17), new Point(15, 15), new Point(12.5, 17) }, RenderTransform = gNeedle });

            // Барометр: дуга-шкала и стрелка
            gPressure = new Canvas { Width = 30, Height = 30 };
            gPressure.Children.Add(new Path { Stroke = Bar.TrackBrush, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Data = ArcGeometry(15, 17, 12.5, -110, 220) });
            gPressure.Children.Add(new Line { X1 = 15, Y1 = 17, X2 = 15, Y2 = 7, Stroke = accent, StrokeThickness = 2, StrokeEndLineCap = PenLineCap.Round, RenderTransform = gPresNeedle });
            var hub = new Ellipse { Width = 5, Height = 5, Fill = Fg };
            Canvas.SetLeft(hub, 12.5); Canvas.SetTop(hub, 14.5);
            gPressure.Children.Add(hub);

            // УФ-индекс: цветная шкала от спокойного к опасному и ползунок
            gUv = new Canvas { Width = 12, Height = 30 };
            var scale = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
            scale.GradientStops.Add(new GradientStop(Color.FromRgb(0x4C, 0xD9, 0x64), 0));
            scale.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xD6, 0x0A), 0.35));
            scale.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x8A, 0x1E), 0.6));
            scale.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x45, 0x3A), 0.8));
            scale.GradientStops.Add(new GradientStop(Color.FromRgb(0xAF, 0x52, 0xDE), 1));
            var strip = new Rectangle { Width = 4, Height = 30, RadiusX = 2, RadiusY = 2, Fill = scale };
            Canvas.SetLeft(strip, 4);
            gUv.Children.Add(strip);
            gUvDot = new Ellipse { Width = 9, Height = 9, Fill = Fg, Stroke = Ink, StrokeThickness = 1.5 };
            Canvas.SetLeft(gUvDot, 1.5); Canvas.SetTop(gUvDot, 21);
            gUv.Children.Add(gUvDot);

            // Путь солнца: дуга от восхода до заката и точка «сейчас»
            gSun = new Canvas { Width = 34, Height = 30 };
            gSun.Children.Add(new Path { Stroke = Faint, StrokeThickness = 1.6, StrokeDashArray = new DoubleCollection { 2, 2 }, Data = ArcGeometry(17, 23, 14, -90, 180) });
            gSun.Children.Add(new Line { X1 = 1, Y1 = 23, X2 = 33, Y2 = 23, Stroke = Bar.TrackBrush, StrokeThickness = 1.5 });
            gSunDot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC2, 0x3D)) };
            gSun.Children.Add(gSunDot);
        }

        // Доля светового дня, которая уже прошла: 0 — восход, 1 — закат; вне дня — -1
        public static double DayProgress(string sunrise, string sunset, DateTime now)
        {
            TimeSpan a, b;
            if (!TimeSpan.TryParse(sunrise, out a) || !TimeSpan.TryParse(sunset, out b) || b <= a) return -1;
            double f = (now.TimeOfDay - a).TotalMinutes / (b - a).TotalMinutes;
            return f < 0 || f > 1 ? -1 : f;
        }

        FrameworkElement WTile(string label, out TextBlock value, out TextBlock sub, bool arrow, FrameworkElement gauge)
        {
            var tile = (Border)WTile(label, out value, out sub, arrow);
            if (gauge == null) return tile;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = (FrameworkElement)tile.Child;
            tile.Child = null;
            text.Margin = new Thickness(10, 0, 2, 0);
            g.Children.Add(text);
            gauge.VerticalAlignment = VerticalAlignment.Center;
            gauge.Margin = new Thickness(0, 0, 9, 0);
            Grid.SetColumn(gauge, 1);
            g.Children.Add(gauge);
            tile.Child = g;
            return tile;
        }

        FrameworkElement WTile(string label, out TextBlock value, out TextBlock sub, bool arrow)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 6, 0) };
            sp.Children.Add(new TextBlock { Text = label, Foreground = Faint, FontSize = 10.5 });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            value = new TextBlock { Foreground = Fg, FontSize = 14.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom };
            row.Children.Add(value);
            if (arrow)
            {
                wWindArrow = new TextBlock { Text = "", FontFamily = Icons, FontSize = 10, Foreground = accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 2, 0, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = wWindRot };
                row.Children.Add(wWindArrow);
            }
            sub = new TextBlock { Foreground = Dim, FontSize = 11, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5, 0, 0, 1) };
            row.Children.Add(sub);
            sp.Children.Add(row);
            return new Border { Background = Card, CornerRadius = new CornerRadius(14), Margin = new Thickness(3), Child = sp };
        }

        void FillWeather()
        {
            Color[] sky = Weather.Sky(weather.Code, weather.IsDay);
            skyA.Color = sky[0]; skyB.Color = sky[1];
            wCity.Text = weather.City;
            wSymbol.Text = weather.IsDay || weather.Code > 1 ? Weather.Symbol(weather.Code) : "☾";
            wDesc.Text = Weather.Describe(weather.Code);
            wFeels.Text = "Ощущается как " + Weather.Temp(weather.Feels);
            wHiLo.Text = "Днём " + Weather.Temp(weather.Max) + " · ночью " + Weather.Temp(weather.Min);
            wWindSub.Text = Weather.WindName(weather.WindDir);
            wUvSub.Text = weather.Uv < 3 ? "низкий" : (weather.Uv < 6 ? "средний" : (weather.Uv < 8 ? "высокий" : "опасный"));
            wSun.Text = weather.Sunrise + " · " + weather.Sunset;
            wHours.Children.Clear();
            var h = (Border)WeatherRow(weather.Hours, false); h.Margin = new Thickness(0);
            wHours.Children.Add(h);
            wDays.Children.Clear();
            var d = (Border)WeatherRow(weather.Days, true); d.Margin = new Thickness(0);
            wDays.Children.Add(d);
            weatherAnimAt = Environment.TickCount | 1;
            AnimateWeather();
        }

        // «Цифры оживают»: при открытии вкладки значения набегают от нуля.
        void AnimateWeather()
        {
            if (weather == null || weather.Error != null) return;
            double t = weatherAnimAt == 0 ? 1 : Math.Min(1, unchecked(Environment.TickCount - weatherAnimAt) / 750.0);
            double e = 1 - Math.Pow(1 - t, 3);
            wTemp.Text = Weather.Temp(Math.Round(weather.Temp * e) + 0.0);
            wWind.Text = (weather.Wind * e).ToString("0.0") + " м/с";
            wHum.Text = Math.Round(weather.Humidity * e) + "%";
            wPres.Text = Math.Round(weather.Pressure * e) + " мм";
            wUv.Text = (weather.Uv * e).ToString("0.#");
            wRain.Text = Math.Round(weather.RainProb * e) + "%";
            wWindRot.Angle = (weather.WindDir + 180) * e;
            if (gHum != null)
            {
                // Приборы оживают вместе с цифрами
                gHum.Set(weather.Humidity / 100.0 * e);
                gRain.Set(weather.RainProb / 100.0 * e);
                gNeedle.Angle = (weather.WindDir + 180) * e;                       // стрелка смотрит туда, куда дует
                double pf = Math.Max(0, Math.Min(1, (weather.Pressure - 720) / 60.0));   // 720…780 мм рт. ст.
                gPresNeedle.Angle = -110 + 220 * pf * e;
                double uf = Math.Max(0, Math.Min(1, weather.Uv / 11.0)) * e;
                Canvas.SetTop(gUvDot, 21 - 21 * uf);
                double day = DayProgress(weather.Sunrise, weather.Sunset, DateTime.Now);
                gSunDot.Visibility = day < 0 ? Visibility.Collapsed : Visibility.Visible;
                if (day >= 0)
                {
                    double ang = Math.PI * day * e;   // от левого края дуги к правому
                    Canvas.SetLeft(gSunDot, 17 - 14 * Math.Cos(ang) - 4);
                    Canvas.SetTop(gSunDot, 23 - 14 * Math.Sin(ang) - 4);
                }
            }
            if (t >= 1) weatherAnimAt = 0;
        }

        void RefreshWeather(string city)
        {
            if (string.IsNullOrEmpty(city) || weatherBusy) return;
            weatherBusy = true;
            weatherAt = Environment.TickCount;
            ThreadPool.QueueUserWorkItem(delegate
            {
                WeatherData w = Weather.Fetch(city);
                Dispatcher.BeginInvoke(new Action(delegate { weatherBusy = false; weather = w; RenderWeather(); RenderCompact(); }));
            });
        }

        void RenderWeather()
        {
            bool ok = weather != null && weather.Error == null;
            weatherGrid.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            weatherBody.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            weatherBody.Children.Clear();
            if (weather == null || weather.Error != null)
            {
                string msg = weather != null ? weather.Error : "Введите город и нажмите «Показать».\nТолько после этого приложение обратится в сеть — к open-meteo.com.";
                weatherBody.Children.Add(new TextBlock { Text = msg, Foreground = Dim, FontSize = 13, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 60, 0, 0), LineHeight = 20 });
                return;
            }
            FillWeather();
        }

        FrameworkElement WeatherRow(List<string[]> items, bool days)
        {
            var ug = new UniformGrid { Rows = 1, Columns = Math.Max(1, items.Count) };
            foreach (string[] it in items)
            {
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new TextBlock { Text = it[0], Foreground = Dim, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = it[1], FontFamily = Symbols, Foreground = Fg, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) });
                sp.Children.Add(new TextBlock { Text = days ? it[2] + " " + it[3] : it[2], Foreground = Fg, FontSize = 11.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
                ug.Children.Add(sp);
            }
            return new Border { Background = Card, CornerRadius = new CornerRadius(16), Height = 64, Margin = new Thickness(0, 8, 0, 0), Child = ug };
        }

        // ------------------------------------------------------ Система
        FrameworkElement BuildSystemPage()
        {
            var sp = new StackPanel();
            var tiles = new UniformGrid { Rows = 3, Columns = 4, Height = 216 };
            var lockTile = new Tile("", "Блокировка", delegate { Native.LockWorkStation(); });
            sleepTile = new Tile("", "Сон", delegate
            {
                int now = Environment.TickCount;
                if (sleepArmedAt != 0 && unchecked(now - sleepArmedAt) < 3000) { sleepArmedAt = 0; sleepTile.Label.Text = "Сон"; Native.SetSuspendState(false, false, false); }
                else { sleepArmedAt = now; sleepTile.Label.Text = "Ещё раз"; }
            });
            themeTile = new Tile("", "Тёмная тема", delegate { SetDarkTheme(!IsDarkTheme()); RenderSystem(); });
            micTile = new Tile("", "Микрофон", delegate { Audio.SetMicMuted(!Audio.MicMuted()); RenderSystem(); });
            autoTile = new Tile("", "Автозапуск", delegate { Autostart.Set(!Autostart.IsOn()); RenderSystem(); });
            lyrTile = new Tile("", "Тексты песен", delegate
            {
                Store.Set("lyrics", LyricsOn() ? "off" : "on");
                lyricsKey = null; SetLyrics(null); RenderMedia(); RenderSystem();
            });
            Tile restartTile = null, offTile = null;
            restartTile = new Tile("", "Перезагрузка", delegate { Arm(restartTile, "Перезагрузка", delegate { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false }); }); });
            offTile = new Tile("", "Выключение", delegate { Arm(offTile, "Выключение", delegate { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false }); }); });
            // Центр управления: сеть, Bluetooth, VPN
            netTile = new Tile("", "Сеть", delegate
            {
                if (control.HasWifi)
                {
                    bool on = control.WifiOn != true;
                    ThreadPool.QueueUserWorkItem(delegate { Radios.Set(true, on); Dispatcher.BeginInvoke(new Action(RefreshControl)); });
                }
                else { try { System.Diagnostics.Process.Start("ms-settings:network-status"); } catch { } }
            });
            btTile = new Tile("", "Bluetooth", delegate { ShowBt(true); });
            vpnTile = new Tile("", "VPN", delegate
            {
                if (control.VpnOn == null) { Toast("", "VPN-клиент WARP не найден", null, -1, null); return; }
                bool on = control.VpnOn != true;
                Toast("", on ? "VPN включается…" : "VPN выключается…", null, -1, accent);
                ThreadPool.QueueUserWorkItem(delegate { Warp.Set(on); Thread.Sleep(2500); Dispatcher.BeginInvoke(new Action(RefreshControl)); });
            });
            var setTile = new Tile("", "Настройки", delegate { OpenSettings(); });
            Tile[] ts = { netTile, btTile, vpnTile, themeTile, micTile, autoTile, lyrTile, setTile, lockTile, sleepTile, restartTile, offTile };
            for (int i = 0; i < ts.Length; i++)
            {
                ts[i].Root.Margin = new Thickness(4);
                tiles.Children.Add(ts[i].Root);
            }
            tiles.Margin = new Thickness(-4);
            sp.Children.Add(tiles);

            var stats = new Border { Background = Card, CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 10, 0, 0) };
            var rows = new StackPanel();
            cpuBar = new Bar(PageW - 32 - 96 - 48, 5, accent, 22);
            ramBar = new Bar(PageW - 32 - 96 - 48, 5, new SolidColorBrush(Color.FromRgb(0x5A, 0xA9, 0xFF)), 22);
            rows.Children.Add(StatRow("Процессор", cpuBar, out cpuText));
            rows.Children.Add(StatRow("Память", ramBar, out ramText));
            stats.Child = rows;
            sp.Children.Add(stats);

            var tr = new Grid { Margin = new Thickness(0, 10, 0, 0), Height = 34 };
            var trLeft = new StackPanel { Orientation = Orientation.Horizontal };
            trLeft.Children.Add(new TextBlock { Text = "Таймер", Foreground = Dim, FontSize = 12.5, Width = 70, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
            string[] labels = { "+1 мин", "+5", "+15", "+30" };
            int[] mins = { 1, 5, 15, 30 };
            for (int i = 0; i < labels.Length; i++)
            {
                int m = mins[i];
                var b = Btn(labels[i], false, 0, 30, delegate { AddTimer(m); });
                b.Margin = new Thickness(0, 0, 6, 0);
                b.VerticalAlignment = VerticalAlignment.Center;
                trLeft.Children.Add(b);
            }
            var reset = Btn("Сброс", false, 0, 30, delegate { timerEnd = DateTime.MinValue; timerRinging = false; RenderCompact(); RenderSystem(); SetTargets(); });
            reset.VerticalAlignment = VerticalAlignment.Center;
            trLeft.Children.Add(reset);
            var settingsBtn = Btn("Настройки…", false, 0, 30, delegate { OpenSettings(); });
            settingsBtn.VerticalAlignment = VerticalAlignment.Center;
            settingsBtn.Margin = new Thickness(14, 0, 0, 0);
            trLeft.Children.Add(settingsBtn);
            tr.Children.Add(trLeft);
            timerText = new TextBlock { Foreground = accent, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 16, 0) };
            tr.Children.Add(timerText);
            // Строка таймера убрана отсюда: таймер живёт на вкладке «Заметка» и в командной строке.
            tr.Visibility = Visibility.Collapsed;
            sp.Children.Add(tr);

            sysMain = sp;
            var page = new Grid();
            page.Children.Add(sp);

            // Панель Bluetooth: открывается поверх плиток
            btPanel = new DockPanel { Visibility = Visibility.Collapsed };
            var head = new Grid { Height = 32, Margin = new Thickness(0, 0, 0, 10) };
            var back = Btn("Назад", false, 0, 30, delegate { ShowBt(false); });
            var backRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            backRow.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 11, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0) });
            backRow.Children.Add(new TextBlock { Text = "Назад", Foreground = Fg, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            back.Child = backRow;
            back.HorizontalAlignment = HorizontalAlignment.Left;
            head.Children.Add(back);
            head.Children.Add(new TextBlock { Text = "Bluetooth", Foreground = Fg, FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            btSwitch = Btn("Включён", false, 0, 30, delegate
            {
                bool on = control.BtOn != true;
                btSwitchLabel.Text = on ? "Включается…" : "Выключается…";
                ThreadPool.QueueUserWorkItem(delegate { Radios.Set(false, on); Thread.Sleep(600); Dispatcher.BeginInvoke(new Action(RefreshControl)); });
            });
            btSwitchLabel = (TextBlock)btSwitch.Child;
            btSwitch.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(btSwitch);
            DockPanel.SetDock(head, Dock.Top);
            btPanel.Children.Add(head);
            var btFoot = new TextBlock { Text = "Здесь наушники и колонки, уже связанные с компьютером. Клик подключает или отключает.", Foreground = Faint, FontSize = 11.5, Margin = new Thickness(4, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(btFoot, Dock.Bottom);
            btPanel.Children.Add(btFoot);
            btList = new StackPanel();
            btPanel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = btList });
            page.Children.Add(btPanel);
            return page;
        }

        void ShowBt(bool on)
        {
            btPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            sysMain.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (on) { RenderBt(); RefreshControl(); }
        }

        // Состояние сети, Bluetooth и VPN опрашивается в фоне, чтобы интерфейс не замирал.
        void RefreshControl()
        {
            if (controlBusy) return;
            controlBusy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var c = new ControlState();
                try
                {
                    foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                        var t = ni.NetworkInterfaceType;
                        if (t == System.Net.NetworkInformation.NetworkInterfaceType.Loopback || t == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;
                        if (ni.GetIPProperties().GatewayAddresses.Count == 0) continue;
                        c.NetUp = true;
                        c.NetName = t == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet";
                        break;
                    }
                }
                catch { }
                c.WifiOn = Radios.Get(true);
                c.HasWifi = c.WifiOn != null;
                c.BtOn = Radios.Get(false);
                c.VpnOn = Warp.Status();
                c.Bt = BtAudio.List();
                Dispatcher.BeginInvoke(new Action(delegate { controlBusy = false; control = c; RenderControl(); }));
            });
        }

        void RenderControl()
        {
            netTile.Label.Text = control.HasWifi ? (control.WifiOn == true ? (control.NetUp && control.NetName == "Wi-Fi" ? "Wi-Fi" : "Wi-Fi вкл") : "Wi-Fi выкл") : (control.NetUp ? control.NetName : "Нет сети");
            netTile.Glyph.Text = control.HasWifi ? "" : "";
            netTile.SetActive(control.HasWifi ? control.WifiOn == true : control.NetUp);
            btTile.SetActive(control.BtOn == true);
            int connected = control.Bt.FindAll(delegate(BtDevice b) { return b.Connected; }).Count;
            btTile.Label.Text = control.BtOn == null ? "Bluetooth нет" : (connected > 0 ? "Bluetooth · " + connected : "Bluetooth");
            vpnTile.SetActive(control.VpnOn == true);
            vpnTile.Label.Text = control.VpnOn == null ? "VPN не найден" : (control.VpnOn == true ? "VPN включён" : "VPN");
            if (btPanel.Visibility == Visibility.Visible) RenderBt();
        }

        void RenderBt()
        {
            btSwitchLabel.Text = control.BtOn == null ? "Нет модуля" : (control.BtOn == true ? "Включён" : "Выключен");
            btList.Children.Clear();
            if (control.Bt.Count == 0)
            {
                btList.Children.Add(new TextBlock { Text = control.BtOn == true ? "Связанных наушников и колонок не найдено" : "Bluetooth выключен", Foreground = Dim, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 70, 0, 0) });
                return;
            }
            foreach (BtDevice bd in control.Bt)
            {
                BtDevice dev = bd;
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 17, Foreground = dev.Connected ? accent : Dim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                var name = new TextBlock { Text = dev.Name, Foreground = Fg, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(name, 1); row.Children.Add(name);
                var status = new TextBlock { Text = dev.Connected ? "Подключено" : "Подключить", Foreground = dev.Connected ? accent : Dim, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 16, 0) };
                Grid.SetColumn(status, 2); row.Children.Add(status);
                var b = new Border { Background = Chip, CornerRadius = new CornerRadius(14), Height = 48, Margin = new Thickness(0, 0, 0, 6), Child = row, Cursor = Cursors.Hand };
                b.MouseEnter += delegate { b.Background = ChipHover; };
                b.MouseLeave += delegate { b.Background = Chip; };
                b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    bool connect = !dev.Connected;
                    status.Text = connect ? "Подключается…" : "Отключается…";
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        bool ok = BtAudio.SetConnected(dev, connect);
                        Thread.Sleep(3500);
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (!ok) Toast("", "Не удалось: " + dev.Name, null, -1, null);
                            RefreshControl();
                        }));
                    });
                };
                btList.Children.Add(b);
            }
        }

        FrameworkElement StatRow(string label, Bar bar, out TextBlock value)
        {
            var r = new StackPanel { Orientation = Orientation.Horizontal, Height = 24 };
            r.Children.Add(new TextBlock { Text = label, Foreground = Dim, FontSize = 12.5, Width = 96, VerticalAlignment = VerticalAlignment.Center });
            r.Children.Add(bar.Root);
            value = new TextBlock { Foreground = Fg, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Width = 48, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            r.Children.Add(value);
            return r;
        }

        static bool IsDarkTheme()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                { object v = k == null ? null : k.GetValue("AppsUseLightTheme"); return v is int && (int)v == 0; }
            }
            catch { return false; }
        }

        static void SetDarkTheme(bool dark)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", true))
                {
                    if (k == null) return;
                    k.SetValue("AppsUseLightTheme", dark ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
                    k.SetValue("SystemUsesLightTheme", dark ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
                IntPtr res;
                Native.SendMessageTimeout(new IntPtr(0xffff), 0x1A, IntPtr.Zero, "ImmersiveColorSet", 2, 200, out res);
            }
            catch { }
        }

        static bool MicBusy()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone"))
                    return k != null && MicScan(k);
            }
            catch { return false; }
        }

        static bool MicScan(Microsoft.Win32.RegistryKey k)
        {
            foreach (string n in k.GetSubKeyNames())
            {
                using (var s = k.OpenSubKey(n))
                {
                    if (s == null) continue;
                    object v = s.GetValue("LastUsedTimeStop");
                    if (v is long && (long)v == 0) return true;
                    if (n == "NonPackaged" && MicScan(s)) return true;
                }
            }
            return false;
        }

        // ------------------------------------------- окно и интеграция с системой
        // ---------------------------------------------------- горячие клавиши
        public List<Hot> Hots = new List<Hot>();

        void InitHotkeys()
        {
            const uint CA = 0x1 | 0x2; // Alt + Ctrl
            Hots.Clear();
            Hots.Add(new Hot { Id = 4, Key = "cmd", Name = "Командная строка", DefMods = 0x1, DefVk = 0x20, Run = ToggleCommand });
            Hots.Add(new Hot { Id = 1, Key = "area", Name = "Снимок области с разметкой", DefMods = CA, DefVk = 0x53, Run = delegate { StartSnip(false); } });
            Hots.Add(new Hot { Id = 2, Key = "full", Name = "Снимок всего экрана", DefMods = CA | 0x4, DefVk = 0x53, Run = FullShot });
            Hots.Add(new Hot { Id = 3, Key = "color", Name = "Пипетка", DefMods = CA, DefVk = 0x43, Run = delegate { StartSnip(true); } });
            Hots.Add(new Hot { Id = 11, Key = "ocr", Name = "Текст с экрана", DefMods = CA, DefVk = 0x4F, Run = StartOcr });
            Hots.Add(new Hot { Id = 7, Key = "qr", Name = "QR-код с экрана", DefMods = CA, DefVk = 0x51, Run = ScanQr });
            Hots.Add(new Hot { Id = 8, Key = "rec", Name = "Запись экрана", DefMods = CA, DefVk = 0x52, Run = delegate { ToggleRecord(false); } });
            Hots.Add(new Hot { Id = 10, Key = "chat", Name = "Чат с Claude", DefMods = CA, DefVk = 0x41, Run = OpenChat });
            Hots.Add(new Hot { Id = 9, Key = "dictate", Name = "Диктовка", DefMods = CA, DefVk = 0x44, Run = Dictate });
            Hots.Add(new Hot { Id = 6, Key = "theme", Name = "Светлая / тёмная тема", DefMods = CA, DefVk = 0x54, Run = ToggleLightDark });
            foreach (Hot h in Hots)
            {
                h.Mods = h.DefMods; h.Vk = h.DefVk;
                string[] p = Store.Get("hk_" + h.Key).Split(',');
                uint m, v;
                if (p.Length == 2 && uint.TryParse(p[0], out m) && uint.TryParse(p[1], out v)) { h.Mods = m; h.Vk = v; }
                RegisterHot(h);
            }
            // Alt+Space часто занят (PowerToys Run и подобные): тогда командная строка сама переезжает на Ctrl+Alt+Space.
            Hot cmd = Hots[0];
            if (!cmd.Ok && cmd.Mods == cmd.DefMods && cmd.Vk == cmd.DefVk)
            {
                // Перебираем запасные варианты и берём первый свободный: Ctrl+Alt+Space, Alt+Shift+Space, Ctrl+Shift+Space, Ctrl+Alt+K.
                uint[][] alt = { new uint[] { CA, 0x20 }, new uint[] { 0x1 | 0x4, 0x20 }, new uint[] { 0x2 | 0x4, 0x20 }, new uint[] { CA, 0x4B } };
                foreach (uint[] a in alt)
                {
                    cmd.Mods = a[0]; cmd.Vk = a[1];
                    RegisterHot(cmd);
                    if (cmd.Ok) break;
                }
            }
        }

        // На время ввода нового сочетания в настройках все сочетания снимаются, потом возвращаются.
        public void SuspendHotkeys(bool on)
        {
            foreach (Hot h in Hots)
            {
                if (on) Native.UnregisterHotKey(hwnd, h.Id);
                else RegisterHot(h);
            }
        }

        void RegisterHot(Hot h)
        {
            Native.UnregisterHotKey(hwnd, h.Id);
            h.Ok = h.Vk == 0 || Native.RegisterHotKey(hwnd, h.Id, h.Mods | 0x4000, h.Vk);
        }

        // Vk = 0 — сочетание отключено. Возвращает false, если сочетание занято другой программой.
        public bool SetHotkey(Hot h, uint mods, uint vk)
        {
            h.Mods = mods; h.Vk = vk;
            Store.Set("hk_" + h.Key, mods + "," + vk);
            RegisterHot(h);
            return h.Ok;
        }

        public static string HotText(uint mods, uint vk)
        {
            if (vk == 0) return "Отключено";
            var sb = new StringBuilder();
            if ((mods & 0x2) != 0) sb.Append("Ctrl+");
            if ((mods & 0x1) != 0) sb.Append("Alt+");
            if ((mods & 0x4) != 0) sb.Append("Shift+");
            if ((mods & 0x8) != 0) sb.Append("Win+");
            Key k = KeyInterop.KeyFromVirtualKey((int)vk);
            string name = k.ToString();
            if (k >= Key.D0 && k <= Key.D9) name = ((int)(k - Key.D0)).ToString();
            else if (k == Key.Space) name = "Space";
            else if (k == Key.OemTilde) name = "`";
            sb.Append(name);
            return sb.ToString();
        }

        // ------------------------------------------------------------ глаза
        void UpdateEyes()
        {
            Native.POINT pt;
            if (!Native.GetCursorPos(out pt)) return;
            // Центр глаз в координатах экрана
            double ex = Left + (BaseW / 2 - sw.X / 2 + Ear + 12 + 13) * Scale, ey = Top + CH / 2 * Scale;
            double dx = pt.X / dpi - ex, dy = pt.Y / dpi - ey, len = Math.Sqrt(dx * dx + dy * dy);
            double reach = Math.Min(1, len / 140);
            double ox = len < 1 ? 0 : dx / len * 2.6 * reach, oy = len < 1 ? 0 : dy / len * 2.4 * reach;
            int now = Environment.TickCount;
            if (blinkAt == 0) blinkAt = now + 2500 + rnd.Next(4000);
            if (unchecked(now - blinkAt) >= 0) { blinkUntil = now + 130; blinkAt = now + 2500 + rnd.Next(4500); }
            bool asleep = Native.IdleMs() > 120000;
            double lid = asleep ? 0.14 : (unchecked(now - blinkUntil) < 0 ? 0.12 : 1);
            if (character != Character) BuildCharacter();

            // Настроение: тревога, когда звенит таймер; задумчивость, пока отвечает нейросеть; радость после удачного действия.
            string mood = TestMood ?? (timerRinging ? "alarm" : chatBusy ? "think" : unchecked(now - happyUntil) < 0 ? "happy" : asleep ? "sleep" : "");
            bool happy = mood == "happy", alarm = mood == "alarm";
            double wide = 1, shrink = 1;
            if (alarm) { ox = rnd.NextDouble() * 1.8 - 0.9; oy = rnd.NextDouble() * 1.4 - 0.7; lid = 1.16; wide = 1.1; shrink = 0.7; }
            else if (mood == "think") { ox = Math.Sin(now / 420.0) * 1.7; oy = -2.3; lid = 1; }
            else if (mood == "sleep") lid = 0.14;
            bool dots = character == "dots";
            for (int i = 0; i < 2; i++)
            {
                double w = pupilW * shrink, h = pupilH * shrink;
                if (dots && lid < 0.5) h = 1.6;   // у «точек» нет белков — при моргании сплющивается сама точка
                pupils[i].Width = w; pupils[i].Height = h;
                Canvas.SetLeft(pupils[i], i * 14 + (11 - w) / 2 + ox);
                Canvas.SetTop(pupils[i], 0.5 + (13 - h) / 2 + oy * (character == "cat" ? 0.4 : 1));
                lids[i].ScaleY = lid; lids[i].ScaleX = wide;
                whites[i].Visibility = happy ? Visibility.Hidden : Visibility.Visible;
                pupils[i].Visibility = happy || (!dots && lid < 0.5) ? Visibility.Hidden : Visibility.Visible;
                smiles[i].Visibility = happy ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // ------------------------------------------------ уведомления Windows
        volatile bool notifStop;
        bool notifRunning;

        static T WaitOp<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            int n = 0;
            while (op.Status == Windows.Foundation.AsyncStatus.Started && n++ < 1000) Thread.Sleep(10);
            return op.Status == Windows.Foundation.AsyncStatus.Completed ? op.GetResults() : default(T);
        }

        // Показывает новые уведомления других программ карточкой на острове. Сами уведомления не сохраняются.
        void StartNotifications()
        {
            if (notifRunning) { notifStop = false; return; }
            notifRunning = true;
            notifStop = false;
            var t = new Thread(delegate()
            {
                var seen = new HashSet<uint>();
                bool first = true;
                try
                {
                    var listener = Windows.UI.Notifications.Management.UserNotificationListener.Current;
                    var access = WaitOp(listener.RequestAccessAsync());
                    if (access != Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate { Toast("", "Windows не дала доступ к уведомлениям", null, -1, null); }));
                        return;
                    }
                    while (!notifStop)
                    {
                        var list = WaitOp(listener.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast));
                        if (list != null)
                            foreach (var n in list)
                            {
                                if (!seen.Add(n.Id) || first) continue; // то, что уже висело при запуске, не показываем
                                string app = "", title = "", body = "";
                                try { app = n.AppInfo.DisplayInfo.DisplayName; } catch { }
                                try
                                {
                                    var b = n.Notification.Visual.GetBinding(Windows.UI.Notifications.KnownNotificationBindings.ToastGeneric);
                                    if (b != null)
                                    {
                                        int i = 0;
                                        foreach (var tx in b.GetTextElements())
                                        {
                                            if (i++ == 0) title = tx.Text; else body += (body.Length > 0 ? " " : "") + tx.Text;
                                        }
                                    }
                                }
                                catch { }
                                string a = app, text = (title + (body.Length > 0 ? " — " + body : "")).Trim();
                                if (text.Length == 0) continue;
                                // Discord уже присылает свои сообщения напрямую (с аватаром) — второй раз то же самое не показываем.
                                if (DiscordBridge.Clients > 0 && a.IndexOf("discord", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                // Значок программы, приславшей уведомление, — в угол карточки
                                byte[] logo = null;
                                try
                                {
                                    var stream = WaitOp(n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(48, 48)).OpenReadAsync());
                                    if (stream != null && stream.Size > 0 && stream.Size < 1024 * 1024)
                                    {
                                        var reader = new Windows.Storage.Streams.DataReader(stream);
                                        WaitOp(reader.LoadAsync((uint)stream.Size));
                                        logo = new byte[stream.Size];
                                        reader.ReadBytes(logo);
                                    }
                                }
                                catch { logo = null; }
                                Dispatcher.BeginInvoke(new Action(delegate
                                {
                                    ShowCardEx(null, "", a.Length > 0 ? a : "Уведомление", text, delegate { }, null, null, false);
                                    SetCardBadge(ImageFrom(logo));
                                    HideCardButton();
                                    cardUntil = Environment.TickCount + 5500;
                                }));
                            }
                        first = false;
                        for (int i = 0; i < 12 && !notifStop; i++) Thread.Sleep(100);
                    }
                }
                catch (Exception ex) { Store.LogError("уведомления Windows", ex); }
                finally { notifRunning = false; }
            });
            t.IsBackground = true;
            t.Start();
        }

        // ------------------------------------------------------ Discord и Steam
        List<SteamGame> steamGames = new List<SteamGame>();
        bool steamScanning, steamFirst = true;
        int steamRunId;
        DateTime steamRunAt;

        static ImageSource ImageFrom(byte[] data)
        {
            if (data == null) return null;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = 96;
                bi.StreamSource = new System.IO.MemoryStream(data);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        // Сообщение или звонок из Discord (приходит от плагина XSOverlay в Vencord)
        public void DiscordNotify(string title, string text, byte[] icon)
        {
            Action open = delegate { try { System.Diagnostics.Process.Start("discord://-/channels/@me"); } catch { } };
            ShowCardEx(ImageFrom(icon), "", title, text, open, null, null, false);
            SetCardBadge(SourceIcon("discord"));
            HideCardButton();
            cardUntil = Environment.TickCount + 6000;
        }

        public bool TestSteam;   // ключ --steamtest

        // ---- подсказки про интеграции: один раз предлагаем то, что можно подключить на этом компьютере
        public bool Plain;        // обычный запуск без проверочных ключей
        public string TestHint;   // ключ --hinttest steam|discord|steamdone

        void StartIntegrationHints()
        {
            if (!Plain && TestHint == null) return;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(TestHint != null ? 2.5 : 12) };
            int tries = 0;
            t.Tick += delegate
            {
                // Ждём спокойного момента: не поверх игры, открытой панели или другой карточки
                if (TestHint == null && (state != 0 || hiddenFs || toastOn) && ++tries < 40) return;
                t.Stop();
                ShowIntegrationHint();
            };
            t.Start();
        }

        void ShowIntegrationHint()
        {
            string which = TestHint;
            if (which == null)
            {
                if (Steam.Found && SteamBridge.MillenniumFound && !SteamBridge.PluginInstalled && Store.Get("hintSteam") != "1") which = "steam";
                else if (DiscordBridge.VencordFound && !DiscordBridge.XsOverlayEnabled && DiscordBridge.Clients == 0 && Store.Get("hintDiscord") != "1") which = "discord";
            }
            if (which == "steam")
            {
                if (TestHint == null) Store.Set("hintSteam", "1");
                ShowCardChoice(SourceIcon("steam"), "", "Уведомления Steam на острове", "Сообщения, приглашения, загрузки игр", "Подключить", delegate
                {
                    if (SteamBridge.InstallPlugin()) SteamPluginInstalledHint();
                    else Toast("", "Не удалось записать в папку Millennium", null, -1, null);
                }, delegate { });
            }
            else if (which == "steamdone") SteamPluginInstalledHint();
            else if (which == "discord")
            {
                if (TestHint == null) Store.Set("hintDiscord", "1");
                ShowCardEx(SourceIcon("discord"), "", "Сообщения Discord на острове", "В Vencord включите плагин XSOverlay", delegate { OpenSettings(); }, "Подробнее", delegate { OpenSettings(); }, false);
                cardUntil = Environment.TickCount + 15000;
            }
        }

        // Плагин положен в папку, но включить его в Millennium может только сам человек — говорим, где.
        void SteamPluginInstalledHint()
        {
            ShowCardEx(SourceIcon("steam"), "", "Остался один шаг", "В Steam: Millennium → Плагины → включите Kapsula", delegate { }, "Понятно", delegate { }, false);
            cardUntil = Environment.TickCount + 20000;
        }

        // Всплывающее уведомление Steam (приходит от плагина Kapsula для Millennium)
        public void SteamNotify(string title, string text, byte[] icon, Action open, string replyTo)
        {
            // У сообщения друга есть кнопка «Ответить»: ответ набирается прямо на острове.
            if (replyTo != null) ShowCardEx(ImageFrom(icon), "", title, text, open, "Ответить", delegate { StartReply(replyTo, title); }, false);
            else ShowCardEx(ImageFrom(icon), "", title, text, open, null, null, false);
            // Значок Steam — только на сообщениях: там важно, откуда пишут. У достижений и «друг в игре» источник и так понятен.
            if (replyTo != null) SetCardBadge(SourceIcon("steam")); else HideCardButton();
            cardUntil = Environment.TickCount + (replyTo != null ? 8000 : 6000);
            if (TestReply && replyTo != null && !replyShown)
            {
                // Проверочный режим: один раз показываем поле ответа с набранным текстом. Ничего не отправляется.
                replyShown = true;
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                t.Tick += delegate { t.Stop(); StartReply(replyTo, title); cmdBox.Text = "привет, сейчас зайду"; };
                t.Start();
            }
        }
        bool replyShown;
        public bool TestReply;   // ключ --replytest

        // ---- загрузка игры в Steam: значок, полоса и проценты в капсуле
        volatile string steamDlApp;
        string steamDlName = "";
        double steamDlPct, steamDlRate;
        bool steamDlPaused, steamDlDone;
        int steamDlAt;
        Border cDlIcon;
        TextBlock cDlName;
        double steamDlNameW;

        public void SteamDownload(string appid, string name, double percent, double rate, bool paused, byte[] icon)
        {
            if (appid == "" || appid == "0")
            {
                // Загрузка кончилась: полоса доезжает до конца, секунду-полторы висит «готово», и только потом капсула возвращается к часам.
                if (steamDlApp != null && !steamDlDone)
                {
                    steamDlDone = true;
                    steamDlAt = Environment.TickCount;
                    if (!toastOn) RenderCompact();
                    var done = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1900) };
                    done.Tick += delegate
                    {
                        done.Stop();
                        if (!steamDlDone) return;   // за это время началась следующая загрузка
                        steamDlDone = false; steamDlApp = null;
                        cBar.SetColor(Fg);
                        RenderCompact(); SetTargets();
                    };
                    done.Start();
                }
                return;
            }
            if (steamDlDone) { steamDlDone = false; cBar.SetColor(Fg); }
            bool started = steamDlApp != appid;
            steamDlApp = appid; steamDlName = name; steamDlPct = percent; steamDlRate = rate; steamDlPaused = paused;
            steamDlAt = Environment.TickCount;
            if (started)
            {
                ImageSource img = ImageFrom(icon);
                cDlIcon.Background = img != null ? (Brush)new ImageBrush(img) { Stretch = Stretch.UniformToFill } : Chip;
                if (state == 0 && !hiddenFs) Toast("", name != "" ? "Скачивается: " + name : "Steam скачивает игру", null, -1, accent);
            }
            if (!toastOn) RenderCompact();
            if (started) SetTargets();   // после RenderCompact: там измеряется ширина названия
        }

        // Приглашение в игру: «Принять» открывает ссылку входа из самого приглашения, крестик отклоняет его в Steam.
        public void SteamInvite(string title, string text, byte[] icon, string url, string steamid)
        {
            Action accept = delegate { try { System.Diagnostics.Process.Start(url); } catch { } };
            Action decline = delegate
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(steamid ?? "", "^\\d{5,20}$"))
                    SteamBridge.Send("{\"cmd\":\"dismissInvite\",\"steamid\":\"" + steamid + "\"}");
            };
            // Вместо длинного «такой-то приглашает вас в игру такую-то» — коротко: имя в заголовке, игра в подписи.
            var m = System.Text.RegularExpressions.Regex.Match(url ?? "", "^steam://(?:joinlobby|rungame)/(\\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success)
            {
                SteamGame g = steamGames.Find(delegate(SteamGame x) { return x.Id == m.Groups[1].Value; });
                text = g != null ? "Зовёт в " + g.Name : "Зовёт в игру";
            }
            ShowCardChoice(ImageFrom(icon), "", title, text, "Принять", accept, decline);
        }

        string SteamName(int id)
        {
            SteamGame g = steamGames.Find(delegate(SteamGame x) { return x.Id == id.ToString(); });
            return g != null ? g.Name : "Игра Steam";
        }

        // Раз в 2 секунды: что запущено. Раз в 15 секунд: список игр и загрузки (чтение файлов — в фоне).
        void PollSteam()
        {
            if (!Steam.Enabled) return;
            int run = Steam.RunningAppId();
            if (run != steamRunId)
            {
                if (steamRunId != 0 && run == 0)
                {
                    TimeSpan played = DateTime.Now - steamRunAt;
                    if (played.TotalMinutes >= 1) Toast("", SteamName(steamRunId) + " · " + (played.TotalHours >= 1 ? (int)played.TotalHours + " ч " + played.Minutes + " мин" : played.Minutes + " мин"), null, -1, accent);
                }
                else if (run != 0 && !steamFirst) Toast("", SteamName(run), null, -1, accent);
                if (run != 0) steamRunAt = DateTime.Now;
                steamRunId = run;
            }
            if ((tick % 30 == 0 || steamFirst) && !steamScanning)
            {
                steamScanning = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    List<SteamGame> fresh = null;
                    try { fresh = Steam.Scan(); } catch (Exception ex) { Store.LogError("Steam", ex); }
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        steamScanning = false;
                        if (fresh == null) return;
                        if (!steamFirst)
                            foreach (SteamGame g in fresh)
                            {
                                // Была в загрузке, стала готова — сообщаем карточкой
                                SteamGame old = steamGames.Find(delegate(SteamGame x) { return x.Id == g.Id; });
                                if (g.Ready && (old == null || !old.Ready) && g.Id != steamRunId.ToString())
                                {
                                    string id = g.Id;
                                    ShowCardEx(null, "", "Steam: игра готова", g.Name, delegate { Steam.Launch(id); }, "Играть", delegate { Steam.Launch(id); }, false);
                                }
                            }
                        steamGames = fresh;
                        steamFirst = false;
                    }));
                });
            }
        }

        // -------------------------------------------- положение, автоскрытие
        void PlaceWindow()
        {
            var all = WF.Screen.AllScreens;
            WF.Screen scr = MonitorIndex >= 0 && MonitorIndex < all.Length ? all[MonitorIndex] : WF.Screen.PrimaryScreen;
            Left = scr.Bounds.X / dpi + (scr.Bounds.Width / dpi - Width) / 2;
            Top = scr.Bounds.Y / dpi;
        }

        public int MonitorCount { get { return WF.Screen.AllScreens.Length; } }

        public void SetMonitor(int idx)
        {
            MonitorIndex = idx;
            Store.Set("monitor", idx.ToString());
            PlaceWindow();
        }

        public void SetLyricOffset(double v)
        {
            LyricOffset = v;
            Store.Set("lyricOffset", v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            var src = PresentationSource.FromVisual(this);
            if (src != null && src.CompositionTarget != null) dpi = src.CompositionTarget.TransformToDevice.M11;
            SetNoActivate(true);
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            Native.AddClipboardFormatListener(hwnd);
            // Остров не попадает на скриншоты, записи и трансляции.
            ApplyCapture();
            PlaceWindow();
            InitHotkeys();
            if (ShowNotifications) StartNotifications();
            if (TestChat)
            {
                // Проверочный режим: образец переписки без обращения к сети.
                AddBubble(true, "Сколько будет 15% от 2400?");
                AddBubble(false, "360.\n\nСчитается так: 2400 × 0,15 = 360. Если нужно отнять эти проценты от суммы, останется 2040.");
                AddBubble(true, "А как это быстро прикинуть в уме?");
                AddBubble(false, "Возьмите 10% — это 240, затем половину от них — 120. В сумме 360.");
            }
            if (TestSettings) OpenSettings();
            if (TestNote)
            {
                // Проверочный режим: образец оформления, в файл заметки не пишется.
                noteLoading = true;
                var d = noteRich.Document;
                d.Blocks.Clear();
                var p1 = new System.Windows.Documents.Paragraph();
                p1.Inlines.Add(new System.Windows.Documents.Run("Выпуск в пятницу") { FontWeight = FontWeights.Bold });
                d.Blocks.Add(p1);
                var p2 = new System.Windows.Documents.Paragraph();
                p2.Inlines.Add(new System.Windows.Documents.Run("Обычный, "));
                p2.Inlines.Add(new System.Windows.Documents.Run("курсив") { FontStyle = FontStyles.Italic });
                p2.Inlines.Add(new System.Windows.Documents.Run(", "));
                p2.Inlines.Add(new System.Windows.Documents.Run("зачёркнутый") { TextDecorations = TextDecorations.Strikethrough });
                p2.Inlines.Add(new System.Windows.Documents.Run(", "));
                p2.Inlines.Add(new System.Windows.Documents.Run("выделенный") { Background = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xD6, 0x0A)) });
                d.Blocks.Add(p2);
                var list = new System.Windows.Documents.List { MarkerStyle = TextMarkerStyle.Disc };
                list.ListItems.Add(new System.Windows.Documents.ListItem(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Пункт списка"))));
                list.ListItems.Add(new System.Windows.Documents.ListItem(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Ещё пункт"))));
                d.Blocks.Add(list);
                d.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("☐ Проверить обновление")));
                d.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("☑ Написать заметку")));
            }
            if (TestRec)
            {
                // Проверочный режим: запись на несколько секунд без нажатий клавиш.
                var t1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
                t1.Tick += delegate
                {
                    t1.Stop();
                    ToggleRecord(false);
                    var t2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(6500) };
                    t2.Tick += delegate { t2.Stop(); ToggleRecord(false); };
                    t2.Start();
                };
                t1.Start();
            }
            if (TestCmd != null || TestCard)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
                t.Tick += delegate
                {
                    t.Stop();
                    if (TestCard) ShowCard(null, "", "Загружено", "Отчёт за квартал.pdf · 2,4 МБ", null);
                    else { ToggleCommand(); cmdBox.Text = TestCmd; }
                };
                t.Start();
            }
            if (ForceOpen >= 0) { ShowPage(Math.Min(ForceOpen, pages.Length - 1)); SetExpanded(true); }
            else if (ForceOpen == -2) SetState(1);
            else if (ForceOpen == -3) SetTargets();
            StartIntegrationHints();
            if (Demo)
            {
                TestLyrics = true;   // не искать текст выдуманной песни в сети
                media = new MediaInfo { Has = true, Playing = true, Title = "Ночной маршрут", Artist = "Kapsula Demo", Cover = DemoCover(), Pos = 47, Dur = 204, PosAt = DateTime.UtcNow };
                RenderMedia();
                LoadLauncher();
                if (TestFs)
                {
                    var next = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4000) };
                    next.Tick += delegate { next.Stop(); media.Title = "Утренний перегон"; media.Pos = 0; media.PosAt = DateTime.UtcNow; RenderMedia(); };
                    next.Start();
                }
                var once = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                once.Tick += delegate
                {
                    once.Stop();
                    var l = new List<LyricLine>();
                    double t0 = media.Now();
                    string[] demo = { "Город гаснет за стеклом", "Фонари плывут, как по воде", "Мы поедем дальше — до рассвета", "Пусть дорога знает, где наш дом", "Тише, мотор, не буди огни", "Ночь длинна, а мы ещё в пути" };
                    for (int i = 0; i < 60; i++) l.Add(new LyricLine { T = t0 - 3 + i * 2.6, Text = demo[i % demo.Length] });
                    SetLyrics(l);
                };
                once.Start();
            }
            else if (TestLyrics)
            {
                // Проверочный режим: подставные строки каждые 1,3 с от текущей позиции трека.
                var once = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
                once.Tick += delegate
                {
                    once.Stop();
                    var l = new List<LyricLine>();
                    double t0 = media.Now();
                    string[] demo = { "Первая строка для проверки", "Вторая строка подлиннее, чтобы она перенеслась на новую", "Третья", "Четвёртая строка ленты", "Пятая — снова с переносом на вторую строчку текста", "Шестая" };
                    for (int i = 0; i < 60; i++) l.Add(new LyricLine { T = t0 + 1 + i * 1.3, Text = demo[i % demo.Length] });
                    SetLyrics(l);
                };
                once.Start();
            }
        }

        void OnClosed(object sender, EventArgs e)
        {
            if (tray != null) tray.Dispose();
            watcher.Stop();
            if (hwnd != IntPtr.Zero)
            {
                Native.RemoveClipboardFormatListener(hwnd);
                foreach (Hot h in Hots) Native.UnregisterHotKey(hwnd, h.Id);
                notifStop = true;
                if (Recorder.Active) Recorder.Stop(); // не оставляем ffmpeg писать после выхода
            }
        }

        void SetNoActivate(bool on)
        {
            if (hwnd == IntPtr.Zero) return;
            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() | Native.WS_EX_TOOLWINDOW;
            if (on) ex |= Native.WS_EX_NOACTIVATE; else ex &= ~Native.WS_EX_NOACTIVATE;
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == Native.WM_CLIPBOARDUPDATE) OnClipboard();
            else if (msg == Native.WM_HOTKEY)
            {
                int id = w.ToInt32();
                Hot hk = Hots.Find(delegate(Hot x) { return x.Id == id; });
                if (hk != null && hk.Run != null) hk.Run();
            }
            return IntPtr.Zero;
        }

        void BuildTray()
        {
            tray = new WF.NotifyIcon();
            using (var bmp = new System.Drawing.Bitmap(32, 32))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(System.Drawing.Color.Transparent);
                    using (var p = new System.Drawing.Pen(System.Drawing.Color.Black, 12))
                    {
                        p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        g.DrawLine(p, 8, 16, 24, 16);
                    }
                    using (var p2 = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0x4C, 0xD9, 0x7B), 4))
                    {
                        p2.StartCap = System.Drawing.Drawing2D.LineCap.Round; p2.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        g.DrawLine(p2, 22, 16, 23, 16);
                    }
                }
                IntPtr hi = bmp.GetHicon();
                tray.Icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hi).Clone();
                Native.DestroyIcon(hi);
            }
            tray.Text = "Kapsula " + Updater.CurrentText;
            var menu = new WF.ContextMenuStrip();
            var auto = new WF.ToolStripMenuItem("Запускать вместе с Windows");
            menu.Opening += delegate { auto.Checked = Autostart.IsOn(); };
            auto.Click += delegate { Autostart.Set(!Autostart.IsOn()); };
            menu.Items.Add(auto);
            var lyr = new WF.ToolStripMenuItem("Тексты песен (lrclib.net)");
            lyr.Click += delegate
            {
                Store.Set("lyrics", LyricsOn() ? "off" : "on");
                lyricsKey = null;
                SetLyrics(null);
                RenderMedia();
            };
            menu.Opening += delegate { lyr.Checked = LyricsOn(); };
            menu.Items.Add(lyr);
            menu.Items.Add("Настройки…", null, delegate { OpenSettings(); });
            menu.Items.Add("Папка со снимками", null, delegate { try { System.Diagnostics.Process.Start(ShotDir()); } catch { } });
            menu.Items.Add("Проверить обновления (сейчас " + Updater.CurrentText + ")", null, delegate { Toast("", "Проверяю обновления…", null, -1, null); Updater.CheckNow(); });
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Выход", null, delegate { tray.Visible = false; Application.Current.Shutdown(); });
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
        }

        // ------------------------------------------------- физика и геометрия
        double CompactWidth()
        {
            if (toastOn) return 300;
            if (timerRinging) return 230;
            if (Recorder.Active) return 240;
            if (steamDlApp != null) return 360 + steamDlNameW;
            if (dlActive != null) return 340;
            bool w = weather != null && weather.Error == null;
            double sec = ShowSeconds ? 24 : 0;
            if (media.Has && media.Playing) return PillTitle || timerEnd > DateTime.Now ? 350 : (w ? 310 : 270) + sec;
            if (timerEnd > DateTime.Now) return 240 + sec;
            return (w ? 220 : 190) + sec;
        }

        void SetTargets()
        {
            sw.Target = StateW();
            sh.Target = StateH();
            sr.Target = state == 2 ? ER : (state == 1 ? MR : (state == 0 ? CR : 28));
            for (int i = 0; i < so.Length; i++) so[i].Target = state == i ? 1 : 0;
            // В полноэкранной программе остров спрятан, но выглядывает на несколько секунд при смене трека (fsPeekUntil).
            bool fsHide = hiddenFs && unchecked(Environment.TickCount - fsPeekUntil) >= 0;
            sy.Target = (fsHide || autoHidden) && state == 0 ? -(CH + 28) : 0;
            Wake();
        }

        double StateW()
        {
            switch (state) { case 1: return MiniW(); case 2: return EW; case 3: return CardW + cardExtra; case 4: return CmdW; default: return CompactWidth(); }
        }

        double StateH()
        {
            switch (state) { case 1: return MH; case 2: return ExpandedH(); case 3: return CardH; case 4: return CmdH(); default: return CH; }
        }

        double CmdH() { return 60 + (cmdItems.Count > 0 ? cmdItems.Count * 42 + 10 : 0); }

        // Жидкий край: каждая «капля» тянется вниз в такт реальной громкости звука.
        bool StepLiquid(double dt)
        {
            bool playingNow = media.Has && media.Playing && !hiddenFs;
            bool on = playingNow && LiquidOn;
            double peak = on ? Audio.Peak() : 0;
            gainMax = Math.Max(peak, gainMax - dt * 0.04);
            if (gainMax < 0.02) gainMax = 0.02;
            double lv = on ? Math.Min(1, peak / gainMax) : 0;
            lv = lv * lv;
            level += (lv - level) * (1 - Math.Exp(-dt * (lv > level ? 45 : 9)));
            double gt = playingNow ? 0.4 + 0.6 * level : 0, et = on ? Math.Min(1, 0.3 + level * 2) : 0;
            glowAmt += (gt - glowAmt) * (1 - Math.Exp(-dt * 8));
            edgeAmt += (et - edgeAmt) * (1 - Math.Exp(-dt * 10));
            liquidClock += dt;
            bool any = glowAmt > 0.004 || edgeAmt > 0.004;
            for (int k = 0; k < amp.Length; k++)
            {
                if (liquidClock >= nextRoll[k]) { noise[k] = rnd.NextDouble(); nextRoll[k] = liquidClock + 0.07 + rnd.NextDouble() * 0.13; }
                double tgt = level * (0.15 + 0.85 * noise[k]);
                amp[k] += (tgt - amp[k]) * (1 - Math.Exp(-dt * (tgt > amp[k] ? 38 : 11)));
                if (amp[k] > 0.004) any = true;
            }
            return on || any;
        }

        static void Wave(StreamGeometryContext c, List<Point> tips, double bl, double br, double h)
        {
            Point prev = new Point(bl, h);
            for (int i = 0; i <= tips.Count; i++)
            {
                Point t = i < tips.Count ? tips[i] : new Point(br, h);
                // Контрольная точка подобрана так, что дуга между каплями касается ровного края.
                c.QuadraticBezierTo(new Point((prev.X + t.X) / 2, h - ((prev.Y - h) + (t.Y - h)) / 2), t, true, false);
                prev = t;
            }
        }

        void Wake()
        {
            if (frameOn) return;
            frameOn = true;
            lastFrame = 0;
            CompositionTarget.Rendering += OnFrame;
        }

        void OnFrame(object sender, EventArgs e)
        {
            double t = ((RenderingEventArgs)e).RenderingTime.TotalSeconds;
            if (t == lastFrame) return;
            if (lastFrame == 0) { liquidAt = t - 0.004; appliedAt = 0; }
            double dt = lastFrame == 0 ? 0.004 : Math.Min(0.05, t - lastFrame);
            lastFrame = t;
            bool springs = sw.Step(dt);
            springs |= sh.Step(dt);
            springs |= sr.Step(dt);
            springs |= sy.Step(dt);
            for (int i = 0; i < so.Length; i++) springs |= so[i].Step(dt);
            // Пока движется форма острова, рисуем каждый кадр монитора. Когда пляшет только жидкий край,
            // хватает около 60 обновлений в секунду: глаз разницы не видит, а нагрузка падает в разы.
            if (springs || t - appliedAt >= 0.0155)
            {
                liquidLive = StepLiquid(Math.Min(0.05, t - liquidAt));
                liquidAt = t;
                ApplyFrame();
                appliedAt = t;
            }
            bool moving = springs || liquidLive;
            if (!moving)
            {
                CompositionTarget.Rendering -= OnFrame;
                frameOn = false;
            }
        }

        static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        void ApplyFrame()
        {
            double w = Math.Max(60, sw.X), h = Math.Max(20, sh.X);
            double r = Math.Max(4, Math.Min(sr.X, h / 2));
            double cx = BaseW / 2, x0 = cx - w / 2, x1 = cx + w / 2, e = Ear;

            // Капли нижнего края: шаг фиксирован от центра, чтобы при растяжении они не прыгали.
            double bl = x0 + r, br = x1 - r, A = state == 0 ? 13 : 16;
            var tips = new List<Point>();
            int kmin = (int)Math.Ceiling((bl + 8 - cx) / Pitch), kmax = (int)Math.Floor((br - 8 - cx) / Pitch);
            for (int k = kmin; k <= kmax; k++)
            {
                int idx = k + 40;
                if (idx < 0 || idx > 80) continue;
                double x = cx + k * Pitch;
                double taper = Clamp01(Math.Min(x - bl, br - x) / 30);
                tips.Add(new Point(x, h + amp[idx] * A * taper));
            }

            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x0 - e, 0), true, true);
                c.ArcTo(new Point(x0, e), new Size(e, e), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(x0, h - r), true, false);
                c.ArcTo(new Point(bl, h), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                Wave(c, tips, bl, br, h);
                c.ArcTo(new Point(x1, h - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                c.LineTo(new Point(x1, e), true, false);
                c.ArcTo(new Point(x1 + e, 0), new Size(e, e), 0, false, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            shape.Data = g;
            clipHost.Clip = g;
            slide.Y = sy.X;

            if (edgeAmt > 0.004)
            {
                var eg = new StreamGeometry();
                using (var c = eg.Open())
                {
                    c.BeginFigure(new Point(x0 + r * 0.3, h - r * 0.3), false, false);
                    c.ArcTo(new Point(bl, h), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                    Wave(c, tips, bl, br, h);
                    c.ArcTo(new Point(x1 - r * 0.3, h - r * 0.3), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                }
                eg.Freeze();
                edge.Data = eg;
                edge.Opacity = Clamp01(edgeAmt);
                edge.Visibility = Visibility.Visible;
            }
            else edge.Visibility = Visibility.Hidden;

            // Слои сменяют друг друга: уходящий гаснет раньше, чем появляется новый.
            compactHost.Width = w + 2 * e;
            for (int i = 0; i < layers.Length; i++)
            {
                double o = Clamp01((so[i].X - 0.5) * 2);
                layers[i].Opacity = o;
                layers[i].Visibility = o <= 0 ? Visibility.Hidden : Visibility.Visible;
                if (layerScale[i] != null)
                {
                    double sc = 0.9 + 0.1 * Clamp01(so[i].X);
                    layerScale[i].ScaleX = sc; layerScale[i].ScaleY = sc;
                }
            }

            double go = Clamp01(glowAmt) * Clamp01(Math.Max(so[1].X, so[2].X * 0.6));
            bottomGlow.Opacity = go;
            bottomGlow.Visibility = go <= 0.004 ? Visibility.Hidden : Visibility.Visible;
            bottomGlow.Margin = new Thickness(0, h - 70 + A, 0, 0);
        }

        void SetExpanded(bool on) { SetState(on ? 2 : 0); }

        void SetState(int s)
        {
            if (state == s) return;
            bool wasFull = state == 2 || state == 4;
            bool wasCmd = state == 4;
            state = s;
            if (wasCmd && s != 4)
            {
                dictating = false;
                if (replySteamId != null) { replySteamId = null; cmdHint.Text = CmdHintDefault; cmdGlyph.Text = cmdGlyphDefault; cmdGlyph.Foreground = Dim; }
                if (prevFg != IntPtr.Zero) { Native.SetForegroundWindow(prevFg); prevFg = IntPtr.Zero; }
            }
            for (int i = 1; i < layers.Length; i++) layers[i].IsHitTestVisible = s == i;
            if (s == 2)
            {
                if (timerRinging) timerRinging = false;
                RenderSystem(); RenderHomeLive(); RenderTextPreview();
            }
            if (s == 1) RenderHomeLive();
            if (wasFull && s != 2 && s != 4)
            {
                Keyboard.ClearFocus();
                SetNoActivate(true);
            }
            RenderCompact();
            SetTargets();
        }

        bool Typing() { return IsActive && Keyboard.FocusedElement is TextBoxBase; }

        void UpdateHover()
        {
            if (ForceOpen != -1 || snipActive) return;
            Native.POINT pt;
            if (!Native.GetCursorPos(out pt)) return;
            double x = (pt.X / dpi - Left) / Scale, y = (pt.Y / dpi - Top) / Scale, cx = BaseW / 2;
            int now = Environment.TickCount;
            if (AutoHide)
            {
                // Автоскрытие: остров уезжает за край и возвращается, когда курсор у верхней кромки или есть что показать.
                bool nearTop = y <= 3 && Math.Abs(x - cx) <= 320;
                bool overPill = !autoHidden && Math.Abs(x - cx) <= sw.X / 2 + 8 && y <= sh.X + 8;
                if (nearTop || overPill || state != 0 || toastOn || timerRinging || Recorder.Active || dlActive != null) revealUntil = now + 1800;
                bool hide = unchecked(now - revealUntil) > 0;
                if (hide != autoHidden) { autoHidden = hide; SetTargets(); }
            }
            else if (autoHidden) { autoHidden = false; SetTargets(); }
            if (state == 0)
            {
                bool inside = !hiddenFs && !autoHidden && Math.Abs(x - cx) <= sw.X / 2 + 2 && y >= -1 && y <= sh.X + 1;
                // Только что нажали кнопку на карточке — курсор ещё над островом, но раскрывать панель не нужно.
                if (hoverBlock) { if (!inside && Math.Abs(x - cx) > (CardW + cardExtra) / 2 + 12 || y > CardH + 16) hoverBlock = false; enterAt = 0; return; }
                if (!inside) { enterAt = 0; return; }
                if (enterAt == 0) enterAt = now;
                else if (unchecked(now - enterAt) >= 60) { enterAt = 0; SetState(media.Has && media.Playing && HoverMini ? 1 : 2); }
            }
            else if (state == 4) { }
            else if (state == 3)
            {
                // Карточка события держится, пока на ней курсор, и уходит сама по истечении времени.
                bool inside = Math.Abs(x - cx) <= (CardW + cardExtra) / 2 + 12 && y <= CardH + 16;
                if (inside) { if (unchecked(cardUntil - now) < 1200) cardUntil = now + 1200; }
                else if (unchecked(now - cardUntil) >= 0) SetState(0);
            }
            else
            {
                double tw = state == 2 ? EW : MiniW(), th = state == 2 ? ExpandedH() : MH;
                bool inside = Math.Abs(x - cx) <= tw / 2 + 14 && y <= th + 20;
                if (inside || Typing() || volBar.Root.IsMouseCaptured) { leaveAt = 0; return; }
                if (leaveAt == 0) leaveAt = now;
                else if (unchecked(now - leaveAt) >= 260) { leaveAt = 0; SetState(0); }
            }
        }

        // ----------------------------------------------------------- отрисовка
        void Toast(string glyph, string text, string right, double value, Brush glyphBrush)
        {
            if (TestCharacter != null) return;   // при съёмке персонажей надписи не нужны
            toastGlyph = glyph; toastText = text; toastRight = right; toastValue = value; toastBrush = glyphBrush;
            if (glyphBrush == accent && value < 0) Happy(3300);   // удачное действие — персонаж радуется, когда надпись уйдёт
            toastUntil = Environment.TickCount + 1800;
            toastOn = true;
            RenderCompact();
            SetTargets();
        }

        void RenderCompact()
        {
            bool playing = media.Has && media.Playing;
            bool timing = timerEnd > DateTime.Now;
            cBar.Root.Visibility = Visibility.Collapsed;
            cText.Visibility = Visibility.Visible;
            cText.MaxWidth = 1000;
            cDlIcon.Visibility = Visibility.Collapsed;
            cDlName.Visibility = Visibility.Collapsed;
            // Steam перестал сообщать о загрузке (закрыт или плагин выгружен) — убираем полосу
            if (steamDlApp != null && unchecked(Environment.TickCount - steamDlAt) > 20000) steamDlApp = null;

            if (toastOn)
            {
                micDot.Visibility = Visibility.Collapsed; cCover.Visibility = Visibility.Collapsed; eq.Visibility = Visibility.Collapsed; eyes.Visibility = Visibility.Collapsed;
                cGlyph.Visibility = string.IsNullOrEmpty(toastGlyph) ? Visibility.Collapsed : Visibility.Visible;
                cGlyph.Text = toastGlyph ?? "";
                cGlyph.Foreground = toastBrush ?? Fg;
                if (toastValue >= 0) { cText.Visibility = Visibility.Collapsed; cBar.Root.Visibility = Visibility.Visible; cBar.Set(toastValue); }
                else { cText.Text = toastText ?? ""; cText.MaxWidth = 210; }
                cRight.Visibility = string.IsNullOrEmpty(toastRight) ? Visibility.Collapsed : Visibility.Visible;
                cRight.Text = toastRight ?? ""; cRight.Foreground = Fg;
                return;
            }

            if (Recorder.Active)
            {
                // Идёт запись экрана: красная точка и время.
                micDot.Visibility = Visibility.Collapsed; cCover.Visibility = Visibility.Collapsed; eq.Visibility = Visibility.Collapsed; eyes.Visibility = Visibility.Collapsed;
                cGlyph.Visibility = Visibility.Visible; cGlyph.Text = ""; cGlyph.Foreground = RecRed;
                TimeSpan el = DateTime.Now - Recorder.Started;
                cText.Text = "Запись · " + (int)el.TotalMinutes + ":" + el.Seconds.ToString("00");
                cRight.Visibility = Visibility.Collapsed;
                return;
            }

            if (steamDlApp != null && !timerRinging)
            {
                // Steam скачивает игру: её значок, полоса и проценты (на паузе — «пауза»).
                micDot.Visibility = Visibility.Collapsed; cCover.Visibility = Visibility.Collapsed; eq.Visibility = Visibility.Collapsed; eyes.Visibility = Visibility.Collapsed;
                cGlyph.Visibility = Visibility.Collapsed;
                cDlIcon.Visibility = Visibility.Visible;
                cDlName.Text = steamDlName; cDlName.Visibility = steamDlName != "" ? Visibility.Visible : Visibility.Collapsed;
                steamDlNameW = Math.Min(112, NaturalWidth(cDlName));   // ширина капсулы зависит от длины названия
                cText.Visibility = Visibility.Collapsed;
                cBar.Root.Visibility = Visibility.Visible;
                cRight.Visibility = Visibility.Visible;
                if (steamDlDone)
                {
                    cBar.SetColor(accent); cBar.Glide(1, 420);
                    cRight.Foreground = accent; cRight.Text = "✓ готово";
                }
                else
                {
                    cBar.Glide(steamDlPct / 100.0, 900);   // плавно между сообщениями Steam, а не рывками
                    cRight.Foreground = Dim;
                    cRight.Text = steamDlPaused ? "пауза" : Math.Round(steamDlPct) + "%" + (steamDlRate > 1024 ? " · " + SizeText(steamDlRate) + "/с" : "");
                }
                return;
            }

            if (dlActive != null && !timerRinging)
            {
                // Идёт загрузка: значок, сколько скачано и скорость.
                micDot.Visibility = Visibility.Collapsed; cCover.Visibility = Visibility.Collapsed; eq.Visibility = Visibility.Collapsed; eyes.Visibility = Visibility.Collapsed;
                cGlyph.Visibility = Visibility.Visible; cGlyph.Text = ""; cGlyph.Foreground = accent;
                cText.Text = "Загрузка · " + SizeText(dlSize);
                cRight.Visibility = Visibility.Visible; cRight.Text = SizeText(dlSpeed) + "/с"; cRight.Foreground = Dim;
                return;
            }

            cGlyph.Visibility = Visibility.Collapsed;
            micDot.Visibility = micInUse && !playing ? Visibility.Visible : Visibility.Collapsed;
            eyes.Visibility = (ShowEyes || TestCharacter != null) && (!playing || TestEyes) ? Visibility.Visible : Visibility.Collapsed;
            if (TestEyes) cCover.Visibility = Visibility.Collapsed;
            cCover.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
            eq.Visibility = playing && !timing && !timerRinging ? Visibility.Visible : Visibility.Collapsed;

            if (timerRinging) { cText.Text = "Время вышло"; cRight.Visibility = Visibility.Collapsed; return; }
            SetClock(cText);
            if (PillTitle && playing)
            {
                // Режим «С названием»: вместо часов — трек и исполнитель.
                cText.Text = string.IsNullOrEmpty(media.Artist) ? media.Title : media.Title + " — " + media.Artist;
                cText.MaxWidth = timing ? 190 : 232;
            }
            if (state == 1) SetClock(nClock);

            if (timing) { cRight.Visibility = Visibility.Visible; cRight.Text = Fmt(timerEnd - DateTime.Now); cRight.Foreground = accent; }
            else cRight.Visibility = Visibility.Collapsed;
        }

        static string Fmt(TimeSpan t)
        {
            if (t.TotalSeconds < 0) t = TimeSpan.Zero;
            int total = (int)Math.Ceiling(t.TotalSeconds);
            if (total >= 3600) return (total / 3600) + ":" + ((total / 60) % 60).ToString("00") + ":" + (total % 60).ToString("00");
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        static string FmtSec(double s) { int t = (int)s; return (t / 60) + ":" + (t % 60).ToString("00"); }

        Color AccentFrom(BitmapSource bi)
        {
            try
            {
                var small = new TransformedBitmap(bi, new ScaleTransform(24.0 / bi.PixelWidth, 24.0 / bi.PixelHeight));
                var fb = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = fb.PixelWidth, h = fb.PixelHeight;
                var px = new byte[w * h * 4];
                fb.CopyPixels(px, w * 4, 0);
                double r = 0, g = 0, b = 0, ws = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    double bb = px[i], gg = px[i + 1], rr = px[i + 2];
                    double max = Math.Max(rr, Math.Max(gg, bb)), min = Math.Min(rr, Math.Min(gg, bb));
                    double sat = max <= 0 ? 0 : (max - min) / max;
                    double wgt = sat * sat * (max / 255.0);
                    r += rr * wgt; g += gg * wgt; b += bb * wgt; ws += wgt;
                }
                if (ws < w * h * 0.02) return Color.FromRgb(0xF2, 0xF2, 0xF4);
                r /= ws; g /= ws; b /= ws;
                double m = Math.Max(r, Math.Max(g, b));
                if (m < 1) return DefaultAccent;
                double k = 240 / m;
                // чуть осветляем, чтобы читалось на чёрном
                return Color.FromRgb((byte)Math.Min(255, r * k * 0.85 + 38), (byte)Math.Min(255, g * k * 0.85 + 38), (byte)Math.Min(255, b * k * 0.85 + 38));
            }
            catch { return DefaultAccent; }
        }

        void RenderMedia()
        {
            playerBody.Visibility = media.Has ? Visibility.Visible : Visibility.Collapsed;
            playerEmpty.Visibility = media.Has ? Visibility.Collapsed : Visibility.Visible;
            if (srcBtn != null)
            {
                bool several = media.Sources != null && media.Sources.Length > 1;
                srcBtn.Visibility = several ? Visibility.Visible : Visibility.Collapsed;
                if (several) srcLabel.Text = MediaWatcher.PrettyName(media.Source);
            }
            if (media.Has)
            {
                mTitle.Text = media.Title;
                mArtist.Text = string.IsNullOrEmpty(media.Artist) ? " " : media.Artist;
                mPlayGlyph.Text = media.Playing ? "" : "";
                nPlayGlyph.Text = mPlayGlyph.Text;
                nTitle.Text = media.Title;
                nArtist.Text = mArtist.Text;
                string lk = media.Title + "|" + media.Artist;
                if (lk != lyricsKey)
                {
                    bool changed = lyricsKey != null;
                    lyricsKey = lk;
                    lyricsDur = media.Dur;
                    SetLyrics(null);
                    // В игре или в видео на весь экран остров спрятан — на смену трека выглядываем с названием.
                    if (changed && hiddenFs && state == 0 && media.Playing && Store.Get("fsTrack") != "0")
                    {
                        Toast("", string.IsNullOrEmpty(media.Artist) ? media.Title : media.Title + " — " + media.Artist, null, -1, accent);
                        toastUntil = Environment.TickCount + 3500;
                        fsPeekUntil = toastUntil + 200;
                        fsPeeking = true;
                        if (AutoHide) revealUntil = fsPeekUntil;
                        SetTargets();
                    }
                    if (LyricsOn() && !TestLyrics) FetchLyrics(lk, media.Title, media.Artist, media.Dur);
                }
                else if (Math.Abs(media.Dur - lyricsDur) > 2 && LyricsOn() && !TestLyrics)
                {
                    // При смене трека плеер сначала отдаёт длительность предыдущего. Когда она уточнилась — ищем заново.
                    lyricsDur = media.Dur;
                    FetchLyrics(lk, media.Title, media.Artist, media.Dur);
                }
                if (!ReferenceEquals(media.Cover, shownCover))
                {
                    shownCover = media.Cover;
                    coverBrush = null;
                    Color ac = DefaultAccent;
                    if (media.Cover != null)
                    {
                        try
                        {
                            var bi = new BitmapImage();
                            bi.BeginInit();
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.DecodePixelWidth = 200;
                            bi.StreamSource = new System.IO.MemoryStream(media.Cover);
                            bi.EndInit();
                            bi.Freeze();
                            coverBrush = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
                            RenderOptions.SetBitmapScalingMode(coverBrush, BitmapScalingMode.HighQuality);
                            ac = AccentFrom(bi);
                        }
                        catch { coverBrush = null; }
                    }
                    UpdateAccent(ac);
                    mCover.Background = coverBrush != null ? (Brush)coverBrush : Chip;
                    cCover.Background = coverBrush != null ? (Brush)coverBrush : Chip;
                    nCover.Background = coverBrush != null ? (Brush)coverBrush : Chip;
                    coverGlow.Visibility = coverBrush != null ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            else
            {
                if (lyricsKey != null) { lyricsKey = null; SetLyrics(null); }
                if (state == 1 && ForceOpen == -1) SetState(0);
            }
            double[] ms = { 420, 300, 520, 360 };
            for (int i = 0; i < eq.Children.Count; i++)
            {
                var st = (ScaleTransform)((Rectangle)eq.Children[i]).RenderTransform;
                if (media.Has && media.Playing)
                {
                    var a = new DoubleAnimation(0.22, 1, TimeSpan.FromMilliseconds(ms[i])) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }, BeginTime = TimeSpan.FromMilliseconds(i * 90) };
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                }
                else st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
            RenderCompact();
            RenderHomeLive();
            SetTargets();
        }

        void RenderHomeLive()
        {
            if (media.Has)
            {
                bool hasTime = media.Dur > 1;
                progressRow.Visibility = hasTime ? Visibility.Visible : Visibility.Hidden;
                if (hasTime)
                {
                    double pos = media.Now();
                    mProgress.Set(pos / media.Dur);
                    mPos.Text = FmtSec(pos);
                    mDur.Text = FmtSec(media.Dur);
                    nProgress.Set(pos / media.Dur);
                    nPos.Text = FmtSec(pos);
                    nRem.Text = "−" + FmtSec(Math.Max(0, media.Dur - pos));
                }
                else { nProgress.Set(0); nPos.Text = ""; nRem.Text = ""; }
            }
            double v = volPlayer ? AppVolume.Get(PlayerKey()) : Audio.GetVolume();
            if (volPlayer && v < 0) { SetVolMode(false); v = Audio.GetVolume(); }   // плеер закрылся — возвращаемся к общей громкости
            if (v >= 0) { volBar.Set(v); volText.Text = Math.Round(v * 100) + "%"; } else volText.Text = "—";
        }

        // ---- ползунок громкости на «Главной»: вся система или только плеер
        bool volPlayer;
        TextBlock volIcon;
        string volIconDefault;

        string PlayerKey() { return MediaWatcher.PrettyName(media.Source); }

        void SetVolMode(bool player)
        {
            volPlayer = player;
            if (volIcon == null) return;
            volIcon.Text = player ? "" : volIconDefault;
            volIcon.Foreground = player ? (Brush)accent : Fg;
            volIcon.ToolTip = player ? "Громкость только плеера. Клик — громкость всей системы" : "Громкость всей системы. Клик — только плеер";
        }

        void ToggleVolMode()
        {
            if (volPlayer) { SetVolMode(false); RenderHomeLive(); return; }
            if (!media.Has || AppVolume.Get(PlayerKey()) < 0) { Toast("", "Не нашёл звук плеера", null, -1, null); return; }
            SetVolMode(true);
            RenderHomeLive();
            Toast("", "Громкость: " + PlayerKey(), null, -1, accent);
        }

        void SetVolumeFrom(Point p)
        {
            double v = volBar.ValueAt(p);
            if (volPlayer) AppVolume.Set(PlayerKey(), v);
            else { Audio.SetVolume(v); lastVol = v; }
            volBar.Set(v);
            volText.Text = Math.Round(v * 100) + "%";
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (!expanded || page != 0) return;
            double v = Audio.GetVolume();
            if (v < 0) return;
            v = Math.Max(0, Math.Min(1, v + (e.Delta > 0 ? 0.02 : -0.02)));
            Audio.SetVolume(v);
            lastVol = v;
            RenderHomeLive();
            e.Handled = true;
        }

        void SampleCpu()
        {
            Native.FILETIME i, k, u;
            if (!Native.GetSystemTimes(out i, out k, out u)) return;
            ulong idle = Native.ToU64(i), total = Native.ToU64(k) + Native.ToU64(u);
            if (lastTotal != 0 && total > lastTotal) cpu = 1.0 - (double)(idle - lastIdle) / (double)(total - lastTotal);
            lastIdle = idle; lastTotal = total;
        }

        void RenderSystem()
        {
            cpuBar.Set(cpu);
            cpuText.Text = Math.Round(cpu * 100) + "%";
            var m = new Native.MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            if (Native.GlobalMemoryStatusEx(ref m)) { ramBar.Set(m.dwMemoryLoad / 100.0); ramText.Text = m.dwMemoryLoad + "%"; }
            timerText.Text = timerEnd > DateTime.Now ? Fmt(timerEnd - DateTime.Now) : "";
            themeTile.SetActive(IsDarkTheme());
            bool muted = Audio.MicMuted();
            micTile.SetActive(muted);
            micTile.Label.Text = muted ? "Микрофон выкл" : "Микрофон";
            autoTile.SetActive(Autostart.IsOn());
            lyrTile.SetActive(LyricsOn());
            if (armedTile != null && unchecked(Environment.TickCount - armedAt) > 3000) { armedTile.Label.Text = armedLabel; armedTile = null; }
            if (sleepArmedAt != 0 && unchecked(Environment.TickCount - sleepArmedAt) > 3000) { sleepArmedAt = 0; sleepTile.Label.Text = "Сон"; }
        }

        void AddTimer(int minutes) { AddTimerSpan(TimeSpan.FromMinutes(minutes)); }

        void AddTimerSpan(TimeSpan span)
        {
            timerRinging = false;
            DateTime from = timerEnd > DateTime.Now ? timerEnd : DateTime.Now;
            timerEnd = from + span;
            timerTotal = timerEnd - DateTime.Now;
            if (ringArc != null) UpdateRing();
            RenderCompact();
            RenderSystem();
            SetTargets();
        }

        // ------------------------------------------------------------ снимки
        static string ShotDir()
        {
            string d = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Kapsula");
            System.IO.Directory.CreateDirectory(d);
            return d;
        }

        static System.Drawing.Bitmap Grab()
        {
            var b = WF.Screen.PrimaryScreen.Bounds;
            var bmp = new System.Drawing.Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(b.X, b.Y, 0, 0, bmp.Size);
            return bmp;
        }

        static BitmapSource ToSource(System.Drawing.Bitmap bmp)
        {
            IntPtr h = bmp.GetHbitmap();
            try
            {
                var s = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                s.Freeze();
                return s;
            }
            finally { Native.DeleteObject(h); }
        }

        void SaveShot(System.Drawing.Bitmap bmp)
        {
            try
            {
                string path = System.IO.Path.Combine(ShotDir(), "Снимок " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".png");
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                BitmapSource bs = ToSource(bmp);
                Clipboard.SetImage(bs);
                ShotCard(bs, path);
            }
            catch { Toast("", "Не удалось сохранить снимок", null, -1, null); }
        }

        // ---------------------------------------------------- QR-код с экрана
        void ScanQr()
        {
            if (state != 0) SetState(0);
            System.Drawing.Bitmap bmp;
            try { bmp = Grab(); } catch { return; }
            Toast("", "Ищу QR-код на экране…", null, -1, null);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string text = null;
                try { text = Qr.Decode(bmp); } catch (Exception ex) { Store.LogError("QR", ex); }
                bmp.Dispose();
                Dispatcher.BeginInvoke(new Action(delegate { ShowQrResult(text); }));
            });
        }

        void ShowQrResult(string text)
        {
            if (text == null) { Toast("", "QR-код на экране не найден", null, -1, null); return; }
            try { Clipboard.SetText(text); } catch { }
            bool url = text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            string one = text.Replace("\r", " ").Replace("\n", " ");
            if (url) ShowCardEx(null, "", "QR-код · скопировано", one, delegate { }, "Открыть", delegate { try { System.Diagnostics.Process.Start(text); } catch { } }, true);
            else ShowCardEx(null, "", "QR-код · скопировано", one, delegate { }, null, null, true);
            cardUntil = Environment.TickCount + 9000;
        }

        // ------------------------------------------------------ запись экрана
        // Выбор прямоугольника на экране (без разметки) — общий шаг для распознавания текста и записи области.
        void PickRect(Action<System.Drawing.Bitmap, System.Drawing.Rectangle> done)
        {
            if (snipActive) return;
            snipActive = true;
            if (state != 0) SetState(0);
            System.Drawing.Bitmap bmp;
            try { bmp = Grab(); } catch { snipActive = false; return; }
            var win = new SnipWindow(bmp, ToSource(bmp), false, dpi) { RectOnly = true };
            win.Cancelled = delegate { snipActive = false; bmp.Dispose(); };
            win.RectPicked = delegate(System.Drawing.Rectangle r)
            {
                snipActive = false;
                try { if (r.Width >= 8 && r.Height >= 8) done(bmp, r); }
                finally { bmp.Dispose(); }
            };
            win.Show();
            win.Activate();
        }

        // ------------------------------------------------- текст с экрана
        void StartOcr()
        {
            PickRect(delegate(System.Drawing.Bitmap bmp, System.Drawing.Rectangle r)
            {
                byte[] png;
                using (var crop = bmp.Clone(r, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    // Мелкий текст распознаётся заметно лучше в увеличенном виде.
                    int k = crop.Height < 500 && crop.Width * 2 <= 4000 ? 2 : 1;
                    using (var big = new System.Drawing.Bitmap(crop.Width * k, crop.Height * k))
                    {
                        using (var g = System.Drawing.Graphics.FromImage(big))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(crop, 0, 0, big.Width, big.Height);
                        }
                        using (var ms = new System.IO.MemoryStream()) { big.Save(ms, System.Drawing.Imaging.ImageFormat.Png); png = ms.ToArray(); }
                    }
                }
                Toast("", "Распознаю текст…", null, -1, null);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string text = null;
                    try { text = Ocr.Recognize(png); } catch (Exception ex) { Store.LogError("распознавание текста", ex); }
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (string.IsNullOrWhiteSpace(text)) { Toast("", "Текст в выделенной области не найден", null, -1, null); return; }
                        try { Clipboard.SetText(text); } catch { }
                        ShowCardEx(null, "", "Текст с экрана · скопировано", text.Replace("\r", " ").Replace("\n", " "), delegate { }, null, null, true);
                        cardUntil = Environment.TickCount + 7000;
                    }));
                });
            });
        }

        void ToggleRecord(bool area)
        {
            if (recBusy) return;
            recBusy = true;
            if (Recorder.Active)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Recorder.Stop();
                    string file = Recorder.FilePath;
                    long len = file != null && System.IO.File.Exists(file) ? FileLen(file) : 0;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        recBusy = false;
                        RenderRec();
                        if (len > 0) ShowCardEx(null, "", "Запись готова", SizeText(len) + (Recorder.HasAudio ? (Recorder.HasMic ? " · звук и микрофон · " : " · со звуком · ") : " · без звука · ") + System.IO.Path.GetFileName(file), delegate { RevealFile(file); }, null, null, true);
                        else Toast("", "Запись не сохранилась", null, -1, null);
                    }));
                });
                return;
            }
            if (state != 0) SetState(0);
            if (area)
            {
                // Сначала выбираем область; запись начнётся после выбора.
                recBusy = false;
                PickRect(delegate(System.Drawing.Bitmap shot, System.Drawing.Rectangle r) { recBusy = true; BeginRecord(r); });
                return;
            }
            BeginRecord(WF.Screen.PrimaryScreen.Bounds);
        }

        void BeginRecord(System.Drawing.Rectangle b)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string ff = Recorder.FindFfmpeg();
                bool ok = false;
                if (ff != null)
                {
                    string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Kapsula");
                    System.IO.Directory.CreateDirectory(dir);
                    string path = System.IO.Path.Combine(dir, "Запись " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".mp4");
                    // Пробуем кодировщики по очереди: не в каждой сборке ffmpeg есть libx264.
                    // Сначала пробуем со звуком; если не вышло ни с одним кодировщиком — записываем хотя бы без звука.
                    bool wantAudio = Store.Get("recAudio") != "0";
                    foreach (bool withAudio in wantAudio ? new bool[] { true, false } : new bool[] { false })
                    {
                        foreach (string codec in new string[] { "-c:v libx264 -preset veryfast -tune zerolatency -crf 23","-c:v h264_mf -b:v 8M", "-c:v mpeg4 -q:v 5" })
                        {
                            if (!Recorder.Start(ff, path, b.X, b.Y, b.Width, b.Height, codec, withAudio, withAudio && Store.Get("recAudio") == "2")) { if (withAudio) break; continue; }
                            Thread.Sleep(withAudio ? 2200 : 1500);
                            if (Recorder.Active) { ok = true; break; }
                            Recorder.DropAudio();
                        }
                        if (ok) break;
                    }
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    recBusy = false;
                    RenderRec();
                    if (ff == null) Toast("", "Для записи нужен ffmpeg — он не найден", null, -1, null);
                    else if (!ok) Toast("", "Не удалось начать запись", null, -1, null);
                }));
            });
        }

        void RenderRec()
        {
            bool on = Recorder.Active;
            recTile.Label.Text = on ? "Стоп" : "Запись";
            recTile.SetActive(on);
            RenderCompact();
            SetTargets();
        }

        void ShotCard(BitmapSource bs, string path)
        {
            ShowCard(bs, null, "Скриншот готов", "В буфере и в папке · " + bs.PixelWidth + " × " + bs.PixelHeight, delegate { RevealFile(path); });
        }

        void FullShot()
        {
            SetExpanded(false);
            // Остров исключён из захвата экрана, поэтому в кадр он не попадает.
            using (var bmp = Grab()) SaveShot(bmp);
        }

        void StartSnip(bool color)
        {
            if (snipActive) return;
            snipActive = true;
            SetExpanded(false);
            System.Drawing.Bitmap bmp;
            try { bmp = Grab(); } catch { snipActive = false; return; }
            var win = new SnipWindow(bmp, ToSource(bmp), color, dpi);
            win.Cancelled = delegate { snipActive = false; bmp.Dispose(); };
            win.AreaPicked = delegate(BitmapSource shot)
            {
                snipActive = false;
                bmp.Dispose();
                try
                {
                    string path = System.IO.Path.Combine(ShotDir(), "Снимок " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".png");
                    using (var fs = System.IO.File.Create(path))
                    {
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(shot));
                        enc.Save(fs);
                    }
                    Clipboard.SetImage(shot);
                    ShotCard(shot, path);
                }
                catch (Exception ex) { Store.LogError("сохранение снимка", ex); Toast("", "Не удалось сохранить снимок", null, -1, null); }
            };
            win.ColorPicked = delegate(System.Drawing.Color c)
            {
                snipActive = false;
                bmp.Dispose();
                string hex = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                try { Clipboard.SetText(hex); } catch { }
                Toast("", hex + " скопирован", null, -1, new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)));
            };
            win.Show();
            win.Activate();
        }

        // -------------------------------------------------------------- таймеры
        bool FullscreenAppInFront()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == hwnd) return false;
            var sb = new StringBuilder(256);
            Native.GetClassName(fg, sb, 256);
            string c = sb.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "XamlExplorerHostIslandWindow" || c == "Windows.UI.Core.CoreWindow") return false;
            if ((Native.GetWindowLongPtr(fg, Native.GWL_STYLE).ToInt64() & Native.WS_CAPTION) == Native.WS_CAPTION) return false;
            Native.RECT r;
            if (!Native.GetWindowRect(fg, out r)) return false;
            var mi = new Native.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
            IntPtr mon = Native.MonitorFromWindow(fg, 2);
            if (!Native.GetMonitorInfo(mon, ref mi)) return false;
            // Прячемся только от окна на том же мониторе, где стоит остров (а не обязательно на основном).
            if (hwnd != IntPtr.Zero ? mon != Native.MonitorFromWindow(hwnd, 2) : (mi.dwFlags & 1) == 0) return false;
            return r.L <= mi.rcMonitor.L && r.T <= mi.rcMonitor.T && r.R >= mi.rcMonitor.R && r.B >= mi.rcMonitor.B;
        }

        void OnPoll()
        {
            pollN++;
            UpdateHover();

            if (toastOn && unchecked(Environment.TickCount - toastUntil) >= 0) { toastOn = false; RenderCompact(); SetTargets(); }
            if (fsPeeking && unchecked(Environment.TickCount - fsPeekUntil) >= 0) { fsPeeking = false; SetTargets(); }

            if (pollN % 3 == 0)
            {
                double v = Audio.GetVolume();
                if (v >= 0)
                {
                    if (lastVol >= 0 && Math.Abs(v - lastVol) > 0.004)
                    {
                        if (expanded) RenderHomeLive();
                        else Toast(v < 0.005 ? "" : "", null, Math.Round(v * 100) + "%", v, null);
                    }
                    lastVol = v;
                }
                bool caps = (Native.GetKeyState(0x14) & 1) != 0;
                if (!firstPoll && caps != capsLast) Toast("", caps ? "Caps Lock включён" : "Caps Lock выключен", null, -1, caps ? (Brush)accent : Dim);
                capsLast = caps;
                firstPoll = false;
                if (ShowLayout && pollN % 9 == 0)
                {
                    // Смена раскладки клавиатуры в активном окне
                    uint pid;
                    uint tid = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
                    int lang = (int)(Native.GetKeyboardLayout(tid).ToInt64() & 0xFFFF);
                    if (lang != 0)
                    {
                        if (lastLayout != 0 && lang != lastLayout)
                        {
                            string nm;
                            try { nm = new System.Globalization.CultureInfo(lang).TwoLetterISOLanguageName.ToUpperInvariant(); } catch { nm = "?"; }
                            Toast("", "Раскладка: " + nm, null, -1, null);
                        }
                        lastLayout = lang;
                    }
                }
            }
            if (state == 0 && eyes.Visibility == Visibility.Visible) UpdateEyes();
            if ((expanded && page == 0 || state == 1) && pollN % 6 == 0) RenderHomeLive();
            if ((expanded && page == 0 || state == 1) && pollN % 2 == 0) UpdateLyrics();
            if (expanded && page == PWeather && weatherAnimAt != 0) AnimateWeather();
            if (expanded && page == PNote && pollN % 4 == 0) UpdateRing();
        }

        void OnTick()
        {
            tick++;
            bool fs = FullscreenAppInFront();
            if (fs != hiddenFs) { hiddenFs = fs; SetTargets(); }
            if (!fs && tick % 6 == 0 && hwnd != IntPtr.Zero && !snipActive)
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);

            if (tick % 4 == 1) PollSteam();
            if (tick % 2 == 0) SampleCpu();
            if (tick % 2 == 1) PollDownloads();
            if (tick % 2 == 0) CheckReminders();
            if (FollowWindows && tick % 4 == 0)
            {
                bool dark = IsDarkTheme();
                if (dark != lastWinDark) { lastWinDark = dark; ApplyEffectiveTheme(); }
            }
            if (tick % 4 == 0)
            {
                bool mic = MicBusy();
                if (mic != micInUse) { micInUse = mic; if (mic) Toast("", "Микрофон используется", null, -1, new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A))); }
            }

            if (timerEnd != DateTime.MinValue && timerEnd <= DateTime.Now)
            {
                timerEnd = DateTime.MinValue;
                timerRinging = true;
                System.Media.SystemSounds.Exclamation.Play();
                SetTargets();
            }

            string q = Store.Get("cityQuery");
            if (q != "" && unchecked(Environment.TickCount - weatherAt) > 30 * 60 * 1000) RefreshWeather(q);

            double before = CompactWidth();
            RenderCompact();
            if (expanded && page == PSystem) { RenderSystem(); if (tick % 6 == 0) RefreshControl(); }
            if (!expanded && Math.Abs(sw.Target - before) > 0.5) SetTargets();
        }
    }
}
