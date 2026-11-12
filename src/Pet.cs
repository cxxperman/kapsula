// Kapsula — «остров» для Windows. Часть исходного кода: Pet.cs
// Зверёк — пиксельный питомец Kapsula. Этот класс рисует его в любом месте интерфейса и сам оживляет:
// настроение задаётся одним словом, движение идёт только пока зверёк виден на экране.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Kapsula
{
    class PetView
    {
        static readonly Brush Body = new SolidColorBrush(Color.FromRgb(0xF0, 0x8A, 0x4B));
        static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
        static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
        static readonly Brush Grey = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96));
        static readonly string[] Shape =
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
        static readonly Random rnd = new Random();

        // ---- сезонные аксессуары: колпак под Новый год, шляпа в конец октября, шарф зимой. Выключаются в настройках.
        public static int TestSeason = -1;   // ключ --season: 1 колпак, 2 шляпа, 3 шарф
        public static int Season()
        {
            if (TestSeason >= 0) return TestSeason;
            if (Store.Get("petSeason") == "0") return 0;
            DateTime d = DateTime.Now;
            if (d.Month == 12 && d.Day >= 20 || d.Month == 1 && d.Day <= 7) return 1;
            if (d.Month == 10 && d.Day >= 25) return 2;
            if (d.Month == 12 || d.Month <= 2) return 3;
            return 0;
        }

        // Клетки аксессуара: x, y, ширина, цвет (0 красный, 1 белый, 2 фиолетовый, 3 оранжевый, 4 синий)
        static readonly int[][][] Outfits =
        {
            new int[0][],
            new int[][] { new int[] { 2, 0, 7, 1 }, new int[] { 3, -1, 5, 0 }, new int[] { 4, -2, 4, 0 }, new int[] { 6, -3, 2, 0 }, new int[] { 8, -3, 1, 1 } },
            new int[][] { new int[] { 1, 0, 9, 2 }, new int[] { 3, -1, 5, 3 }, new int[] { 3, -2, 5, 2 }, new int[] { 4, -3, 3, 2 }, new int[] { 5, -4, 1, 2 } },
            new int[][] { new int[] { 1, 7, 9, 4 }, new int[] { 7, 8, 2, 4 } }
        };
        static readonly Color[] OutfitColors = { Color.FromRgb(0xE5, 0x48, 0x4D), Color.FromRgb(0xF4, 0xF4, 0xF6), Color.FromRgb(0x7B, 0x4F, 0xBF), Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0x4F, 0x8F, 0xE0) };

        public static void Dress(Canvas c, double px)
        {
            foreach (int[] o in Outfits[Season()])
            {
                var r = new Rectangle { Width = o[2] * px + 0.3, Height = px + 0.3, Fill = new SolidColorBrush(OutfitColors[o[3]]), SnapsToDevicePixels = true };
                Canvas.SetLeft(r, o[0] * px); Canvas.SetTop(r, o[1] * px);
                c.Children.Add(r);
            }
        }

        public readonly StackPanel Root;
        readonly double px;
        readonly TranslateTransform shift = new TranslateTransform();
        readonly List<Rectangle>[] eyes = new List<Rectangle>[5];   // 0 обычные, 1 вниз, 2 круглые, 3 довольные, 4 закрытые
        readonly TextBlock mark;
        readonly DispatcherTimer timer;
        string mood = "idle";
        int face = -1, blinkAt, blinkUntil, lookAt, lookDir, born = Environment.TickCount;

        Rectangle Pixel(Canvas c, int x, int y, int w, int h, Brush fill)
        {
            var r = new Rectangle { Width = w * px + 0.3, Height = h * px + 0.3, Fill = fill, SnapsToDevicePixels = true };
            Canvas.SetLeft(r, x * px); Canvas.SetTop(r, y * px);
            r.Tag = new double[] { x * px, y * px };
            c.Children.Add(r);
            return r;
        }

        // Зверёк как значок программы: картинка size×size на прозрачном фоне (для значка у часов, окон и файла)
        public static System.Drawing.Bitmap Bitmap(int size) { return Bitmap(size, 0); }

        // variant: 0 обычный, 1 спит («Не беспокоить»), 2 с красной точкой-счётчиком (непрочитанное), 3 идёт запись
        public static System.Drawing.Bitmap Bitmap(int size, int variant)
        {
            var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                float cell = size / 12f, ox = (size - cell * 11) / 2f, oy = (size - cell * 9) / 2f;
                using (var body = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xF0, 0x8A, 0x4B)))
                using (var eye = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x1A, 0x1A, 0x1E)))
                {
                    for (int y = 0; y < Shape.Length; y++)
                        for (int x = 0; x < 11; x++)
                            if (Shape[y][x] == 'B') g.FillRectangle(body, ox + x * cell, oy + y * cell, cell + 0.5f, cell + 0.5f);
                    if (variant == 1)
                    {
                        g.FillRectangle(eye, ox + 1 * cell, oy + 5 * cell, cell * 3, cell);
                        g.FillRectangle(eye, ox + 7 * cell, oy + 5 * cell, cell * 3, cell);
                    }
                    else
                    {
                        g.FillRectangle(eye, ox + 2 * cell, oy + 4 * cell, cell, cell * 2);
                        g.FillRectangle(eye, ox + 8 * cell, oy + 4 * cell, cell, cell * 2);
                    }
                }
                if (variant >= 2)
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    float d = size * 0.42f;
                    using (var dot = new System.Drawing.SolidBrush(variant == 3 ? System.Drawing.Color.FromArgb(0xFF, 0x3B, 0x30) : System.Drawing.Color.FromArgb(0xE5, 0x48, 0x4D)))
                        g.FillEllipse(dot, size - d - 0.5f, variant == 3 ? size - d - 0.5f : 0.5f, d, d);
                }
            }
            return bmp;
        }

        // Тот же значок для окон WPF
        public static ImageSource WindowIcon()
        {
            try
            {
                using (var bmp = Bitmap(64))
                using (var ms = new System.IO.MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    var bi = new System.Windows.Media.Imaging.BitmapImage();
                    bi.BeginInit(); bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
                    return bi;
                }
            }
            catch { return null; }
        }

        // pixel — размер одной клетки рисунка; eyeBrush — цвет глаз (обычно цвет фона под зверьком)
        public PetView(double pixel, Brush eyeBrush)
        {
            px = pixel;
            var c = new Canvas { Width = 11 * px, Height = 9 * px, RenderTransform = shift, VerticalAlignment = VerticalAlignment.Center };
            for (int y = 0; y < Shape.Length; y++)
            {
                int x = 0;
                while (x < 11)
                {
                    if (Shape[y][x] != 'B') { x++; continue; }
                    int start = x;
                    while (x < 11 && Shape[y][x] == 'B') x++;
                    Pixel(c, start, y, x - start, 1, Body);
                }
            }
            int[][][] sets =
            {
                new int[][] { new int[] { 2, 4, 1, 2 } },
                new int[][] { new int[] { 2, 5, 1, 2 } },
                new int[][] { new int[] { 2, 3, 2, 3 } },
                new int[][] { new int[] { 1, 5, 1, 1 }, new int[] { 2, 4, 1, 1 }, new int[] { 3, 5, 1, 1 } },
                new int[][] { new int[] { 1, 5, 3, 1 } }
            };
            for (int m = 0; m < sets.Length; m++)
            {
                eyes[m] = new List<Rectangle>();
                foreach (int[] e in sets[m])
                {
                    eyes[m].Add(Pixel(c, e[0], e[1], e[2], e[3], eyeBrush));
                    eyes[m].Add(Pixel(c, 11 - e[0] - e[2], e[1], e[2], e[3], eyeBrush));
                }
            }
            Dress(c, px);
            mark = new TextBlock { FontSize = Math.Max(10, px * 6.5), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(px * 1.4, -px * 2.2, 0, 0), MinWidth = px * 5 };
            Root = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            Root.Children.Add(c); Root.Children.Add(mark);
            Face(0, "", null);
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += delegate { Step(); };
            // Двигается, только пока его видно: спрятанный зверёк ничего не тратит
            Root.IsVisibleChanged += delegate { if (Root.IsVisible) timer.Start(); else timer.Stop(); };
        }

        // Настроение: idle — смотрит по сторонам и моргает; think — думает; work — трудится; ask — ждёт вашего решения;
        // happy — радуется; done — доволен, с галочкой; sleep — спит; alarm — тревога; sad — огорчён
        public void Mood(string m) { if (m != mood) { mood = m; born = Environment.TickCount; } }
        public string CurrentMood { get { return mood; } }
        public string Prop = "";        // значок рядом со зверьком в спокойном состоянии: зонтик, снежинка…
        public Brush PropBrush;

        void Face(int kind, string sign, Brush signBrush)
        {
            if (kind != face)
            {
                face = kind;
                for (int m = 0; m < eyes.Length; m++)
                    foreach (Rectangle r in eyes[m]) r.Visibility = m == kind ? Visibility.Visible : Visibility.Collapsed;
            }
            if (mark.Text != sign) mark.Text = sign;
            mark.Foreground = signBrush ?? Grey;
        }

        void Step()
        {
            int now = Environment.TickCount;
            double t = now / 1000.0, age = unchecked(now - born) / 1000.0;
            shift.X = 0; shift.Y = 0; mark.Opacity = 1;
            double hop = px * 1.4;
            if (mood != "hello" && mark.RenderTransform != Transform.Identity) mark.RenderTransform = Transform.Identity;
            switch (mood)
            {
                case "think":
                    Face(1, new string('.', 1 + (now / 380) % 3), Grey);
                    shift.Y = Math.Round(Math.Sin(t * 3) * hop * 0.35 * 2) / 2;
                    break;
                case "work":
                    Face(1, "", null);
                    shift.Y = Math.Round(Math.Abs(Math.Sin(t * 5.5)) * -hop * 2) / 2;
                    break;
                case "ask":
                    Face(2, "!", Orange);
                    mark.Opacity = (now / 420) % 2 == 0 ? 1 : 0.25;
                    break;
                case "happy":
                    Face(3, "", null);
                    shift.Y = Math.Round(Math.Abs(Math.Sin(t * 7)) * -hop * 0.8 * 2) / 2;
                    break;
                case "done":
                    Face(3, "✓", Green);
                    // сначала пара радостных прыжков, потом стоит довольный
                    if (age < 1.2) shift.Y = Math.Round(Math.Abs(Math.Sin(age * 8)) * -hop * 2) / 2;
                    break;
                case "sleep":
                    Face(4, "z", Grey);
                    mark.Opacity = 0.35 + 0.45 * (0.5 + 0.5 * Math.Sin(t * 1.6));
                    shift.Y = Math.Round(Math.Sin(t * 1.2) * px * 0.3 * 2) / 2;   // едва заметно «дышит»
                    break;
                case "alarm":
                    Face(2, "!", Brushes.OrangeRed);
                    shift.X = rnd.NextDouble() * hop - hop / 2; shift.Y = rnd.NextDouble() * hop * 0.7 - hop * 0.35;
                    break;
                case "hello":   // машет новому человеку
                    Face(3, "✋", Orange);
                    shift.Y = Math.Round(Math.Abs(Math.Sin(t * 5)) * -hop * 0.7 * 2) / 2;
                    mark.RenderTransform = new RotateTransform(Math.Sin(t * 7) * 18, px * 2, px * 4);
                    break;
                case "gift":    // с подарком: обновление приехало
                    Face(3, "✦", Orange);
                    if (age < 2.4) shift.Y = Math.Round(Math.Abs(Math.Sin(age * 7)) * -hop * 2) / 2;
                    mark.Opacity = 0.55 + 0.45 * Math.Abs(Math.Sin(t * 3));
                    break;
                case "tea":     // перерыв: пьёт чай
                    Face(4, "☕", Grey);
                    shift.Y = Math.Round(Math.Sin(t * 1.4) * px * 0.3 * 2) / 2;
                    break;
                case "run":     // бежит вместе с таймером
                    Face(1, "", null);
                    shift.X = Math.Round(Math.Sin(t * 6) * hop * 0.6 * 2) / 2;
                    shift.Y = Math.Round(Math.Abs(Math.Sin(t * 12)) * -hop * 0.6 * 2) / 2;
                    break;
                case "hurt":    // что-то сломалось
                    Face(1, "✚", Brushes.OrangeRed);
                    shift.Y = px * 0.6;
                    break;
                case "sad":
                    Face(1, "", null);
                    shift.Y = px * 0.6;
                    break;
                default:
                    // Просто живёт: моргает и время от времени смотрит то влево, то вправо
                    if (blinkAt == 0) blinkAt = now + 2000 + rnd.Next(3500);
                    if (unchecked(now - blinkAt) >= 0) { blinkUntil = now + 140; blinkAt = now + 2200 + rnd.Next(4200); }
                    if (lookAt == 0 || unchecked(now - lookAt) >= 0) { lookDir = rnd.Next(3) - 1; lookAt = now + 1400 + rnd.Next(2600); }
                    if (unchecked(now - blinkUntil) < 0) { Face(4, Prop, PropBrush); break; }
                    Face(0, Prop, PropBrush);
                    foreach (Rectangle r in eyes[0])
                    {
                        double[] home = (double[])r.Tag;
                        Canvas.SetLeft(r, home[0] + lookDir * px);
                    }
                    break;
            }
        }
    }
}
