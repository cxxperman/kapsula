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
    // Kapsula — украинский проект. Флаг рисуется в коде: две полосы, в большой карточке — с мягкой волной, как на ветру
    static class Ua
    {
        public const string Made = "Зроблено в Україні";
        static readonly Color Blue = Color.FromRgb(0x00, 0x5B, 0xBB), Yellow = Color.FromRgb(0xFF, 0xD5, 0x00);

        public static FrameworkElement Flag(double w, double h)
        {
            var g = new Grid { Width = w, Height = h, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Kapsula — украинский проект" };
            g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());
            var top = new Border { Background = new SolidColorBrush(Blue), CornerRadius = new CornerRadius(h * 0.18, h * 0.18, 0, 0) };
            var bottom = new Border { Background = new SolidColorBrush(Yellow), CornerRadius = new CornerRadius(0, 0, h * 0.18, h * 0.18) };
            Grid.SetRow(bottom, 1);
            g.Children.Add(top); g.Children.Add(bottom);
            return g;
        }

        // Плашка: маленький флаг и подпись, рамка переливается из синего в жёлтый
        public static FrameworkElement Pill(double size)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Flag(size * 1.45, size));
            sp.Children.Add(new TextBlock { Text = Made, Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xEA)), FontSize = size * 1.15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 1), VerticalAlignment = VerticalAlignment.Center });
            return new Border
            {
                Child = sp, CornerRadius = new CornerRadius(size + 4), Padding = new Thickness(size * 0.8, size * 0.35, size, size * 0.4), HorizontalAlignment = HorizontalAlignment.Left,
                Background = new LinearGradientBrush(Color.FromArgb(0x30, Blue.R, Blue.G, Blue.B), Color.FromArgb(0x24, Yellow.R, Yellow.G, Yellow.B), 0),
                BorderBrush = new LinearGradientBrush(Color.FromArgb(0xA0, Blue.R, Blue.G, Blue.B), Color.FromArgb(0xA0, Yellow.R, Yellow.G, Yellow.B), 0), BorderThickness = new Thickness(1),
                ToolTip = "Kapsula — украинский проект"
            };
        }

        // Флаг на ветру: полосы изгибаются бегущей волной, по краю — лёгкая тень складок. Анимация идёт, только пока флаг виден.
        public static FrameworkElement WavyFlag(double w, double h)
        {
            var c = new Canvas { Width = w, Height = h + 6, ClipToBounds = false };
            var top = new Path { Fill = new SolidColorBrush(Blue) };
            var bottom = new Path { Fill = new SolidColorBrush(Yellow) };
            var shade = new Path { Fill = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0x38, 0, 0, 0), 0.5), new GradientStop(Color.FromArgb(0, 0, 0, 0), 1) }, 0) { MappingMode = BrushMappingMode.RelativeToBoundingBox }, Opacity = 0.9 };
            c.Children.Add(top); c.Children.Add(bottom); c.Children.Add(shade);
            double phase = 0;
            Func<double, double, double> Y = delegate(double x, double y0)
            {
                double k = x / w;   // у древка флаг почти неподвижен, к краю волна сильнее
                return y0 + Math.Sin(phase + k * 5.2) * 2.6 * k + 3;
            };
            Action draw = delegate
            {
                int n = 24;
                Func<double, double, Geometry> band = delegate(double y0, double y1)
                {
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(0, Y(0, y0)), true, true);
                        for (int i = 1; i <= n; i++) { double x = w * i / n; ctx.LineTo(new Point(x, Y(x, y0)), true, true); }
                        for (int i = n; i >= 0; i--) { double x = w * i / n; ctx.LineTo(new Point(x, Y(x, y1)), true, true); }
                    }
                    g.Freeze();
                    return g;
                };
                top.Data = band(0, h / 2 + 0.4);
                bottom.Data = band(h / 2, h);
                // складки: тень там, где волна уходит вниз
                var sg = new StreamGeometry();
                using (var ctx = sg.Open())
                {
                    ctx.BeginFigure(new Point(0, Y(0, 0)), true, true);
                    for (int i = 1; i <= n; i++) { double x = w * i / n; ctx.LineTo(new Point(x, Y(x, 0)), true, true); }
                    for (int i = n; i >= 0; i--) { double x = w * i / n; ctx.LineTo(new Point(x, Y(x, h)), true, true); }
                }
                sg.Freeze();
                shade.Data = sg;
                double pos = (Math.Sin(phase) + 1) / 2;
                ((LinearGradientBrush)shade.Fill).GradientStops[1].Offset = 0.25 + pos * 0.5;
            };
            draw();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += delegate { phase += 0.11; draw(); };
            c.Loaded += delegate { timer.Start(); };
            c.Unloaded += delegate { timer.Stop(); };
            c.IsVisibleChanged += delegate { if (c.IsVisible) timer.Start(); else timer.Stop(); };
            return c;
        }

        // Карточка «Украинский проект» для раздела «Общее»
        public static Border Banner(FrameworkElement button)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var flag = WavyFlag(66, 44);
            flag.Margin = new Thickness(2, 2, 20, 0); flag.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(flag);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = "Український проєкт", Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.Bold });
            texts.Children.Add(new TextBlock { Text = "Kapsula делают в Украине. Программа бесплатная, исходный код открыт — его можно посмотреть и проверить.", Foreground = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF)), FontSize = 12, LineHeight = 17, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) });
            button.HorizontalAlignment = HorizontalAlignment.Left;
            texts.Children.Add(button);
            Grid.SetColumn(texts, 1);
            g.Children.Add(texts);
            var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x0B, 0x2E, 0x63), 0));
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x14, 0x22, 0x3D), 0.55));
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x33, 0x12), 1));
            return new Border
            {
                Child = g, Background = bg, CornerRadius = new CornerRadius(18), Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 0, 0, 16),
                BorderBrush = new LinearGradientBrush(Color.FromArgb(0x70, Blue.R, Blue.G, Blue.B), Color.FromArgb(0x70, Yellow.R, Yellow.G, Yellow.B), 0), BorderThickness = new Thickness(1)
            };
        }
    }
    class WelcomeWindow : Window
    {
        static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x18));
        static readonly Brush Sel = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));          // выбранный раздел — спокойная серая подложка
        static readonly Brush NavHover = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
        static readonly Brush PaneBg = new SolidColorBrush(Color.FromArgb(0xD8, 0x15, 0x15, 0x18));       // правая часть поверх полупрозрачного окна

        // ---- полупрозрачное окно (материал Mica в Windows 11): боковая панель просвечивает, как в современных системных окнах
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);
        struct Margins { public int L, R, T, B; }
        static readonly bool MicaOk = false;   // полупрозрачный материал Windows отключён: на некоторых системах окно рисовалось белым; фон — сплошной тёмный
        static int WinBuild()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                { int b; return int.TryParse(k.GetValue("CurrentBuildNumber") as string, out b) ? b : 0; }
            }
            catch { return 0; }
        }

        void ApplyMaterial()
        {
            try
            {
                IntPtr h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                // окно настроек простое и почти неподвижное — рисуем его процессором: так оно не остаётся белым после сбоя видеодрайвера
                var hs = System.Windows.Interop.HwndSource.FromHwnd(h);
                if (hs != null && hs.CompositionTarget != null) hs.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                int dark = 1;
                DwmSetWindowAttribute(h, 20, ref dark, 4);   // тёмный заголовок окна
                if (!MicaOk) return;
                var src = System.Windows.Interop.HwndSource.FromHwnd(h);
                src.CompositionTarget.BackgroundColor = Colors.Transparent;
                var m = new Margins { L = -1, R = -1, T = -1, B = -1 };
                DwmExtendFrameIntoClientArea(h, ref m);
                int mica = 2;
                if (DwmSetWindowAttribute(h, 38, ref mica, 4) != 0) Background = Bg;
            }
            catch { Background = Bg; }
        }
        static readonly Brush RowBg = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x23));
        static readonly Brush Txt = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6));
        static readonly Brush Sub = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96));
        static readonly Brush ChipBg = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x31));

        public WelcomeWindow(IslandWindow island)
        {
            Title = "Kapsula";
            Icon = PetView.WindowIcon();
            Width = 520; SizeToContent = SizeToContent.Height;
            Background = MicaOk ? Brushes.Transparent : Bg;
            SourceInitialized += delegate { ApplyMaterial(); };
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            UseLayoutRounding = true;

            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            var hello = new StackPanel { Orientation = Orientation.Horizontal };
            var greeter = new PetView(3.2, Bg);   // зверёк встречает нового человека
            greeter.Mood("hello");
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
            // у каждой подсказки — белый значок на цветном скруглённом квадрате, как у разделов настроек
            Color[] tipTints = { Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x8E, 0x8E, 0x93), Color.FromRgb(0xFF, 0x37, 0x5F), Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0x63, 0x66, 0xF1) };
            int tipN = 0;
            foreach (string[] t in tips)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Color tint = tipTints[tipN++ % tipTints.Length];
                row.Children.Add(new Border
                {
                    Width = 32, Height = 32, CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                    Background = new LinearGradientBrush(Color.FromRgb((byte)Math.Min(255, tint.R + 22), (byte)Math.Min(255, tint.G + 22), (byte)Math.Min(255, tint.B + 22)), tint, 90),
                    Child = new TextBlock { Text = ((char)Convert.ToInt32(t[0], 16)).ToString(), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                });
                var texts = new StackPanel();
                texts.Children.Add(new TextBlock { Text = t[1], Foreground = Txt, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
                texts.Children.Add(new TextBlock { Text = t[2], Foreground = Sub, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
                Grid.SetColumn(texts, 1); row.Children.Add(texts);
                sp.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x1F, 0x1F, 0x23)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 11, 14, 12), Margin = new Thickness(0, 0, 0, 8), Child = row });
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
            var b = new Border { Background = main ? Sel : ChipBg, CornerRadius = new CornerRadius(15), Padding = new Thickness(18, 7, 18, 8), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, Foreground = main ? Brushes.White : Txt, FontSize = 13, FontWeight = FontWeights.SemiBold } };
            Fx.Press(b, 0.95);
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }
    }

    // ============================================================= Настройки
    class SettingsWindow : Window
    {
        IslandWindow island;
        static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x18));
        static readonly Brush Sel = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));          // выбранный раздел — спокойная серая подложка
        static readonly Brush NavHover = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
        static readonly Brush PaneBg = new SolidColorBrush(Color.FromArgb(0xD8, 0x15, 0x15, 0x18));       // правая часть поверх полупрозрачного окна

        // ---- полупрозрачное окно (материал Mica в Windows 11): боковая панель просвечивает, как в современных системных окнах
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);
        struct Margins { public int L, R, T, B; }
        static readonly bool MicaOk = false;   // полупрозрачный материал Windows отключён: на некоторых системах окно рисовалось белым; фон — сплошной тёмный
        static int WinBuild()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                { int b; return int.TryParse(k.GetValue("CurrentBuildNumber") as string, out b) ? b : 0; }
            }
            catch { return 0; }
        }

        void ApplyMaterial()
        {
            try
            {
                IntPtr h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                // окно настроек простое и почти неподвижное — рисуем его процессором: так оно не остаётся белым после сбоя видеодрайвера
                var hs = System.Windows.Interop.HwndSource.FromHwnd(h);
                if (hs != null && hs.CompositionTarget != null) hs.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                int dark = 1;
                DwmSetWindowAttribute(h, 20, ref dark, 4);   // тёмный заголовок окна
                if (!MicaOk) return;
                var src = System.Windows.Interop.HwndSource.FromHwnd(h);
                src.CompositionTarget.BackgroundColor = Colors.Transparent;
                var m = new Margins { L = -1, R = -1, T = -1, B = -1 };
                DwmExtendFrameIntoClientArea(h, ref m);
                int mica = 2;
                if (DwmSetWindowAttribute(h, 38, ref mica, 4) != 0) Background = Bg;
            }
            catch { Background = Bg; }
        }
        static readonly Brush RowBg = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x23));
        static readonly Brush Txt = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6));
        static readonly Brush Sub = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96));
        static readonly Brush ChipBg = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x31));
        static readonly Brush OnBg = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5C));    // включённые плашки и главные кнопки — светлее фона, без цвета
        static readonly Brush OnTxt = Brushes.White;
        static readonly Brush SegTrack = new SolidColorBrush(Color.FromArgb(0x66, 0x0A, 0x0A, 0x0C));   // дорожка переключателя вариантов
        static readonly Brush SegThumb = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x52));          // «бегунок» под выбранным вариантом
        static readonly Brush SegLine = new SolidColorBrush(Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF));
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
            Background = MicaOk ? Brushes.Transparent : Bg;
            SourceInitialized += delegate { ApplyMaterial(); };
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
            {
                // свой стикер: выбор картинки и сброс
                var stRow = new StackPanel { Orientation = Orientation.Horizontal };
                var stNote = new TextBlock { Foreground = Sub, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
                var stCol = new StackPanel(); stCol.Children.Add(stRow); stCol.Children.Add(stNote);
                Action stPaint = null;
                stPaint = delegate
                {
                    stRow.Children.Clear();
                    string cur = IslandWindow.StickerPath;
                    if (cur != null)
                    {
                        var img = new Image { Height = 30, MaxWidth = 60, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                        try { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(cur); bi.EndInit(); img.Source = bi; } catch { }
                        stRow.Children.Add(img);
                    }
                    stRow.Children.Add(PillBtn(cur == null ? "Выбрать картинку…" : "Другая картинка…", cur == null, delegate
                    {
                        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Картинка для капсулы", Filter = "Картинки (PNG, GIF, WEBP, JPG)|*.png;*.gif;*.webp;*.jpg;*.jpeg" };
                        if (dlg.ShowDialog(this) != true) return;
                        bool ok = island.SetSticker(dlg.FileName);
                        stNote.Text = ok ? "Готово — стикер уже в капсуле." : "Эту картинку открыть не получилось. Попробуйте PNG или GIF.";
                        stPaint();
                    }));
                    if (cur != null)
                    {
                        var rm = PillBtn("Вернуть зверька", false, delegate { island.SetSticker(null); stNote.Text = "Зверёк вернулся."; stPaint(); });
                        rm.Margin = new Thickness(8, 0, 0, 0); stRow.Children.Add(rm);
                    }
                };
                stPaint();
                stNote.Text = "PNG, GIF (с анимацией) или WEBP. Стикер из Telegram: сохраните его картинкой — анимированные стикеры Telegram в форматах TGS и WEBM не подойдут, GIF — подойдёт. Пока Claude Code чем-то занят или идёт загрузка, вместо стикера ненадолго появляется зверёк — он это показывает.";
                Sec("Внешний вид").Children.Add(Row("Свой стикер вместо зверька", "Поставьте в капсулу любую картинку вместо зверька — например, любимый стикер.", stCol, true));
            }
            var banner = Ua.Banner(LinkButton("Исходный код на GitHub", "https://github.com/cxxperman/kapsula"));
            sp.Children.Add(banner);
            rowsByTitle["Украинский проект"] = banner;
            rowText[banner] = "украинский проект украина сделано в украине github исходный код";

            sp = Sec("Внешний вид");
            sp.Children.Add(Row("Цвет острова", "Наведите на цвет — остров сверху сразу покажет его. Клик сохраняет.", BuildSwatches(), true));
            sp.Children.Add(Row("Тема как в Windows", "Светлый остров при светлой теме Windows, тёмный — при тёмной. Выбранный выше цвет при этом используется для тёмной темы.", Toggle(island.FollowWindows, delegate(bool v) { island.SetFlag("followWin", v); }), false));
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            sp.Children.Add(Row("Размер", "Потяните — остров меняется сразу. Для больших мониторов удобнее покрупнее.", SliderCtl(0.8, 2, 0.05, island.Scale,
                delegate(double v) { return Math.Round(v * 100) + "%"; },
                delegate(double v) { island.PreviewScale(v.ToString("0.##", inv)); },
                delegate(double v) { island.ApplyScale(v); }), true));
            sp.Children.Add(Row("Шрифт острова", "«Как в Windows» — системный. Остальные готовые есть в каждой Windows; «Другой…» — любой установленный у вас. Остров меняется сразу; буквы, которых в шрифте нет, подставит системный.", Chips(IslandWindow.FontNames, island.FontChoice(), delegate(int i)
            {
                if (i < IslandWindow.FontNames.Length - 1) { island.ApplyFont(island.FontFace(i), true); return; }
                using (var dlg = new System.Windows.Forms.FontDialog { ShowEffects = false, ShowColor = false, FontMustExist = true, AllowVerticalFonts = false, AllowScriptChange = false })
                {
                    try { string cur = island.IslandFont; if (cur.Length > 0) dlg.Font = new System.Drawing.Font(cur.Split(',')[0].Trim(), 11); } catch { }
                    var dlgOwner = new DialogOwner { Handle = new System.Windows.Interop.WindowInteropHelper(this).Handle };
                    if (dlg.ShowDialog(dlgOwner) == System.Windows.Forms.DialogResult.OK) island.ApplyFont(dlg.Font.FontFamily.Name, true);
                }
            }), true));
            sp.Children.Add(Row("Ширина капсулы", "Растяните капсулу вширь — например, под вырез вашего монитора или просто для вида. Остров меняется сразу.", SliderCtl(0, 240, 4, island.PillExtraW,
                delegate(double v) { return v < 1 ? "как есть" : "+" + Math.Round(v); },
                delegate(double v) { island.SetPillSize(v, island.PillH, false); },
                delegate(double v) { island.SetPillSize(v, island.PillH, true); }), true));
            sp.Children.Add(Row("Высота капсулы", "Капсула повыше — заметнее и удобнее попадать курсором.", SliderCtl(34, 60, 1, island.PillH,
                delegate(double v) { return Math.Round(v) <= 34 ? "как есть" : "+" + Math.Round(v - 34); },
                delegate(double v) { island.SetPillSize(island.PillExtraW, v, false); },
                delegate(double v) { island.SetPillSize(island.PillExtraW, v, true); }), true));
            sp.Children.Add(Row("Светлая кромка", "Тонкий блик по краю острова — он не сливается с тёмными обоями и окнами.", Toggle(island.RimOn, delegate(bool v) { island.SetFlag("rim", v); }), false));
            sp.Children.Add(Row("Тень под островом", "Мягкая тень: на светлых окнах остров словно парит над ними.", Toggle(island.ShadowOn, delegate(bool v) { island.SetFlag("shadow", v); }), false));
            sp.Children.Add(Row("Характер анимаций", "Как остров раскрывается и сворачивается. Выберите — остров сразу покажет. «Капля» сначала стекает вниз, потом расплывается вширь. «Меньше движения» — без отскоков и прыжков; включается само, если в Windows выключены анимации.", Chips(IslandWindow.MotionNames, island.MotionStyle, delegate(int i) { island.SetMotion(i); }), true));
            sp.Children.Add(Row("Зверёк в переходах", "Зверёк сам переходит туда, куда вы смотрите: выпрыгивает из капсулы на край панели и сидит, свесив лапки, перескакивает за вкладками, занимает место зверьков погоды, таймера и пустых списков, приносит уведомления — садится на край карточки и отдаёт вам письмо, — качается на жидком крае под музыку. Пока вкладку перестраивают, надевает каску и следит за стройкой. Щёлкните по нему — подпрыгнет.", Toggle(island.RideOn, delegate(bool v) { island.SetFlag("petRide", v); }), false));
            sp.Children.Add(Row("Зверёк гуляет по окнам", "Когда остров свёрнут и ничего не происходит, зверёк иногда выпрыгивает из капсулы и садится на верхний край окна, в котором вы работаете: сидит, свесив лапки, прохаживается, засыпает, если вы отошли. Окно двигают — едет с ним. Что-то случилось — возвращается. Сквозь него можно щёлкать. На развёрнутых окнах, в играх и на весь экран не гуляет. Наведите на эту строку — сядет на окно настроек.", Toggle(island.WalkOn, delegate(bool v) { island.SetFlag("walk", v); }), false));
            sp.Children.Add(Row("Блоки по очереди", "Когда открывается панель или вкладка, блоки проявляются один за другим, волной сверху вниз.", Toggle(island.CascadeOn, delegate(bool v) { island.SetFlag("cascade", v); }), false));
            sp.Children.Add(Row("Матовое стекло · бета", "Раскрытая панель и строка команд становятся полупрозрачными, а то, что за ними, размывается. Капсула и карточки остаются чёрными. Функция пробная: на некоторых компьютерах фон может обновляться с задержкой или нагружать систему — тогда выключите.", Toggle(island.GlassOn, delegate(bool v) { island.SetGlass(v); }), false));
            Sec("Поведение").Children.Add(Row("Остров можно отцепить", "Потяните остров за пустое место — он отцепится от верхнего края и останется там, где отпустите; у краёв экрана прилипает. Поднесите к верхнему краю — встанет на место. Если панели не хватает места, остров сам приподнимается, пока она открыта.", Toggle(island.AllowDetach, delegate(bool v) { island.SetFlag("detach", v); }), false));
            Sec("Поведение").Children.Add(Row("Вернуть остров наверх", "Если отцепленный остров мешает или потерялся.", PillBtn("Вернуть наверх", false, delegate { island.Attach(true); }), false));
            Sec("Поведение").Children.Add(Row("Автоскрытие", "Остров уезжает за край и возвращается, когда курсор у верхней кромки экрана или есть что показать.", Toggle(island.AutoHide, delegate(bool v) { island.SetFlag("autoHide", v); }), false));
            Sec("Поведение").Children.Add(Row("Показывать в играх", "Остров остаётся на месте поверх игр и видео на весь экран, а не прячется за край. Работает в оконном режиме без рамки; в «эксклюзивном» полноэкранном режиме игра сама закрывает собой все окна. Переключается и из меню значка у часов.", Toggle(Store.Get("showInGames") == "1", delegate(bool v) { island.SetFlag("showInGames", v); }), false));
            Sec("Поведение").Children.Add(Row("Прятаться в программах", "Если остров закрывает вкладки или кнопки какой-то программы, добавьте её сюда: пока она активна, остров уезжает за край и возвращается, когда курсор у верхней кромки или есть что показать. В играх и видео на весь экран он прячется и так — если выше не включено «Показывать в играх».", HideAppsEditor(), true));
            sp.Children.Add(Row("Зверёк в капсуле", "Следит за курсором, задумывается, пока отвечает нейросеть, пугается таймера, радуется удаче и показывает, чем занят Claude Code. Если выключить, останутся только часы и погода.", Toggle(island.ShowEyes, delegate(bool v) { island.SetFlag("eyes", v); }), false));
            sp.Children.Add(Row("Сезонные наряды зверька", "Колпак под Новый год, шляпа в конце октября, шарф зимой.", Toggle(Store.Get("petSeason") != "0", delegate(bool v) { island.SetFlag("petSeason", v); }), false));
            sp.Children.Add(Row("Наряд зверька", "Обновки за привычки: повязка — за 3 перерыва после долгой работы, бабочка — за 10 выполненных задач на «Сегодня», наушники — за 10 часов музыки, корона — за 10 кругов помодоро. Новый наряд приносит карточка. «Сам» — носит последнюю обновку, а пока их нет — сезонный наряд.", OutfitPicker(), true));
            if (island.MonitorCount > 1)
            {
                var names = new string[island.MonitorCount];
                for (int i = 0; i < names.Length; i++) names[i] = "Монитор " + (i + 1);
                Sec("Поведение").Children.Add(Row("Монитор", "На каком экране живёт остров.", Chips(names, Math.Max(0, Math.Min(names.Length - 1, island.MonitorIndex)), delegate(int i) { island.SetMonitor(i); }), true));
            }

            // Конструктор вкладок: каждая вкладка острова — сетка блоков, которые перетаскиваются прямо на острове
            sp = Sec("Вкладки острова");
            sp.Children.Add(Header("Как это работает"));
            sp.Children.Add(Row("Карандаш в углу панели", "На вкладке нажмите карандаш (справа вверху, рядом с булавкой) — блоки начнут покачиваться. Тащите любой блок мышью: остальные расступятся заранее, а место, куда он встанет, подсветится зелёным (красным — если не помещается). «−» прячет блок, спрятанные возвращаются кнопками снизу, «Как было» — исходная раскладка, Esc или «Готово» — закончить.", new TextBlock(), false));
            sp.Children.Add(Header("Вкладки"));
            foreach (string[] tab in new[] {
                new[] { "home", "Главная", "Плеер, шесть плиток, громкость и ярлыки." },
                new[] { "clip", "Буфер", "Поиск, фильтры, история и нижняя строка." },
                new[] { "shelf", "Полка", "Кнопки полки и файлы." },
                new[] { "text", "Текст", "Текст из буфера, счётчик и кнопки преобразований." },
                new[] { "note", "Заметка", "Папки, заметка и таймер." },
                new[] { "weather", "Погода", "Небо, прогноз и шесть приборов." },
                new[] { "system", "Система", "Двенадцать плиток и нагрузка." },
                new[] { "claude", "Claude Code", "Статистика, сессии, папка и новая задача." },
                new[] { "today", "Сегодня", "Месяц и день." },
                new[] { "notif", "Уведомления", "Список и нижняя строка." } })
            {
                string id = tab[0];
                var btns = new StackPanel { Orientation = Orientation.Horizontal };
                btns.Children.Add(PillBtn("Настроить", true, delegate { island.OpenEditor(id); }));
                var rs = PillBtn("Как было", false, delegate { island.ResetBoard(id); });
                rs.Margin = new Thickness(8, 0, 0, 0); btns.Children.Add(rs);
                sp.Children.Add(Row(tab[1], tab[2], btns, false));
            }
            sp.Children.Add(Header("Главная"));
            sp.Children.Add(Row("Недавно скопированное вместо плеера", "Когда музыка не играет, в блоке плеера — три последние записи из буфера.", Toggle(Store.Get("homeClips") != "0", delegate(bool v) { Store.Set("homeClips", v ? "1" : "0"); island.ApplyHomeLayout(); }), false));
            sp = Sec("Музыка");
            sp.Children.Add(Row("Капсула во время музыки", "Что показывать в свёрнутом виде. «Строка песни» работает, когда для трека нашёлся текст; иначе показывается название.", Chips(new string[] { "Часы и погода", "Название", "Строка песни", "Полоса трека" }, island.PillMode, delegate(int i) { island.SetPillMode(i); }), true));
            sp.Children.Add(Row("Плеер на «Главной»", "«Компактный» — обложка слева. «Крупная обложка» — обложка по центру, название под ней. «В цвет обложки» — весь плеер залит её цветом. «Мягкое зеркало» — обложка по центру, а за ней она же, сильно размытая.", Chips(new string[] { "Компактный", "Крупная обложка", "В цвет обложки", "Мягкое зеркало" }, island.PlayerStyle, delegate(int i) { island.SetPlayerStyle(i); }), true));
            sp.Children.Add(Row("Мини-плеер при наведении", "Во время музыки сначала открывается мини-плеер, клик по нему — вся панель.", Toggle(island.HoverMini, delegate(bool v) { island.SetFlag("hoverMini", v); }), false));
            sp.Children.Add(Row("Мини-плеер: только текст песни", "Когда для трека нашёлся текст, мини-плеер показывает только его — крупно, во всю ширину, с маленькой обложкой и полосой времени. Переключается и кнопкой в самом мини-плеере.", Toggle(island.MiniLyricsOnly, delegate(bool v) { island.SetMiniLyricsOnly(v); }), false));
            sp.Children.Add(Row("Новый трек поверх игр и видео", "Когда остров спрятан от игры или видео на весь экран, он выглядывает на пару секунд с названием нового трека.", Toggle(Store.Get("fsTrack") != "0", delegate(bool v) { island.SetFlag("fsTrack", v); }), false));
            sp.Children.Add(Header("Жидкий край"));
            sp.Children.Add(Row("Жидкий край", "Нижний край пляшет под звук.", Toggle(island.LiquidOn, delegate(bool v) { island.SetFlag("liquid", v); }), false));
            sp.Children.Add(Row("Край как эквалайзер", "Слева бас, справа высокие частоты. Выключено — край пляшет от общей громкости.", Toggle(Store.Get("liquidSpectrum") != "0", delegate(bool v) { Store.Set("liquidSpectrum", v ? "1" : "0"); }), false));
            sp.Children.Add(Header("Тексты песен"));
            sp.Children.Add(Row("Тексты песен", "Загружаются с lrclib.net для играющего трека.", Toggle(island.LyricsEnabled, delegate(bool v) { island.SetFlag("lyrics", v); }), false));

            double[] offs = { -1, -0.5, 0, 0.5, 1 };
            int oi = 2;
            for (int i = 0; i < offs.Length; i++) if (Math.Abs(offs[i] - island.LyricOffset) < 0.01) oi = i;
            sp.Children.Add(Row("Сдвиг текста песни", "Если строки отстают от звука — сдвиньте вперёд, если спешат — назад.", Chips(new string[] { "−1 с", "−0,5 с", "Без сдвига", "+0,5 с", "+1 с" }, oi, delegate(int i) { island.SetLyricOffset(offs[i]); }), true));
            sp.Children.Add(Header("Наушники"));
            sp.Children.Add(Header("Cider"));
            sp.Children.Add(Row("«В избранное» и «Не интересует»", "Когда играет Cider, по бокам кнопок плеера появляются звезда (в избранное Apple Music) и перечёркнутый круг (предлагать меньше такого и дальше). У других плееров Windows не даёт программам ставить оценки, поэтому там этих кнопок нет.", Toggle(Store.Get("ciderRate") != "0", delegate(bool v) { Store.Set("ciderRate", v ? "1" : "0"); }), false));
            sp.Children.Add(Row("Ключ Cider", "Cider пускает программы только по ключу. В Cider откройте настройки, раздел подключений (Connectivity), управление доступом внешних приложений — создайте ключ и вставьте сюда. Ключ хранится только на этом компьютере, зашифрованный Windows, и уходит только самому Cider на этом же компьютере.", BuildSecretEditor("ciderKey", "CIDER_TOKEN"), true));

            sp.Children.Add(Row("Оценки в других плеерах · альфа", "Сырая функция, по умолчанию выключена. Звезда и «Не интересует» для Spotify, Яндекс Музыки, YouTube Music, SoundCloud и других — в браузере и в приложениях, без расширений и ключей. Kapsula находит кнопки «Нравится» и «Не нравится» в окне плеера через специальные возможности Windows и нажимает их, не трогая мышь. Вкладка с плеером должна быть выбранной в своём окне (окно может быть свёрнуто). Кнопки появляются, только если нашлись в плеере.", Toggle(Store.Get("uiaRate") == "1", delegate(bool v) { Store.Set("uiaRate", v ? "1" : "0"); }), false));

            sp = Sec("AirPods и Beats");
            sp.Children.Add(Row("Заряд AirPods и Beats", "Когда наушники появляются рядом, остров показывает их заряд и предупреждает на 20% и 10%. Наушники сами сообщают заряд по Bluetooth — Kapsula только слушает. Команда «наушники» в строке острова покажет заряд в любой момент.", Toggle(Earbuds.Enabled, delegate(bool v) { Store.Set("earbuds", v ? "1" : "0"); if (v) Earbuds.Start(); else Earbuds.Stop(); }), false));
            sp.Children.Add(Row("Пауза, когда сняли наушники", "Музыка встаёт на паузу, когда вы снимаете AirPods, и продолжается, когда надеваете обратно. Проба: на некоторых моделях может срабатывать неточно.", Toggle(Store.Get("earbudsPause") == "1", delegate(bool v) { Store.Set("earbudsPause", v ? "1" : "0"); }), false));

            sp = Sec("Нейросети");
            sp.Children.Add(Row("Кто отвечает в чате", "«Сайты без ключа» — Duck.ai, DeepSeek или Gemini прямо во вкладке «Чат»: переключаются строкой над чатом, кнопка «В окне» открывает сайт отдельным окном. Duck.ai работает без входа; в DeepSeek и Gemini входите в свой аккаунт на самом сайте. «Qwen на этом ПК» — нейросеть прямо на компьютере: без ключа, без интернета и без отправки вопросов куда-либо (сначала её нужно скачать ниже). Claude и Gemini отвечают по вашему ключу; кнопкам «Перевести», «Исправить», «Сократить», «Объяснить» ключ нужен в любом случае. К Claude Code это не относится.", Chips(new string[] { "Сайты без ключа", "Claude по ключу", "Gemini по ключу", "Qwen на этом ПК" }, Ai.Local ? 3 : Ai.Duck ? 0 : Ai.Gemini ? 2 : 1, delegate(int i) { Store.Set("aiProvider", i == 0 ? "duck" : i == 2 ? "gemini" : i == 3 ? "local" : "claude"); }), true));
            sp.Children.Add(Header("Qwen на этом компьютере"));
            {
                int rec = QwenLocal.Recommended();
                var names = new string[QwenLocal.Models.Length];
                for (int i = 0; i < names.Length; i++) names[i] = QwenLocal.Models[i].Name + " · " + QwenLocal.Models[i].SizeGb.ToString("0.#") + " ГБ";
                string ramNote = "В этом ПК " + Math.Round(QwenLocal.RamGb()) + " ГБ памяти — советуем " + QwenLocal.Models[rec].Name + ".";
                TextBlock desc = null;
                Func<int, string> descOf = delegate(int i) { return "Чем больше модель, тем умнее, но медленнее и тяжелее. " + ramNote + " Выбрана " + QwenLocal.Models[i].Name + ": " + QwenLocal.Models[i].Note + "; нужно около " + QwenLocal.Models[i].RamGb + " ГБ свободной памяти."; };
                int cur = Array.IndexOf(QwenLocal.Models, QwenLocal.Chosen);
                var modelRow = Row("Модель Qwen 3.5", descOf(cur), Chips(names, cur, delegate(int i) { QwenLocal.Choose(QwenLocal.Models[i].Id); if (desc != null) desc.Text = descOf(i); if (qwenRefresh != null) qwenRefresh(); }), true);
                try { desc = ((StackPanel)((Grid)((Border)modelRow).Child).Children[0]).Children[1] as TextBlock; } catch { }
                sp.Children.Add(modelRow);
                sp.Children.Add(Row("Скачать на компьютер", "Скачиваются движок llama.cpp (github.com/ggml-org/llama.cpp, " + QwenLocal.EngineSizeText + ") и выбранная модель (huggingface.co, сборка unsloth). Всё хранится в папке Kapsula и удаляется кнопкой «Удалить». Во время разговора интернет не нужен; Модель загружается на первом вопросе в чате и полностью выгружается из памяти кнопкой «Очистить» (и после 15 минут без вопросов). Работает на видеокарте: на NVIDIA — через CUDA (быстрее всего), на AMD и Intel — через Vulkan, а без видеокарты — на процессоре, медленнее.", BuildQwenEditor(), true));
            }
            sp.Children.Add(Header("Gemini"));
            sp.Children.Add(Row("Ключ Gemini API", "Ключ создаётся в Google AI Studio под вашим аккаунтом Google; у Gemini есть бесплатный уровень с ограничением числа запросов. Ключ хранится только на этом компьютере, зашифрованный Windows, и отправляется только на generativelanguage.googleapis.com.", BuildGeminiKeyEditor(), true));
            sp.Children.Add(Row("Модель Gemini", "Flash-Lite — самая быстрая, на бесплатном уровне отвечает стабильнее всех. Flash умнее, но часто перегружена; если она занята, Kapsula сама попробует другую модель.", Chips(GeminiChat.ModelNames, Array.IndexOf(GeminiChat.Models, GeminiChat.Model()), delegate(int i) { Store.Set("geminiModel", GeminiChat.Models[i]); }), true));
            sp.Children.Add(Header("Claude"));
            sp.Children.Add(Row("Ключ Claude API", "Нужен свой ключ: console.anthropic.com → API Keys. Он хранится только на этом компьютере, зашифрованный Windows, и отправляется только на api.anthropic.com.", BuildKeyEditor(), true));
            sp.Children.Add(Row("Модель Claude", "Opus 5.5 — умнее; Sonnet 5.5 — быстрее и дешевле.", Chips(new string[] { "Opus 5.5", "Sonnet 5.5" }, ClaudeChat.Model() == "claude-sonnet-5-5" ? 1 : 0, delegate(int i) { island.SetClaudeModel(i == 1 ? "claude-sonnet-5-5" : "claude-opus-5-5"); }), true));

            if (ClaudeBridge.ClaudeCodeFound)
            {
                Action<bool> cSet;
                Action cClick = null;
                var cBtn = ToggleSet(ClaudeBridge.Installed && ClaudeBridge.Enabled, delegate(bool v) { if (cClick != null) cClick(); }, out cSet);
                var cRow = Row("Claude Code на острове", "—", cBtn, false);
                var cText = ((StackPanel)((Grid)((Border)cRow).Child).Children[0]).Children[1] as TextBlock;
                string cNote = "";
                Action cShow = delegate
                {
                    bool have = ClaudeBridge.Installed && ClaudeBridge.Enabled;
                    bool old = have && !ClaudeBridge.UpToDate;
                    cSet(have);
                    string st = !have ? "Kapsula допишет свои строки в настройки Claude Code (прежний файл сохранится рядом). В интернет ничего не уходит."
                        : old ? "Подключение устарело: нажмите на переключатель — оно обновится и останется включённым."
                        : ClaudeBridge.Error != null ? "Не работает: " + ClaudeBridge.Error + "."
                        : "Подключено. Новые сессии Claude Code подхватят это сразу, уже открытые — после перезапуска.";
                    if (cText != null) cText.Text = "Пока вы в другой программе, остров показывает запросы разрешений и вопросы Claude Code с кнопками, сообщает, что он закончил, и даёт ответить ему. Остановить его можно из значка Kapsula у часов или командой «стоп claude». Не ответили за полминуты — Claude Code спросит сам, как обычно. " + st + cNote;
                };
                cClick = delegate
                {
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
                sp.Children.Add(Header("Claude Code"));
                sp.Children.Add(cRow);
                sp.Children.Add(Row("Звук, когда Claude Code ждёт вас", "Системный звук вместе с карточкой разрешения или вопроса — чтобы не пропустить, если вы отвернулись от экрана.", Toggle(Store.Get("claudeSound") != "0", delegate(bool v) { Store.Set("claudeSound", v ? "1" : "0"); }), false));
                sp.Children.Add(Row("Кнопка «Открыть в VS Code» · альфа", "Сырая функция, по умолчанию выключена. На карточке «Claude закончил» появляется кнопка, открывающая папку проекта в VS Code (без VS Code — в Проводнике), а в строке острова — команда «открыть проект в VS Code».", Toggle(Store.Get("vscodeButton") == "1", delegate(bool v) { Store.Set("vscodeButton", v ? "1" : "0"); }), false));
                sp.Children.Add(Row("Показывать, что делает Claude Code", "Короткая надпись на острове о каждом его действии: какую команду выполняет, какой файл правит. По умолчанию выключено — без этого остров показывает только важное: запросы разрешений, вопросы и «закончил» после работы дольше полуминуты.", Toggle(Store.Get("claudeActivity") == "1", delegate(bool v) { Store.Set("claudeActivity", v ? "1" : "0"); }), false));
            }

            sp = Sec("Steam и Discord");
            if (DiscordBridge.VencordFound)
            {
                var dRow = Row("Discord через Vencord · бета", "—", Toggle(Store.Get("discordVencord") != "0", delegate(bool v) { island.SetFlag("discordVencord", v); if (v) DiscordBridge.Start(); else DiscordBridge.Stop(); }), false);
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
                sp.Children.Add(Row("Discord через Vencord · бета", "Сообщения и звонки Discord на острове. Нужен Vencord — неофициальная модификация Discord; ставить её или нет, решаете вы.", LinkButton("Сайт Vencord", "https://vencord.dev/"), false));
            }
            // Звонки Discord напрямую — наш плагин в сборке Vencord, ставится и снимается одной кнопкой
            {
                TextBlock vText = null;
                Border vBtn = null;
                bool vBusy = false;
                string vResult = null;
                bool vInBuild = DiscordInstall.Installed && DiscordInstall.PluginInBuild;
                Action vShow = null;
                vBtn = PillBtn(DiscordInstall.Installed ? "Отключить" : "Подключить", !DiscordInstall.Installed, delegate
                {
                    if (vBusy) return;
                    vBusy = true;
                    bool off = DiscordInstall.Installed;
                    if (vText != null) vText.Text = off ? "Возвращаю Discord как было…" : "Скачиваю сборку Vencord с плагином Kapsula и ставлю…";
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        string err = off ? DiscordInstall.Uninstall() : DiscordInstall.Install();
                        bool inBuild = !off && err == null && DiscordInstall.PluginInBuild;
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            vBusy = false; vInBuild = inBuild;
                            vResult = err != null ? "Не вышло: " + err + "." : off ? "Отключено — перезапустите Discord." : "Готово — перезапустите Discord, и звонки появятся на острове.";
                            vShow();
                        }));
                    });
                });
                var vRow = Row("Звонки Discord на острове · бета", "—", vBtn, false);
                vText = ((StackPanel)((Grid)((Border)vRow).Child).Children[0]).Children[1] as TextBlock;
                vShow = delegate
                {
                    bool inst = DiscordInstall.Installed;
                    ((TextBlock)vBtn.Child).Text = inst ? "Отключить" : "Подключить";
                    string st = island.DiscordLive ? "Плагин на связи." : vResult ?? (!inst ? "" : vInBuild ? "Подключено — перезапустите Discord, если ещё не перезапускали."
                        : "Плагин пропал из Discord (видимо, Vencord переустановили) — нажмите «Отключить», потом «Подключить».");
                    if (vText != null && !vBusy)
                        vText.Text = "Канал и время звонка как в самом Discord, мут, полный мут и демонстрация экрана — в капсуле, а на «Главной» кнопки: выключить микрофон, полный мут, выйти. Ставит в Discord нашу сборку Vencord — официальный Vencord плюс плагин Kapsula; ваши плагины и настройки остаются, «Отключить» вернёт всё как было. Это неофициальная модификация Discord — решаете вы. " + st;
                };
                vShow();
                var vTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                vTimer.Tick += delegate { if (!vBusy) vShow(); };
                vTimer.Start();
                Closed += delegate { vTimer.Stop(); };
                sp.Children.Add(vRow);
            }
            if (Steam.Found)
                sp.Children.Add(Row("Steam · бета", "Игры в командной строке, карточка «игра готова» после загрузки, время игровой сессии. Данные берутся из папки Steam на этом компьютере — без входа в аккаунт.", Toggle(Store.Get("steam") != "0", delegate(bool v) { island.SetFlag("steam", v); }), false));

            if (Steam.Found && !SteamBridge.MillenniumFound)
                sp.Children.Add(Row("Уведомления Steam · бета", "Сообщения друзей, «друг в игре» и другие уведомления Steam на острове. Нужен Millennium — неофициальная модификация клиента Steam; ставить её или нет, решаете вы.", LinkButton("Сайт Millennium", "https://steambrew.app/"), false));
            else if (Steam.Found)
            {
                Action<bool> sSet;
                Action sClick = null;
                var sBtn = ToggleSet(SteamBridge.PluginInstalled, delegate(bool v) { if (sClick != null) sClick(); }, out sSet);
                var sRow = Row("Уведомления Steam · бета", "—", sBtn, false);
                var sText = ((StackPanel)((Grid)((Border)sRow).Child).Children[0]).Children[1] as TextBlock;
                string sNote = "";
                Action sShow = delegate
                {
                    bool have = SteamBridge.PluginInstalled;
                    sSet(have);
                    string st = !have ? "Kapsula положит свой плагин в папку Millennium. В интернет ничего не уходит."
                        : SteamBridge.Error != null ? "Не работает: " + SteamBridge.Error + "."
                        : SteamBridge.Clients > 0 ? "Подключено (плагин " + SteamBridge.InstalledVersion + ")."
                        : "Плагин установлен, но Steam не подключился. В Steam откройте Millennium → Плагины, включите Kapsula и перезапустите Steam.";
                    if (sText != null) sText.Text = "Сообщения друзей, «друг в игре» и другие уведомления Steam — карточками на острове. " + st + sNote;
                };
                sClick = delegate
                {
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
                sp.Children.Add(Row("Не дублировать уведомления Steam · бета", "Когда уведомление показал остров, окошко Steam справа снизу не появляется (звук остаётся). В игре Steam показывает уведомления как обычно.", Toggle(Store.Get("steamHideToasts") != "0", delegate(bool v) { island.SetFlag("steamHideToasts", v); SteamBridge.SendConfig(); }), false));
            }

            Sec("Внешний вид").Children.Add(Row("Секунды в часах", "В капсуле вместо 12:30 будет 12:30:45.", Toggle(island.ShowSeconds, delegate(bool v) { island.SetFlag("seconds", v); }), false));
            sp = Sec("Запись и снимки");
            string ra = Store.Get("recAudio");
            sp.Children.Add(Row("Звук в записи экрана", "Звук компьютера — то, что играет в колонках или наушниках: видео, игра, звонок. Микрофон добавляется поверх, если выбран третий вариант.", Chips(new string[] { "Без звука", "Звук компьютера", "Компьютер и микрофон" }, ra == "0" ? 0 : (ra == "2" ? 2 : 1), delegate(int i) { Store.Set("recAudio", i.ToString()); }), true));
            sp = Sec("Общее");
            sp.Children.Add(Row("Запускать вместе с Windows", null, Toggle(Autostart.IsOn(), delegate(bool v) { Autostart.Set(v); }), false));
            sp.Children.Add(Row("Автообновление", "Kapsula сама скачивает новые версии с GitHub, сверяет контрольную сумму файла и перезапускается, когда вы пару минут не трогаете компьютер.", Toggle(Store.Get("autoUpdate") != "0", delegate(bool v) { island.SetFlag("autoUpdate", v); }), false));

            sp = Sec("Уведомления");
            sp.Children.Add(Row("Смена раскладки", "Короткая подсказка «Раскладка: RU» при переключении языка.", Toggle(island.ShowLayout, delegate(bool v) { island.SetFlag("layout", v); }), false));
            sp.Children.Add(Row("Яркость на острове", "Меняете яркость клавишами ноутбука или в Windows — остров показывает её полоской, как громкость. Колёсико над островом с зажатым Ctrl тоже меняет яркость; у внешних мониторов — если они позволяют это программам.", Toggle(island.BrightHud, delegate(bool v) { island.SetFlag("brightHud", v); }), false));
            sp.Children.Add(Row("Уведомления Windows на острове", "Новые уведомления других программ показываются карточкой. Нигде не сохраняются.", Toggle(island.ShowNotifications, delegate(bool v) { island.SetFlag("notifications", v); }), false));
            sp.Children.Add(Row("Уведомления только на острове", "Чтобы уведомления не показывались дважды, Kapsula убирает уведомление Windows сразу, как только показала его на острове: окошко Windows успевает лишь мелькнуть. Пропущенное можно посмотреть в истории острова (до закрытия Kapsula). Пока Kapsula закрыта, уведомления Windows работают как обычно. Предупреждения безопасности Windows не трогаются.", Toggle(Banners.On, delegate(bool v) { Banners.Set(v); }), false));
            sp.Children.Add(Row("Напоминать о событиях календаря", "За десять минут до события из календаря Windows появится карточка. Список ближайших событий — по команде «календарь» в строке острова.", Toggle(Store.Get("calendar") != "0", delegate(bool v) { Store.Set("calendar", v ? "1" : "0"); }), false));
            sp.Children.Add(Row("Отсчёт до встречи в капсуле", "За 15 минут до события из календаря или задачи со временем капсула показывает «Созвон · через 12 мин». Пока играет музыка — только последние 5 минут.", Toggle(Store.Get("soonPill") != "0", delegate(bool v) { Store.Set("soonPill", v ? "1" : "0"); }), false));
            sp.Children.Add(Row("Счётчик непрочитанного", "Красный кружок с числом сообщений из Discord и Steam, пришедших, пока вы в них не смотрели. Сбрасывается, когда вы открываете программу.", Toggle(Store.Get("unread") != "0", delegate(bool v) { Store.Set("unread", v ? "1" : "0"); }), false));

            BuildExtrasSection(Sec("Дополнительно"));
            BuildSecuritySection(Sec("Безопасность"));

            sp = Sec("Клавиши");
            sp.Children.Add(new TextBlock { Foreground = Sub, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 0, 8), Text = "Нажмите на сочетание и введите новое. Backspace отключает, правый клик возвращает исходное. Красным отмечены сочетания, занятые другой программой." });
            foreach (Hot hk in island.Hots) sp.Children.Add(Row(hk.Name, null, HotChip(hk), false));
            PreviewKeyDown += OnCaptureKey;
            Closed += delegate { if (capturing != null) { capturing = null; island.SuspendHotkeys(false); } };
            try { Arrange(); } catch (Exception ex) { Store.LogError("порядок настроек", ex); }
            try { BuildLayout(); } catch (Exception ex) { Store.LogError("окно настроек", ex); Content = new TextBlock { Text = ex.ToString(), Foreground = Txt, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) }; }
        }

        // ---- разделы настроек
        static readonly string[] SectionOrder = { "Общее", "Внешний вид", "Поведение", "Главная", "Вкладки острова", "Музыка", "Уведомления", "Игры", "Нейросети", "Телефон", "AirPods и Beats", "Steam и Discord", "Запись и снимки", "Безопасность", "Дополнительно", "Клавиши" };
        static readonly string[] SectionGlyphs = { "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "" };
        static readonly string[] SectionHints =
        {
            "Версия, обновления, запуск, монитор",
            "Цвет, размер, анимации, кромка, зверёк",
            "Когда остров прячется и что подсказывает",
            "Что показывать на главной вкладке",
            "Конструктор: блоки вкладок перетаскиваются",
            "Плеер, жидкий край, тексты песен, оценки",
            "Карточки уведомлений и напоминания",
            "Остров в играх, игровой режим, Twitch",
            "Чат, Qwen на компьютере, ключи, Claude Code",
            "Пульт и веб-камера из телефона",
            "Заряд наушников и пауза, когда их сняли",
            "Карточки и плагины Steam и Discord",
            "Звук в записи, остров на снимках",
            "Камера, микрофон, пароли, текст уведомлений",
            "Пробные функции: звонки, дека, виджет",
            "Сочетания клавиш"
        };        Dictionary<string, StackPanel> sections = new Dictionary<string, StackPanel>();
        List<Border> navItems = new List<Border>();
        TextBlock paneTitle, paneHint;
        string currentSection;

        // ---- Порядок: каждая настройка — там, где её станут искать. Строки создаются по ходу кода, а здесь
        // раскладываются по разделам и подзаголовкам («#…»). Чего нет в списке — остаётся в конце своего раздела.
        readonly Dictionary<string, Border> rowsByTitle = new Dictionary<string, Border>();
        readonly Dictionary<Border, string> rowText = new Dictionary<Border, string>();

        static readonly string[][] Layout =
        {
            new[] { "Общее", "Украинский проект", "Kapsula ", "Запускать вместе с Windows", "Автообновление", "Монитор" },
            new[] { "Внешний вид", "#Остров", "Цвет острова", "Тема как в Windows", "Размер", "Шрифт острова", "Матовое стекло", "Светлая кромка", "Тень под островом", "Секунды в часах", "#Анимации", "Характер анимаций", "Блоки по очереди", "#Форма капсулы", "Ширина капсулы", "Высота капсулы", "#Зверёк", "Зверёк в капсуле", "Зверёк в переходах", "Зверёк гуляет по окнам", "Свой стикер вместо зверька", "Наряд зверька", "Сезонные наряды зверька" },
            new[] { "Поведение", "#Где остров", "Остров можно отцепить", "Вернуть остров наверх", "#Когда прятаться", "Автоскрытие", "Прятаться в программах", "#Подсказки", "Смена раскладки", "Яркость на острове" },
            new[] { "Музыка", "#Плеер", "Капсула во время музыки", "Плеер на «Главной»", "Мини-плеер при наведении", "Мини-плеер: только текст песни", "#Жидкий край", "Жидкий край", "Край как эквалайзер", "#Тексты песен", "Тексты песен", "Сдвиг текста песни", "#Оценки треков", "«В избранное» и «Не интересует»", "Ключ Cider", "Оценки в других плеерах" },
            new[] { "Уведомления", "#Карточки", "Уведомления Windows на острове", "Уведомления только на острове", "Счётчик непрочитанного", "#Напоминания", "Напоминать о событиях календаря", "Утренняя карточка" },
            new[] { "Игры", "#Остров в играх", "Игровой режим", "Показывать в играх", "Новый трек поверх игр и видео", "Нагрузка в играх", "#Twitch", "Чат Twitch поверх игры", "Канал Twitch", "Чат Twitch и вне игр" },
            new[] { "Нейросети", "#Чат", "Кто отвечает в чате", "#Qwen на этом компьютере", "Модель Qwen 3.5", "Скачать на компьютер", "#Claude", "Ключ Claude API", "Модель Claude", "#Gemini", "Ключ Gemini API", "Модель Gemini", "#Claude Code", "Claude Code на острове", "Показывать, что делает Claude Code", "Звук, когда Claude Code ждёт вас", "Кнопка «Открыть в VS Code»", "#Другие ИИ-агенты (MCP)", "Остров для любых агентов" },
            new[] { "Телефон", "#Пульт", "Пульт с телефона", "Подключить телефон", "Новая секретная ссылка", "#Камера телефона", "Телефон как веб-камера", "Качество камеры" },
            new[] { "Запись и снимки", "Звук в записи экрана", "Показывать остров на скриншотах" },
            new[] { "Безопасность", "#Камера и микрофон", "Индикатор микрофона", "Индикатор камеры", "#Приватность", "Скрывать текст уведомлений", "Не запоминать пароли из буфера" },
            new[] { "Дополнительно", "#Пробные функции", "Режим звонка", "Дека", "Клавиши деки", "Виджет на рабочем столе" },
        };

        Border FindRow(string key)
        {
            Border b;
            if (rowsByTitle.TryGetValue(key, out b)) return b;
            foreach (var kv in rowsByTitle) if (kv.Key.StartsWith(key)) return kv.Value;   // «Kapsula 0.2.5» и подобные
            return null;
        }

        void Arrange()
        {
            // Игровой режим — отдельной строкой в «Играх», чтобы о нём знали не только по сочетанию клавиш
            var gameRow = (Border)Row("Игровой режим", "Остров уезжает за край и не выезжает от мыши — открыть его можно двойным щелчком у верхней кромки. Уведомления приходят как обычно, Qwen выгружается из видеопамяти, курсор с телефона не работает. Быстро включается сочетанием из раздела «Клавиши» (по умолчанию Ctrl+Alt+G).",
                Toggle(island.GamerMode, delegate(bool v) { if (v != island.GamerMode) island.ToggleGamerMode(); }), false);
            Sec("Игры").Children.Add(gameRow);
            rowsByTitle["Игровой режим"] = gameRow;   // одноимённая строка в «Клавишах» — это сочетание, она остаётся там
            // все строки из списка сначала вынимаем из их прежних мест
            var placed = new Dictionary<string, List<UIElement>>();
            foreach (string[] spec in Layout)
            {
                var items = new List<UIElement>();
                for (int i = 1; i < spec.Length; i++)
                {
                    if (spec[i].StartsWith("#")) { items.Add(Header(spec[i].Substring(1))); continue; }
                    Border r = FindRow(spec[i]);
                    if (r == null) continue;   // строки нет на этом компьютере (например, один монитор)
                    var parent = r.Parent as Panel;
                    if (parent != null) parent.Children.Remove(r);
                    items.Add(r);
                }
                // подзаголовок без строк под ним не нужен
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    bool hdr = items[i] is TextBlock && (string)((TextBlock)items[i]).Tag == "hdr";
                    bool nextHdr = i == items.Count - 1 || items[i + 1] is TextBlock && (string)((TextBlock)items[i + 1]).Tag == "hdr";
                    if (hdr && nextHdr) items.RemoveAt(i);
                }
                placed[spec[0]] = items;
            }
            foreach (var kv in placed)
            {
                StackPanel sec = Sec(kv.Key);
                var rest = new List<UIElement>();
                foreach (UIElement k in sec.Children)
                {
                    var tb = k as TextBlock;
                    if (tb != null && (string)tb.Tag == "hdr") continue;   // старые подзаголовки заменяются новыми
                    rest.Add(k);
                }
                sec.Children.Clear();
                foreach (UIElement u in kv.Value) sec.Children.Add(u);
                foreach (UIElement u in rest) sec.Children.Add(u);
            }
            // разделы, из которых всё переехало, могли остаться с одними подзаголовками
            foreach (StackPanel sec in sections.Values)
            {
                bool any = false;
                foreach (UIElement k in sec.Children) if (!(k is TextBlock && (string)((TextBlock)k).Tag == "hdr")) any = true;
                if (!any) sec.Children.Clear();
            }
        }

        // ---- Поиск по настройкам: найденные строки на время поиска собираются на одну страницу
        TextBox searchBox;
        StackPanel searchPage = new StackPanel();
        readonly List<KeyValuePair<UIElement, KeyValuePair<Panel, int>>> borrowed = new List<KeyValuePair<UIElement, KeyValuePair<Panel, int>>>();

        void ReturnBorrowed()
        {
            for (int i = borrowed.Count - 1; i >= 0; i--)
            {
                var b = borrowed[i];
                searchPage.Children.Remove(b.Key);
                Panel home = b.Value.Key;
                home.Children.Insert(Math.Min(b.Value.Value, home.Children.Count), b.Key);
            }
            borrowed.Clear();
            searchPage.Children.Clear();
            foreach (StackPanel sec in sections.Values) GroupRows(sec);
        }

        void RunSearch(string q)
        {
            ReturnBorrowed();
            q = (q ?? "").Trim().ToLowerInvariant();
            if (q.Length < 2) { ShowSection(currentSection ?? "Общее"); return; }
            string[] words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int found = 0;
            foreach (string name in SectionOrder)
            {
                StackPanel sec;
                if (!sections.TryGetValue(name, out sec)) continue;
                var hits = new List<Border>();
                foreach (UIElement k in sec.Children)
                {
                    var b = k as Border; string txt;
                    if (b == null || !rowText.TryGetValue(b, out txt)) continue;
                    bool all = true;
                    foreach (string w in words) if (!txt.Contains(w) && !name.ToLowerInvariant().Contains(w)) { all = false; break; }
                    if (all) hits.Add(b);
                }
                if (hits.Count == 0) continue;
                var head = new TextBlock { Text = name, Foreground = Sub, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 12, 0, 8), Cursor = Cursors.Hand, ToolTip = "Открыть раздел" };
                string target = name;
                head.MouseLeftButtonDown += delegate { searchBox.Text = ""; ShowSection(target); };
                searchPage.Children.Add(head);
                foreach (Border b in hits)
                {
                    borrowed.Add(new KeyValuePair<UIElement, KeyValuePair<Panel, int>>(b, new KeyValuePair<Panel, int>(sec, sec.Children.IndexOf(b))));
                    sec.Children.Remove(b);
                    searchPage.Children.Add(b);
                    found++;
                }
            }
            if (found == 0) searchPage.Children.Add(new TextBlock { Text = "Ничего не нашлось. Попробуйте другое слово — например, «звук», «телефон» или «игры».", Foreground = Sub, FontSize = 13, Margin = new Thickness(4, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
            GroupRows(searchPage);
            paneTitle.Text = "Поиск";
            if (pvBox != null) pvBox.Visibility = Visibility.Visible;
            paneHint.Text = found > 0 ? "Найдено: " + found : "";
            scroller.Content = searchPage;
            scroller.ScrollToTop();
            foreach (Border b in navItems) b.Background = Brushes.Transparent;
        }

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
            Color.FromRgb(0x8E, 0x8E, 0x93), Color.FromRgb(0x5E, 0x5C, 0xE6), Color.FromRgb(0x34, 0xC7, 0x59), Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x64, 0xD2, 0xFF), Color.FromRgb(0xFF, 0x37, 0x5F), Color.FromRgb(0xFF, 0x45, 0x3A),
            Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0xE0, 0x7B, 0x45), Color.FromRgb(0x34, 0xC7, 0x59), Color.FromRgb(0xAC, 0xAC, 0xB2), Color.FromRgb(0x30, 0xB0, 0xC7), Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0xAF, 0x52, 0xDE), Color.FromRgb(0x63, 0x63, 0x66)
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
            var navPet = new PetView(2.0, RowBg);
            navPet.Context = "nav";   // зверёк рядом с названием: моргает и поглядывает по сторонам
            navPet.Root.Margin = new Thickness(0, 0, 2, 0);
            brand.Children.Add(navPet.Root);
            brand.Children.Add(new TextBlock { Text = "Kapsula", Foreground = Txt, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            nav.Children.Add(brand);
            nav.Children.Add(new TextBlock { Text = "версия " + Updater.CurrentText, Foreground = Sub, FontSize = 11.5, Margin = new Thickness(12, 1, 0, 12) });
            // поиск по всем настройкам
            var sGrid = new Grid { Margin = new Thickness(2, 0, 2, 12) };
            searchBox = new TextBox { Height = 32, Background = ChipBg, Foreground = Txt, CaretBrush = Txt, BorderThickness = new Thickness(0), Padding = new Thickness(30, 7, 10, 0), FontSize = 13 };
            var sIcon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Foreground = Sub, Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var sHint = new TextBlock { Text = "Поиск настроек", Foreground = Sub, FontSize = 13, Margin = new Thickness(32, 0, 0, 1), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var sClip = new Border { CornerRadius = new CornerRadius(10), Background = ChipBg, Child = searchBox, ClipToBounds = true };
            sGrid.Children.Add(sClip); sGrid.Children.Add(sIcon); sGrid.Children.Add(sHint);
            searchBox.TextChanged += delegate { sHint.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; if (!searchMuted) RunSearch(searchBox.Text); };
            searchBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) { searchBox.Text = ""; e.Handled = true; } };
            nav.Children.Add(sGrid);
            for (int i = 0; i < SectionOrder.Length; i++)
            {
                string name = SectionOrder[i];
                if (!sections.ContainsKey(name) || sections[name].Children.Count == 0) continue;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                // значок раздела — белый на цветном скруглённом квадрате, у каждого раздела свой цвет
                Color tint = SectionColors[i % SectionColors.Length];
                GC.KeepAlive(tint);
                row.Children.Add(new TextBlock { Text = SectionGlyphs[i], FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, Foreground = Sub, Width = 22, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = name, Foreground = Txt, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var item = new Border { Height = 36, CornerRadius = new CornerRadius(11), Padding = new Thickness(9, 0, 10, 0), Margin = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = row, Tag = name };
                item.MouseLeftButtonDown += delegate { ShowSection(name); };
                item.MouseEnter += delegate { if ((string)item.Tag != currentSection) item.Background = NavHover; };
                Fx.Press(item, 0.97);
                item.MouseLeave += delegate { if ((string)item.Tag != currentSection) item.Background = Brushes.Transparent; };
                navItems.Add(item);
                nav.Children.Add(item);
            }
            var navDock = new DockPanel();
            var saved = new TextBlock { Text = "Изменения сохраняются сразу", Foreground = Sub, FontSize = 11, Margin = new Thickness(24, 8, 12, 14) };
            DockPanel.SetDock(saved, Dock.Bottom);
            navDock.Children.Add(saved);
            navDock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = nav });
            root.Children.Add(new Border { Background = MicaOk ? Brushes.Transparent : RowBg, Child = navDock });

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
            // правая часть — плотнее боковой панели, с тонкой линией-разделителем слева
            var paneBack = new Border { Background = MicaOk ? PaneBg : Bg, BorderBrush = Divider, BorderThickness = new Thickness(1, 0, 0, 0) };
            Grid.SetColumn(paneBack, 1);
            root.Children.Add(paneBack);
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

        int hoverKind = -1, hoverSeq;

        // Какой пример показывает остров при наведении на строку
        static string DemoFor(string title)
        {
            switch (title)
            {
                case "Уведомления Windows на острове": case "Уведомления только на острове": case "Скрывать текст уведомлений": return "notif";
                case "Напоминать о событиях календаря": return "calendar";
                case "Отсчёт до встречи в капсуле": return "soon";
                case "Смена раскладки": return "layout";
                case "Яркость на острове": return "bright";
                case "Зверёк гуляет по окнам": return "walk";
                case "Игровой режим": return "gamer";
                case "Новый трек поверх игр и видео": return "track";
                case "Счётчик непрочитанного": return "unread";
                case "Индикатор микрофона": case "Индикатор камеры": return "privacy";
                case "Зверёк в переходах": case "Блоки по очереди": return "motion";   // остров раскрывается и сворачивается сам
            }
            return null;
        }

        // Что показать в превью для строки настроек: 1 — главная панель, 2 — мини-плеер, 0 — сам остров, -1 — как у раздела
        static int PreviewKindFor(string title)
        {
            string t = title ?? "";
            if (t.Contains("Главн")) return 1;
            if (t.StartsWith("Мини-плеер") || t.Contains("екст")) return 2;
            if (t.StartsWith("Капсула") || t.Contains("край") || t.Contains("Край") || t.StartsWith("Новый трек") || t.StartsWith("Зверёк") || t.StartsWith("Секунды")) return 0;
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
            // Высота рамки зависит только от раздела: иначе при наведении на разные строки рамка прыгала, строки
            // уезжали из-под курсора, и превью начинало мигать.
            pvBox.Height = currentSection == "Главная" || currentSection == "Музыка" ? PvHomeH : PvH;
            pvCaption.Text = kind == 1 ? "Главная" : kind == 2 ? "Мини-плеер" : "Остров сейчас";
        }

        bool searchMuted;
        public void ShowSection(string name)
        {
            if (!sections.ContainsKey(name)) return;
            if (borrowed.Count > 0 || scroller != null && scroller.Content == searchPage) ReturnBorrowed();
            if (searchBox != null && searchBox.Text.Length > 0) { searchMuted = true; searchBox.Text = ""; searchMuted = false; }
            // где настройки ничего не меняют на самом острове, большое превью только мешает
            if (pvBox != null) pvBox.Visibility = name == "Нейросети" || name == "Телефон" || name == "Клавиши" ? Visibility.Collapsed : Visibility.Visible;
            currentSection = name;
            UpdatePreview();
            int idx = Array.IndexOf(SectionOrder, name);
            paneTitle.Text = name;
            paneHint.Text = idx >= 0 ? SectionHints[idx] : "";
            scroller.Content = sections[name];
            scroller.ScrollToTop();
            foreach (Border b in navItems) b.Background = (string)b.Tag == name ? Sel : Brushes.Transparent;
            // раздел появляется мягко: чуть всплывает и проявляется
            StackPanel sec = sections[name];
            var rise = sec.RenderTransform as TranslateTransform;
            if (rise == null) { rise = new TranslateTransform(); sec.RenderTransform = rise; }
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            rise.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            sec.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
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
            return new TextBlock { Text = text, Foreground = Sub, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 12, 0, 8), Tag = "hdr" };
        }

        FrameworkElement Row(string title, string desc, FrameworkElement control, bool below)
        {
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            // «Название · бета» — оранжевая плашка «БЕТА»; «Название · альфа» — голубая «АЛЬФА»: совсем сырая функция, по умолчанию выключена
            const string betaMark = " · бета", alphaMark = " · альфа";
            bool alpha = title.EndsWith(alphaMark);
            if (title.EndsWith(betaMark) || alpha)
            {
                string mark = alpha ? alphaMark : betaMark;
                Color mc = alpha ? Color.FromRgb(0x40, 0xB4, 0xFF) : Color.FromRgb(0xFF, 0x9F, 0x0A);
                var head = new StackPanel { Orientation = Orientation.Horizontal };
                head.Children.Add(new TextBlock { Text = title.Substring(0, title.Length - mark.Length), Foreground = Txt, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                head.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x33, mc.R, mc.G, mc.B)), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = alpha ? "АЛЬФА" : "БЕТА", Foreground = new SolidColorBrush(mc), FontSize = 10.5, FontWeight = FontWeights.Bold }
                });
                texts.Children.Add(head);
            }
            else texts.Children.Add(new TextBlock { Text = title, Foreground = Txt, FontSize = 14, FontWeight = FontWeights.SemiBold });
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
            string baseTitle = title.Replace(" · бета", "").Replace(" · альфа", "");
            if (!rowsByTitle.ContainsKey(baseTitle)) rowsByTitle[baseTitle] = rowBox;
            rowText[rowBox] = (baseTitle + " " + (desc ?? "")).ToLowerInvariant();
            // Наведение на строку — остров сам показывает пример: карточку, подсказку, напоминание
            string demo = DemoFor(baseTitle);
            if (demo != null)
            {
                var dt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                dt.Tick += delegate { dt.Stop(); island.ShowDemo(demo); };
                rowBox.MouseEnter += delegate { dt.Stop(); dt.Start(); };
                rowBox.MouseLeave += delegate { dt.Stop(); };
            }
            rowBox.MouseEnter += delegate { rowBox.Background = RowHover; };
            rowBox.MouseLeave += delegate { rowBox.Background = RowBg; };
            // Превью показывает то, что меняет строка под курсором: главную панель, мини-плеер или сам остров.
            int kind = PreviewKindFor(title);
            if (kind >= 0)
            {
                rowBox.MouseEnter += delegate { hoverSeq++; hoverKind = kind; UpdatePreview(); };
                rowBox.MouseLeave += delegate
                {
                    // сбрасываем не сразу: при переходе на соседнюю строку превью не должно на миг возвращаться к общему виду
                    int seq = ++hoverSeq;
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                    t.Tick += delegate { t.Stop(); if (seq == hoverSeq) { hoverKind = -1; UpdatePreview(); } };
                    t.Start();
                };
            }
            return rowBox;
        }

        // Переключатель, которым можно управлять снаружи: set ставит его в нужное положение без вызова changed.
        // Нужен там, где действие может не получиться (подключение плагина) и переключатель должен вернуться назад.
        FrameworkElement ToggleSet(bool on, Action<bool> changed, out Action<bool> set)
        {
            bool state = on;
            var knob = new Ellipse { Width = 18, Height = 18, Fill = Brushes.White, Margin = new Thickness(3), HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            var b = new Border { Width = 44, Height = 24, CornerRadius = new CornerRadius(12), Background = state ? Green : ChipBg, Child = knob, Cursor = Cursors.Hand };
            Action<bool> show = delegate(bool v)
            {
                state = v;
                knob.HorizontalAlignment = v ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                b.Background = v ? Green : ChipBg;
            };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; show(!state); changed(state); };
            set = show;
            return b;
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

        // Выбор одного варианта из нескольких: варианты лежат в общей дорожке, подсветка выбранного
        // переезжает от одного к другому — так же, как вкладки на самом острове.
        FrameworkElement Chips(string[] labels, int selected, Action<int> pick)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var items = new List<Border>();
            var tx = new TranslateTransform();
            var thumb = new Border { Height = 28, CornerRadius = new CornerRadius(14), Background = SegThumb, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, RenderTransform = tx, Width = 0, IsHitTestVisible = false };
            int cur = selected;
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            Action<bool> move = delegate(bool animate)
            {
                for (int i = 0; i < items.Count; i++) ((TextBlock)items[i].Child).Foreground = i == cur ? Txt : Sub;
                if (cur < 0 || cur >= items.Count) { thumb.Opacity = 0; return; }
                Border it = items[cur];
                double w = it.ActualWidth;
                if (w <= 0) return;
                double x = it.TranslatePoint(new Point(0, 0), row).X;
                thumb.Opacity = 1;
                if (animate && thumb.ActualWidth > 0)
                {
                    var dur = TimeSpan.FromMilliseconds(260);
                    tx.BeginAnimation(TranslateTransform.XProperty, new System.Windows.Media.Animation.DoubleAnimation(x, dur) { EasingFunction = ease });
                    thumb.BeginAnimation(FrameworkElement.WidthProperty, new System.Windows.Media.Animation.DoubleAnimation(thumb.ActualWidth, w, dur) { EasingFunction = ease });
                }
                else
                {
                    tx.BeginAnimation(TranslateTransform.XProperty, null); thumb.BeginAnimation(FrameworkElement.WidthProperty, null);
                    tx.X = x; thumb.Width = w;
                }
            };
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var c = new Border { Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 0, 14, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = new TextBlock { Text = labels[i], FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } };
                c.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; if (cur == idx) return; cur = idx; move(true); pick(idx); };
                items.Add(c);
                row.Children.Add(c);
            }
            var grid = new Grid();
            grid.Children.Add(thumb); grid.Children.Add(row);
            var track = new Border { Background = SegTrack, BorderBrush = SegLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17), Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, Child = grid };
            row.SizeChanged += delegate { move(false); };
            track.Loaded += delegate { move(false); };
            return track;
        }
        // Гардероб зверька: «Сам», «По сезону», «Без наряда» и обновки за привычки. Ещё не полученные — бледные,
        // с тем, сколько осталось; выбрать их нельзя.
        FrameworkElement OutfitPicker()
        {
            var wrap = new WrapPanel { MaxWidth = 470 };
            var ids = new List<string> { "", "season", "none" };
            var labels = new List<string> { "Сам", "По сезону", "Без наряда" };
            var locked = new List<string> { null, null, null };
            for (int i = 0; i < IslandWindow.OutfitIds.Length; i++)
            {
                ids.Add(IslandWindow.OutfitIds[i]);
                bool have = island.OutfitHave(i);
                labels.Add(IslandWindow.OutfitNames[i] + (have ? "" : " · " + island.OutfitProgress(i)));
                locked.Add(have ? null : "Пока нет: нужно " + IslandWindow.OutfitFor(i) + " (сейчас " + island.OutfitProgress(i) + ")");
            }
            string cur = island.OutfitChoice();
            if (ids.IndexOf(cur) < 0) cur = "";
            var chips = new List<Border>();
            Action paint = delegate
            {
                for (int i = 0; i < chips.Count; i++)
                {
                    bool on = ids[i] == cur;
                    chips[i].Background = on ? OnBg : Brushes.Transparent;
                    chips[i].BorderBrush = on ? Brushes.Transparent : Divider;
                    ((TextBlock)chips[i].Child).Foreground = on ? OnTxt : Sub;
                }
            };
            for (int i = 0; i < ids.Count; i++)
            {
                int idx = i;
                var tb = new TextBlock { Text = labels[i], FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                var c = new Border { Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 0, 14, 1), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(1), Child = tb };
                if (locked[i] != null) { c.Opacity = 0.45; c.ToolTip = locked[i]; }
                else
                {
                    c.Cursor = Cursors.Hand;
                    c.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; cur = ids[idx]; paint(); island.SetOutfit(cur); };
                }
                chips.Add(c);
                wrap.Children.Add(c);
            }
            paint();
            return wrap;
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
                Action paint = delegate { c.Background = state ? OnBg : Brushes.Transparent; c.BorderBrush = state ? Brushes.Transparent : Divider; c.BorderThickness = new Thickness(1); tb.Foreground = state ? OnTxt : Sub; };
                c.MouseLeftButtonDown += delegate { state = !state; paint(); changed(idx, state); };
                paint();
                wrap.Children.Add(c);
            }
            return wrap;
        }

        // «Что на «Главной»»: группы «Слева», «Плитки», «Снизу» и схема того, как ляжет панель
        FrameworkElement HomeEditor()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = "Что на «Главной»", Foreground = Txt, FontSize = 14, FontWeight = FontWeights.SemiBold });
            left.Children.Add(new TextBlock { Text = "Оставьте только нужное — например, один плеер или плеер и снимок области. «Главная» перестроится сама. Без музыки на месте плеера — недавно скопированное.", Foreground = Sub, FontSize = 12, LineHeight = 17, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 12, 0) });
            var schema = new Canvas { Width = 150, Height = 96, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0) };
            Action draw = null;
            Func<string, bool, bool> on = delegate(string key, bool def) { string v = Store.Get(key); return v == "" ? def : v != "0"; };
            Action<string, string[], string[], bool[]> group = null;
            group = delegate(string head, string[] keys, string[] labels, bool[] defs)
            {
                left.Children.Add(new TextBlock { Text = head, Foreground = Sub, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 7) });
                var states = new bool[keys.Length];
                for (int i = 0; i < keys.Length; i++) states[i] = on(keys[i], defs[i]);
                left.Children.Add(MultiChips(labels, states, delegate(int i, bool v) { Store.Set(keys[i], v ? "1" : "0"); island.ApplyHomeLayout(); draw(); }));
            };
            group("СЛЕВА", new string[] { "homePlayer", "homeClips", "homeVolume" }, new string[] { "Плеер", "Недавно скопировано", "Громкость" }, new bool[] { true, true, true });
            // плитки хранятся списком выключенных
            left.Children.Add(new TextBlock { Text = "ПЛИТКИ", Foreground = Sub, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 7) });
            var tilesOff = new HashSet<string>(Store.Get("homeTilesOff").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            var tilesOn = new bool[IslandWindow.HomeTileIds.Length];
            for (int i = 0; i < tilesOn.Length; i++) tilesOn[i] = !tilesOff.Contains(IslandWindow.HomeTileIds[i]);
            left.Children.Add(MultiChips(IslandWindow.HomeTileNames, tilesOn, delegate(int i, bool v)
            {
                if (v) tilesOff.Remove(IslandWindow.HomeTileIds[i]); else tilesOff.Add(IslandWindow.HomeTileIds[i]);
                Store.Set("homeTilesOff", string.Join(",", new List<string>(tilesOff).ToArray()));
                island.ApplyHomeLayout(); draw();
            }));
            group("СНИЗУ", new string[] { "homeLauncher" }, new string[] { "Ярлыки" }, new bool[] { false });

            // схема: прямоугольники на месте частей панели
            Brush part = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)), hot = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x66));
            draw = delegate
            {
                schema.Children.Clear();
                var frame = new Border { Width = 150, Height = 96, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x11)), BorderBrush = Divider, BorderThickness = new Thickness(1) };
                schema.Children.Add(frame);
                bool pl = on("homePlayer", true) || on("homeClips", true), vol = on("homeVolume", true), lau = on("homeLauncher", false);
                var off2 = new HashSet<string>(Store.Get("homeTilesOff").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                int n = 0; foreach (string id in IslandWindow.HomeTileIds) if (!off2.Contains(id)) n++;
                double x = 8, y = 8, w = 134, bottom = lau ? 72 : 88;
                double tilesW = n > 0 ? 50 : 0, leftW = w - (n > 0 ? tilesW + 5 : 0);
                Action<double, double, double, double, Brush> rect = delegate(double rx, double ry, double rw, double rh, Brush b)
                {
                    var r = new Border { Width = Math.Max(2, rw), Height = Math.Max(2, rh), CornerRadius = new CornerRadius(4), Background = b };
                    Canvas.SetLeft(r, rx); Canvas.SetTop(r, ry); schema.Children.Add(r);
                };
                double mainH = bottom - y - (vol ? 13 : 0);
                if (pl) rect(x, y, leftW, mainH, hot);
                if (vol) rect(x, pl ? y + mainH + 4 : y, leftW, 9, part);
                if (n > 0)
                {
                    int rows = Math.Max(1, (n + 1) / 2);
                    double th = (bottom - y - (rows - 1) * 3) / rows;
                    for (int k = 0; k < n; k++) rect(x + leftW + 5 + (k % 2) * 26, y + (k / 2) * (th + 3), 24, th, part);
                }
                if (lau) rect(x, bottom + 4, w, 12, part);
            };
            draw();
            Grid.SetColumn(schema, 1);
            g.Children.Add(left); g.Children.Add(schema);
            return new Border { Background = RowBg, CornerRadius = new CornerRadius(16), Padding = new Thickness(18, 16, 18, 18), Margin = new Thickness(0, 0, 0, 16), Child = g };
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
                    var menu = new System.Windows.Forms.ContextMenuStrip();
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
                            string shown = title.Length > 40 ? title.Substring(0, 39) + "…" : title;
                            string app = pn;
                            menu.Items.Add(pn + "  —  " + shown, null, delegate { List<string> cur = IslandWindow.HideApps(); cur.Add(app); IslandWindow.SetHideApps(cur); fill(); });
                        }
                    }
                    catch { }
                    if (menu.Items.Count == 0) menu.Items.Add("Нет открытых программ").Enabled = false;
                    DarkMenuRenderer.Apply(menu, 420, false);
                    Point at = add.PointToScreen(new Point(0, add.ActualHeight + 6));
                    menu.Show(new System.Drawing.Point((int)at.X, (int)at.Y));
                };
                wrap.Children.Add(add);
            };
            fill();
            return wrap;
        }

        ScrollViewer scroller;
        public void ScrollTo(double y) { if (scroller != null) scroller.ScrollToVerticalOffset(y); }
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
        // ---- «Безопасность»: индикаторы камеры и микрофона, приватность уведомлений и буфера, скрытие от записи
        void BuildSecuritySection(StackPanel sp)
        {
            sp.Children.Add(Header("Камера и микрофон"));
            sp.Children.Add(Row("Индикатор микрофона", "Оранжевая точка в капсуле и короткое сообщение, когда какая-то программа включает микрофон.", Toggle(Store.Get("micDot") == "1", delegate(bool v) { Store.Set("micDot", v ? "1" : "0"); }), false));
            sp.Children.Add(Row("Индикатор камеры", "Зелёная точка в капсуле и короткое сообщение, когда какая-то программа включает веб-камеру.", Toggle(Store.Get("camDot") == "1", delegate(bool v) { Store.Set("camDot", v ? "1" : "0"); }), false));
            sp.Children.Add(Header("Приватность"));
            sp.Children.Add(Row("Скрывать текст уведомлений", "Карточки уведомлений Windows на острове показывают только программу, без текста сообщения, — если рядом кто-то смотрит на экран.", Toggle(Store.Get("hideNotifText") == "1", delegate(bool v) { Store.Set("hideNotifText", v ? "1" : "0"); }), false));
            sp.Children.Add(Row("Не запоминать пароли из буфера", "Пароли, скопированные из менеджеров паролей, не попадают в историю буфера Kapsula. История буфера и так хранится только в памяти и стирается при выходе.", Toggle(Store.Get("clipSecrets") != "0", delegate(bool v) { Store.Set("clipSecrets", v ? "1" : "0"); }), false));
            sp.Children.Add(Row("Показывать остров на скриншотах", "По умолчанию он скрыт от снимков, записи экрана и трансляций.", Toggle(island.ShowInCapture, delegate(bool v) { island.SetFlag("showInCapture", v); }), false));
            sp.Children.Add(Header("Доступ с телефона"));
            sp.Children.Add(Row("Новая секретная ссылка", "Ссылка из QR-кода открывает пульт и камеру. Если она могла попасть не туда — сделайте новую, старая сразу перестанет работать.", PillBtn("Сделать новую", false, delegate { Phone.NewToken(); MessageBox.Show(this, "Старая ссылка больше не работает. Чтобы подключить телефон снова, покажите ему новый QR-код.", "Kapsula"); }), false));
        }

        // ---- «Дополнительно»: звонки, дека, телефон, игры, Twitch, виджет, утро, MCP
        void BuildExtrasSection(StackPanel sp)
        {
            sp.Children.Add(Header("Каждый день"));
            sp.Children.Add(Row("Утренняя карточка", "Раз в день, когда вы впервые садитесь за компьютер до полудня: погода, первое событие, задачи и кнопка «Фокус».", Toggle(Store.Get("morningCard") != "0", delegate(bool v) { Store.Set("morningCard", v ? "1" : "0"); }), false));
            sp.Children.Add(Row("Режим звонка · альфа", "Сырая функция, по умолчанию выключена. Kapsula сама угадывает звонок: Discord, Zoom, Teams, Telegram или браузер держит микрофон и в разговоре кто-то говорит. Тогда капсула показывает длительность звонка, а «Главная» — большую кнопку микрофона и выбор устройств. Может ошибаться.", Toggle(Store.Get("callMode") == "1", delegate(bool v) { Store.Set("callMode", v ? "1" : "0"); }), false));
            sp.Children.Add(Row("Дека · альфа", "Вкладка рядом с «Главной»: до 9 ярлыков программ и действий с запуском по Ctrl+Alt+1…9. Сырая функция, по умолчанию выключена.", Toggle(Store.Get("deckOn") == "1", delegate(bool v) { Store.Set("deckOn", v ? "1" : "0"); island.ReloadDeckHotkeys(); island.RefreshTabs(); }), false));
            sp.Children.Add(Row("Клавиши деки", "Ctrl+Alt+1…9 запускают места на вкладке «Дека» — программы и действия.", Toggle(Store.Get("deckHotkeys") != "0", delegate(bool v) { Store.Set("deckHotkeys", v ? "1" : "0"); island.ReloadDeckHotkeys(); }), false));

            sp.Children.Add(Header("Поверх экрана"));
            sp.Children.Add(Row("Нагрузка в играх · бета", "В игре на весь экран в левом верхнем углу — маленькая полоска: процессор, видеокарта, память и время. Мышь проходит сквозь неё.", Toggle(Store.Get("gameStrip") == "1", delegate(bool v) { Store.Set("gameStrip", v ? "1" : "0"); }), false));
            sp.Children.Add(Row("Виджет на рабочем столе", "Крупный день недели и дата в левом нижнем углу рабочего стола, под окнами; ниже — погода, ближайшее событие, таймер и задачи.", Toggle(Store.Get("deskWidget") == "1", delegate(bool v) { Store.Set("deskWidget", v ? "1" : "0"); }), false));
            sp.Children.Add(Row("Чат Twitch поверх игры · бета", "Сообщения чата выбранного канала справа поверх игры. Читаются анонимно — входить в Twitch не нужно, писать в чат отсюда нельзя. Чат YouTube так показать нельзя: YouTube требует ключ разработчика.", Toggle(Store.Get("twitchOverlay") == "1", delegate(bool v) { Store.Set("twitchOverlay", v ? "1" : "0"); island.TwitchSync(); }), false));
            var chan = new TextBox { Width = 220, Height = 30, Background = ChipBg, Foreground = Txt, BorderThickness = new Thickness(0), Padding = new Thickness(10, 5, 10, 0), CaretBrush = Txt, FontSize = 13, Text = Store.Get("twitchChannel") };
            chan.LostFocus += delegate { Store.Set("twitchChannel", chan.Text.Trim().TrimStart('#')); island.TwitchSync(); };
            chan.KeyDown += delegate(object o, KeyEventArgs e) { if (e.Key == Key.Enter) { Store.Set("twitchChannel", chan.Text.Trim().TrimStart('#')); island.TwitchSync(); } };
            sp.Children.Add(Row("Канал Twitch", "Имя канала из адреса: twitch.tv/имя.", chan, false));
            sp.Children.Add(Row("Чат Twitch и вне игр", "Показывать окно чата всегда, а не только в играх на весь экран.", Toggle(Store.Get("twitchAlways") == "1", delegate(bool v) { Store.Set("twitchAlways", v ? "1" : "0"); }), false));

            sp = Sec("Телефон");
            sp.Children.Add(Header("Камера телефона"));
            var camRow = new StackPanel { Orientation = Orientation.Horizontal };
            var camStatus = new TextBlock { Foreground = Sub, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            var camCol = new StackPanel();
            camCol.Children.Add(camRow); camCol.Children.Add(camStatus);
            Action camPaint = null;
            camPaint = delegate
            {
                camRow.Children.Clear();
                bool inst = PhoneCam.Installed, fresh = inst && PhoneCam.UpToDate;
                if (!inst || !fresh)
                    camRow.Children.Add(PillBtn(inst ? "Обновить драйвер" : "Установить камеру", true, delegate
                    {
                        bool ok = PhoneCam.Setup(true);
                        if (ok) PhoneCam.Start(island);
                        camStatus.Text = ok ? "Готово. В Discord, Zoom или браузере появилась камера «Kapsula Camera» — если программа была открыта, перезапустите её." : "Не установилось: Windows не дала прав администратора.";
                        camPaint();
                    }));
                if (inst)
                {
                    var conn = PillBtn("Подключить телефон", fresh, delegate { PhoneCam.Start(island); PhoneCam.ShowQr(); });
                    conn.Margin = new Thickness(inst && !fresh ? 8 : 0, 0, 0, 0);
                    camRow.Children.Add(conn);
                    var rm = PillBtn("Удалить", false, delegate
                    {
                        if (MessageBox.Show(this, "Удалить камеру «Kapsula Camera» из Windows? Понадобятся права администратора.", "Kapsula", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                        bool ok = PhoneCam.Setup(false);
                        camStatus.Text = ok ? "Камера удалена." : "Не удалось удалить: Windows не дала прав администратора.";
                        camPaint();
                    });
                    rm.Margin = new Thickness(8, 0, 0, 0);
                    camRow.Children.Add(rm);
                }
                if (camStatus.Text.Length == 0)
                    camStatus.Text = !inst ? "Windows спросит разрешение администратора: драйвер камеры ставится для всего компьютера. Сам драйвер — часть Kapsula, ничего не скачивается."
                        : !fresh ? "Вышла новая версия драйвера — обновите, чтобы она заработала."
                        : "Установлена. Камера в программах называется «Kapsula Camera». Драйвер камеры загружают все программы, которые ищут камеры; редкие игры с камерой и строгим античитом могут не пустить его к себе — тогда будет вылет, а не бан. Не пользуетесь камерой — удалите её, поставить снова можно в один клик.";
            };
            camPaint();
            sp.Children.Add(Row("Телефон как веб-камера · бета", "Камера телефона становится обычной камерой Windows: её видят Discord, Zoom, Teams, браузеры и OBS. Телефон подключается по QR-коду через домашнюю сеть Wi-Fi.", camCol, true));
            sp.Children.Add(Row("Качество камеры", "Что камера предлагает программам первым. С вариантами «· 60» программы и сайты получают только режимы на 60 кадров. Discord для камеры сам берёт не больше 30. Для 1080p · 60 и 4K нужны хороший телефон и быстрый Wi-Fi; если картинка дёргается — выберите ниже. Программу с открытой камерой после смены нужно переоткрыть.", Chips(PhoneCam.ModeNames, PhoneCam.Mode, delegate(int i) { PhoneCam.Mode = i; }), true));
            sp.Children.Add(Header("Телефон"));
            Action<bool> pSet;
            var pBtn = ToggleSet(Store.Get("phoneRemote") == "1", delegate(bool v)
            {
                Store.Set("phoneRemote", v ? "1" : "0");
                if (v) Phone.Start(island); else Phone.Stop();
            }, out pSet);
            sp.Children.Add(Row("Пульт с телефона · бета", "Телефон в той же сети Wi-Fi открывает страницу по QR-коду: музыка и громкость, курсор пальцем, ввод текста, слайды, программы из деки, файлы с полки, блокировка и сон. Работает только в домашней сети и только по секретной ссылке. Windows может спросить разрешение брандмауэра — разрешите для частных сетей. Пока идёт игра на весь экран или включён игровой режим, курсор, клавиши и ввод текста с телефона не работают — чтобы античит не принял их за макрос; музыка и громкость работают как обычно.", pBtn, false));
            var pRow = new StackPanel { Orientation = Orientation.Horizontal };
            var qr = PillBtn("Показать QR-код", true, delegate { Phone.ShowQr(island, null); pSet(Store.Get("phoneRemote") == "1"); });
            pRow.Children.Add(qr);
            var renew = PillBtn("Новая ссылка", false, delegate { Phone.NewToken(); MessageBox.Show(this, "Старая ссылка больше не работает. Покажите новый QR-код телефону.", "Kapsula"); });
            renew.Margin = new Thickness(8, 0, 0, 0);
            pRow.Children.Add(renew);
            sp.Children.Add(Row("Подключить телефон", "Если ссылка попала не туда — сделайте новую, старая перестанет работать.", pRow, false));

            sp = Sec("Дополнительно");
            sp.Children.Add(Header("Другие ИИ-агенты (MCP)"));
            var mRow = new StackPanel { Orientation = Orientation.Horizontal };
            var copy = PillBtn("Скопировать настройку", true, delegate
            {
                string tok = ClaudeBridge.Token();
                if (tok.Length == 0) { MessageBox.Show(this, "Сначала включите «Claude Code на острове» в разделе «Нейросети» — MCP работает через то же подключение.", "Kapsula"); return; }
                string json = "{\n  \"mcpServers\": {\n    \"kapsula\": {\n      \"type\": \"http\",\n      \"url\": \"" + ClaudeBridge.McpUrl + "\",\n      \"headers\": { \"X-Kapsula-Token\": \"" + tok + "\", \"X-Kapsula-Agent\": \"Мой агент\" }\n    }\n  }\n}";
                try { Clipboard.SetText(json); } catch { }
                MessageBox.Show(this, "Настройка скопирована. Вставьте её в файл MCP-серверов вашего агента (Cursor, Codex, Claude Desktop и т. п.) и поменяйте «Мой агент» на его имя.\n\nВ настройке есть секретный ключ — не публикуйте её.", "Kapsula");
            });
            mRow.Children.Add(copy);
            sp.Children.Add(Row("Остров для любых агентов", "Агенты с поддержкой MCP могут показывать на острове уведомления (notify) и задавать вопросы с кнопками (ask). Сервер слушает только этот компьютер: " + ClaudeBridge.McpUrl + " и пускает только с ключом.", mRow, true));
        }
        Border PillBtn(string text, bool main, Action click)
        {
            var b = new Border { Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 0, 14, 1), Background = main ? OnBg : ChipBg, Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, Foreground = main ? OnTxt : Txt, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } };
            b.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }
        // Скачивание Qwen: кнопка, ход скачивания, удаление
        Action qwenRefresh;
        FrameworkElement BuildQwenEditor()
        {
            var col = new StackPanel();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var status = new TextBlock { Foreground = Sub, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            var bar = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = ChipBg, Margin = new Thickness(0, 8, 0, 0), Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
            var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = OnBg, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            bar.Child = fill;
            col.Children.Add(row); col.Children.Add(bar); col.Children.Add(status);
            string shown = null;
            Action paint = null;
            paint = delegate
            {
                var m = QwenLocal.Chosen;
                string mode = QwenLocal.Busy ? "busy" : QwenLocal.Downloaded(m) && QwenLocal.EngineReady ? (QwenLocal.CanSpeedUp ? "ready-slow" : "ready") : "none";
                if (mode != shown + "") { }
                string key = mode + m.Id;
                if (key != shown)
                {
                    shown = key;
                    row.Children.Clear();
                    if (mode == "busy") row.Children.Add(PillBtn("Отменить", false, delegate { QwenLocal.Cancel(); }));
                    else if (mode.StartsWith("ready"))
                    {
                        var use = PillBtn(Ai.Local ? "Используется в чате" : "Отвечать в чате", !Ai.Local, delegate { Store.Set("aiProvider", "local"); shown = null; paint(); });
                        row.Children.Add(use);
                        var del = PillBtn("Удалить", false, delegate
                        {
                            if (MessageBox.Show(this, "Удалить Qwen " + m.Name + " с компьютера? Освободится около " + m.SizeGb.ToString("0.#") + " ГБ.", "Kapsula", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                            QwenLocal.Delete(m); shown = null; paint();
                        });
                        del.Margin = new Thickness(6, 0, 0, 0); row.Children.Add(del);
                        if (QwenLocal.CanSpeedUp)
                        {
                            var fast = PillBtn("Ускорить на NVIDIA · 0,6 ГБ", true, delegate { QwenLocal.SpeedUp(); shown = null; paint(); });
                            fast.ToolTip = "Скачать сборку llama.cpp на CUDA: на видеокартах NVIDIA отвечает в разы быстрее";
                            fast.Margin = new Thickness(6, 0, 0, 0); row.Children.Add(fast);
                        }
                    }
                    else row.Children.Add(PillBtn("Скачать Qwen " + m.Name + " · " + m.SizeGb.ToString("0.#") + " ГБ", true, delegate { QwenLocal.Download(m); shown = null; paint(); }));
                }
                bar.Visibility = QwenLocal.Busy ? Visibility.Visible : Visibility.Collapsed;
                if (QwenLocal.Busy)
                {
                    long d = System.Threading.Interlocked.Read(ref QwenLocal.Done), tot = System.Threading.Interlocked.Read(ref QwenLocal.Total);
                    double p = tot > 0 ? Math.Min(1, (double)d / tot) : 0;
                    fill.Width = 320 * p;
                    status.Text = QwenLocal.Status + (tot > 0 ? " — " + Math.Round(p * 100) + "% · " + (d / 1048576.0).ToString("0") + " из " + (tot / 1048576.0).ToString("0") + " МБ" : "");
                }
                else if (QwenLocal.Error != null) status.Text = QwenLocal.Error + ".";
                else if (mode.StartsWith("ready")) status.Text = (QwenLocal.EngineKind.StartsWith("cuda") ? "Скачана, работает через CUDA. " : "Скачана. ") + "Всего занято " + QwenLocal.UsedGb().ToString("0.#") + " ГБ." + (Ai.Local ? " Отвечает в чате и в действиях с текстом." : "");
                else status.Text = QwenLocal.EngineReady ? "llama.cpp уже скачан, осталась модель." : "Пока ничего не скачано.";
            };
            qwenRefresh = delegate { shown = null; paint(); };
            paint();
            var tm = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            tm.Tick += delegate { paint(); };
            tm.Start();
            Closed += delegate { tm.Stop(); };
            return col;
        }

        // Поле для секретного ключа: сохранить, удалить, состояние
        FrameworkElement BuildSecretEditor(string name, string env)
        {
            var col = new StackPanel();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var pw = new PasswordBox { Width = 250, Height = 30, Background = ChipBg, Foreground = Txt, BorderThickness = new Thickness(0), Padding = new Thickness(10, 5, 10, 0), CaretBrush = Txt, FontSize = 13 };
            row.Children.Add(pw);
            var status = new TextBlock { Foreground = Sub, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            Action paint = delegate { status.Text = Secrets.IsSaved(name) ? "Ключ сохранён." : "Ключ не задан."; };
            var save = PillBtn("Сохранить", true, delegate { if (pw.Password.Trim().Length == 0) return; Secrets.SetKey(name, pw.Password.Trim()); pw.Clear(); paint(); });
            save.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(save);
            var del = PillBtn("Удалить", false, delegate { Secrets.SetKey(name, ""); pw.Clear(); paint(); });
            del.Margin = new Thickness(6, 0, 0, 0); row.Children.Add(del);
            col.Children.Add(row); col.Children.Add(status);
            paint();
            return col;
        }

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

    // Хозяин для диалогов WinForms (выбор шрифта): окно настроек, чтобы диалог не прятался за ним
    class DialogOwner : System.Windows.Forms.IWin32Window { public IntPtr Handle { get; set; } }
}
