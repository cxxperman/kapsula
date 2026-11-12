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
        const int PHome = 0, PClip = 1, PShelf = 2, PText = 3, PChat = 4, PNote = 5, PWeather = 6, PSystem = 7, PClaude = 8;

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

        // Персонаж в капсуле один — пиксельный зверёк. Прежние (глаза, кот, робот и другие) убраны.
        public static readonly string[] CharacterIds = { "pet" };
        public static readonly string[] CharacterNames = { "Зверёк" };
        public string Character { get { return "pet"; } }

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
            if (character == "bear")
            {
                // круглые уши за глазами
                for (int i = 0; i < 2; i++)
                {
                    var ear = new Ellipse { Width = 8, Height = 8, Fill = Fg };
                    Canvas.SetLeft(ear, i == 0 ? -1.5 : 18.5); Canvas.SetTop(ear, -3.5);
                    eyes.Children.Insert(0, ear);
                }
            }
            if (character == "bunny")
            {
                // длинные уши торчком
                for (int i = 0; i < 2; i++)
                {
                    var ear = new Rectangle { Width = 4.6, Height = 10.5, RadiusX = 2.3, RadiusY = 2.3, Fill = Fg, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = new RotateTransform(i == 0 ? -9 : 9) };
                    Canvas.SetLeft(ear, i == 0 ? 3.2 : 17.2); Canvas.SetTop(ear, -8.5);
                    eyes.Children.Insert(0, ear);
                }
            }
            if (character == "owl")
            {
                // кисточки по краям и клюв между глазами
                eyes.Children.Insert(0, new Polygon { Fill = Fg, Points = new PointCollection { new Point(0, 3), new Point(-2.5, -4), new Point(5, 0) } });
                eyes.Children.Insert(0, new Polygon { Fill = Fg, Points = new PointCollection { new Point(25, 3), new Point(27.5, -4), new Point(20, 0) } });
                eyes.Children.Add(new Polygon { Fill = Amber, Points = new PointCollection { new Point(10.3, 8.2), new Point(14.7, 8.2), new Point(12.5, 12.6) } });
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
        public bool TestChat, TestEyes, TestChatLong;
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
        const double ChatGrowMax = 260;   // на сколько чат может вытянуть панель вниз под длинный разговор
        const double BaseW = EW + 120, BaseH = EH + 90 + ChatGrowMax;
        public static readonly Color LightIsland = Color.FromRgb(0xF4, 0xF4, 0xF6);
        public Color IslandColor = Colors.Black, DarkColor = Colors.Black;
        public bool FollowWindows, PillTitle, HoverMini = true, LiquidOn = true, ShowSeconds, ShowInCapture;
        public int PillMode, PlayerStyle;
        string currentLyric = "";

        public void SetPillMode(int mode)
        {
            PillMode = mode; PillTitle = mode != 0;
            Store.Set("pillMode", mode.ToString());
            RenderCompact(); SetTargets();
        }

        public void SetPlayerStyle(int style)
        {
            PlayerStyle = style;
            Store.Set("playerStyle", style.ToString());
            ApplyPlayerStyle();
        }
        public double Scale = 1;
        public string TestColor;
        public bool TestSettings, TestSettingsTop;
        public string TestSettingsTab;   // ключ --settingstab «Раздел»
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
            // Вид капсулы при музыке: 0 — часы и погода, 1 — название, 2 — строка песни, 3 — полоса трека
            int pm;
            PillMode = int.TryParse(Store.Get("pillMode"), out pm) && pm >= 0 && pm <= 3 ? pm : (Store.Get("pillTitle") == "1" ? 1 : 0);
            PillTitle = PillMode != 0;
            int ps;
            PlayerStyle = int.TryParse(Store.Get("playerStyle"), out ps) && ps >= 0 && ps <= 2 ? ps : 0;
            HoverMini = Store.Get("hoverMini") != "0";
            // Разовый перевод настроек на новый вид программы (выполняется один раз, дальше выбор человека не трогаем):
            // зверёк включается у всех, у кого персонаж был выключен, а постоянное автоскрытие выключается.
            // Скрытие в играх на весь экран и список «Прятаться в программах» остаются как были.
            if (Store.Get("petMigrated") != "1")
            {
                if (Store.Get("eyes") == "0") Store.Set("eyes", "1");
                if (Store.Get("autoHide") == "1") Store.Set("autoHide", "0");
                Store.Set("petMigrated", "1");
            }
            LiquidOn = Store.Get("liquid") != "0";
            ShowSeconds = Store.Get("seconds") == "1";
            AutoHide = Store.Get("autoHide") == "1";
            GlassOn = Store.Get("glass") == "1";
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
            // На «матовом стекле» под текстом не ровный чёрный, а размытый фон: серые подписи делаем заметно светлее
            Dim.Color = Mix(text, island, GlassOn ? 0.22 : 0.42);
            Faint.Color = Mix(text, island, GlassOn ? 0.38 : 0.64);
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

        // Прозрачность фона острова (сам текст и значки остаются плотными): от 0.5 до 1
        public double IslandOpacity = 1;
        public void ApplyOpacity(double v, bool save)
        {
            IslandOpacity = Math.Max(0.5, Math.Min(1, v));
            if (shape != null) shape.Opacity = IslandOpacity;
            if (save) Store.Set("opacity", IslandOpacity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        }

        void ApplyCapture()
        {
            // glassHide: пока открыта панель с «матовым стеклом», остров в снимки экрана не попадает —
            // иначе он попадал бы и в собственный снимок фона, и размытие не могло бы быть живым
            if (hwnd != IntPtr.Zero) Native.SetWindowDisplayAffinity(hwnd, Capturable || (ShowInCapture && !glassHide) ? 0u : 0x11u);
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
            if (TestSettings) { settingsWin.Topmost = true; settingsWin.WindowStartupLocation = WindowStartupLocation.Manual; settingsWin.Top = 60; settingsWin.Left = (SystemParameters.PrimaryScreenWidth - 840) / 2; if (TestSettingsTab != null) settingsWin.ShowSection(TestSettingsTab); }
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
            ClaudeBridge.Asked += delegate(ClaudeBridge.Ask a) { Dispatcher.BeginInvoke(new Action(delegate { ClaudeAsk(a); })); };
            ClaudeBridge.Questioned += delegate(ClaudeBridge.Question q) { Dispatcher.BeginInvoke(new Action(delegate { ClaudeQuestion(q); })); };
            ClaudeBridge.Done += delegate(ClaudeBridge.Finished f) { Dispatcher.BeginInvoke(new Action(delegate { ClaudeDone(f); })); };
            ClaudeBridge.Working += delegate(string tool, string detail, string project) { Dispatcher.BeginInvoke(new Action(delegate { ClaudeWorking(tool, detail, project); })); };
            ClaudeBridge.Waiting += delegate(string project) { Dispatcher.BeginInvoke(new Action(delegate { ClaudeWaiting(project); })); };
            ClaudeBridge.Start();
            Earbuds.Start();
            SteamBridge.Received += delegate(Dictionary<string, object> d)
            {
                object type;
                d.TryGetValue("type", out type);
                if ((type as string) == "hello")
                {
                    Dispatcher.BeginInvoke(new Action(delegate { if (StartingUp()) return; Toast("", "Steam подключён к Kapsula", null, -1, accent); }));
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
            double op;
            if (double.TryParse(Store.Get("opacity"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out op) && op >= 0.5 && op <= 1) IslandOpacity = op;
            // слой «матового стекла» лежит под фоном острова и показывает размытый снимок того, что за панелью
            glassBrush = new ImageBrush { Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, BaseW, BaseH) };
            RenderOptions.SetBitmapScalingMode(glassBrush, BitmapScalingMode.HighQuality);
            glassLayer = new Path { Fill = glassBrush, SnapsToDevicePixels = false, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(glassLayer, BitmapScalingMode.HighQuality);
            root.Children.Add(glassLayer);
            shape = new Path { Fill = Ink, SnapsToDevicePixels = false, Opacity = IslandOpacity };
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
            // Содержимое панели и мини-плеера лежит во вложенной сетке: её рисует у себя превью в настройках
            // (сам слой для этого не годится — у свёрнутого острова он прозрачный).
            previewHome = Nest(expandedHost);
            previewMini = Nest(miniHost);
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
            // Кольцо заряда вокруг значка — для карточки наушников: заполняется плавно, когда она появляется
            cardRingTrack = new Path { Stroke = Chip, StrokeThickness = 3.5, Data = ArcGeometry(26, 26, 22, 0, 359.9), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            cardRing = new Path { StrokeThickness = 3.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            thumb.Children.Add(cardRingTrack); thumb.Children.Add(cardRing);
            // Зверёк вместо значка — на карточках Claude Code: он показывает, чего от вас ждут
            cardPet = new PetView(2.4, Ink);
            cardPet.Root.HorizontalAlignment = HorizontalAlignment.Center;
            cardPet.Root.Margin = new Thickness(7, 0, 0, 0);
            cardPet.Root.Visibility = Visibility.Collapsed;
            thumb.Children.Add(cardPet.Root);
            cardGlyph.RenderTransformOrigin = new Point(0.5, 0.5);
            cardGlyph.RenderTransform = cardGlyphScale;
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
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(cardNo, 6); g.Children.Add(cardNo);
            // Ещё две кнопки с подписью — для карточек с несколькими вариантами (например, вопрос от Claude Code).
            for (int i = 0; i < 2; i++)
            {
                int idx = i;
                var lbl = new TextBlock { Foreground = Fg, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 120, TextTrimming = TextTrimming.CharacterEllipsis };
                var ab = new Border { Height = 36, CornerRadius = new CornerRadius(18), Background = Chip, Padding = new Thickness(12, 0, 12, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Child = lbl };
                ab.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    var a = cardAltAction[idx];
                    hoverBlock = true;
                    SetState(0);
                    if (a != null) a();
                };
                Grid.SetColumn(ab, 4 + i); g.Children.Add(ab);
                cardAlt[i] = ab; cardAltLabel[i] = lbl;
            }
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
            cardGrid = g;
            BuildBudsPanel();
        }

        void ShowCard(ImageSource img, string glyph, string title, string sub, Action act) { ShowCardEx(img, glyph, title, sub, act, null, null, false); }

        Border cardNo;
        Action cardNoAction;
        PetView cardPet;

        // Показать на текущей карточке зверька вместо значка
        void CardPet(string mood)
        {
            if (cardPet == null || state != 3) return;
            cardGlyph.Text = "";
            cardThumb.Background = Brushes.Transparent;
            cardPet.Root.Visibility = Visibility.Visible;
            cardPet.Mood(mood);
        }

        Path cardRingTrack, cardRing;
        ScaleTransform cardGlyphScale = new ScaleTransform(1, 1);
        DispatcherTimer cardRingTimer;

        // ---- крупная карточка наушников: нарисованные наушники слева, модель, проценты и полоса заряда справа
        const double BudsH = 92, BudsW = 268, BudsBarW = 118;
        bool cardTall;
        Grid cardGrid, budsPanel;
        Canvas budsOver, budsIn;                 // рисунки: накладные наушники и вкладыши
        ScaleTransform budsScale = new ScaleTransform(1, 1);
        TranslateTransform budsFloat = new TranslateTransform();
        TextBlock budsName, budsPct, budsNote;
        Image budsSpin, budsSpin2;
        BitmapSource[] budsFrames;
        bool budsFramesTried;

        // Кадры оборота лежат внутри программы одним листом 10×6; режем его на отдельные картинки при первом показе
        BitmapSource[] BudsFrames()
        {
            if (budsFramesTried) return budsFrames;
            budsFramesTried = true;
            // Свои картинки наушников можно положить в папку «headphones» рядом с настройками (f_00.png, f_01.png…):
            // Kapsula покажет их в карточке, плавно переходя от одной к другой. В саму программу они не входят.
            try
            {
                string dir = System.IO.Path.Combine(Store.Dir, "headphones");
                if (System.IO.Directory.Exists(dir))
                {
                    string[] files = System.IO.Directory.GetFiles(dir, "*.png");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    var own = new List<BitmapSource>();
                    foreach (string f in files)
                    {
                        if (own.Count >= 120 || new System.IO.FileInfo(f).Length > 4 * 1024 * 1024) continue;
                        var bi = new BitmapImage();
                        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(f); bi.DecodePixelWidth = 260; bi.EndInit(); bi.Freeze();
                        own.Add(bi);
                    }
                    if (own.Count > 0) { budsFrames = own.ToArray(); return budsFrames; }
                }
            }
            catch (Exception ex) { Store.LogError("свои картинки наушников", ex); }
            try
            {
                using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Kapsula.headphones.png"))
                {
                    if (s == null) return null;
                    var sheet = new BitmapImage();
                    sheet.BeginInit(); sheet.CacheOption = BitmapCacheOption.OnLoad; sheet.StreamSource = s; sheet.EndInit(); sheet.Freeze();
                    int cols = 10, rows = 6, fw = sheet.PixelWidth / cols, fh = sheet.PixelHeight / rows;
                    var list = new BitmapSource[cols * rows];
                    for (int i = 0; i < list.Length; i++)
                    {
                        var c = new CroppedBitmap(sheet, new Int32Rect((i % cols) * fw, (i / cols) * fh, fw, fh));
                        c.Freeze();
                        list[i] = c;
                    }
                    budsFrames = list;
                }
            }
            catch (Exception ex) { Store.LogError("кадры наушников", ex); }
            return budsFrames;
        }
        Border budsBarFill, budsBarTrack;

        static LinearGradientBrush Metal(byte a, byte b, double angle)
        {
            return new LinearGradientBrush(Color.FromRgb(a, a, (byte)Math.Min(255, a + 6)), Color.FromRgb(b, b, (byte)Math.Min(255, b + 8)), angle);
        }

        static void Put(Canvas c, UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); c.Children.Add(e); }

        // Накладные наушники спереди: дужка, стойки, две чашки с амбушюрами и бликом. Рисунок свой, из простых фигур.
        static Canvas DrawOverEar()
        {
            var c = new Canvas { Width = 100, Height = 104 };
            // дужка: тёмная основа и светлая полоса сверху, как сетчатое оголовье
            c.Children.Add(new Path { Data = Geometry.Parse("M 20,56 C 20,4 80,4 80,56"), Stroke = Metal(0x8E, 0x5E, 90), StrokeThickness = 9, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            c.Children.Add(new Path { Data = Geometry.Parse("M 27,34 C 34,13 66,13 73,34"), Stroke = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            // стойки
            Put(c, new Rectangle { Width = 4.5, Height = 16, RadiusX = 2.2, RadiusY = 2.2, Fill = Metal(0xC8, 0x8A, 0) }, 17.8, 46);
            Put(c, new Rectangle { Width = 4.5, Height = 16, RadiusX = 2.2, RadiusY = 2.2, Fill = Metal(0xC8, 0x8A, 0) }, 77.7, 46);
            for (int i = 0; i < 2; i++)
            {
                double x = i == 0 ? 6 : 68;
                // амбушюр выглядывает с внутренней стороны
                Put(c, new Rectangle { Width = 12, Height = 40, RadiusX = 6, RadiusY = 6, Fill = Metal(0x4A, 0x2C, 90) }, i == 0 ? x + 20 : x - 6, 58);
                // чашка
                Put(c, new Rectangle { Width = 26, Height = 46, RadiusX = 12, RadiusY = 12, Fill = Metal(0xEE, 0xA6, i == 0 ? 25 : 155) }, x, 55);
                // блик по внешнему краю
                Put(c, new Rectangle { Width = 3.2, Height = 26, RadiusX = 1.6, RadiusY = 1.6, Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)) }, i == 0 ? x + 4.5 : x + 18.3, 63);
            }
            return c;
        }

        // Вкладыши: два наушника с ножками
        static Canvas DrawInEar()
        {
            var c = new Canvas { Width = 100, Height = 104 };
            for (int i = 0; i < 2; i++)
            {
                double x = i == 0 ? 18 : 56;
                Put(c, new Rectangle { Width = 8, Height = 44, RadiusX = 4, RadiusY = 4, Fill = Metal(0xF2, 0xB4, 90) }, x + (i == 0 ? 15 : 3), 44);
                Put(c, new Ellipse { Width = 26, Height = 28, Fill = Metal(0xFA, 0xC0, i == 0 ? 35 : 145) }, x, 26);
                Put(c, new Ellipse { Width = 9, Height = 12, Fill = Metal(0x50, 0x30, 90) }, x + (i == 0 ? 2 : 15), 33);
                Put(c, new Ellipse { Width = 6, Height = 4, Fill = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)) }, x + 10, 30);
            }
            return c;
        }

        void BuildBudsPanel()
        {
            budsPanel = new Grid { Margin = new Thickness(22, 6, 22, 12), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            budsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            budsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var stage = new Grid { Width = 70, Height = 72, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.6) };
            var tg = new TransformGroup(); tg.Children.Add(budsScale); tg.Children.Add(budsFloat);
            stage.RenderTransform = tg;
            budsOver = DrawOverEar(); budsIn = DrawInEar();
            stage.Children.Add(budsOver); stage.Children.Add(budsIn);
            // Объёмные наушники: 60 кадров оборота собственной модели, сменяются по кругу. Нет кадров — остаётся рисунок.
            budsSpin = new Image { Width = 74, Height = 74, Margin = new Thickness(-2, -1, -2, -1), Visibility = Visibility.Collapsed };
            RenderOptions.SetBitmapScalingMode(budsSpin, BitmapScalingMode.HighQuality);
            stage.Children.Add(budsSpin);
            // второй слой — следующий ракурс: он проявляется поверх текущего, и поворот выглядит плавным даже при четырёх картинках
            budsSpin2 = new Image { Width = 74, Height = 74, Margin = new Thickness(-2, -1, -2, -1), Visibility = Visibility.Collapsed, Opacity = 0 };
            RenderOptions.SetBitmapScalingMode(budsSpin2, BitmapScalingMode.HighQuality);
            stage.Children.Add(budsSpin2);
            budsPanel.Children.Add(stage);
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            budsName = new TextBlock { Foreground = Fg, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            budsNote = new TextBlock { Foreground = Dim, FontSize = 11, Margin = new Thickness(0, 1, 0, 0), Visibility = Visibility.Collapsed, TextTrimming = TextTrimming.CharacterEllipsis };
            budsPct = new TextBlock { Foreground = Fg, FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 1, 0, 5) };
            budsBarFill = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            budsBarTrack = new Border { Height = 5, Width = BudsBarW, CornerRadius = new CornerRadius(2.5), Background = Chip, HorizontalAlignment = HorizontalAlignment.Left, Child = budsBarFill };
            right.Children.Add(budsName); right.Children.Add(budsNote); right.Children.Add(budsPct); right.Children.Add(budsBarTrack);
            Grid.SetColumn(right, 1);
            budsPanel.Children.Add(right);
            cardHost.Children.Add(budsPanel);
        }

        // Наушники появились: рисунок вырастает и слегка покачивается, проценты отсчитываются, полоса заряда заполняется.
        void ShowBudsCard(EarbudsState s)
        {
            ShowCardEx(null, BudsGlyph, s.Model, s.Text, delegate { }, null, null, false);
            if (state != 3) return;
            cardTall = true;
            cardGrid.Visibility = Visibility.Collapsed;
            budsPanel.Visibility = Visibility.Visible;
            cardHost.Height = BudsH;
            cardExtra = BudsW - CardW; cardHost.Width = BudsW;
            bool over = s.Single >= 0 || s.Model.Contains("Max") || s.Model.StartsWith("Beats S");
            BitmapSource[] spin = over ? BudsFrames() : null;
            budsSpin.Visibility = spin != null ? Visibility.Visible : Visibility.Collapsed;
            budsSpin2.Visibility = spin != null && spin.Length < 24 ? Visibility.Visible : Visibility.Collapsed;
            budsSpin.Opacity = 1; budsSpin2.Opacity = 0;
            budsOver.Visibility = over && spin == null ? Visibility.Visible : Visibility.Collapsed;
            budsIn.Visibility = over ? Visibility.Collapsed : Visibility.Visible;
            if (spin != null) budsSpin.Source = spin[0];
            budsName.Text = s.Model;
            int level = s.Lowest;
            bool known = level >= 0;
            budsNote.Text = !known ? "Подключены" : s.Single >= 0 ? (s.Charging ? "Заряжаются" : "Подключены") : s.Text;
            budsPct.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
            budsBarTrack.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
            budsBarFill.Background = level > 50 ? ClaudeGreen : level > 20 ? (Brush)Amber : RecRed;
            budsBarFill.Width = 0; budsPct.Text = "0%";
            SetTargets();
            cardUntil = Environment.TickCount + 7000;

            var pop = new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut } };
            budsScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            budsScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            budsPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));

            int started = Environment.TickCount;
            const double dur = 1300, delay = 380;
            if (cardRingTimer != null) cardRingTimer.Stop();
            cardRingTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            DispatcherTimer mine = cardRingTimer;
            mine.Tick += delegate
            {
                if (state != 3 || !cardTall) { mine.Stop(); return; }
                int el = unchecked(Environment.TickCount - started);
                if (spin != null && spin.Length == 1) { }   // одна картинка — стоит неподвижно
                else if (spin != null && spin.Length < 24)
                {
                    // Мало ракурсов: каждый держится около секунды, затем плавно перетекает в следующий
                    double per = 1500;
                    double pos = el / per;
                    int a = (int)pos % spin.Length, b = (a + 1) % spin.Length;
                    double f = pos - Math.Floor(pos);
                    double mix = f < 0.45 ? 0 : (f - 0.45) / 0.55;
                    mix = mix * mix * (3 - 2 * mix);
                    if (!ReferenceEquals(budsSpin.Source, spin[a])) budsSpin.Source = spin[a];
                    if (!ReferenceEquals(budsSpin2.Source, spin[b])) budsSpin2.Source = spin[b];
                    budsSpin.Opacity = 1 - mix; budsSpin2.Opacity = mix;
                    budsFloat.Y = Math.Sin(el / 620.0) * 1.2;
                }
                else if (spin != null)
                {
                    // Оборот: сначала быстрый разворот, потом ровное медленное вращение (круг примерно за пять секунд)
                    double turns = el / 5200.0 + 0.55 * (1 - Math.Exp(-el / 420.0));
                    int frame = (int)(turns * spin.Length) % spin.Length;
                    if (!ReferenceEquals(budsSpin.Source, spin[frame])) budsSpin.Source = spin[frame];
                }
                else budsFloat.Y = Math.Sin(el / 520.0) * 1.8;   // рисунок слегка «дышит», пока карточка на экране
                if (!known) return;
                double t = Math.Max(0, Math.Min(1, (el - delay) / dur));
                double k = 1 - Math.Pow(1 - t, 3);          // быстро в начале, мягко в конце
                budsPct.Text = Math.Round(level * k) + "%";
                budsBarFill.Width = BudsBarW * level / 100.0 * k;
            };
            mine.Start();
        }

        void ShowBudsCardOld(EarbudsState s)
        {
            ShowCardEx(null, BudsGlyph, s.Model, s.Text, delegate { }, null, null, false);
            if (state != 3) return;
            HideCardButton();
            int target = s.Lowest;
            cardUntil = Environment.TickCount + 6500;
            if (target < 0) return;
            Brush col = target > 50 ? ClaudeGreen : target > 20 ? (Brush)Amber : RecRed;
            cardThumb.Background = Brushes.Transparent;
            cardRingTrack.Visibility = Visibility.Visible;
            cardRing.Visibility = Visibility.Visible;
            cardRing.Stroke = col;
            cardRing.Data = null;
            bool single = s.Single >= 0;
            string tail = s.Charging ? " · заряжаются" : "";
            // значок появляется с лёгким «пружинящим» увеличением
            var pop = new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = new BackEase { Amplitude = 0.55, EasingMode = EasingMode.EaseOut } };
            cardGlyphScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            cardGlyphScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            int started = Environment.TickCount;
            const double dur = 1250, delay = 220;
            if (cardRingTimer != null) cardRingTimer.Stop();
            cardRingTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            DispatcherTimer mine = cardRingTimer;
            mine.Tick += delegate
            {
                double t = (unchecked(Environment.TickCount - started) - delay) / dur;
                if (state != 3 || cardRing.Visibility != Visibility.Visible) { mine.Stop(); return; }
                if (t < 0) return;
                if (t > 1) t = 1;
                double k = 1 - Math.Pow(1 - t, 3);   // быстро в начале, мягко в конце
                double shown = target * k;
                cardRing.Data = shown < 0.5 ? null : ArcGeometry(26, 26, 22, 0, Math.Min(359.9, 3.6 * shown));
                if (single) cardSub.Text = Math.Round(shown) + "%" + tail;
                if (t >= 1) mine.Stop();
            };
            if (single) cardSub.Text = "0%" + tail;
            mine.Start();
        }

        Border[] cardAlt = new Border[2];
        TextBlock[] cardAltLabel = new TextBlock[2];
        Action[] cardAltAction = new Action[2];

        // Карточка с несколькими вариантами: до трёх кнопок с подписью и крестик.
        void ShowCardOptions(string glyph, string title, string sub, string[] labels, Action[] acts, Action no, int holdMs)
        {
            ShowCardChoice(null, glyph, title, sub, labels[0], acts[0], no);
            if (state != 3) return;
            for (int i = 0; i < 2; i++)
            {
                bool on = i + 1 < labels.Length;
                cardAlt[i].Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                cardAltAction[i] = on ? acts[i + 1] : null;
                if (on) cardAltLabel[i].Text = labels[i + 1];
            }
            FitCard();
            cardUntil = Environment.TickCount + holdMs;
        }
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
            double buttons = NaturalWidth(cardBtn) + NaturalWidth(cardNo) + NaturalWidth(cardAlt[0]) + NaturalWidth(cardAlt[1]);   // вместе со своими отступами
            if (buttons > 260) text = Math.Min(text, 200);   // много кнопок — тексту меньше места, иначе карточка не поместится
            double total = 18 + cardThumbBox.Width + 14 + text + buttons + 22;
            total = Math.Max(220, Math.Min(BaseW - 24, total));
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
            for (int i = 0; i < 2; i++) if (cardAlt[i] != null) { cardAlt[i].Visibility = Visibility.Collapsed; cardAltAction[i] = null; }
            if (cardPet != null) cardPet.Root.Visibility = Visibility.Collapsed;
            if (budsPanel != null)
            {
                // обычная карточка: крупная панель наушников спрятана, высота прежняя
                cardTall = false;
                budsPanel.Visibility = Visibility.Collapsed;
                cardGrid.Visibility = Visibility.Visible;
                cardHost.Height = CardH;
            }
            if (cardRing != null)
            {
                // у обычных карточек кольца заряда нет
                if (cardRingTimer != null) cardRingTimer.Stop();
                cardRing.Visibility = Visibility.Collapsed; cardRingTrack.Visibility = Visibility.Collapsed;
                cardGlyphScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); cardGlyphScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                cardGlyphScale.ScaleX = 1; cardGlyphScale.ScaleY = 1;
            }
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
            // Зверёк справа в строке: ждёт, пока вы печатаете, радуется, когда нашлось, и огорчается, когда нет
            cmdPet = new PetView(1.6, Ink);
            cmdPet.Root.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(cmdPet.Root, 1); top.Children.Add(cmdPet.Root);
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

        // Та же строка ввода служит и для ответа Claude Code: по ключу «claude:…» находим, куда отправить текст.
        Dictionary<string, Action<string>> replySends = new Dictionary<string, Action<string>>();
        Dictionary<string, Action> replyCancels = new Dictionary<string, Action>();

        void StartReplyTo(string key, string name, Action<string> send, Action cancel)
        {
            replySends[key] = send; replyCancels[key] = cancel;
            StartReply(key, name);
        }

        // Строку ввода закрыли. Если ответ так и не отправили — сообщаем об отмене (чуть позже: отправка идёт первой).
        void ReplyClosed(string key)
        {
            if (!replyCancels.ContainsKey(key)) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                Action c;
                if (!replyCancels.TryGetValue(key, out c)) return;
                replyCancels.Remove(key); replySends.Remove(key);
                if (c != null) c();
            }), DispatcherPriority.Background);
        }

        void SendSteamReply(string steamid, string text)
        {
            Action<string> custom;
            if (replySends.TryGetValue(steamid, out custom))
            {
                replySends.Remove(steamid); replyCancels.Remove(steamid);
                custom(text);
                return;
            }
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
            if (choice != null)
            {
                // Режим выбора: варианты ответа на вопрос Claude Code
                if (cmdSel < 0) cmdSel = 0;
                FillChoice(q);
                if (cmdSel >= cmdItems.Count) cmdSel = cmdItems.Count - 1;
                RenderCmd();
                if (state == 4) SetTargets();
                return;
            }
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
            if (q.Length >= 4)
            {
                string pq = q.ToLowerInvariant();
                if (pq.StartsWith("календ") || pq.StartsWith("событи") || pq.StartsWith("calend")) { FillCalendar(); RenderCmd(); if (state == 4) SetTargets(); return; }
                if (pq.StartsWith("наушн") || pq.StartsWith("airpods") || pq.StartsWith("аирпод") || pq.StartsWith("эирпод")) { FillEarbuds(); RenderCmd(); if (state == 4) SetTargets(); return; }
                if (pq.StartsWith("не бесп") || pq.StartsWith("тихо") || pq.StartsWith("тишин") || pq == "dnd") { FillQuiet(); RenderCmd(); if (state == 4) SetTargets(); return; }
                if (pq.StartsWith("уведомл") || pq.StartsWith("истори")) { FillHistory(); RenderCmd(); if (state == 4) SetTargets(); return; }
                if (pq.StartsWith("ошибк") || pq.StartsWith("сообщить") || pq.StartsWith("баг")) AddCmd("Сообщить об ошибке", "сведения скопируются, откроется страница на GitHub", "", null, delegate { ReportBug(); });
                // Закреплённые в буфере фразы находятся по любому слову из них и вставляются одним нажатием
                foreach (ClipItem ci in clips)
                {
                    if (!ci.Pinned || ci.Image != null || string.IsNullOrEmpty(ci.Text) || ci.Text.ToLowerInvariant().IndexOf(pq, StringComparison.Ordinal) < 0) continue;
                    ClipItem pin = ci;
                    string one = pin.Text.Replace("\r", " ").Replace("\n", " ").Trim();
                    AddCmd("Вставить: " + (one.Length > 54 ? one.Substring(0, 53) + "…" : one), "закреплено в буфере", "", null, delegate { PasteText(pin.Text); });
                    if (cmdItems.Count >= 3) break;
                }
                if (pq.StartsWith("помод") || pq.StartsWith("pomod"))
                {
                    if (pomoPhase != 0) AddCmd("Остановить помодоро", "кругов сделано: " + pomoRounds, "", null, delegate { PomodoroStop(); });
                    else AddCmd("Помодоро: 25 минут работы, 5 отдыха", "отсчёт в капсуле", "", null, delegate { PomodoroStart(); });
                }
            }
            if (q.Length > 0 && ClaudeBridge.Enabled)
            {
                // «стоп claude» — остановить Claude Code, не переключаясь в его окно
                string ql = q.ToLowerInvariant();
                bool named = ql.Contains("claude") || ql.Contains("клод");
                if (named && (ql.Contains("стоп") || ql.Contains("stop") || ql.Contains("останов")))
                    AddCmd("Остановить Claude Code", ClaudeBridge.Busy ? "все работающие сессии, перед следующим действием" : "Сейчас он, похоже, ничего не делает", ClaudeGlyph, null, delegate { ClaudeStop(null, null); });
                else if (ql == "claude" || ql == "клод" || ql == "claude code") { FillClaudeSessions(); RenderCmd(); if (state == 4) SetTargets(); return; }
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
                // Надиктованное или набранное после слова «заметка» можно одним нажатием дописать в заметку
                string noteText = dictating ? q : NoteCommandText(q);
                if (noteText.Length > 0) { string nt = noteText; AddCmd("Сохранить в заметку", dictating ? "допишется в конец" : nt.Length > 40 ? nt.Substring(0, 40) + "…" : nt, "", null, delegate { AppendNote(nt); }); }
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
                    if (HasKey(ql, "чат", "claude", "клод", "ии ")) AddCmd("Открыть чат с нейросетью", "Ctrl+Alt+A", "", null, delegate { OpenChat(); });
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
            if (cmdItems[i].Stay)
            {
                // Отметка в списке: строка остаётся открытой, список перерисовывается
                if (run != null) run();
                int keep = cmdSel;
                UpdateCmd();
                cmdSel = Math.Min(keep, cmdItems.Count - 1);
                RenderCmd();
                return;
            }
            SetState(0);
            if (run != null) run();
        }

        PetView cmdPet;

        void RenderCmd()
        {
            if (cmdPet != null)
            {
                bool typed = cmdBox != null && cmdBox.Text.Trim().Length > 0;
                cmdPet.Mood(choice != null ? "ask" : !typed ? "idle" : cmdItems.Count > 0 ? "happy" : "think");
            }
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
            if (list == null && currentLyric != "") { currentLyric = ""; if (state == 0 && PillMode == 2) RenderCompact(); }
            miniLyrics.SetLines(list);
            homeLyrics.SetLines(list);
            miniLyrics.Root.Visibility = list != null ? Visibility.Visible : Visibility.Collapsed;
            homeLyrics.Root.Visibility = list != null && homeLyricsShown ? Visibility.Visible : Visibility.Collapsed;
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
            string line = idx >= 0 && idx < lyrics.Count ? lyrics[idx].Text : "";
            if (line != currentLyric)
            {
                currentLyric = line;
                if (state == 0 && PillMode == 2 && !toastOn) RenderCompact();   // капсула «С текстом» показывает новую строку
            }
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
            if (playerTint != null) playerTint.Color = Color.FromArgb(0xB0, c.R, c.G, c.B);
        }

        // ---- вид плеера на «Главной»: 0 — компактный, 1 — крупная обложка по центру, 2 — в цвет обложки
        GradientStop playerTint, playerTintEnd;
        LinearGradientBrush playerTintBrush;
        Grid homeTop;
        StackPanel homeNames;

        void ApplyPlayerStyle()
        {
            if (homePlayerCard == null || homeTop == null) return;
            bool centered = PlayerStyle == 1, tinted = PlayerStyle == 2;
            if (playerTintBrush == null)
            {
                playerTint = new GradientStop(Color.FromArgb(0xB0, accent.Color.R, accent.Color.G, accent.Color.B), 0);
                playerTintEnd = new GradientStop(Card.Color, 1);
                playerTintBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1.1) };
                playerTintBrush.GradientStops.Add(playerTint); playerTintBrush.GradientStops.Add(playerTintEnd);
            }
            playerTintEnd.Color = Card.Color;
            homePlayerCard.Background = tinted ? (Brush)playerTintBrush : Card;
            coverGlow.Opacity = tinted ? 0 : 1;   // в залитом плеере отдельное свечение лишнее
            // На цветном фоне серые подписи теряются — делаем их светлее
            Brush soft = tinted ? (Brush)new SolidColorBrush(Color.FromArgb(0xD0, Fg.Color.R, Fg.Color.G, Fg.Color.B)) : Dim;
            mArtist.Foreground = soft; mPos.Foreground = soft; mDur.Foreground = soft;

            // Крупная обложка: она по центру, название и исполнитель под ней
            playerBody.RowDefinitions[0].Height = new GridLength(centered ? 110 : 84);
            mCover.Width = mCover.Height = centered ? 66 : 84;
            mCover.HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            mCover.VerticalAlignment = centered ? VerticalAlignment.Top : VerticalAlignment.Center;
            Grid.SetColumnSpan(mCover, centered ? 3 : 1);
            Grid.SetColumn(homeNames, centered ? 0 : 2);
            Grid.SetColumnSpan(homeNames, centered ? 3 : 1);
            homeNames.VerticalAlignment = centered ? VerticalAlignment.Bottom : VerticalAlignment.Center;
            homeNames.HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            mTitle.TextAlignment = mArtist.TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left;
            mTitle.FontSize = centered ? 14.5 : 16;
            mArtist.Margin = new Thickness(0, centered ? 1 : 3, 0, 0);
            homeLyricsShown = !centered;
            homeLyrics.Root.Visibility = homeLyricsShown && lyrics != null ? Visibility.Visible : Visibility.Collapsed;
            coverGlow.HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            coverGlow.Margin = centered ? new Thickness(0, -52, 0, 0) : new Thickness(-43, -43, 0, 0);
        }
        bool homeLyricsShown = true;

        void BuildCompact()
        {
            var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Ear + 12, 0, 0, 0) };
            micDot = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, ToolTip = "Микрофон используется" };
            cCover = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(6), Background = Chip, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            cGlyph = new TextBlock { FontFamily = Icons, FontSize = 14, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            left.Children.Add(micDot);
            claudeDot = BuildClaudePet();
            left.Children.Add(claudeDot);
            unreadText = new TextBlock { Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            unreadBadge = new Border { MinWidth = 16, Height = 16, CornerRadius = new CornerRadius(8), Padding = new Thickness(4, 0, 4, 1), Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0), Visibility = Visibility.Collapsed, Child = unreadText };
            left.Children.Add(unreadBadge);
            cDlIcon =new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Background = Chip, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
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
            gapRow = new RowDefinition { Height = new GridLength(12) };
            g.RowDefinitions.Add(gapRow);
            pageRow = new RowDefinition { Height = new GridLength(PageH) };
            g.RowDefinitions.Add(pageRow);
            // Строка разделов открытой группы — под значками
            subRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 0, 0) };
            Grid.SetRow(subRow, 1);
            g.Children.Add(subRow);

            var tabRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            string[] names = { "Главная", "Буфер", "Полка", "Текст", "Чат", "Заметка", "Погода", "Система", "Claude Code" };
            // Вкладки — значки; подпись видна только у активной.
            string[] glyphs = { "", "", "", "", "", "", "", "", ClaudeGlyph };
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
                b.MouseLeftButtonDown += delegate { ShowPage(TabTarget(idx)); };
                tabs[i] = b; tabLabels[i] = tb; tabGlyphs[i] = gl;
            }
            // В строке вкладки идут по группам; у закрытой группы виден один значок
            foreach (int i in TabOrder) tabRow.Children.Add(tabs[i]);
            tabs[PClip].ToolTip = "Буфер, полка и текст";
            tabs[PChat].ToolTip = "Чат и Claude Code";
            tabs[PWeather].ToolTip = "Погода и система";
            // Подсветка активной вкладки — отдельная плашка под значками: она скользит между ними, а не перескакивает.
            tabGlow = new Border { Height = 30, CornerRadius = new CornerRadius(15), Background = TabOn, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, RenderTransform = tabGlowX, IsHitTestVisible = false };
            tabHost = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent };
            tabRow.HorizontalAlignment = HorizontalAlignment.Left;
            tabHost.Children.Add(tabGlow); tabHost.Children.Add(tabRow);
            // Колёсико мыши над вкладками листает их
            tabHost.MouseWheel += delegate(object s, MouseWheelEventArgs e) { e.Handled = true; StepPage(e.Delta < 0 ? 1 : -1); };
            g.Children.Add(tabHost);
            // Заряд подключённых Bluetooth-наушников — справа в строке вкладок
            // Без плашки, в одном стиле со значками вкладок и шестерёнкой: картинка наушников и процент.
            budsChipText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 1) };
            var budsRow = new StackPanel { Orientation = Orientation.Horizontal };
            budsChipGlyph = new TextBlock { Text = BudsGlyph, FontFamily = Icons, FontSize = 14, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center };
            // если в папке headphones лежит своя картинка наушников, показываем её вместо общего значка
            budsChipImage = new Image { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            RenderOptions.SetBitmapScalingMode(budsChipImage, BitmapScalingMode.HighQuality);
            budsRow.Children.Add(budsChipGlyph); budsRow.Children.Add(budsChipImage);
            budsRow.Children.Add(budsChipText);
            budsChip = new Border { Height = 30, Margin = new Thickness(0, 0, 40, 0), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Child = budsRow, Visibility = Visibility.Collapsed };
            g.Children.Add(budsChip);
            // Настройки — отдельной кнопкой в правом углу, на любой вкладке
            var gear = IconBtn(((char)0xE713).ToString(), 30, delegate { OpenSettings(); });
            gear.HorizontalAlignment = HorizontalAlignment.Right; gear.VerticalAlignment = VerticalAlignment.Center;
            gear.ToolTip = "Настройки";
            g.Children.Add(gear);

            pages = new FrameworkElement[] { BuildHome(), BuildClipPage(), BuildShelfPage(), BuildTextPage(), BuildChatPage(), BuildNotePage(), BuildWeatherPage(), BuildSystemPage(), BuildClaudePage() };
            foreach (var p in pages) { Grid.SetRow(p, 2); g.Children.Add(p); }
            expandedHost.Children.Add(g);
            ShowPage(0);
        }

        // ---- чат растёт вниз вместе с разговором, чтобы длинные ответы было удобно читать
        RowDefinition pageRow;
        double chatExtra;

        double PageExtra() { return page == PChat ? chatExtra : 0; }

        void ApplyPageExtra()
        {
            double ex = PageExtra(), sub = SubExtra();
            pageRow.Height = new GridLength(PageH + ex);
            if (gapRow != null) gapRow.Height = new GridLength(12 + sub);
            expandedHost.Height = EH + ex + sub;
            if (state == 2) SetTargets();
        }

        // Пересчитать высоту чата и прокрутить к последнему сообщению
        void ChatGrow()
        {
            chatScroll.ScrollToEnd();
            Dispatcher.BeginInvoke(new Action(delegate
            {
                // 220 — высота списка сообщений в обычной панели; расти можно, пока панель помещается на экране
                double room = SystemParameters.WorkArea.Height / Scale - EH - 40;
                double want = Math.Max(0, Math.Min(Math.Min(ChatGrowMax, room), chatList.ActualHeight - 220));
                if (Math.Abs(want - chatExtra) >= 1) { chatExtra = want; ApplyPageExtra(); }
                chatScroll.ScrollToEnd();
            }), DispatcherPriority.Loaded);
        }

        // ---- плавная смена вкладок: страницы съезжают вбок, подсветка скользит от значка к значку
        Border tabGlow;
        TranslateTransform tabGlowX = new TranslateTransform();
        Grid tabHost;
        int pageSlide;   // номер последней смены: по нему запоздавшая анимация понимает, что она уже не нужна

        static readonly IEasingFunction SlideEase = new CubicEase { EasingMode = EasingMode.EaseOut };

        // Подсветка переезжает под активную вкладку и растягивается до её ширины
        void MoveTabGlow(bool animate)
        {
            if (tabGlow == null || tabHost == null) return;
            tabHost.UpdateLayout();
            Border t = tabs[TabGroups[GroupOf(page)][0]];
            if (t.ActualWidth <= 0) return;
            double x = t.TranslatePoint(new Point(0, 0), tabHost).X, w = t.ActualWidth;
            if (!animate || tabGlow.ActualWidth <= 0 || state != 2)
            {
                tabGlowX.BeginAnimation(TranslateTransform.XProperty, null); tabGlow.BeginAnimation(FrameworkElement.WidthProperty, null);
                tabGlowX.X = x; tabGlow.Width = w;
                return;
            }
            var dur = TimeSpan.FromMilliseconds(260);
            tabGlowX.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, dur) { EasingFunction = SlideEase });
            tabGlow.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = SlideEase });
        }

        // ---- вкладки собраны в группы, чтобы в строке было пять значков, а не девять:
        // Главная · Буфер (буфер, полка, текст) · Заметка · ИИ (чат, Claude) · Сводка (погода, система)
        static readonly int[][] TabGroups = { new int[] { PHome }, new int[] { PClip, PShelf, PText }, new int[] { PNote }, new int[] { PChat, PClaude }, new int[] { PWeather, PSystem } };
        static readonly int[] TabOrder = { PHome, PClip, PShelf, PText, PNote, PChat, PClaude, PWeather, PSystem };
        int[] lastInGroup = { PHome, PClip, PNote, PChat, PWeather };   // на какой вкладке группы были в прошлый раз

        static int GroupOf(int p) { for (int g = 0; g < TabGroups.Length; g++) if (Array.IndexOf(TabGroups[g], p) >= 0) return g; return 0; }

        bool TabAvailable(int p) { return p != PClaude || ClaudeBridge.Enabled; }   // вкладка Claude есть только у тех, кто его подключил

        // Клик по значку закрытой группы открывает ту её вкладку, на которой были в прошлый раз
        int TabTarget(int p)
        {
            int g = GroupOf(p);
            if (g == GroupOf(page)) return p;
            int last = lastInGroup[g];
            return TabAvailable(last) ? last : p;
        }

        // Сколько места занимает строка разделов (она есть только у групп из нескольких вкладок)
        const double SubRowH = 30;
        RowDefinition gapRow;
        StackPanel subRow;

        int GroupSize(int g) { int n = 0; foreach (int m in TabGroups[g]) if (TabAvailable(m)) n++; return n; }

        double SubExtra() { return GroupSize(GroupOf(page)) > 1 ? SubRowH : 0; }

        // Строка разделов открытой группы: «Буфер · Полка · Текст»
        void RenderSubTabs(int idx)
        {
            if (subRow == null) return;
            subRow.Children.Clear();
            int g = GroupOf(idx);
            if (GroupSize(g) < 2) return;
            foreach (int m in TabGroups[g])
            {
                if (!TabAvailable(m)) continue;
                int target = m;
                bool on = m == idx;
                var chip = new Border { Height = 24, CornerRadius = new CornerRadius(12), Padding = new Thickness(13, 0, 13, 1), Margin = new Thickness(2, 0, 2, 0), Background = on ? TabOn : Brushes.Transparent, Cursor = Cursors.Hand,
                    Child = new TextBlock { Text = tabLabels[m].Text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = on ? Fg : Dim, VerticalAlignment = VerticalAlignment.Center } };
                chip.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; ShowPage(target); };
                subRow.Children.Add(chip);
            }
        }

        // Соседняя вкладка в порядке строки (для колёсика мыши)
        void StepPage(int dir)
        {
            int at = Array.IndexOf(TabOrder, page);
            for (int n = 0; n < TabOrder.Length; n++)
            {
                at += dir;
                if (at < 0 || at >= TabOrder.Length) return;
                if (TabAvailable(TabOrder[at])) { ShowPage(TabOrder[at]); return; }
            }
        }

        void ShowPage(int idx)
        {
            int prev = page;
            page = idx;
            ApplyPageExtra();
            if (state == 2) SetTargets();   // у «Главной» своя высота
            int slide = ++pageSlide;
            bool moving = prev != idx && state == 2 && prev >= 0 && prev < pages.Length;
            double dir = idx > prev ? 1 : -1;
            for (int i = 0; i < pages.Length; i++)
            {
                bool on = i == idx;
                pages[i].Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                // В верхней строке — по одному значку на группу, они всегда стоят на своих местах.
                // Разделы открытой группы — отдельной строкой ниже (RenderSubTabs).
                int grp = GroupOf(i);
                tabs[i].Visibility = TabGroups[grp][0] == i ? Visibility.Visible : Visibility.Collapsed;
                tabLabels[i].Visibility = Visibility.Collapsed;
                tabGlyphs[i].Foreground = grp == GroupOf(idx) ? Fg : Dim;
            }
            lastInGroup[GroupOf(idx)] = idx;
            RenderSubTabs(idx);
            MoveTabGlow(true);
            {
                // Спокойный переход: прежняя страница исчезает сразу (две страницы друг на друге не видны),
                // новая проявляется с коротким сдвигом на несколько точек.
                FrameworkElement cur = pages[idx];
                var curT = cur.RenderTransform as TranslateTransform;
                if (curT == null) { curT = new TranslateTransform(); cur.RenderTransform = curT; }
                cur.IsHitTestVisible = true;
                cur.Visibility = Visibility.Visible;
                if (moving) curT.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dir * 14, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = SlideEase });
                else { curT.BeginAnimation(TranslateTransform.XProperty, null); curT.X = 0; }
            }
            if (idx != PClip && idx != PNote && idx != PWeather && idx != PChat) { Keyboard.ClearFocus(); }
            if (idx == PChat) { RenderChatEmpty(); ChatGrow(); }
            if (idx == PClaude) { RenderClaudePage(); ClaudeUsage.Refresh(); }
            if (idx == PShelf) RenderShelf();
            pages[idx].Opacity = 0;
            var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(moving ? 280 : 180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
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
        Border homePlayerCard, homeVolCard, homeLaunchCard, homeClipCard;
        Grid homeClipRow;
        public bool TestHomeClips;   // ключ --homeclips
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
            bool clip = TestHomeClips || Store.Get("homeClips") == "1";
            homeClipCard.Visibility = clip ? Visibility.Visible : Visibility.Collapsed;

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
            bool nothing = top == 0 && !vol && !launch && !clip;
            homeEmpty.Visibility = nothing ? Visibility.Visible : Visibility.Collapsed;
            if (nothing) top = 46;
            homeGrid.RowDefinitions[0].Height = new GridLength(top);
            homeGrid.RowDefinitions[1].Height = new GridLength(top > 0 && (vol || launch || clip) ? 10 : 0);
            homeGrid.RowDefinitions[2].Height = new GridLength(vol ? 34 : 0);
            homeGrid.RowDefinitions[3].Height = new GridLength(vol && (launch || clip) ? 10 : 0);
            homeGrid.RowDefinitions[4].Height = new GridLength(launch ? 46 : 0);
            homeGrid.RowDefinitions[5].Height = new GridLength(launch && clip ? 10 : 0);
            homeGrid.RowDefinitions[6].Height = new GridLength(clip ? 38 : 0);
            double total = 0;
            foreach (RowDefinition r in homeGrid.RowDefinitions) total += r.Height.Value;
            homeGrid.Height = total;
            if (clip) RenderHomeClips();
            homeCut = PageH - total; // со строкой «Недавно скопированное» панель может стать и выше обычной (тогда число отрицательное)
            if (state == 2) SetTargets();
        }

        // Высота раскрытого острова: «Главная» может быть ниже остальных вкладок
        // Панель подстраивается под содержимое: у «Главной» и «Текста» его меньше, чем у остальных вкладок
        double PageCut() { return page == 0 ? homeCut : page == PText ? 78 : 0; }

        double ExpandedH() { return EH - PageCut() + PageExtra() + SubExtra(); }

        // Для окна настроек: оно рисует у себя живую копию острова, главной панели и мини-плеера.
        public Visual PreviewRoot { get { return root; } }
        Grid previewHome, previewMini;
        static Grid Nest(Grid host)
        {
            var inner = new Grid();
            var kids = new List<UIElement>();
            foreach (UIElement k in host.Children) kids.Add(k);
            host.Children.Clear();
            foreach (UIElement k in kids) inner.Children.Add(k);
            host.Children.Add(inner);
            return inner;
        }
        public Visual PreviewHome { get { return previewHome; } }
        public Visual PreviewMini { get { return previewMini; } }
        public static readonly double PreviewEH = EH;
        public Brush PreviewInk { get { return Ink; } }
        public double PreviewHomeH { get { return EH - homeCut; } }
        public bool PreviewHasMedia { get { return media.Has; } }
        public static readonly double PreviewBaseW = BaseW, PreviewEW = EW, PreviewER = ER, PreviewMW = MW, PreviewMH = MH, PreviewMR = MR;
        public void PreviewRefresh() { if (state == 0) { if (page != 0) ShowPage(0); RenderHomeLive(); } }

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
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });

            // Недавно скопированное (по умолчанию выключено)
            homeClipRow = new Grid { Margin = new Thickness(40, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            var clipBody = new Grid();
            clipBody.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 14, Foreground = Dim, Margin = new Thickness(15, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            clipBody.Children.Add(homeClipRow);
            homeClipCard = new Border { Background = Card, CornerRadius = new CornerRadius(17), Child = clipBody };
            Grid.SetRow(homeClipCard, 6); Grid.SetColumnSpan(homeClipCard, 3);
            g.Children.Add(homeClipCard);

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

            var top = homeTop = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mCover = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(16), Background = Chip };
            top.Children.Add(mCover);
            var names = homeNames = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
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
            vol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(VolChipW) });
            vol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            vol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            vol.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 15, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center });
            volBar = new Bar(PageW - 32 - VolChipW - 44, 5, Fg, 22);
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
            // Это заметная кнопка с подписью: «Весь звук» или название плеера.
            volIcon = vol.Children[0] as TextBlock;
            volIconDefault = volIcon.Text;
            vol.Children.RemoveAt(0);
            volIcon.FontSize = 13; volIcon.Margin = new Thickness(0, 0, 6, 0);
            volLabel = new TextBlock { Foreground = Fg, FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MaxWidth = VolChipW - 50, TextTrimming = TextTrimming.CharacterEllipsis };
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal };
            chipRow.Children.Add(volIcon); chipRow.Children.Add(volLabel);
            volChip = new Border { Background = Chip, CornerRadius = new CornerRadius(13), Height = 26, Padding = new Thickness(9, 0, 10, 1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Child = chipRow };
            volChip.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; ToggleVolMode(); };
            vol.Children.Add(volChip);
            SetVolMode(false);
            Grid.SetRow(volCard, 2); Grid.SetColumnSpan(volCard, 3);
            g.Children.Add(volCard);
            ApplyHomeLayout();
            ApplyPlayerStyle();
            // Холст не обрезает содержимое: со строкой «Недавно скопированное» «Главная» бывает выше остальных вкладок.
            g.Width = PageW;
            var cv = new Canvas();
            cv.Children.Add(g);
            return cv;
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

        // «Недавно скопированное» на «Главной»: три последние текстовые записи, клик вставляет.
        void RenderHomeClips()
        {
            if (homeClipRow == null) return;
            homeClipRow.Children.Clear();
            int shown = 0;
            IEnumerable<ClipItem> source = clips;
            if (TestHomeClips)   // для снимков: выдуманные записи вместо настоящего буфера
                source = new ClipItem[] { new ClipItem { Text = "Встреча в 15:00, переговорная 2" }, new ClipItem { Text = "kapsula-0.1.9.zip" }, new ClipItem { Text = "#34C759" } };
            foreach (ClipItem it in source)
            {
                if (it.Image != null || string.IsNullOrEmpty(it.Text)) continue;
                ClipItem item = it;
                string one = item.Text.Replace("\r", " ").Replace("\n", " ").Trim();
                if (one.Length == 0) continue;
                if (one.Length > 60) one = one.Substring(0, 60);
                var b = new Border { Background = Chip, CornerRadius = new CornerRadius(10), Height = 24, Margin = new Thickness(shown == 0 ? 0 : 6, 0, 0, 0), Padding = new Thickness(10, 0, 10, 1), Cursor = Cursors.Hand, ToolTip = "Вставить",
                    Child = new TextBlock { Text = one, Foreground = Fg, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center } };
                b.MouseEnter += delegate { b.Background = ChipHover; };
                b.MouseLeave += delegate { b.Background = Chip; };
                b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    try { Clipboard.SetText(item.Text); } catch { return; }
                    if (!IsActive) Native.PasteIntoActiveWindow();
                };
                Grid.SetColumn(b, shown);
                homeClipRow.Children.Add(b);
                if (++shown == 3) break;
            }
            homeClipRow.ColumnDefinitions.Clear();
            for (int i = 0; i < Math.Max(1, shown); i++) homeClipRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (shown == 0) homeClipRow.Children.Add(new TextBlock { Text = "Скопируйте что-нибудь — появится здесь", Foreground = Dim, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
        }

        void RenderClips()
        {
            RenderHomeClips();
            clipList.Children.Clear();
            if (clips.Count == 0)
            {
                clipList.Children.Add(PetSpot("sleep", 58, 12));
                clipList.Children.Add(new TextBlock { Text = "Скопируйте что-нибудь — появится здесь", Foreground = Dim, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
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
            var top = new Grid { Height = 28, Margin = new Thickness(0, 0, 0, 16) };
            chatModelLabel =new TextBlock { Foreground = Faint, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            top.Children.Add(chatModelLabel);
            var fresh = Btn("Очистить", false, 0, 28, delegate { chatGen++; chatBusy = false; chatHistory.Clear(); chatList.Children.Clear(); RenderChatEmpty(); ChatGrow(); });
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

        // Зверёк для пустых мест интерфейса: стоит по центру над подписью, настроение — по ситуации
        FrameworkElement PetSpot(string mood, double top, double bottom)
        {
            var pet = new PetView(2.6, Ink);
            pet.Mood(mood);
            pet.Root.HorizontalAlignment = HorizontalAlignment.Center;
            pet.Root.Margin = new Thickness(pet.Root.Children.Count > 1 ? 14 : 0, top, 0, bottom);   // сдвиг вправо уравновешивает место под значок
            return pet.Root;
        }

        void RenderChatEmpty()
        {
            bool key = Ai.Key() != "";
            chatModelLabel.Text = key ? Ai.Label : Ai.ProviderName + " · ключ не задан";
            chatEmpty.Children.Clear();
            chatEmpty.Visibility = chatHistory.Count == 0 && chatList.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            chatEmpty.Children.Add(PetSpot(key ? "idle" : "sleep", 0, 12));
            if (key)
            {
                chatEmpty.Children.Add(new TextBlock { Text = "Спросите о чём угодно — ответит " + Ai.ProviderName + ".", Foreground = Dim, FontSize = 13, TextAlignment = TextAlignment.Center });
                chatEmpty.Children.Add(new TextBlock { Text = "Разговор хранится только в памяти и стирается кнопкой «Очистить».", Foreground = Faint, FontSize = 11.5, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
            }
            else
            {
                chatEmpty.Children.Add(new TextBlock { Text = "Чтобы чат заработал, добавьте свой ключ " + Ai.ProviderName + " API.", Foreground = Dim, FontSize = 13, TextAlignment = TextAlignment.Center });
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
            // сообщение появляется мягко: проявляется и чуть подъезжает снизу
            var rise = new TranslateTransform(0, 10);
            b.RenderTransform = rise;
            b.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            chatEmpty.Visibility = Visibility.Collapsed;
            ChatGrow();
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
            string key = Ai.Key();
            if (key == "") { Toast("", "Нужен ключ нейросети — добавьте в настройках", null, -1, null); return; }
            string text;
            try { text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch { return; }
            if (text.Trim().Length == 0) { Toast("", "В буфере нет текста", null, -1, null); return; }
            if (text.Length > 60000) { Toast("", "Текст слишком длинный для одного запроса", null, -1, null); return; }
            chatBusy = true;
            textPreview.Text = Ai.ProviderName + " работает…";
            textPreview.Foreground = Dim;
            string model = Ai.Model();
            var msgs = new List<ChatMsg> { new ChatMsg { Role = "user", Text = "Задача: " + task + "\n\nТекст:\n" + text } };
            const string sys = "Ты выполняешь одно преобразование текста. Верни только результат — без пояснений, вступлений, кавычек и разметки. Сохраняй абзацы и переносы строк исходного текста.";
            var sb = new StringBuilder();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = Ai.Send(key, model, msgs, sys, delegate(string piece) { lock (sb) sb.Append(piece); });
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
            string key = Ai.Key();
            if (key == "") { RenderChatEmpty(); Toast("", "Сначала добавьте ключ нейросети", null, -1, null); return; }
            chatBox.Text = "";
            string image = pendingImage;
            SetAttachment(null);
            chatHistory.Add(new ChatMsg { Role = "user", Text = q, ImageB64 = image });
            AddBubble(true, image != null ? "[картинка] " + q : q);
            TextBlock answer = AddBubble(false, "Думаю");
            answer.Foreground = Dim;
            chatBusy = true;
            int gen = chatGen;
            string model = Ai.Model();
            var snapshot = new List<ChatMsg>(chatHistory);
            var sb = new StringBuilder();
            // Ответ появляется плавно, как будто его печатают, — даже если нейросеть прислала весь текст одним куском.
            // Пока ответа нет, после «Думаю» бегут точки.
            int shown = 0;
            bool finished = false;
            // Зверёк сидит в сообщении и думает, пока нейросеть не начала отвечать
            var thinker = new PetView(1.5, Ink);
            thinker.Mood("think");
            thinker.Root.Margin = new Thickness(0, 0, 4, 0);
            var bubble = answer.Parent as Border;
            if (bubble != null)
            {
                var waitRow = new StackPanel { Orientation = Orientation.Horizontal };
                bubble.Child = null;
                waitRow.Children.Add(thinker.Root); waitRow.Children.Add(answer);
                bubble.Child = waitRow;
            }
            var type = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            type.Tick += delegate
            {
                if (gen != chatGen) { type.Stop(); return; }
                if (sb.Length == 0) { if (finished) type.Stop(); return; }   // пока ответа нет, в сообщении думает зверёк
                if (thinker.Root.Visibility == Visibility.Visible) thinker.Root.Visibility = Visibility.Collapsed;
                if (shown >= sb.Length) { if (finished) type.Stop(); return; }
                // чем больше текста ждёт показа, тем быстрее печатаем: короткий ответ идёт по буквам, длинный не тянется
                int left = sb.Length - shown;
                shown += Math.Max(1, Math.Min(left, left / 14 + 1));
                answer.Foreground = Fg;
                answer.Text = sb.ToString(0, shown);
                ChatGrow();
            };
            type.Start();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = Ai.Send(key, model, snapshot, null, delegate(string piece)
                {
                    Dispatcher.BeginInvoke(new Action(delegate { if (gen == chatGen) sb.Append(piece); }));
                });
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (gen != chatGen) return;
                    chatBusy = false;
                    finished = true;
                    if (err == null && sb.Length > 0) { chatHistory.Add(new ChatMsg { Role = "assistant", Text = sb.ToString() }); return; }
                    // Неудача: убираем вопрос из истории, чтобы следующий запрос не начинался с двух сообщений пользователя подряд.
                    type.Stop();
                    chatHistory.RemoveAt(chatHistory.Count - 1);
                    thinker.Root.Visibility = Visibility.Visible;
                    thinker.Mood("sad");   // не получилось — зверёк огорчён
                    thinker.Root.Margin = new Thickness(0, 0, 8, 0);
                    answer.Foreground = Dim;
                    answer.Text = (sb.Length > 0 ? sb + "\n\n" : "") + (err ?? "Пустой ответ.");
                    ChatGrow();
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
            shelfEmpty = new TextBlock { Text = "Перетащите файлы на остров — они лягут на полку", Foreground = Dim, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };
            // над подписью пустой полки — зверёк; виден вместе с ней
            var shelfPet = PetSpot("idle", 0, 44);
            shelfPet.VerticalAlignment = VerticalAlignment.Center;
            shelfEmpty.IsVisibleChanged += delegate { shelfPet.Visibility = shelfEmpty.Visibility; };
            var zone = new Rectangle { Stroke = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40)), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection(new double[] { 5, 4 }), RadiusX = 18, RadiusY = 18, Fill = Brushes.Transparent };
            body.Children.Add(zone);
            body.Children.Add(shelfEmpty);
            body.Children.Add(shelfPet);
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
            b.ToolTip = "Выполнит нейросеть; результат попадёт в буфер обмена";
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

            // подпись над числами: без неё непонятно, что это минуты таймера
            var presetsCaption = new TextBlock { Text = "Таймер, минут", Foreground = Faint, FontSize = 11, Margin = new Thickness(2, 8, 0, 0) };
            DockPanel.SetDock(presetsCaption, Dock.Top);
            right.Children.Add(presetsCaption);
            var presets = new UniformGrid { Rows = 1, Columns = 5, Margin = new Thickness(-2, 4, -2, 0), Height = 26 };
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

        // «заметка купить молоко» → «купить молоко»; для остальных запросов — пусто
        public static string NoteCommandText(string q)
        {
            foreach (string k in new string[] { "заметка ", "в заметку ", "запиши ", "note " })
                if (q.StartsWith(k, StringComparison.OrdinalIgnoreCase) && q.Length > k.Length) return q.Substring(k.Length).Trim();
            return "";
        }

        // Дописать строку в конец текущей заметки (с временем) и сразу сохранить
        void AppendNote(string text)
        {
            try
            {
                var p = new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(DateTime.Now.ToString("d MMM, HH:mm") + " — " + text.Trim()));
                p.Margin = new Thickness(0, 0, 0, 4);
                noteRich.Document.Blocks.Add(p);
                SaveNote();
                Toast("", "Сохранено в заметку", null, -1, accent);
            }
            catch (Exception ex) { Store.LogError("запись в заметку", ex); Toast("", "Не удалось сохранить заметку", null, -1, null); }
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
        Tile quietTile;

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
            quietTile = new Tile(((char)0xE708).ToString(), "Не беспокоить", delegate { SetQuiet(QuietNow ? 0 : 60); RenderSystem(); });
            // Плитки разложены по смыслу, у каждой группы подпись: что подключено, как ведёт себя Kapsula, питание компьютера.
            // Плитки «Настройки» здесь больше нет — шестерёнка стоит в углу панели на любой вкладке.
            GC.KeepAlive(setTile); GC.KeepAlive(tiles);
            string[] captions = { "Подключения и звук", "Kapsula", "Питание" };
            Tile[][] groups = { new Tile[] { netTile, btTile, vpnTile, micTile }, new Tile[] { themeTile, autoTile, lyrTile, quietTile }, new Tile[] { lockTile, sleepTile, restartTile, offTile } };
            double[] heights = { 56, 56, 50 };   // вместе с подписями и нагрузкой всё должно уместиться в высоту вкладки
            for (int gi = 0; gi < groups.Length; gi++)
            {
                sp.Children.Add(new TextBlock { Text = captions[gi], Foreground = Faint, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, gi == 0 ? 0 : 5, 0, 1) });
                var rowGrid = new UniformGrid { Rows = 1, Columns = 4, Height = heights[gi], Margin = new Thickness(-4, 0, -4, 0) };
                foreach (Tile tl in groups[gi]) { tl.Root.Margin = new Thickness(4, 3, 4, 3); tl.Label.Margin = new Thickness(0, 5, 0, 0); rowGrid.Children.Add(tl.Root); }
                sp.Children.Add(rowGrid);
            }

            var stats = new Border { Background = Card, CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 6, 0, 0) };
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
                                    if (Quiet(a.Length > 0 ? a : "Windows", a.Length > 0 ? a : "Уведомление", text)) return;
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
            if (TestDemoCard == null && Quiet("Discord", title, text)) { UnreadAdd(true); return; }
            Action open = delegate { try { System.Diagnostics.Process.Start("discord://-/channels/@me"); } catch { } };
            ShowCardEx(ImageFrom(icon), "", title, text, open, null, null, false);
            SetCardBadge(SourceIcon("discord"));
            HideCardButton();
            cardUntil = Environment.TickCount + 6000;
            if (TestDemoCard == null) UnreadAdd(true);
        }

        // ---- Claude Code: запрос разрешения карточкой, «ждёт ответа» и «закончил» — короткой надписью
        public int TestPet;         // ключ --pet 1..4: настроение питомца для снимков
        public string TestClaude;   // ключ --claudetest allow|deny|wait: что «нажать» на карточке в проверочном режиме

        // Сейчас перед глазами сам Claude Code (приложение, терминал или редактор)? Тогда остров не вмешивается.
        bool ClaudeInFront()
        {
            uint pid;
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            if (pid == 0) return false;
            string n;
            try { n = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); } catch { return false; }
            foreach (string s in new string[] { "claude", "windowsterminal", "conhost", "powershell", "pwsh", "cmd", "code", "cursor", "wezterm", "alacritty" })
                if (n == s || n.StartsWith(s + " ") || n.StartsWith(s + "-")) return true;
            return false;
        }

        static readonly string ClaudeGlyph = ((char)0xE756).ToString();
        int claudeSeq, claudeToastAt;

        bool ClaudeAway() { return TestClaude != null || !ClaudeInFront(); }

        void ClaudeAfter(int ms, Action a)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += delegate { t.Stop(); a(); };
            t.Start();
        }

        // Открыто несколько сессий — в заголовке карточки видно, какая именно спрашивает.
        string ClaudeTitle(ClaudeBridge.Pending p, string what)
        {
            bool many = ClaudeBridge.Sessions().Count > 1;
            return (many && p.Project.Length > 0 ? "Claude · " + p.Project : "Claude Code") + (what.Length > 0 ? " · " + what : "");
        }

        // ---- очередь: запросы от разных сессий показываются по одному, следующий — когда ответили на предыдущий
        class ClaudeItem { public ClaudeBridge.Pending P; public Action Show; }
        List<ClaudeItem> claudeQueue = new List<ClaudeItem>();
        ClaudeBridge.Pending claudeNow;   // запрос, чья карточка сейчас на острове
        bool claudeBusy;                  // пользователь выбирает вариант или печатает ответ — не перебиваем

        void ClaudeEnqueue(ClaudeBridge.Pending p, Action show)
        {
            if (!ClaudeAway() || QuietNow) { p.Pass(); return; }   // пользователь и так смотрит на Claude Code (или просил не беспокоить) — пусть спросит сам
            claudeQueue.Add(new ClaudeItem { P = p, Show = show });
            ClaudePump();
        }

        void ClaudePump()
        {
            claudeQueue.RemoveAll(delegate(ClaudeItem x) { return x.P.Answered; });
            if (claudeQueue.Count == 0 || claudeBusy) return;
            if (state == 3 && claudeNow != null && !claudeNow.Answered) return;   // на предыдущий ещё не ответили
            if (state == 3 && claudeNow == null) return;                          // на острове чужая карточка — подождём
            if (state == 1) return;
            ClaudeItem it = claudeQueue[0];
            claudeQueue.RemoveAt(0);
            if (state == 2 || state == 4) { it.P.Pass(); return; }   // панель открыта, карточку показать некуда
            claudeNow = it.P;
            it.Show();
            if (state == 3 && !(it.P is ClaudeBridge.Finished)) ClaudeSound();
            if (state != 3) { it.P.Pass(); claudeNow = null; }
        }

        // Запрос разрешения: «Разрешить», «Всегда» (если Claude Code сам предложил такое правило) и крестик — отклонить.
        void ClaudeAsk(ClaudeBridge.Ask ask)
        {
            ClaudeEnqueue(ask, delegate
            {
                string sub = ask.Detail.Length > 0 ? ask.Detail : "Просит разрешение";
                string[] labels = ask.CanAlways ? new string[] { "Разрешить", "Всегда" } : new string[] { "Разрешить" };
                Action[] acts = { delegate { ask.Allow(); }, delegate { ask.AllowAlways(); } };
                ShowCardOptions(ClaudeGlyph, ClaudeTitle(ask, ask.Tool), sub, labels, acts, delegate { ask.Deny(); }, ClaudeBridge.WaitMs - 1500);
                CardPet("ask");
                if (TestClaude == "allow") ClaudeAfter(2500, delegate { ask.Allow(); });
                else if (TestClaude == "always") ClaudeAfter(2500, delegate { ask.AllowAlways(); });
                else if (TestClaude == "deny" || TestClaude == "queue") ClaudeAfter(2500, delegate { ask.Deny(); SetState(0); });
            });
        }

        // Вопрос с вариантами. Простой (один вопрос, один ответ, до трёх вариантов) — сразу кнопками.
        // Остальные — кнопка «Выбрать»: открывается список, где можно отметить несколько или вписать свой ответ.
        void ClaudeQuestion(ClaudeBridge.Question q)
        {
            ClaudeEnqueue(q, delegate
            {
                ClaudeBridge.QItem first = q.Items[0];
                string title = ClaudeTitle(q, first.Header.Length > 0 ? first.Header : "вопрос");
                if (q.Items.Length == 1 && !first.Multi && first.Options.Length <= 3)
                {
                    var acts = new Action[first.Options.Length];
                    for (int i = 0; i < acts.Length; i++)
                    {
                        string label = first.Options[i];
                        acts[i] = delegate { var a = new Dictionary<string, string>(); a[first.Text] = label; q.Answer(a); };
                    }
                    ShowCardOptions(ClaudeGlyph, title, ClaudeBridge.Clean(first.Text, 160), first.Options, acts, delegate { q.Pass(); }, ClaudeBridge.WaitMs - 1500);
                    CardPet("ask");
                    if (TestClaude == "answer") ClaudeAfter(2500, acts[1]);
                    return;
                }
                string sub = q.Items.Length > 1 ? "Вопросов: " + q.Items.Length + ". " + ClaudeBridge.Clean(first.Text, 120) : ClaudeBridge.Clean(first.Text, 160);
                Action choose = delegate { q.Extend(ClaudeBridge.TypingWaitMs); ClaudeChoose(q, 0, new Dictionary<string, string>()); };
                ShowCardOptions(ClaudeGlyph, title, sub, new string[] { "Выбрать" }, new Action[] { choose }, delegate { q.Pass(); }, ClaudeBridge.WaitMs - 1500);
                CardPet("ask");
                if (TestClaude == "choose") ClaudeAfter(1200, delegate { SetState(0); choose(); });
            });
        }

        // Список вариантов для вопроса номер n; после ответа — следующий вопрос или отправка всех ответов.
        void ClaudeChoose(ClaudeBridge.Question q, int n, Dictionary<string, string> answers)
        {
            ClaudeBridge.QItem it = q.Items[n];
            claudeBusy = true;
            StartChoice(new Choice
            {
                Title = (q.Items.Length > 1 ? (n + 1) + " из " + q.Items.Length + ". " : "") + ClaudeBridge.Clean(it.Text, 90),
                Labels = it.Options, Notes = it.Notes, Multi = it.Multi,
                Done = delegate(string answer)
                {
                    answers[it.Text] = answer;
                    if (n + 1 < q.Items.Length) ClaudeAfter(180, delegate { ClaudeChoose(q, n + 1, answers); });
                    else { claudeBusy = false; q.Answer(answers); Toast(ClaudeGlyph, "Ответ отправлен Claude Code", null, -1, accent); }
                },
                Cancel = delegate { claudeBusy = false; q.Pass(); }
            });
            // Проверочный режим: отмечаем первые два варианта и жмём «Готово», как это сделал бы пользователь.
            if (TestClaude == "choose")
                ClaudeAfter(1800, delegate
                {
                    if (choice == null) return;
                    if (choice.Multi) { RunCmd(1); RunCmd(2); ClaudeAfter(1500, delegate { RunCmd(cmdItems.Count - 1); }); }
                    else ClaudeAfter(1500, delegate { RunCmd(2); });
                });
        }

        // ---- выбор из списка в строке ввода (варианты ответа, отметки, свой текст)
        class Choice
        {
            public string Title; public string[] Labels, Notes; public bool Multi;
            public HashSet<int> Picked = new HashSet<int>();
            public Action<string> Done; public Action Cancel; public bool Finished;
        }
        Choice choice;

        void StartChoice(Choice c)
        {
            if (state == 4) SetState(0);
            choice = c;
            cmdHint.Text = c.Multi ? "Отметьте нужное и нажмите «Готово» — или впишите свой ответ" : "Выберите вариант или впишите свой ответ";
            ToggleCommand();
            UpdateCmd();
        }

        // Строки списка для текущего вопроса
        void FillChoice(string typed)
        {
            Choice c = choice;
            AddCmd(c.Title, "", ClaudeGlyph, null, null);
            cmdItems[0].Stay = true;
            for (int i = 0; i < c.Labels.Length; i++)
            {
                int idx = i;
                string label = c.Labels[i];
                if (c.Multi)
                {
                    AddCmd((c.Picked.Contains(idx) ? "✓  " : "      ") + label, c.Notes[i], "", null, delegate { if (!c.Picked.Remove(idx)) c.Picked.Add(idx); });
                    cmdItems[cmdItems.Count - 1].Stay = true;
                }
                else AddCmd(label, c.Notes[i], "", null, delegate { c.Finished = true; c.Done(label); });
            }
            if (typed.Length > 0) AddCmd("Свой ответ: " + (typed.Length > 50 ? typed.Substring(0, 50) + "…" : typed), "", "", null, delegate { c.Finished = true; c.Done(typed); });
            else if (c.Multi && c.Picked.Count > 0)
            {
                var chosen = new List<string>();
                for (int i = 0; i < c.Labels.Length; i++) if (c.Picked.Contains(i)) chosen.Add(c.Labels[i]);
                string joined = string.Join(", ", chosen.ToArray());
                AddCmd("Готово", "выбрано: " + c.Picked.Count, "", null, delegate { c.Finished = true; c.Done(joined); });
            }
            if (cmdSel == 0 && cmdItems.Count > 1) cmdSel = 1;   // первая строка — сам вопрос, выбирать её незачем
        }

        // Claude закончил: показываем начало ответа и кнопку «Ответить» — написать ему можно прямо отсюда.
        void ClaudeDone(ClaudeBridge.Finished f)
        {
            ClaudeEnqueue(f, delegate
            {
                Action reply = delegate
                {
                    f.Extend(ClaudeBridge.TypingWaitMs);
                    claudeBusy = true;
                    StartReplyTo("claude:" + (++claudeSeq), "Claude Code",
                        delegate(string text) { claudeBusy = false; f.Send(text); Toast(ClaudeGlyph, "Отправлено Claude Code", null, -1, accent); },
                        delegate { claudeBusy = false; f.Pass(); });
                };
                ShowCardEx(null, ClaudeGlyph, ClaudeTitle(f, "закончил"), f.Text.Length > 0 ? f.Text : "Можно ответить прямо отсюда", delegate { f.Pass(); }, "Ответить", reply, false);
                if (state != 3) return;
                CardPet("done");
                FitCard();
                cardUntil = Environment.TickCount + ClaudeBridge.FinishWaitMs - 500;
                if (TestClaude == "reply") ClaudeAfter(2500, delegate { f.Send("проверь ещё раз тесты"); });
            });
        }

        // Что Claude Code делает сейчас — короткой надписью, не чаще раза в несколько секунд.
        void ClaudeWorking(string tool, string detail, string project)
        {
            if (Store.Get("claudeActivity") == "0" || QuietNow || !ClaudeAway() || state != 0 || claudeQueue.Count > 0) return;
            int now = Environment.TickCount;
            if (claudeToastAt != 0 && unchecked(now - claudeToastAt) < 6000) return;
            claudeToastAt = now | 1;
            if (detail.Length > 44) detail = detail.Substring(0, 43) + "…";
            bool many = ClaudeBridge.Sessions().Count > 1;
            Toast(ClaudeGlyph, (many && project.Length > 0 ? project : "Claude") + " · " + tool + (detail.Length > 0 ? ": " + detail : ""), null, -1, accent);
        }

        void ClaudeWaiting(string project)
        {
            if (!ClaudeAway() || state != 0) return;
            bool many = ClaudeBridge.Sessions().Count > 1;
            Toast(ClaudeGlyph, (many && project.Length > 0 ? "Claude · " + project : "Claude Code") + " ждёт ответа", null, -1, accent);
        }

        // sessionId = null — остановить все сессии, которые сейчас работают
        public void ClaudeStop(string sessionId, string name)
        {
            ClaudeBridge.RequestStop(sessionId);
            Toast(ClaudeGlyph, (name != null ? "Claude · " + name : "Claude Code") + " остановится перед следующим действием", null, -1, accent);
        }

        // ---- вкладка «Claude»: открытые сессии, что делает каждая, и расход за сегодня
        StackPanel claudeList;
        TextBlock claudeUsage;
        Border claudeStopAll;

        FrameworkElement BuildClaudePage()
        {
            var dock = new DockPanel();
            var top = new Grid { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
            claudeUsage = new TextBlock { Foreground = Faint, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 138, 0), TextTrimming = TextTrimming.CharacterEllipsis };   // справа место под кнопку
            top.Children.Add(claudeUsage);
            var stopAll = claudeStopAll = Btn("Остановить все", false, 0, 28, delegate { ClaudeStop(null, null); });
            stopAll.HorizontalAlignment = HorizontalAlignment.Right;
            stopAll.Visibility = Visibility.Collapsed;   // кнопка есть, только когда есть что останавливать
            top.Children.Add(stopAll);
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);
            claudeList = new StackPanel();
            dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = claudeList });
            ClaudeUsage.Updated += delegate { Dispatcher.BeginInvoke(new Action(delegate { if (expanded && page == PClaude) RenderClaudeUsage(); })); };
            return dock;
        }

        void RenderClaudeUsage()
        {
            if (!ClaudeUsage.Ready) { claudeUsage.Text = "Считаю расход за сегодня…"; return; }
            if (ClaudeUsage.Replies == 0) { claudeUsage.Text = "Сегодня Claude Code ещё не отвечал"; return; }
            // коротко и по делу; подробности (ввод, кэш, по дням и проектам) — в подсказке при наведении
            claudeUsage.Text = "Сегодня: " + ClaudeUsage.Replies + " ответов · " + ClaudeUsage.Short(ClaudeUsage.Out) + " токенов";
            var tip = new StringBuilder();
            try
            {
                Dictionary<DateTime, long> byDay;
                lock (ClaudeUsage.Days) byDay = new Dictionary<DateTime, long>(ClaudeUsage.Days);
                var days = new List<DateTime>(byDay.Keys);
                days.Sort();
                tip.Append("\n\nНаписано по дням:");
                foreach (DateTime d in days) tip.Append("\n" + d.ToString("ddd d MMM") + " — " + ClaudeUsage.Short(byDay[d]));
                var projects = new List<KeyValuePair<string, long>>(ClaudeUsage.Projects);
                projects.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b) { return b.Value.CompareTo(a.Value); });
                if (projects.Count > 0) tip.Append("\n\nСегодня по проектам:");
                for (int i = 0; i < projects.Count && i < 6; i++) tip.Append("\n" + (projects[i].Key.Length > 34 ? projects[i].Key.Substring(0, 33) + "…" : projects[i].Key) + " — " + ClaudeUsage.Short(projects[i].Value));
                claudeUsage.Text += " · неделя: " + ClaudeUsage.Short(ClaudeUsage.Week);
            }
            catch { }
            claudeUsage.ToolTip = tip.Insert(0, ClaudeUsageTip()).ToString();
        }

        static string ClaudeUsageTip()
        {
            return "Токены за сегодня по журналам Claude Code на этом компьютере.\nНаписал (выход): " + ClaudeUsage.Out.ToString("N0") + "\nНовый ввод: " + ClaudeUsage.In.ToString("N0") + "\nЗаписано в кэш: " + ClaudeUsage.CacheWrite.ToString("N0") + "\nПрочитано из кэша: " + ClaudeUsage.CacheRead.ToString("N0");
        }

        void RenderClaudePage()
        {
            if (claudeList == null) return;
            RenderClaudeUsage();
            bool busy = ClaudeBridge.Busy;
            claudeStopAll.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            claudeUsage.Margin = new Thickness(4, 0, busy ? 138 : 0, 0);
            claudeList.Children.Clear();
            List<ClaudeBridge.Session> list = ClaudeBridge.Sessions();
            if (list.Count == 0)
            {
                claudeList.Children.Add(PetSpot("sleep", 48, 12));
                claudeList.Children.Add(new TextBlock { Text = "Claude Code пока не давал о себе знать", Foreground = Dim, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
                claudeList.Children.Add(new TextBlock { Text = "Сессии появятся здесь, как только начнут работать. Уже открытые нужно перезапустить.", Foreground = Faint, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
                return;
            }
            foreach (ClaudeBridge.Session s in list)
            {
                ClaudeBridge.Session ses = s;
                bool working = ses.State == "работает" && unchecked(Environment.TickCount - ses.At) < 60000;
                bool needs = ses.State == "ждёт разрешения" || ses.State == "спрашивает" || ses.State == "ждёт ответа";
                string sub = ses.Detail.Length > 0 ? ses.Detail : (ses.State == "закончил" && ses.Last.Length > 0 ? ses.Last : Ago(ses.At));
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = needs ? ClaudeOrange : working ? (Brush)accent : Faint, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                texts.Children.Add(new TextBlock { Text = ses.Name + " — " + ses.State, Foreground = Fg, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                texts.Children.Add(new TextBlock { Text = sub, Foreground = Dim, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
                Grid.SetColumn(texts, 1); row.Children.Add(texts);
                if (working)
                {
                    var stop = Btn("Стоп", false, 0, 28, delegate { ClaudeStop(ses.Id, ses.Name); RenderClaudePage(); });
                    stop.Margin = new Thickness(10, 0, 0, 0); stop.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(stop, 2); row.Children.Add(stop);
                }
                else
                {
                    var ago = new TextBlock { Text = Ago(ses.At), Foreground = Faint, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                    Grid.SetColumn(ago, 2); row.Children.Add(ago);
                }
                claudeList.Children.Add(new Border { Background = Card, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 9, 14, 10), Margin = new Thickness(0, 0, 0, 6), Child = row });
            }
        }

        static readonly Brush ClaudeGreen = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
        static readonly Brush ClaudeOrange =new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));

        // Значок в капсуле: 0 — нет, 1 — Claude Code работает, 2 — ждёт вас, 3 — только что закончил
        int claudeState;
        StackPanel claudeDot;

        // ---- питомец Claude Code в капсуле: пиксельный зверёк, настроение зависит от того, чем занят Claude.
        // Работает — смотрит вниз и подпрыгивает; ждёт вас — глаза круглые и мигает «!»; закончил — довольный, с галочкой;
        // давно молчит — спит. Рисунок свой, из квадратиков; это не копия чужого персонажа.
        static readonly Brush PetBody = new SolidColorBrush(Color.FromRgb(0xF0, 0x8A, 0x4B));
        static readonly string[] PetShape =
        {
            ".B.......B.",
            ".BB.....BB.",
            ".BBBBBBBBB.",
            "BBBBBBBBBBB",
            "BBBBBBBBBBB",
            "BBBBBBBBBBB",
            "BBBBBBBBBBB",
            ".BBBBBBBBB.",
            ".B.B...B.B."
        };
        const double PetPx = 1.6;
        TranslateTransform petShift = new TranslateTransform();
        List<Rectangle>[] petEyes = new List<Rectangle>[5];   // 0 обычные, 1 смотрит вниз, 2 круглые, 3 довольные, 4 спит
        TextBlock petMark;
        int petMood = -1;

        Rectangle PetPixel(Canvas c, int x, int y, int w, int h, Brush fill)
        {
            // +0.3: соседние квадратики чуть перекрываются, чтобы между ними не было щелей при масштабе
            var r = new Rectangle { Width = w * PetPx + 0.3, Height = h * PetPx + 0.3, Fill = fill, SnapsToDevicePixels = true };
            Canvas.SetLeft(r, x * PetPx); Canvas.SetTop(r, y * PetPx);
            r.Tag = new double[] { x * PetPx, y * PetPx };   // исходное место — глаза потом сдвигаются за курсором
            c.Children.Add(r);
            return r;
        }

        StackPanel BuildClaudePet()
        {
            var c = new Canvas { Width = 11 * PetPx, Height = 9 * PetPx, RenderTransform = petShift, VerticalAlignment = VerticalAlignment.Center };
            for (int y = 0; y < PetShape.Length; y++)
            {
                // подряд идущие клетки строки рисуем одним прямоугольником
                int x = 0;
                while (x < 11)
                {
                    if (PetShape[y][x] != 'B') { x++; continue; }
                    int start = x;
                    while (x < 11 && PetShape[y][x] == 'B') x++;
                    PetPixel(c, start, y, x - start, 1, PetBody);
                }
            }
            // Глаза — «вырезы» цвета острова. Каждая пара координат: левый глаз, правый — зеркально.
            int[][][] eyes =
            {
                new int[][] { new int[] { 2, 4, 1, 2 } },                                                   // обычные
                new int[][] { new int[] { 2, 5, 1, 2 } },                                                   // смотрит вниз
                new int[][] { new int[] { 2, 3, 2, 3 } },                                                   // круглые
                new int[][] { new int[] { 1, 5, 1, 1 }, new int[] { 2, 4, 1, 1 }, new int[] { 3, 5, 1, 1 } }, // довольные: дужкой
                new int[][] { new int[] { 1, 5, 3, 1 } }                                                    // спит: чёрточки
            };
            for (int m = 0; m < eyes.Length; m++)
            {
                petEyes[m] = new List<Rectangle>();
                foreach (int[] e in eyes[m])
                {
                    petEyes[m].Add(PetPixel(c, e[0], e[1], e[2], e[3], Ink));
                    petEyes[m].Add(PetPixel(c, 11 - e[0] - e[2], e[1], e[2], e[3], Ink));
                }
            }
            petMark = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2.5, -4, 0, 0), Width = 8 };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 5, 0), Visibility = Visibility.Collapsed };
            sp.Children.Add(c); sp.Children.Add(petMark);
            PetFace(0, "", null);
            return sp;
        }

        // Глаза: 0 обычные, 1 вниз, 2 круглые, 3 довольные, 4 закрытые. Значок справа от зверька — «!», «✓», «z», «…».
        void PetMood(int state) { }   // настроение теперь считает PetAnimate — и по Claude Code, и по обычным событиям

        void PetFace(int eyesKind, string mark, Brush markBrush)
        {
            if (petEyes[0] == null) return;
            if (eyesKind != petMood)
            {
                petMood = eyesKind;
                for (int m = 0; m < petEyes.Length; m++)
                    foreach (Rectangle r in petEyes[m]) r.Visibility = m == eyesKind ? Visibility.Visible : Visibility.Collapsed;
            }
            if (petMark.Text != mark) petMark.Text = mark;
            petMark.Foreground = markBrush ?? Faint;
        }

        // Зверёк — сам персонаж капсулы (а не только значок Claude Code)?
        bool PetIsCharacter() { return Character == "pet" && (ShowEyes || TestCharacter != null) && (!(media.Has && media.Playing) || TestEyes); }

        int petBlinkAt, petBlinkUntil;

        // Настроение и движение зверька. Важное перебивает обычное: ждёт вас → тревога таймера → работает → думает →
        // радуется → спит → просто смотрит за курсором и моргает.
        void PetAnimate()
        {
            if (claudeDot == null || claudeDot.Visibility != Visibility.Visible) return;
            int now = Environment.TickCount;
            double t = now / 1000.0;
            bool chr = PetIsCharacter();
            bool asleep = Native.IdleMs() > 120000;
            string mood = TestMood ?? (timerRinging ? "alarm" : chatBusy ? "think" : unchecked(now - happyUntil) < 0 ? "happy" : asleep ? "sleep" : "");
            if (!chr && TestMood == null) mood = "";   // значок Claude Code при другом персонаже на таймер и чат не реагирует
            petShift.X = 0; petShift.Y = 0; petMark.Opacity = 1;

            if (claudeState == 2) { PetFace(2, "!", ClaudeOrange); petMark.Opacity = (now / 420) % 2 == 0 ? 1 : 0.25; }
            else if (mood == "alarm") { PetFace(2, "!", RecRed); petShift.X = rnd.NextDouble() * 2.4 - 1.2; petShift.Y = rnd.NextDouble() * 1.6 - 0.8; }
            else if (claudeState == 1) { PetFace(1, "", null); petShift.Y = Math.Round(Math.Abs(Math.Sin(t * 5.5)) * -2.2 * 2) / 2; }
            else if (mood == "think") { PetFace(1, "…", Dim); petShift.Y = Math.Round(Math.Sin(t * 3) * 0.8 * 2) / 2; }
            else if (claudeState == 3) PetFace(3, "✓", ClaudeGreen);
            else if (mood == "happy") { PetFace(3, "", null); petShift.Y = Math.Round(Math.Abs(Math.Sin(t * 7)) * -1.6 * 2) / 2; }
            else if (chr && pomoPhase == 1 && timerEnd > DateTime.Now) { PetFace(1, "", null); petShift.Y = Math.Round(Math.Abs(Math.Sin(t * 2.2)) * -1.2 * 2) / 2; }   // помодоро, работа: сосредоточен
            else if (chr && pomoPhase == 2 && timerEnd > DateTime.Now) { PetFace(4, "z", Faint); petMark.Opacity = 0.35 + 0.45 * (0.5 + 0.5 * Math.Sin(t * 1.6)); }       // помодоро, перерыв: отдыхает
            else if (mood == "sleep" || (claudeState == 4 && !chr)) { PetFace(4, "z", Faint); petMark.Opacity = 0.35 + 0.45 * (0.5 + 0.5 * Math.Sin(t * 1.6)); }
            else
            {
                // Обычное состояние: глаза сдвигаются на клетку в сторону курсора; раз в несколько секунд зверёк моргает
                if (petBlinkAt == 0) petBlinkAt = now + 2500 + rnd.Next(4000);
                if (unchecked(now - petBlinkAt) >= 0) { petBlinkUntil = now + 140; petBlinkAt = now + 2500 + rnd.Next(4500); }
                if (unchecked(now - petBlinkUntil) < 0) { PetFace(4, "", null); return; }
                PetFace(0, "", null);
                Native.POINT pt;
                int sx = 0, sy = 0;
                if (Native.GetCursorPos(out pt))
                {
                    double ex = Left + (BaseW / 2 - sw.X / 2 + Ear + 12 + 12) * Scale, ey = Top + CH / 2 * Scale;
                    double dx = pt.X / dpi - ex, dy = pt.Y / dpi - ey;
                    sx = dx > 70 ? 1 : dx < -70 ? -1 : 0;
                    sy = dy > 120 ? 1 : 0;
                }
                foreach (Rectangle r in petEyes[0])
                {
                    double[] home = (double[])r.Tag;
                    Canvas.SetLeft(r, home[0] + sx * PetPx);
                    Canvas.SetTop(r, home[1] + sy * PetPx);
                }
            }
        }

        int ClaudeStateNow()
        {
            if (!ClaudeBridge.Enabled) return 0;
            int now = Environment.TickCount, best = 0;
            if (TestPet != 0) return TestPet;
            foreach (ClaudeBridge.Session s in ClaudeBridge.Sessions())
            {
                int age = unchecked(now - s.At);
                if ((s.State == "ждёт разрешения" || s.State == "спрашивает" || s.State == "ждёт ответа") && age < 5 * 60000) return 2;
                if (s.State == "работает" && age < 60000) best = 1;
                else if (s.State == "закончил" && age < 45000 && best == 0) best = 3;
            }
            // Сессии были, но сейчас тихо — питомец спит (пока о Claude Code слышно хоть что-то за последние полчаса)
            if (best == 0 && ClaudeBridge.Sessions().Count > 0) best = 4;
            return best;
        }

        // ---- «Не беспокоить» и история уведомлений (история живёт только в памяти, до закрытия программы)
        class Seen { public DateTime At; public string Source, Title, Text; }
        List<Seen> seenNotes = new List<Seen>();
        DateTime quietUntil = DateTime.MinValue;

        public bool QuietNow { get { return quietUntil > DateTime.Now; } }

        // Запоминает уведомление в истории. true — сейчас «Не беспокоить», показывать карточку не нужно.
        bool Quiet(string source, string title, string text)
        {
            seenNotes.Add(new Seen { At = DateTime.Now, Source = source, Title = title ?? "", Text = text ?? "" });
            if (seenNotes.Count > 60) seenNotes.RemoveAt(0);
            return QuietNow;
        }

        public void SetQuiet(int minutes)
        {
            quietUntil = minutes > 0 ? DateTime.Now.AddMinutes(minutes) : DateTime.MinValue;
            Toast("", minutes > 0 ? "Не беспокоить: " + (minutes >= 60 ? (minutes / 60) + " ч" : minutes + " мин") : "Уведомления снова показываются", null, -1, minutes > 0 ? (Brush)accent : null);
        }

        void FillQuiet()
        {
            if (QuietNow)
            {
                int left = (int)Math.Ceiling((quietUntil - DateTime.Now).TotalMinutes);
                AddCmd("Выключить «Не беспокоить»", "осталось " + left + " мин", "", null, delegate { SetQuiet(0); });
                return;
            }
            AddCmd("Не беспокоить 1 час", "карточки не появляются, всё попадает в историю", "", null, delegate { SetQuiet(60); });
            AddCmd("Не беспокоить 3 часа", "", "", null, delegate { SetQuiet(180); });
            AddCmd("Не беспокоить 20 минут", "", "", null, delegate { SetQuiet(20); });
        }

        void FillHistory()
        {
            if (seenNotes.Count == 0) { AddCmd("Уведомлений пока не было", "история хранится до закрытия Kapsula", "", null, null); return; }
            for (int i = seenNotes.Count - 1; i >= 0 && cmdItems.Count < 6; i--)
            {
                Seen s = seenNotes[i];
                string line = s.Title + (s.Text.Length > 0 ? " — " + s.Text : "");
                AddCmd(line.Length > 70 ? line.Substring(0, 69) + "…" : line, s.Source + " · " + s.At.ToString("H:mm"), "", null, delegate { CopyResult(s.Title + (s.Text.Length > 0 ? " — " + s.Text : "")); });
            }
        }

        // ---- сообщение об ошибке: собираем сведения в буфер обмена и открываем страницу, куда их вставить
        public static string Scrub(string text)
        {
            // В журнале встречаются пути с именем пользователя Windows — заменяем их, чтобы имя не утекло.
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home)) text = System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(home), "%USERPROFILE%", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            string user = Environment.UserName;
            if (!string.IsNullOrEmpty(user) && user.Length >= 3) text = System.Text.RegularExpressions.Regex.Replace(text, "(?<![A-Za-zА-Яа-я0-9_])" + System.Text.RegularExpressions.Regex.Escape(user) + "(?![A-Za-zА-Яа-я0-9_])", "<user>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return text;
        }

        public void ReportBug()
        {
            var sb = new StringBuilder();
            sb.AppendLine("**Что случилось:** (опишите здесь)").AppendLine();
            sb.AppendLine("**Сведения**");
            sb.AppendLine("- Kapsula " + Updater.CurrentText);
            sb.AppendLine("- " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? ", 64-bit" : ", 32-bit"));
            sb.AppendLine("- Экран: " + (int)SystemParameters.PrimaryScreenWidth + "×" + (int)SystemParameters.PrimaryScreenHeight + ", масштаб " + Math.Round(dpi * 100) + "%, размер острова " + Math.Round(Scale * 100) + "%");
            try
            {
                string log = System.IO.Path.Combine(Store.Dir, "error.log");
                if (System.IO.File.Exists(log))
                {
                    string[] lines = System.IO.File.ReadAllLines(log, Encoding.UTF8);
                    int from = Math.Max(0, lines.Length - 25);
                    sb.AppendLine().AppendLine("**Последние строки журнала ошибок**").AppendLine("```");
                    for (int i = from; i < lines.Length; i++) sb.AppendLine(lines[i].Length > 300 ? lines[i].Substring(0, 300) : lines[i]);
                    sb.AppendLine("```");
                }
            }
            catch { }
            try { Clipboard.SetText(Scrub(sb.ToString())); } catch { Toast("", "Не удалось скопировать сведения", null, -1, null); return; }
            Toast("", "Сведения скопированы — вставьте их на открывшейся странице", null, -1, accent);
            try { System.Diagnostics.Process.Start("https://github.com/" + Updater.Repo + "/issues/new"); } catch { }
        }

        // Первые двадцать секунд после запуска остров молчит о том, что и так было: микрофон уже занят, Steam уже
        // подключён, наушники уже разряжены. Иначе при каждом запуске и обновлении подряд идут три-четыре надписи.
        readonly int bootAt = Environment.TickCount;
        bool StartingUp() { return unchecked(Environment.TickCount - bootAt) < 20000; }

        // ---- наушники AirPods и Beats: заряд при появлении, предупреждение о низком заряде, пауза, когда сняли
        bool budsSeen, budsWorn, budsPausedByUs;
        int budsCardAt, budsWarned = 101;
        static readonly string BudsGlyph = ((char)0xE7F6).ToString();

        int budsAnnounceUntil;                 // до какого момента ждём заряд, чтобы показать карточку подключения
        bool budsWasConnected, budsConnKnown;

        // Карточка «наушники подключены»: крупная, если есть свои картинки (папка headphones), иначе обычная
        void BudsAnnounce(EarbudsState s)
        {
            if (QuietNow || s.Lowest < 0) return;
            budsCardAt = Environment.TickCount | 1;
            bool overEar = s.Single >= 0 || s.Model.Contains("Max");
            if (overEar && BudsFrames() != null) { ShowBudsCard(s); return; }
            ShowCardEx(null, BudsGlyph, s.Model, s.Text, delegate { }, null, null, false);
            HideCardButton();
            cardUntil = Environment.TickCount + 5000;
        }

        // Вызывается, когда обновился список подключённых Bluetooth-наушников
        void BudsConnection(bool connected, string name, int pct)
        {
            if (!budsConnKnown) { budsConnKnown = true; budsWasConnected = connected; return; }   // при запуске Kapsula карточку не показываем
            if (connected == budsWasConnected) return;
            budsWasConnected = connected;
            if (!connected) { budsAnnounceUntil = 0; return; }
            // Заряд от Windows известен сразу — показываем сразу. Иначе ждём до пятнадцати секунд сигнал самих наушников.
            if (pct >= 0 && Earbuds.Current == null && state == 0)
            {
                string clean = System.Text.RegularExpressions.Regex.Replace(name, "\\s*\\(.*?\\)", "").Trim();
                BudsAnnounce(new EarbudsState { Model = clean.Length > 0 ? clean : "Наушники", Single = pct, At = Environment.TickCount });
            }
            else { budsAnnounceUntil = Environment.TickCount + 15000; if (budsAnnounceUntil == 0) budsAnnounceUntil = 1; }
        }

        void EarbudsTick()
        {
            EarbudsState s = Earbuds.Current;
            if (s == null) { budsSeen = false; return; }
            int now = Environment.TickCount;
            if (!budsSeen) { budsSeen = true; budsWorn = s.InEar; }
            // Наушники только что подключились к компьютеру, и их заряд уже известен — показываем карточку
            if (budsAnnounceUntil != 0)
            {
                if (unchecked(now - budsAnnounceUntil) > 0) budsAnnounceUntil = 0;
                else if (s.Lowest >= 0 && state == 0 && !toastOn) { budsAnnounceUntil = 0; BudsAnnounce(s); }
            }
            int low = s.Lowest;
            if (low > 25) budsWarned = 101;
            else if (low >= 0)
                foreach (int mark in new int[] { 10, 20 })
                    if (low <= mark && budsWarned > mark)
                    {
                        budsWarned = mark;
                        if (StartingUp()) break;   // при запуске не предупреждаем: заряд и так виден в панели
                        Toast(BudsGlyph, s.Model + ": заряд " + low + "%", null, -1, low <= 10 ? RecRed : (Brush)accent);
                        break;
                    }
            // Сняли наушники — ставим музыку на паузу; надели обратно — продолжаем (только если паузу поставили мы)
            if (Store.Get("earbudsPause") == "1" && s.InEar != budsWorn)
            {
                budsWorn = s.InEar;
                if (!s.InEar && media.Has && media.Playing) { watcher.Command(0xB3); budsPausedByUs = true; }
                else if (s.InEar && budsPausedByUs && media.Has && !media.Playing) { watcher.Command(0xB3); budsPausedByUs = false; }
                else if (s.InEar) budsPausedByUs = false;
            }
        }

        // ---- заряд подключённых наушников в раскрытой панели (справа от вкладок)
        Border budsChip;
        TextBlock budsChipText, budsChipGlyph;
        Image budsChipImage;
        string budsChipName = "";
        int budsChipPct = -1, budsChipBusy;

        // Раз в несколько секунд узнаём в фоне, какие Bluetooth-наушники подключены и сколько у них заряда.
        // Заряд берём у Windows, а если она его не знает и это наушники Apple — из их собственного сигнала.
        void BudsChipRefresh()
        {
            if (!Earbuds.Enabled) { budsChipPct = -1; return; }
            if (System.Threading.Interlocked.Exchange(ref budsChipBusy, 1) != 0) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string name = ""; int pct = -1;
                bool any = false; string anyName = "";
                try
                {
                    foreach (BtBattery.Device d in BtBattery.Connected())
                    {
                        if (!any) { any = true; anyName = d.Name; }
                        int p = d.Battery;
                        if (p < 0 && (d.Name.IndexOf("AirPods", StringComparison.OrdinalIgnoreCase) >= 0 || d.Name.IndexOf("Beats", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            EarbudsState s = Earbuds.Current;
                            if (s != null) p = s.Lowest;
                        }
                        if (p >= 0) { name = d.Name; pct = p; break; }
                    }
                }
                catch { }
                finally { budsChipBusy = 0; }
                Dispatcher.BeginInvoke(new Action(delegate { budsChipName = name; budsChipPct = pct; BudsChipRender(); BudsConnection(any, anyName, pct); }));
            });
        }

        void BudsChipRender()
        {
            if (budsChip == null) return;
            int pct = TestChip >= 0 ? TestChip : TestBuds >= 0 ? TestBuds : budsChipPct;
            budsChip.Visibility = pct >= 0 ? Visibility.Visible : Visibility.Collapsed;
            if (pct < 0) return;
            budsChipText.Text = pct + "%";
            budsChipText.Foreground = pct <= 10 ? RecRed : pct <= 20 ? (Brush)Amber : Dim;
            BitmapSource[] own = BudsFrames();
            bool pic = own != null && own.Length > 0;
            if (pic && budsChipImage.Source == null) budsChipImage.Source = own[0];
            budsChipImage.Visibility = pic ? Visibility.Visible : Visibility.Collapsed;
            budsChipGlyph.Visibility = pic ? Visibility.Collapsed : Visibility.Visible;
            budsChip.ToolTip = (TestBuds >= 0 || TestChip >= 0 ? "AirPods Max" : budsChipName) + ": заряд " + pct + "%";
        }

        void FillEarbuds()
        {
            EarbudsState s = Earbuds.Current;
            if (s == null) { AddCmd(Earbuds.Error != null ? "Наушники: " + Earbuds.Error : "Наушников AirPods или Beats рядом не слышно", "наденьте их или откройте футляр", BudsGlyph, null, null); return; }
            AddCmd(s.Model + " — " + s.Text, s.InEar ? "надеты" : "не надеты", BudsGlyph, null, null);
        }

        // ---- батарея ноутбука: предупреждаем на 20% и 10%, пока он не на зарядке
        int batteryWarned = 101;

        void BatteryTick()
        {
            try
            {
                WF.PowerStatus ps = WF.SystemInformation.PowerStatus;
                if ((ps.BatteryChargeStatus & WF.BatteryChargeStatus.NoSystemBattery) != 0 || ps.BatteryLifePercent > 1) return;
                int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
                if (ps.PowerLineStatus != WF.PowerLineStatus.Offline) { batteryWarned = 101; return; }
                foreach (int mark in new int[] { 10, 20 })
                    if (pct <= mark && batteryWarned > mark)
                    {
                        batteryWarned = mark;
                        Toast(((char)0xE850).ToString(), "Батарея " + pct + "% — пора на зарядку", null, -1, pct <= 10 ? RecRed : (Brush)accent);
                        return;
                    }
            }
            catch { }
        }

        // Звук, когда Claude Code ждёт решения: карточку можно не заметить, если вы отвернулись от экрана
        void ClaudeSound()
        {
            if (Store.Get("claudeSound") == "0" || TestClaude != null) return;
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
        }

        // ---- календарь Windows: напоминание за десять минут до события и список по команде «календарь»
        HashSet<string> calAnnounced = new HashSet<string>();

        void CalendarTick()
        {
            if (Store.Get("calendar") == "0") return;
            Calendar.Refresh(false);
            DateTime now = DateTime.Now;
            foreach (CalEvent e in Calendar.Events())
            {
                if (e.AllDay) continue;
                double min = (e.Start - now).TotalMinutes;
                if (min > 10 || min < -1) continue;
                string key = e.Subject + "|" + e.Start.Ticks;
                if (!calAnnounced.Add(key)) continue;
                ShowCardEx(null, ((char)0xE787).ToString(), e.Subject, Calendar.In(e.Start, now) + (e.Location.Length > 0 ? " · " + e.Location : ""), delegate { }, null, null, false);
                HideCardButton();
                cardUntil = Environment.TickCount + 12000;
                return;
            }
        }

        void FillCalendar()
        {
            Calendar.Refresh(false);
            DateTime now = DateTime.Now;
            List<CalEvent> list = Calendar.Events();
            if (list.Count == 0)
            {
                AddCmd(Calendar.Failed ? "Календарь Windows не открылся" : "На ближайшие два дня событий нет", "календарь Windows", ((char)0xE787).ToString(), null, null);
                return;
            }
            foreach (CalEvent e in list)
                AddCmd(e.Subject, Calendar.When(e, now) + (!e.AllDay && e.Start > now && e.Start.Date == now.Date ? " · " + Calendar.In(e.Start, now) : ""), ((char)0xE787).ToString(), null, null);
        }

        // ---- непрочитанное: сообщения из Discord и Steam, пришедшие, пока вы не смотрели в эти программы
        int unreadDiscord, unreadSteam;
        Border unreadBadge;
        TextBlock unreadText;

        void UnreadAdd(bool discord)
        {
            if (Store.Get("unread") == "0") return;
            if (discord) unreadDiscord++; else unreadSteam++;
            if (!toastOn) RenderCompact();
            SetTargets();
        }

        // Открыли Discord или Steam — их счётчик сбрасывается
        void UnreadTick()
        {
            if (unreadDiscord == 0 && unreadSteam == 0) return;
            uint pid;
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            if (pid == 0) return;
            string n;
            try { n = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); } catch { return; }
            bool changed = false;
            if (unreadDiscord > 0 && (n.StartsWith("discord") || n == "vesktop")) { unreadDiscord = 0; changed = true; }
            if (unreadSteam > 0 && n.StartsWith("steam")) { unreadSteam = 0; changed = true; }
            if (changed) { if (!toastOn) RenderCompact(); SetTargets(); }
        }

        // ---- помодоро: 25 минут работы, 5 отдыха; после четырёх кругов — длинный перерыв
        int pomoPhase, pomoRounds;   // 0 — выключено, 1 — работа, 2 — перерыв

        void PomodoroStart()
        {
            pomoPhase = 1; pomoRounds = 0;
            timerRinging = false;
            timerEnd = DateTime.Now.AddMinutes(25);
            RenderCompact(); SetTargets();
            Toast("", "Помодоро: 25 минут работы", null, -1, accent);
        }

        void PomodoroStop()
        {
            pomoPhase = 0;
            timerEnd = DateTime.MinValue; timerRinging = false;
            RenderCompact(); SetTargets();
            Toast("", "Помодоро остановлен", null, -1, null);
        }

        // Таймер дошёл до нуля. true — это был круг помодоро, и следующий отрезок уже запущен.
        bool PomodoroNext()
        {
            if (pomoPhase == 0) return false;
            System.Media.SystemSounds.Exclamation.Play();
            if (pomoPhase == 1)
            {
                pomoRounds++;
                int rest = pomoRounds % 4 == 0 ? 15 : 5;
                pomoPhase = 2;
                timerEnd = DateTime.Now.AddMinutes(rest);
                Toast("", "Перерыв " + rest + " минут · кругов: " + pomoRounds, null, -1, accent);
            }
            else
            {
                pomoPhase = 1;
                timerEnd = DateTime.Now.AddMinutes(25);
                Toast("", "За работу: 25 минут", null, -1, accent);
            }
            RenderCompact(); SetTargets();
            return true;
        }

        static string Ago(int at)
        {
            int sec = Math.Max(0, unchecked(Environment.TickCount - at) / 1000);
            return sec < 60 ? "только что" : (sec / 60) + " мин назад";
        }

        // «claude» в строке ввода: все открытые сессии и что каждая делает; Enter на работающей — остановить её.
        void FillClaudeSessions()
        {
            List<ClaudeBridge.Session> list = ClaudeBridge.Sessions();
            if (list.Count == 0) { AddCmd("Claude Code пока не давал о себе знать", "новые сессии появятся здесь", ClaudeGlyph, null, null); return; }
            foreach (ClaudeBridge.Session s in list)
            {
                ClaudeBridge.Session ses = s;
                bool working = ses.State == "работает" && unchecked(Environment.TickCount - ses.At) < 60000;
                string detail = ses.Detail.Length > 46 ? ses.Detail.Substring(0, 45) + "…" : ses.Detail;
                AddCmd(ses.Name + " — " + ses.State + (detail.Length > 0 ? " · " + detail : ""), working ? "Enter — остановить" : Ago(ses.At), ClaudeGlyph, null,
                    working ? (Action)delegate { ClaudeStop(ses.Id, ses.Name); } : null);
            }
        }


        public bool TestSteam;   // ключ --steamtest
        public string TestDemoCard;   // ключ --democard message|invite|discord: карточка с выдуманными данными для снимков

        // Выдуманная аватарка для демо-карточек: цветной квадрат с буквой.
        static byte[] DemoAvatar(string letter, Color a, Color b)
        {
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(a, b, 45), null, new Rect(0, 0, 96, 96));
                var ft = new FormattedText(letter, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 50, Brushes.White);
                dc.DrawText(ft, new Point((96 - ft.Width) / 2, (96 - ft.Height) / 2));
            }
            var rt = new RenderTargetBitmap(96, 96, 96, 96, PixelFormats.Pbgra32);
            rt.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rt));
            using (var ms = new System.IO.MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
        }

        // ---- подсказки про интеграции: один раз предлагаем то, что можно подключить на этом компьютере
        public bool Plain;        // обычный запуск без проверочных ключей
        public string TestHint;   // ключ --hinttest steam|discord|steamdone

        // Текущее сочетание клавиш для действия (например «Alt+Space»)
        public string HotkeyText(string key)
        {
            foreach (Hot h in Hots) if (h.Key == key) return HotText(h.Mods, h.Vk);
            return "задаётся в настройках";
        }

        public bool TestWelcome;   // ключ --welcome
        public bool TestSlide;     // ключ --slidetest
        public int TestChip = -1;   // ключ --chiptest N: только значок заряда в панели
        public int TestBuds = -1;  // ключ --budstest N: карточка наушников с зарядом N%

        // Первый запуск на этом компьютере — один раз показываем короткое приветствие.
        void StartWelcome()
        {
            if (!(TestWelcome || Plain && Store.Fresh && Store.Get("welcomed") != "1")) return;
            if (!TestWelcome) Store.Set("welcomed", "1");
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            t.Tick += delegate
            {
                t.Stop();
                var w = new WelcomeWindow(this);
                if (TestWelcome) { w.Topmost = true; w.WindowStartupLocation = WindowStartupLocation.Manual; w.Top = 70; w.Left = (SystemParameters.PrimaryScreenWidth - 520) / 2; }
                w.Show();
            };
            t.Start();
        }

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
            if (TestDemoCard == null && Quiet("Steam", title, text)) { if (replyTo != null) UnreadAdd(false); return; }
            // У сообщения друга есть кнопка «Ответить»: ответ набирается прямо на острове.
            if (replyTo != null) ShowCardEx(ImageFrom(icon), "", title, text, open, "Ответить", delegate { StartReply(replyTo, title); }, false);
            else ShowCardEx(ImageFrom(icon), "", title, text, open, null, null, false);
            // Значок Steam — только на сообщениях: там важно, откуда пишут. У достижений и «друг в игре» источник и так понятен.
            if (replyTo != null) SetCardBadge(SourceIcon("steam")); else HideCardButton();
            cardUntil = Environment.TickCount + (replyTo != null ? 8000 : 6000);
            if (replyTo != null && TestDemoCard == null) UnreadAdd(false);
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
            if (TestDemoCard == null && Quiet("Steam", title, text)) return;
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
                if (TestChatLong)
                {
                    AddBubble(true, "А если процент неудобный, например 17%?");
                    AddBubble(false, "Разбейте его на удобные части: 17% = 10% + 5% + 2%.\n\n10% от 2400 — это 240.\n5% — половина от этого, 120.\n2% — пятая часть от 10%, то есть 48.\n\nСкладываем: 240 + 120 + 48 = 408.\n\nТот же приём работает с любым числом: сначала найдите 10% и 1%, а остальное соберите из них сложением.");
                }
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
                    if (TestDemoCard == "message") SteamNotify("Марк", "го вечером в кооп?", DemoAvatar("М", Color.FromRgb(0x4F, 0x8D, 0xF7), Color.FromRgb(0x8A, 0x5C, 0xF6)), delegate { }, "0");
                    else if (TestDemoCard == "invite") SteamInvite("Лена", "Зовёт в игру", DemoAvatar("Л", Color.FromRgb(0xF2, 0x8B, 0x52), Color.FromRgb(0xE8, 0x4A, 0x7A)), "", "");
                    else if (TestDemoCard == "discord") DiscordNotify("Саша", "скинул скрин, глянь", DemoAvatar("С", Color.FromRgb(0x34, 0xC7, 0x8A), Color.FromRgb(0x2A, 0x8F, 0xD8)));
                    else if (TestCard) ShowCard(null,"", "Загружено", "Отчёт за квартал.pdf · 2,4 МБ", null);
                    else { ToggleCommand(); cmdBox.Text = TestCmd; }
                };
                t.Start();
            }
            if (ForceOpen >= 0) { ShowPage(Math.Min(ForceOpen, pages.Length - 1)); SetExpanded(true); }
            else if (ForceOpen == -2) SetState(1);
            else if (ForceOpen == -3) SetTargets();
            StartIntegrationHints();
            StartWelcome();
            if (TestBuds >= 0) ClaudeAfter(2500, delegate { ShowBudsCard(new EarbudsState { Model = "AirPods Max", Single = TestBuds, At = Environment.TickCount }); });
            if (TestSlide) { ClaudeAfter(3000, delegate { ShowPage(PText); }); ClaudeAfter(5200, delegate { ShowPage(PSystem); }); }   // проверочный режим: смена вкладки без нажатий
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
            // Значок у часов — зверёк
            using (var bmp = PetView.Bitmap(32))
            {
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
            var quiet = new WF.ToolStripMenuItem("Не беспокоить 1 час");
            quiet.Click += delegate { SetQuiet(QuietNow ? 0 : 60); };
            menu.Items.Add(quiet);
            menu.Opening += delegate { quiet.Text = QuietNow ? "Выключить «Не беспокоить»" : "Не беспокоить 1 час"; };
            menu.Items.Add("Сообщить об ошибке…", null, delegate { ReportBug(); });
            var stopClaude = new WF.ToolStripMenuItem("Остановить Claude Code");
            stopClaude.Click += delegate { ClaudeStop(null, null); };
            menu.Items.Add(stopClaude);
            menu.Opening += delegate { stopClaude.Visible = ClaudeBridge.Enabled && ClaudeBridge.Listening; stopClaude.Enabled = ClaudeBridge.Busy; };
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
            double sec = (ShowSeconds ? 24 : 0) + (claudeState != 0 && !PetIsCharacter() ? 36 : 0) +(unreadDiscord + unreadSteam > 0 ? 26 : 0);   // место под значок Claude Code и счётчик непрочитанного
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
            switch (state) { case 1: return MH; case 2: return ExpandedH(); case 3: return cardTall ? BudsH : CardH; case 4: return CmdH(); default: return CH; }
        }

        double CmdH() { return 60 + (cmdItems.Count > 0 ? cmdItems.Count * 42 + 10 : 0); }

        double[] specBands;
        bool spikes;   // край рисуется иглами, а не каплями
        int visMin = 30, visMax = 50;   // какие капли сейчас видны (остров бывает узким и широким)

        // Полосы спектра растягиваются на видимую часть края: самая левая капля — бас, самая правая — верха.
        double BandAt(int k)
        {
            double p = Clamp01((double)(k - visMin) / Math.Max(1, visMax - visMin)) * (specBands.Length - 1);
            int i = (int)p;
            if (i >= specBands.Length - 1) return specBands[specBands.Length - 1];
            return specBands[i] + (specBands[i + 1] - specBands[i]) * (p - i);
        }

        // Жидкий край: каждая «капля» тянется вниз в такт реальной громкости звука.
        bool StepLiquid(double dt)
        {
            bool playingNow = media.Has && media.Playing && !hiddenFs;
            // Звенит таймер — остров «ощетинивается»: край встаёт дыбом и дрожит, пока таймер не остановят.
            bool alarm = (timerRinging || TestMood == "alarm") && !hiddenFs;
            bool on = playingNow && LiquidOn || alarm;
            if (alarm) spikes = true; else if (level < 0.05) spikes = false;
            // Край как эквалайзер: слева низкие частоты, справа высокие. Не вышло снять звук — пляшет от общей громкости.
            if (specBands == null) specBands = new double[24];
            bool spec = on && !alarm && Store.Get("liquidSpectrum") != "0" && Spectrum.Read(specBands);
            double peak = alarm ? 1 : (on ? Audio.Peak() : 0);
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
                if (liquidClock >= nextRoll[k]) { noise[k] = rnd.NextDouble(); nextRoll[k] = liquidClock + (alarm ? 0.03 + rnd.NextDouble() * 0.05 : 0.07 + rnd.NextDouble() * 0.13); }
                double tgt = alarm ? (k % 2 == 0 ? 0.4 + 0.6 * noise[k] : 0.25 + 0.45 * noise[k])
                           : spec ? Math.Max(0.06 * level, BandAt(k))
                           : level * (0.15 + 0.85 * noise[k]);
                amp[k] += (tgt - amp[k]) * (1 - Math.Exp(-dt * (alarm ? 60 : spec ? (tgt > amp[k] ? 50 : 14) : (tgt > amp[k] ? 38 : 11))));
                if (amp[k] > 0.004) any = true;
            }
            return on || any;
        }

        static void Wave(StreamGeometryContext c, List<Point> tips, double bl, double br, double h, bool sharp)
        {
            Point prev = new Point(bl, h);
            for (int i = 0; i <= tips.Count; i++)
            {
                Point t = i < tips.Count ? tips[i] : new Point(br, h);
                if (sharp)
                {
                    // «Щетина»: прямые иглы с острыми кончиками вместо округлых капель.
                    if (i > 0 && i < tips.Count) c.LineTo(new Point((prev.X + t.X) / 2, h), true, true);
                    c.LineTo(t, true, true);
                    prev = t;
                    continue;
                }
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

        // ---- «Матовое стекло». Windows умеет размывать фон только прямоугольником — из-под скруглённой панели торчали
        // бы углы. Поэтому Kapsula сама снимает то, что находится за панелью, сильно уменьшает снимок (это и даёт
        // размытие) и рисует его фоном острова по его же форме. Снимок никуда не сохраняется.
        public bool GlassOn;       // настройка «Матовое стекло»
        Path glassLayer;
        ImageBrush glassBrush;
        int glassShotAt, glassHideAt;
        bool glassFailed, glassHide;
        const double GlassPad = 24;

        public void SetGlass(bool on)
        {
            GlassOn = on;
            Store.Set("glass", on ? "1" : "0");
            ApplyEffectiveTheme();   // у подписей на стекле другой контраст
            Wake();
        }

        public string TestGlassDump;   // ключ --glassdump: складывать снимки фона в папку, чтобы проверить, что они живые
        int glassDumpN;

        // Снимок фона обновляется около десяти раз в секунду, пока панель открыта. Вызывается и из отрисовки, и по
        // обычному таймеру: когда панель открыта и неподвижна, отрисовка останавливается, а фон должен жить дальше.
        void GlassTick()
        {
            if (!GlassOn || glassFailed || !glassHide || glassLayer.Visibility != Visibility.Visible) return;
            int now = Environment.TickCount;
            // первый снимок — чуть позже, когда система уже убрала остров из снимков экрана
            bool ready = Capturable || unchecked(now - glassHideAt) > 70;
            if (ready && (glassShotAt == 0 || unchecked(now - glassShotAt) > 100)) GlassShot();
        }

        void GlassShot()
        {
            int now = Environment.TickCount;
            // В проверочном режиме (--capturable) остров виден на снимках и попал бы сюда сам. Тогда фон
            // снимается один раз, в момент открытия, и дальше не обновляется.
            bool selfVisible = Capturable;
            if (selfVisible && glassShotAt != 0) return;
            glassShotAt = now | 1;
            try
            {
                double bx = BaseW / 2 - EW / 2 - GlassPad, bw = EW + 2 * GlassPad, bh = BaseH - 20;
                double k = Scale * dpi;
                int sx = (int)Math.Round((Left + bx * Scale) * dpi), sy0 = (int)Math.Round((Top + sy.X * Scale) * dpi);
                int sw0 = Math.Max(16, (int)Math.Round(bw * k)), sh0 = Math.Max(16, (int)Math.Round(bh * k));
                using (var full = new System.Drawing.Bitmap(sw0, sh0, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                {
                    using (var gfx = System.Drawing.Graphics.FromImage(full)) gfx.CopyFromScreen(sx, sy0, 0, 0, full.Size);
                    // два уменьшения подряд: вчетверо и ещё вчетверо — получается мягкое ровное размытие
                    // чем сильнее уменьшение, тем сильнее размытие: при 1/46 мелкий узор и текст сливаются в ровные пятна
                    int w1 = Math.Max(8, sw0 / 6), h1 = Math.Max(8, sh0 / 6), w2 = Math.Max(4, sw0 / 46), h2 = Math.Max(4, sh0 / 46);
                    using (var mid = new System.Drawing.Bitmap(w1, h1, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                    using (var small = new System.Drawing.Bitmap(w2, h2, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                    {
                        using (var g1 = System.Drawing.Graphics.FromImage(mid)) { g1.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear; g1.DrawImage(full, 0, 0, w1, h1); }
                        using (var g2 = System.Drawing.Graphics.FromImage(small)) { g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear; g2.DrawImage(mid, 0, 0, w2, h2); }
                        if (TestGlassDump != null && glassDumpN < 60) { try { small.Save(System.IO.Path.Combine(TestGlassDump, "g_" + (glassDumpN++).ToString("00") + ".png")); } catch { } }
                        var data = small.LockBits(new System.Drawing.Rectangle(0, 0, w2, h2), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                        try
                        {
                            var src = BitmapSource.Create(w2, h2, 96, 96, PixelFormats.Bgr32, null, data.Scan0, data.Stride * h2, data.Stride);
                            src.Freeze();
                            glassBrush.ImageSource = src;
                        }
                        finally { small.UnlockBits(data); }
                    }
                }
                glassBrush.Viewport = new Rect(bx, 0, bw, bh);
            }
            catch (Exception ex) { glassFailed = true; Store.LogError("матовое стекло", ex); }
        }

        void ApplyFrame()
        {
            double w = Math.Max(60, sw.X), h = Math.Max(20, sh.X);
            double r = Math.Max(4, Math.Min(sr.X, h / 2));
            double cx = BaseW / 2, x0 = cx - w / 2, x1 = cx + w / 2, e = Ear;

            // Капли нижнего края: шаг фиксирован от центра, чтобы при растяжении они не прыгали.
            double bl = x0 + r, br = x1 - r, A = state == 0 ? 13 : 16;
            var tips = new List<Point>();
            int kmin = (int)Math.Ceiling((bl + 8 - cx) / Pitch), kmax = (int)Math.Floor((br - 8 - cx) / Pitch);
            if (kmax > kmin) { visMin = Math.Max(0, kmin + 40); visMax = Math.Min(80, kmax + 40); }
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
                Wave(c, tips, bl, br, h, spikes);
                c.ArcTo(new Point(x1, h - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                c.LineTo(new Point(x1, e), true, false);
                c.ArcTo(new Point(x1 + e, 0), new Size(e, e), 0, false, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            shape.Data = g;
            clipHost.Clip = g;
            slide.Y = sy.X;

            // «Матовое стекло»: раскрытая панель и строка команд становятся полупрозрачными, а фон за ними размывается.
            // Капсула и карточки остаются плотными чёрными.
            double glassAmt = GlassOn && !glassFailed ? Clamp01(Math.Max(so[2].X, so[4].X)) : 0;
            shape.Opacity = IslandOpacity * (1 - 0.26 * glassAmt);   // стекло тёмное: фон просвечивает на четверть, текст остаётся читаемым
            if (glassAmt > 0.03)
            {
                glassLayer.Data = g;   // та же форма, что у острова: скругления и край совпадают точно
                glassLayer.Opacity = glassAmt;
                glassLayer.Visibility = Visibility.Visible;
                if (!glassHide) { glassHide = true; glassHideAt = Environment.TickCount; ApplyCapture(); }
                GlassTick();
            }
            else if (glassLayer.Visibility == Visibility.Visible)
            {
                glassLayer.Visibility = Visibility.Collapsed; glassShotAt = 0;
                glassBrush.ImageSource = null;   // следующий раз начнём с чистого фона, а не со старой картинки
                if (glassHide) { glassHide = false; ApplyCapture(); }
            }

            if (edgeAmt > 0.004)
            {
                var eg = new StreamGeometry();
                using (var c = eg.Open())
                {
                    c.BeginFigure(new Point(x0 + r * 0.3, h - r * 0.3), false, false);
                    c.ArcTo(new Point(bl, h), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                    Wave(c, tips, bl, br, h, spikes);
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
                if (choice != null)
                {
                    // Список вариантов закрыли. Если так ничего и не выбрали — отмена (чуть позже: выбор срабатывает первым).
                    Choice gone = choice;
                    choice = null;
                    cmdHint.Text = CmdHintDefault;
                    Dispatcher.BeginInvoke(new Action(delegate { if (!gone.Finished && gone.Cancel != null) gone.Cancel(); }), DispatcherPriority.Background);
                }
                if (replySteamId != null) { ReplyClosed(replySteamId); replySteamId = null; cmdHint.Text = CmdHintDefault; cmdGlyph.Text = cmdGlyphDefault; cmdGlyph.Foreground = Dim; }
                if (prevFg != IntPtr.Zero) { Native.SetForegroundWindow(prevFg); prevFg = IntPtr.Zero; }
            }
            for (int i = 1; i < layers.Length; i++) layers[i].IsHitTestVisible = s == i;
            if (s == 2)
            {
                BudsChipRender(); BudsChipRefresh();
                Dispatcher.BeginInvoke(new Action(delegate { MoveTabGlow(false); }), DispatcherPriority.Loaded);   // панель открылась — ставим подсветку на место без анимации
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
            if (AutoHide || appHide)
            {
                // Автоскрытие (всегда или только в выбранных программах): остров уезжает за край и возвращается, когда курсор у верхней кромки или есть что показать.
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
                bool inside = Math.Abs(x - cx) <= (CardW + cardExtra) / 2 + 12 && y <= (cardTall ? BudsH : CardH) + 16;
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
            // Точка Claude Code: цветная — работает, оранжевая — ждёт вас, зелёная — только что закончил
            bool plain = !toastOn && !Recorder.Active && steamDlApp == null && dlActive == null;
            claudeDot.Visibility = (claudeState != 0 || PetIsCharacter()) && plain ? Visibility.Visible : Visibility.Collapsed;
            PetMood(claudeState);
            int unread = unreadDiscord + unreadSteam;
            unreadBadge.Visibility = unread > 0 && plain ? Visibility.Visible : Visibility.Collapsed;
            unreadText.Text = unread > 99 ? "99+" : unread.ToString();
            unreadBadge.ToolTip = "Непрочитанные сообщения: Discord — " + unreadDiscord + ", Steam — " + unreadSteam;
            claudeDot.ToolTip = claudeState == 2 ? "Claude Code ждёт вас" : claudeState == 3 ? "Claude Code закончил" : claudeState == 4 ? "Claude Code отдыхает" : "Claude Code работает";
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
            // Зверёк рисуется отдельно (он же показывает, чем занят Claude Code), остальные персонажи — в «глазах»
            eyes.Visibility = Character != "pet" && (ShowEyes || TestCharacter != null) && (!playing || TestEyes) ? Visibility.Visible : Visibility.Collapsed;
            if (TestEyes) cCover.Visibility = Visibility.Collapsed;
            cCover.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
            eq.Visibility = playing && !timing && !timerRinging ? Visibility.Visible : Visibility.Collapsed;

            if (timerRinging) { cText.Text = "Время вышло"; cRight.Visibility = Visibility.Collapsed; return; }
            // Без музыки и таймера: часы по центру, погода — справа, а не одной строкой посередине
            bool weatherRight = !playing && !timing && weather != null && weather.Error == null;
            if (weatherRight) cText.Text = DateTime.Now.ToString(ShowSeconds ? "HH:mm:ss" : "HH:mm"); else SetClock(cText);
            if (playing && PillMode != 0)
            {
                string title = string.IsNullOrEmpty(media.Artist) ? media.Title : media.Title + " — " + media.Artist;
                if (PillMode == 3 && media.Dur > 0 && !timing)
                {
                    // «Полоса трека»: сколько прошло и сколько осталось
                    double pos = media.Now();
                    cText.Visibility = Visibility.Collapsed;
                    cBar.Root.Visibility = Visibility.Visible; cBar.Set(pos / media.Dur);
                    eq.Visibility = Visibility.Collapsed;
                    cRight.Visibility = Visibility.Visible; cRight.Foreground = Dim;
                    cRight.Text = "−" + FmtSec(Math.Max(0, media.Dur - pos));
                    if (state == 1) SetClock(nClock);
                    return;
                }
                // «С текстом»: строка песни, которая звучит сейчас; пока текста нет — название. «С названием»: трек и исполнитель.
                cText.Text = PillMode == 2 && !string.IsNullOrEmpty(currentLyric) ? currentLyric : title;
                cText.MaxWidth = timing ? 190 : 232;
            }
            if (state == 1) SetClock(nClock);

            if (timing) { cRight.Visibility = Visibility.Visible; cRight.Text = Fmt(timerEnd - DateTime.Now); cRight.Foreground = accent; }
            else if (weatherRight)
            {
                cRight.Visibility = Visibility.Visible; cRight.Foreground = Fg;
                cRight.Inlines.Clear();
                cRight.Inlines.Add(new System.Windows.Documents.Run(Weather.Symbol(weather.Code) + " ") { FontFamily = Symbols, Foreground = Amber });
                cRight.Inlines.Add(new System.Windows.Documents.Run(Weather.Temp(weather.Temp)));
            }
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
            // Выбранный режим запоминается: «только плеер» включается сам, как только у плеера находится свой звук.
            bool wantPlayer = Store.Get("volPlayer") == "1";
            if (wantPlayer && !volPlayer && media.Has && AppVolume.Get(PlayerKey()) >= 0) SetVolMode(true);
            else if (!wantPlayer && volPlayer) SetVolMode(false);
            double v = volPlayer ? AppVolume.Get(PlayerKey()) : Audio.GetVolume();
            if (volPlayer && v < 0) { SetVolMode(false); v = Audio.GetVolume(); }   // плеер закрылся — возвращаемся к общей громкости
            if (v >= 0) { volBar.Set(v); volText.Text = Math.Round(v * 100) + "%"; } else volText.Text = "—";
        }

        // ---- ползунок громкости на «Главной»: вся система или только плеер
        bool volPlayer;
        TextBlock volIcon, volLabel;
        Border volChip;
        const double VolChipW = 118;
        string volIconDefault;

        string PlayerKey() { return MediaWatcher.PrettyName(media.Source); }

        void SetVolMode(bool player)
        {
            volPlayer = player;
            if (volIcon == null) return;
            volIcon.Text = player ? "" : volIconDefault;
            volIcon.Foreground = player ? (Brush)accent : Fg;
            if (volLabel != null) volLabel.Text = player ? PlayerKey() : "Весь звук";
            volIcon.ToolTip = player ? "Громкость только плеера. Клик — громкость всей системы" : "Громкость всей системы. Клик — только плеер";
        }

        void ToggleVolMode()
        {
            if (volPlayer) { Store.Set("volPlayer", "0"); SetVolMode(false); RenderHomeLive(); return; }
            if (!media.Has || AppVolume.Get(PlayerKey()) < 0) { Toast("", "Не нашёл звук плеера", null, -1, null); return; }
            Store.Set("volPlayer", "1");
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
            if (quietTile != null) quietTile.SetActive(QuietNow);
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

        // ---- программы, в которых остров прячется (выбирает пользователь в настройках)
        bool appHide;
        public string TestHideApps;   // ключ --hideapps: список на один запуск, настройки не трогает
        uint appHidePid; string appHideName = "";

        public static List<string> HideApps()
        {
            var list = new List<string>();
            foreach (string s in Store.Get("hideApps").Split('|')) if (s.Trim().Length > 0) list.Add(s.Trim());
            return list;
        }

        public static void SetHideApps(List<string> apps) { Store.Set("hideApps", string.Join("|", apps.ToArray())); }

        // Активное окно принадлежит программе из списка?
        bool HideAppInFront()
        {
            string raw = TestHideApps ?? Store.Get("hideApps");
            if (raw.Length == 0) return false;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == hwnd) return appHide && fg == hwnd;   // клик по самому острову ничего не меняет
            uint pid;
            Native.GetWindowThreadProcessId(fg, out pid);
            if (pid == 0) return false;
            if (pid != appHidePid)
            {
                appHidePid = pid;
                try { appHideName = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { appHideName = ""; }
            }
            if (appHideName.Length == 0) return false;
            foreach (string s in raw.Split('|')) if (string.Equals(s.Trim(), appHideName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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
            if (state == 0) PetAnimate();
            GlassTick();
            if ((expanded && page == 0 || state == 1) && pollN % 6 == 0) RenderHomeLive();
            if ((expanded && page == 0 || state == 1 || state == 0 && PillMode == 2) && pollN % 2 == 0) UpdateLyrics();
            if (state == 0 && PillMode == 3 && pollN % 15 == 0 && media.Has && media.Playing && !toastOn) RenderCompact();   // полоса трека в капсуле
            if (expanded && page == PWeather && weatherAnimAt != 0) AnimateWeather();
            if (expanded && page == PNote && pollN % 4 == 0) UpdateRing();
        }

        void OnTick()
        {
            tick++;
            bool fs = FullscreenAppInFront();
            if (fs != hiddenFs) { hiddenFs = fs; SetTargets(); }
            if (tick % 20 == 3 && state == 0 && !toastOn) CalendarTick();
            if (tick % 60 == 7) BatteryTick();
            if (tick % 4 == 2) EarbudsTick();
            if (tick % 4 == 1) BudsChipRefresh();   // раз в две секунды: так подключение наушников замечается почти сразу
            UnreadTick();
            int cs = ClaudeStateNow();
            if (cs != claudeState) { claudeState = cs; if (!toastOn) RenderCompact(); SetTargets(); }
            if (expanded && page == PClaude) { RenderClaudePage(); if (tick % 30 == 0) ClaudeUsage.Refresh(); }
            if (claudeQueue.Count > 0) ClaudePump();
            if (state != 3 && claudeNow != null && claudeNow.Answered) claudeNow = null;
            bool ah = HideAppInFront();
            if (ah != appHide) { appHide = ah; if (!ah) revealUntil = Environment.TickCount + 600; }
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
                if (mic != micInUse) { micInUse = mic; if (mic && !StartingUp()) Toast("", "Микрофон используется", null, -1, new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A))); }
            }

            if (timerEnd != DateTime.MinValue && timerEnd <= DateTime.Now && PomodoroNext()) { }
            else if (timerEnd != DateTime.MinValue && timerEnd <= DateTime.Now)
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
