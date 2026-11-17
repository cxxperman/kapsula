// Kapsula — «остров» для Windows. Часть исходного кода: Design.cs
// Оформление в духе Apple: кнопки, плитки и вкладки — «жидкое стекло», как в iOS и macOS последних лет.
// Полупрозрачная заливка чуть светлее сверху, тонкая светлая кромка с бликом в верхнем левом углу и едва заметным
// отсветом снизу; под курсором стекло светлеет, выбранное — плотнее остальных. Это только приём оформления:
// ни шрифтов, ни значков, ни картинок Apple в Kapsula нет.
using DevTxt;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Kapsula
{
    // Единые правила оформления — один язык для острова, карточек, мини-плеера и настроек.
    // Сами числа (цвета, стекло, радиусы, шрифты, длительности) — в Tokens.cs: это общие токены dev.txt, файл
    // собирается в kit.txt и руками не правится. Здесь — как из них получается оформление Kapsula:
    //   • блок (плитка, плеер, громкость, календарь, прибор погоды, карточка) — стекло, углы 18;
    //   • строка списка (уведомление, задача, сеть Wi-Fi, строка буфера) — стекло, углы 14;
    //   • кнопка — стекло-капсула; главная — залита цветом; значок — без подложки, под курсором стекло;
    //   • выбранное (вкладка, раздел, режим) — плотное стекло с кромкой, одинаково во всех переключателях;
    //   • шрифты (по этой шкале — всё новое и переделанное): заголовок 17, заголовок блока 13.5, текст 12.5,
    //     подпись 11.5, мелкая подпись 10.5;
    //   • зверёк — главное: своя вкладка с лапкой сразу после «Главной», первый раздел настроек, пустые списки
    //     и подсказки — с ним; его цвет (PetColor) — только для того, что про него.
    static class Ui
    {
        public const double BlockR = Tokens.Radius.Block, RowR = Tokens.Radius.Row;
        public static readonly CornerRadius Block = new CornerRadius(BlockR), Row = new CornerRadius(RowR);

        // Переключатель из нескольких вариантов (вкладки, режимы): выбранный — плотное стекло с кромкой
        public static void Seg(Border b, bool on)
        {
            b.Background = on ? (Brush)Glass.Strong : Brushes.Transparent;
            b.BorderBrush = on ? (Brush)Glass.Rim : Brushes.Transparent;
            b.BorderThickness = new Thickness(1);
        }

        // Дорожка такого переключателя — стеклянная капсула
        public static void Track(Border b)
        {
            b.Background = Glass.Fill; b.BorderBrush = Glass.Rim; b.BorderThickness = new Thickness(1);
            if (!double.IsNaN(b.Height) && b.Height > 0) b.CornerRadius = new CornerRadius(b.Height / 2);
        }

        // Текст не больше n строк: переносится по словам (длинное слово без пробелов — по буквам), а то, что
        // не влезло, кончается «…» в последней строке. Высота строки — своя, если задана, иначе по размеру шрифта.
        public static void Lines(TextBlock tb, int n)
        {
            tb.TextWrapping = TextWrapping.Wrap;
            tb.TextTrimming = TextTrimming.CharacterEllipsis;
            double lh = !double.IsNaN(tb.LineHeight) && tb.LineHeight > 0 ? tb.LineHeight : Math.Ceiling(tb.FontSize * 1.32);
            tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            tb.LineHeight = lh;
            tb.MaxHeight = lh * n + 0.5;
        }

        // Короче, но по-человечески: середина уходит в «…», начало и расширение остаются
        // («Отчёт за третий кв….pdf» вместо «Отчёт за тр…»). max — сколько знаков всего.
        // Годится и для заголовков окон: расширение ищется без Path.GetExtension (он падает на «|», «<», «"»),
        // а эмодзи (пара половинок-суррогатов) пополам не режется.
        public static string MiddleEllipsis(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max || max < 6) return s;
            int dot = s.LastIndexOf('.');
            string ext = dot > 0 && dot < s.Length - 1 && s.Length - dot <= 8 && s.IndexOfAny(new char[] { ' ', '\\', '/' }, dot) < 0 ? s.Substring(dot) : "";
            string name = s.Substring(0, s.Length - ext.Length);
            int keep = max - ext.Length - 1;
            if (keep < 4)
            {
                int n = max - 1;
                if (char.IsHighSurrogate(s[n - 1])) n--;
                return s.Substring(0, n) + "…";
            }
            int head = (int)Math.Ceiling(keep * 0.7), tail = keep - head;
            if (char.IsHighSurrogate(name[head - 1])) head--;
            int from = name.Length - tail;
            if (from < name.Length && char.IsLowSurrogate(name[from])) from++;
            return name.Substring(0, head).TrimEnd() + "…" + name.Substring(from).TrimStart() + ext;
        }

        // Цвет зверька — им же помечено всё, что про него (раздел настроек, вкладка)
        public static readonly Color PetColor = Tokens.Palette.Brand;

        // Пиксельная лапка — значок зверька во вкладке острова и в разделе настроек. Рисунок свой.
        static readonly string[] PawMap = { "..XX.XX..", "..XX.XX..", "XX.....XX", "XX.XXX.XX", "..XXXXX..", ".XXXXXXX.", ".XXXXXXX.", "..XX.XX.." };
        static Geometry pawGeo;
        public static Geometry PawGeometry
        {
            get
            {
                if (pawGeo != null) return pawGeo;
                var sb = new System.Text.StringBuilder("F1");
                for (int y = 0; y < PawMap.Length; y++)
                    for (int x = 0; x < PawMap[y].Length; x++)
                        if (PawMap[y][x] == 'X') sb.Append(" M" + x + "," + y + "h1v1h-1z");
                pawGeo = Geometry.Parse(sb.ToString());
                pawGeo.Freeze();
                return pawGeo;
            }
        }

        public static System.Windows.Shapes.Path Paw(double cell, Brush fill)
        {
            return new System.Windows.Shapes.Path
            {
                Data = PawGeometry, Fill = fill, Width = 9 * cell, Height = 8 * cell, Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
            };
        }
    }

    static class Glass
    {
        // Стекло острова: кисти общие, тема острова (тёмная или светлая) перекрашивает их разом — Theme()
        public static readonly LinearGradientBrush Fill = Vertical(), Hover = Vertical(), Strong = Vertical(), Rim = Diagonal();
        // Окно настроек всегда тёмное — у него своё стекло, не зависящее от цвета острова
        public static readonly Brush DarkFill = Frozen(Vertical(), false, 0), DarkHover = Frozen(Vertical(), false, 1), DarkStrong = Frozen(Vertical(), false, 2), DarkRim = FrozenRim(false);

        static Glass() { Theme(false); }

        static LinearGradientBrush Vertical()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            b.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            b.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            return b;
        }

        static LinearGradientBrush Diagonal()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.7, 1) };
            foreach (double o in Tokens.GlassDark.RimOffsets) b.GradientStops.Add(new GradientStop(Colors.Transparent, o));
            return b;
        }

        static Color A(Color c, double a) { return Color.FromArgb((byte)Math.Round(255 * Math.Max(0, Math.Min(1, a))), c.R, c.G, c.B); }

        // Насколько плотное стекло: 0 — обычная кнопка, 1 — под курсором, 2 — выбранное
        static void Paint(LinearGradientBrush b, bool light, int level)
        {
            double top, bottom;
            // Плотность стекла — из общих токенов (kit.txt): обычное, под курсором, выбранное
            if (light)
            {
                top = new double[] { Tokens.GlassLight.FillTop, Tokens.GlassLight.HoverTop, Tokens.GlassLight.StrongTop }[level];
                bottom = new double[] { Tokens.GlassLight.FillBottom, Tokens.GlassLight.HoverBottom, Tokens.GlassLight.StrongBottom }[level];
            }
            else
            {
                top = new double[] { Tokens.GlassDark.FillTop, Tokens.GlassDark.HoverTop, Tokens.GlassDark.StrongTop }[level];
                bottom = new double[] { Tokens.GlassDark.FillBottom, Tokens.GlassDark.HoverBottom, Tokens.GlassDark.StrongBottom }[level];
            }
            b.GradientStops[0].Color = A(Colors.White, top);
            b.GradientStops[1].Color = A(Colors.White, bottom);
        }

        static void PaintRim(LinearGradientBrush b, bool light)
        {
            Color[] rim = light ? Tokens.GlassLight.RimColors : Tokens.GlassDark.RimColors;
            for (int i = 0; i < rim.Length; i++) b.GradientStops[i].Color = rim[i];
        }

        static Brush Frozen(LinearGradientBrush b, bool light, int level) { Paint(b, light, level); b.Freeze(); return b; }
        static Brush FrozenRim(bool light) { var b = Diagonal(); PaintRim(b, light); b.Freeze(); return b; }

        public static void Theme(bool light)
        {
            Paint(Fill, light, 0); Paint(Hover, light, 1); Paint(Strong, light, 2);
            PaintRim(Rim, light);
        }

        // Кнопка или плитка из стекла острова: светлеет под курсором
        public static void Button(Border b)
        {
            b.Background = Fill; b.BorderBrush = Rim; b.BorderThickness = new Thickness(1);
            b.MouseEnter += delegate { if (b.Background == Fill) b.Background = Hover; };
            b.MouseLeave += delegate { if (b.Background == Hover) b.Background = Fill; };
        }

        // То же для тёмного окна настроек
        public static void DarkButton(Border b)
        {
            b.Background = DarkFill; b.BorderBrush = DarkRim; b.BorderThickness = new Thickness(1);
            b.MouseEnter += delegate { if (b.Background == DarkFill) b.Background = DarkHover; };
            b.MouseLeave += delegate { if (b.Background == DarkHover) b.Background = DarkFill; };
        }

        // Подсказки во всей программе — тёмное стекло со скруглёнными углами, тонкой светлой кромкой и мягкой тенью,
        // как в macOS; длинный текст переносится, а не тянется во всю ширину экрана.
        public static void Tooltips(ResourceDictionary res)
        {
            try
            {
                var style = (Style)System.Windows.Markup.XamlReader.Parse(
                    "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ToolTip'>" +
                    "<Setter Property='OverridesDefaultStyle' Value='True'/><Setter Property='HasDropShadow' Value='False'/>" +
                    "<Setter Property='Foreground' Value='#F5F5F7'/><Setter Property='FontSize' Value='12'/>" +
                    "<Setter Property='FontFamily' Value='" + Tokens.Font.Text + "'/>" +
                    "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToolTip'><Grid Margin='8,4,8,12'>" +
                    "<Border CornerRadius='" + Tokens.Radius.Tip + "' Background='#F2242427'><Border.Effect><DropShadowEffect BlurRadius='14' ShadowDepth='3' Direction='270' Opacity='0.42' Color='Black'/></Border.Effect></Border>" +
                    "<Border CornerRadius='" + Tokens.Radius.Tip + "' BorderThickness='1' Padding='9,5,9,6' MaxWidth='300'><Border.BorderBrush><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'>" +
                    "<GradientStop Color='#47FFFFFF' Offset='0'/><GradientStop Color='#14FFFFFF' Offset='1'/></LinearGradientBrush></Border.BorderBrush>" +
                    "<ContentPresenter><ContentPresenter.Resources><Style TargetType='TextBlock'><Setter Property='TextWrapping' Value='Wrap'/></Style></ContentPresenter.Resources></ContentPresenter>" +
                    "</Border></Grid></ControlTemplate></Setter.Value></Setter></Style>");
                res[typeof(ToolTip)] = style;
            }
            catch { }
        }

        // Главная кнопка, как в iOS: залита цветом, с тем же бликом сверху
        public static Brush Tinted(Color c)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            b.GradientStops.Add(new GradientStop(Color.FromRgb((byte)Math.Min(255, c.R + 26), (byte)Math.Min(255, c.G + 26), (byte)Math.Min(255, c.B + 26)), 0));
            b.GradientStops.Add(new GradientStop(c, 1));
            b.Freeze();
            return b;
        }
    }

    // Переключатель как в iOS: бегунок скользит с мягким разгоном и торможением, дорожка плавно наливается
    // зелёным; у бегунка лёгкая тень. При «Меньше движения» — сразу на месте.
    class Switch
    {
        public readonly Border Root;
        readonly TranslateTransform knobX = new TranslateTransform();
        readonly SolidColorBrush track;
        readonly Color onColor, offColor;
        bool on;
        public static bool Reduced;   // «Меньше движения»
        const double W = 46, H = 28, Knob = 24;

        public Switch(bool state, Color onC, Color offC, Action<bool> changed)
        {
            on = state; onColor = onC; offColor = offC;
            track = new SolidColorBrush(on ? onC : offC);
            var knob = new Border
            {
                Width = Knob, Height = Knob, CornerRadius = new CornerRadius(Knob / 2), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0), RenderTransform = knobX,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1.5, Direction = 270, Opacity = 0.28, Color = Colors.Black }
            };
            knobX.X = on ? W - Knob - 4 : 0;
            Root = new Border { Width = W, Height = H, CornerRadius = new CornerRadius(H / 2), Background = track, Child = knob, Cursor = Cursors.Hand };
            Root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                on = !on;
                Move();
                changed(on);
            };
        }

        void Move()
        {
            double to = on ? W - Knob - 4 : 0;
            Color c = on ? onColor : offColor;
            if (Reduced) { knobX.BeginAnimation(TranslateTransform.XProperty, null); knobX.X = to; track.BeginAnimation(SolidColorBrush.ColorProperty, null); track.Color = c; return; }
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            knobX.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(Tokens.Motion.BaseMs)) { EasingFunction = ease });
            track.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(Tokens.Motion.BaseMs)) { EasingFunction = ease });
        }
    }

    // Бегущая строка, как в плеерах Apple: если текст не влезает, после паузы он плавно уезжает влево до конца,
    // стоит, возвращается к началу и стоит снова; у правого края — мягкое затухание вместо обрезанной буквы,
    // а пока строка сдвинута, тает и левый край. Влезает — стоит на месте. При «Меньше движения» не едет,
    // остаётся затухание. Таймер идёт, только пока текст длиннее места, блок виден и строку не остановили (Paused).
    // Использование: var m = new Marquee(textBlock); кладётся m.Root.
    class Marquee
    {
        public readonly Grid Root;
        public readonly TextBlock Text;
        public static bool Reduced;   // «Меньше движения»
        readonly Canvas box = new Canvas { ClipToBounds = true };
        readonly TranslateTransform tx = new TranslateTransform();
        readonly System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer();
        bool back, paused;
        double overflow;

        // Остановить эту строку (остров спрятан или стоит поверх игры): она встаёт к началу с затуханием и не едет,
        // пока её не отпустят, — каждый её шаг перерисовывает окно. Полный текст — в подсказке у того, кто её показывает.
        public bool Paused
        {
            get { return paused; }
            set { if (paused == value) return; paused = value; Restart(); }
        }

        // Затухание краёв — в точках самого окошка шириной w, как у текста песни (LyricView), а не в долях: маска в долях
        // ложилась на всю длинную строку, далеко за обрезом, и буква у края резалась пополам без всякого затухания.
        // Последние 10% ширины тают справа; left — пока строка сдвинута, тает и левый край (16 точек), чтобы
        // уезжающая буква не была срезана посередине.
        static Brush EdgeFade(double w, bool left)
        {
            w = Math.Max(1, w);
            var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(w, 0) };
            b.GradientStops.Add(new GradientStop(left ? Colors.Transparent : Colors.Black, 0));
            if (left) b.GradientStops.Add(new GradientStop(Colors.Black, Math.Min(0.1, 16 / w)));
            b.GradientStops.Add(new GradientStop(Colors.Black, 0.9));
            b.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            b.Freeze();
            return b;
        }

        public Marquee(TextBlock tb)
        {
            Text = tb;
            tb.TextTrimming = TextTrimming.None; tb.TextWrapping = TextWrapping.NoWrap; tb.RenderTransform = tx;
            box.Children.Add(tb);
            Root = new Grid { Children = { box } };
            tb.SizeChanged += delegate { box.Height = tb.ActualHeight; Restart(); };
            Root.SizeChanged += delegate { Restart(); };
            Root.IsVisibleChanged += delegate { Restart(); };
            timer.Tick += delegate { Step(); };
        }

        // Заново с начала: сменился текст или ширина, блок показали или спрятали
        public void Restart()
        {
            timer.Stop(); back = false;
            tx.BeginAnimation(TranslateTransform.XProperty, null); tx.X = 0;
            overflow = Text.ActualWidth - Root.ActualWidth;
            box.OpacityMask = overflow > 1 ? EdgeFade(Root.ActualWidth, false) : null;   // ширина сменилась — маска заново по ней
            if (overflow > 1 && !Reduced && !paused && Root.IsVisible) { timer.Interval = TimeSpan.FromMilliseconds(2500); timer.Start(); }
        }

        void Step()
        {
            timer.Stop();
            if (Reduced || paused) { Restart(); return; }   // «Меньше движения» включили (или строку остановили) на полпути — встаёт к началу и не едет
            if (!Root.IsVisible) return;
            double to = back ? 0 : -(overflow + Root.ActualWidth * 0.1 + 4);   // до конца, чтобы последняя буква вышла из затухания
            double ms = back ? 600 : Math.Max(900, -to / 32.0 * 1000);         // 32 точки в секунду — читать удобно
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            // поехала — тает и левый край; вернулась к началу — снова только правый
            if (!back) box.OpacityMask = EdgeFade(Root.ActualWidth, true);
            else anim.Completed += delegate { if (overflow > 1) box.OpacityMask = EdgeFade(Root.ActualWidth, false); };
            tx.BeginAnimation(TranslateTransform.XProperty, anim);
            timer.Interval = TimeSpan.FromMilliseconds(ms + (back ? 3500 : 1800));
            back = !back;
            timer.Start();
        }
    }
}
