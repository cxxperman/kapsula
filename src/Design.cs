// Kapsula — «остров» для Windows. Часть исходного кода: Design.cs
// Оформление в духе Apple: кнопки, плитки и вкладки — «жидкое стекло», как в iOS и macOS последних лет.
// Полупрозрачная заливка чуть светлее сверху, тонкая светлая кромка с бликом в верхнем левом углу и едва заметным
// отсветом снизу; под курсором стекло светлеет, выбранное — плотнее остальных. Это только приём оформления:
// ни шрифтов, ни значков, ни картинок Apple в Kapsula нет.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Kapsula
{
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
            foreach (double o in new double[] { 0, 0.32, 0.68, 1 }) b.GradientStops.Add(new GradientStop(Colors.Transparent, o));
            return b;
        }

        static Color A(Color c, double a) { return Color.FromArgb((byte)Math.Round(255 * Math.Max(0, Math.Min(1, a))), c.R, c.G, c.B); }

        // Насколько плотное стекло: 0 — обычная кнопка, 1 — под курсором, 2 — выбранное
        static void Paint(LinearGradientBrush b, bool light, int level)
        {
            double top, bottom;
            if (light) { top = new double[] { 0.86, 0.96, 1.0 }[level]; bottom = new double[] { 0.66, 0.80, 0.90 }[level]; }
            else { top = new double[] { 0.15, 0.22, 0.27 }[level]; bottom = new double[] { 0.075, 0.12, 0.16 }[level]; }
            b.GradientStops[0].Color = A(Colors.White, top);
            b.GradientStops[1].Color = A(Colors.White, bottom);
        }

        static void PaintRim(LinearGradientBrush b, bool light)
        {
            if (light)
            {
                b.GradientStops[0].Color = A(Colors.White, 1); b.GradientStops[1].Color = A(Colors.Black, 0.07);
                b.GradientStops[2].Color = A(Colors.Black, 0.06); b.GradientStops[3].Color = A(Colors.Black, 0.12);
            }
            else
            {
                b.GradientStops[0].Color = A(Colors.White, 0.5); b.GradientStops[1].Color = A(Colors.White, 0.13);
                b.GradientStops[2].Color = A(Colors.White, 0.05); b.GradientStops[3].Color = A(Colors.White, 0.17);
            }
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
                    "<Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI'/>" +
                    "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToolTip'><Grid Margin='8,4,8,12'>" +
                    "<Border CornerRadius='9' Background='#F2242427'><Border.Effect><DropShadowEffect BlurRadius='14' ShadowDepth='3' Direction='270' Opacity='0.42' Color='Black'/></Border.Effect></Border>" +
                    "<Border CornerRadius='9' BorderThickness='1' Padding='9,5,9,6' MaxWidth='300'><Border.BorderBrush><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'>" +
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
            knobX.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease });
            track.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease });
        }
    }
}
