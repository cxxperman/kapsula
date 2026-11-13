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
        static readonly Brush Paper = new SolidColorBrush(Color.FromRgb(0xF4, 0xEF, 0xE2));   // конверт, который зверёк приносит с уведомлением
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

        // Клетки аксессуара: x, y, ширина, цвет (0 красный, 1 белый, 2 фиолетовый, 3 оранжевый, 4 синий, 5 золото, 6 графит)
        static readonly int[][][] Outfits =
        {
            new int[0][],
            new int[][] { new int[] { 2, 0, 7, 1 }, new int[] { 3, -1, 5, 0 }, new int[] { 4, -2, 4, 0 }, new int[] { 6, -3, 2, 0 }, new int[] { 8, -3, 1, 1 } },
            new int[][] { new int[] { 1, 0, 9, 2 }, new int[] { 3, -1, 5, 3 }, new int[] { 3, -2, 5, 2 }, new int[] { 4, -3, 3, 2 }, new int[] { 5, -4, 1, 2 } },
            new int[][] { new int[] { 1, 7, 9, 4 }, new int[] { 7, 8, 2, 4 } }
        };
        static readonly Color[] OutfitColors = { Color.FromRgb(0xE5, 0x48, 0x4D), Color.FromRgb(0xF4, 0xF4, 0xF6), Color.FromRgb(0x7B, 0x4F, 0xBF), Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0x4F, 0x8F, 0xE0), Color.FromRgb(0xF7, 0xC9, 0x48), Color.FromRgb(0x3A, 0x3A, 0x42) };

        // ---- наряды за привычки (их выдаёт остров, см. Habits.cs). Глаза открыты: зверёк в них по-прежнему всё показывает.
        public static readonly string[] HabitIds = { "band", "bow", "phones", "crown" };
        static readonly int[][][] HabitOutfits =
        {
            // повязка: красная лента по лбу и хвостик справа
            new int[][] { new int[] { 1, 2, 9, 0 }, new int[] { 10, 2, 1, 0 }, new int[] { 11, 3, 1, 0 } },
            // бабочка на груди: два крыла и узелок посередине
            new int[][] { new int[] { 3, 6, 2, 2 }, new int[] { 6, 6, 2, 2 }, new int[] { 3, 7, 2, 2 }, new int[] { 6, 7, 2, 2 }, new int[] { 5, 6, 1, 6 }, new int[] { 5, 7, 1, 6 } },
            // наушники: дужка над ушками и синие амбушюры по бокам
            new int[][] { new int[] { 1, -1, 9, 6 }, new int[] { 0, 0, 1, 6 }, new int[] { 10, 0, 1, 6 }, new int[] { 0, 1, 1, 6 }, new int[] { 10, 1, 1, 6 },
                          new int[] { -1, 2, 2, 4 }, new int[] { -1, 3, 2, 4 }, new int[] { 10, 2, 2, 4 }, new int[] { 10, 3, 2, 4 } },
            // корона между ушками: золотое основание, три зубца и рубин
            new int[][] { new int[] { 3, 1, 5, 5 }, new int[] { 3, 0, 1, 5 }, new int[] { 5, 0, 1, 5 }, new int[] { 7, 0, 1, 5 }, new int[] { 5, 1, 1, 0 } }
        };

        public static string Outfit = "";     // "" — по сезону, "none" — без наряда, иначе один из HabitIds
        public static int OutfitVersion;      // сменили наряд — каждый зверёк переодевается на своём следующем шаге

        static int[][] OutfitCells(string outfit)
        {
            if (outfit == "none") return new int[0][];
            int k = Array.IndexOf(HabitIds, outfit);
            return k >= 0 ? HabitOutfits[k] : Outfits[Season()];
        }

        public static void Dress(Canvas c, double px) { Dress(c, px, Outfit); }

        static void Dress(Canvas c, double px, string outfit)
        {
            foreach (int[] o in OutfitCells(outfit))
            {
                var r = new Rectangle { Width = o[2] * px + 0.3, Height = px + 0.3, Fill = new SolidColorBrush(OutfitColors[o[3]]), SnapsToDevicePixels = true };
                Canvas.SetLeft(r, o[0] * px); Canvas.SetTop(r, o[1] * px);
                c.Children.Add(r);
            }
        }

        public readonly StackPanel Root;
        readonly double px;
        public double Px { get { return px; } }
        // само тело (без значка рядом): по нему остров ставит попутчика точно на место этого зверька
        public FrameworkElement Figure { get { return body; } }
        Canvas body;
        readonly List<Rectangle> legs = new List<Rectangle>();      // нижний ряд: четыре лапки слева направо
        readonly List<UIElement> outfit = new List<UIElement>();    // сезонный наряд
        List<Rectangle> hat;
        bool seated, forced;
        readonly TranslateTransform shift = new TranslateTransform();
        readonly ScaleTransform squash = new ScaleTransform(1, 1);
        readonly RotateTransform tilt = new RotateTransform(0);
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
            var tg = new TransformGroup(); tg.Children.Add(squash); tg.Children.Add(tilt); tg.Children.Add(shift);
            var c = new Canvas { Width = 11 * px, Height = 9 * px, RenderTransform = tg, RenderTransformOrigin = new Point(0.5, 1), VerticalAlignment = VerticalAlignment.Center };
            body = c;
            // Тело — одной фигурой: если рисовать ряд за рядом, при наклоне между рядами видны тонкие щели.
            // Лапки — отдельно: они умеют свешиваться и болтаться.
            var torso = new GeometryGroup { FillRule = FillRule.Nonzero };
            for (int y = 0; y < Shape.Length; y++)
            {
                int x = 0;
                while (x < 11)
                {
                    if (Shape[y][x] != 'B') { x++; continue; }
                    int start = x;
                    while (x < 11 && Shape[y][x] == 'B') x++;
                    if (y == Shape.Length - 1) legs.Add(Pixel(c, start, y, x - start, 1, Body));
                    else torso.Children.Add(new RectangleGeometry(new Rect(start * px, y * px, (x - start) * px, px + (y < Shape.Length - 2 ? 0.3 : 0.15))));
                }
            }
            torso.Freeze();
            c.Children.Insert(0, new Path { Data = torso, Fill = Body, SnapsToDevicePixels = true });
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
            int dressed = c.Children.Count;
            Dress(c, px);
            for (int i = dressed; i < c.Children.Count; i++) outfit.Add(c.Children[i]);
            dressedAs = OutfitVersion;
            mark = new TextBlock { FontSize = Math.Max(10, px * 6.5), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(px * 1.4, -px * 2.2, 0, 0), MinWidth = px * 5 };
            Root = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            Root.Children.Add(c); Root.Children.Add(mark);
            Root.Tag = this;   // по метке остров находит зверьков на страницах
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

        // Сидит на краю острова, свесив лапки: две средние лапки длиннее и болтаются, крайние поджаты
        public bool Seated
        {
            get { return seated; }
            set
            {
                if (seated == value || legs.Count != 4) return;
                seated = value;
                for (int i = 0; i < legs.Count; i++)
                {
                    Rectangle r = legs[i];
                    bool inner = i == 1 || i == 2;
                    r.Visibility = !value || inner ? Visibility.Visible : Visibility.Collapsed;
                    r.Height = (value && inner ? 2 : 1) * px + 0.3;
                    Canvas.SetLeft(r, ((double[])r.Tag)[0]);
                }
            }
        }

        // Каска прораба — пока вкладку перестраивают (сезонный наряд на это время снимается)
        static readonly Brush HatYellow = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3D)), HatShade = new SolidColorBrush(Color.FromRgb(0xE0, 0x96, 0x1E));
        bool hatOn;
        public bool Hat
        {
            get { return hatOn; }
            set
            {
                if (hatOn == value) return;
                hatOn = value;
                if (value && hat == null)
                {
                    hat = new List<Rectangle>();
                    hat.Add(Pixel(body, 0, 1, 11, 1, HatShade));   // козырёк
                    hat.Add(Pixel(body, 2, 0, 7, 1, HatYellow));
                    hat.Add(Pixel(body, 3, -1, 5, 1, HatYellow));
                    hat.Add(Pixel(body, 5, -2, 1, 2, HatShade));   // гребень
                    hat.Add(Pixel(body, 4, -2, 3, 1, HatYellow));
                    hat.Add(Pixel(body, 5, -2, 1, 1, HatShade));
                }
                if (hat != null) foreach (Rectangle r in hat) r.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                foreach (UIElement o in outfit) o.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        // Сценка по команде — прыжок, кружение, «нет», взмах; поверх любого настроения
        public void Play(string e, int ms)
        {
            ev = e; lastEv = e; evAt = Environment.TickCount; evDur = Math.Max(200, ms); forced = true;
            mark.RenderTransform = Transform.Identity;
        }

        // Посмотреть в сторону: −1 влево, 1 вправо, 0 прямо — на ms миллисекунд (в спокойном настроении)
        public void Glance(int dir, int ms) { lookDir = Math.Max(-1, Math.Min(1, dir)); lookAt = Environment.TickCount + ms; }
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
            if (mark.Text != sign)
            {
                mark.Text = sign;
                // значок из шрифта Windows (колокольчик напоминания) — тем же шрифтом, что и все значки острова
                bool icon = sign.Length == 1 && sign[0] >= '' && sign[0] <= '';
                if (icon) mark.FontFamily = IconFont; else mark.ClearValue(TextBlock.FontFamilyProperty);
            }
            mark.Foreground = signBrush ?? Grey;
        }
        static readonly FontFamily IconFont = new FontFamily("Segoe MDL2 Assets");
        public const string BellSign = "";   // колокольчик — зверёк несёт напоминание

        // ---- Случайные сценки. Где живёт зверёк (Context), то он и делает: на полке таскает коробки, в чате «печатает»,
        // в погоде отряхивается от дождя или ловит снежинки, у таймера постукивает ногой. Раз в 6–14 секунд, на 1–2,5 секунды.
        public string Context = "";
        static readonly bool Fast = Environment.GetEnvironmentVariable("KAPSULA_PETEVENT") == "1";   // для проверки: сценки сразу и подряд
        string ev;
        int evAt, evDur, evNext;
        static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x4A));
        static readonly Brush Pink = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x8A));
        static readonly Brush Ice = new SolidColorBrush(Color.FromRgb(0x9E, 0xD8, 0xFF));
        static readonly Brush Drop = new SolidColorBrush(Color.FromRgb(0x4D, 0xA8, 0xFF));
        static readonly Brush Cardboard = new SolidColorBrush(Color.FromRgb(0xC8, 0x92, 0x5A));

        string[] EventsFor()
        {
            string m = mood == "" ? "idle" : mood;
            if (m == "sleep")
                switch (Context)
                {
                    case "clips": return new[] { "copydream", "peekclip", "snore" };      // буфер: снится копирование, проверяет, не скопировали ли что
                    case "notif": return new[] { "bell", "letter", "turn" };              // уведомления: вздрагивает от «звонка», снится письмо
                    case "claude": return new[] { "codedream", "mumble", "snore" };       // Claude Code: снится код, бормочет
                    default: return new[] { "snore", "turn", "dream", "peek" };
                }
            if (m == "think") return new[] { "scratch", "idea" };
            if (m == "run") return new[] { "sweat", "sprint" };
            if (m == "tea") return new[] { "sip", "sigh" };
            if (m != "idle") return null;   // радость, тревога, вопрос и прочие настроения не перебиваем
            switch (Context)
            {
                case "shelf": return new[] { "box", "sweep", "stretch", "wave", "sit" };
                case "chat": return new[] { "type", "tilt", "idea", "wave", "yawn" };
                case "timer": return new[] { "tap", "check", "yawn", "stretch" };
                case "cmd": return new[] { "peek", "nod", "yawn" };
                case "nav": return new[] { "wave", "tinker", "proud", "stretch", "spin" };   // настройки: подкручивает шестерёнку
                case "weather":
                    if (Prop.IndexOf('☂') >= 0 || Prop.IndexOf('☔') >= 0) return new[] { "shake", "look", "lean" };
                    if (Prop.IndexOf('❄') >= 0) return new[] { "shiver", "catch", "look" };
                    if (Prop.IndexOf('☀') >= 0) return new[] { "sunbathe", "stretch", "look" };
                    return new[] { "lean", "look", "catch" };
                case "": return null;
                default: return new[] { "look", "stretch", "yawn", "hop", "tilt" };
            }
        }

        bool EventTick(int now, double t, double hop)
        {
            if (Context == "" && !forced) return false;
            if (ev != null && unchecked(now - evAt) < evDur)
            {
                double p = unchecked(now - evAt) / (double)evDur;
                PlayEvent(ev, p, t, hop, now);
                return true;
            }
            if (ev != null) { ev = null; forced = false; mark.RenderTransform = Transform.Identity; evNext = now + (Fast ? 200 : 6000 + rnd.Next(8000)); }
            if (Context == "") return false;
            if (evNext == 0) evNext = now + (Fast ? 300 : 3000 + rnd.Next(7000));
            if (unchecked(now - evNext) >= 0)
            {
                string[] list = EventsFor();
                evNext = now + 6000 + rnd.Next(8000);
                if (list == null) return false;
                string pick = list[rnd.Next(list.Length)];
                if (pick == lastEv && list.Length > 1) pick = list[(Array.IndexOf(list, pick) + 1) % list.Length];
                ev = pick; lastEv = pick; evAt = now;
                evDur = pick == "snore" || pick == "box" || pick == "sweep" ? 2400 : pick == "spin" || pick == "hop" || pick == "nod" ? 1000 : 1600;
                PlayEvent(ev, 0, t, hop, now);
                return true;
            }
            return false;
        }
        string lastEv;

        static double Ease(double v) { v = Math.Max(0, Math.Min(1, v)); return v * v * (3 - 2 * v); }

        void LookEyes(int dir, int up)
        {
            foreach (Rectangle r in eyes[0]) { double[] home = (double[])r.Tag; Canvas.SetLeft(r, home[0] + dir * px); Canvas.SetTop(r, home[1] + up * px); }
        }

        void PlayEvent(string e, double p, double t, double hop, int now)
        {
            double bell = Math.Sin(p * Math.PI);
            mark.RenderTransform = Transform.Identity;
            switch (e)
            {
                case "look":     // огляделся: влево, вправо, прямо
                    Face(0, Prop, PropBrush); LookEyes(p < 0.33 ? -1 : p < 0.66 ? 1 : 0, 0); break;
                case "stretch":  // потянулся вверх
                    Face(4, "", null); squash.ScaleY = 1 + 0.18 * bell; squash.ScaleX = 1 - 0.08 * bell; break;
                case "yawn":     // зевнул
                    Face(4, "o", Grey); squash.ScaleY = 1 + 0.06 * bell; mark.Opacity = bell; break;
                case "hop":      // пара прыжков от хорошего настроения
                    Face(3, "", null); shift.Y = -Math.Abs(Math.Sin(p * Math.PI * 2)) * hop * 1.2; break;
                case "tilt":     // наклонил голову: «а?»
                    Face(2, "?", Grey); tilt.Angle = bell * 14; break;
                case "wave":     // помахал
                    Face(3, "✋", Orange); mark.RenderTransform = new RotateTransform(Math.Sin(t * 9) * 20, px * 2, px * 4); shift.Y = -Math.Abs(Math.Sin(p * Math.PI * 3)) * hop * 0.4; break;
                case "spin":     // крутанулся на месте
                    Face(3, "", null); tilt.Angle = 360 * Ease(p); shift.Y = -bell * hop; break;
                case "proud":    // гордо выпрямился
                    Face(3, "✦", Yellow); squash.ScaleY = 1 + 0.06 * bell; mark.Opacity = 0.4 + 0.6 * Math.Abs(Math.Sin(t * 4)); break;
                case "sit":      // присел отдохнуть
                    Face(3, "", null); squash.ScaleY = 1 - 0.14 * bell; squash.ScaleX = 1 + 0.08 * bell; break;
                case "box":      // таскает коробку по полке
                    Face(1, "■", Cardboard); shift.X = Math.Sin(p * Math.PI * 2) * hop * 1.2; shift.Y = -Math.Abs(Math.Sin(p * Math.PI * 6)) * hop * 0.5; break;
                case "sweep":    // подметает полку
                    Face(1, "~", Grey); shift.X = Math.Sin(p * Math.PI * 6) * hop * 0.7; tilt.Angle = Math.Sin(p * Math.PI * 6) * 6; break;
                case "type":     // «печатает» ответ
                    Face(1, new string('.', 1 + (now / 200) % 3), Grey); shift.Y = -Math.Abs(Math.Sin(t * 18)) * hop * 0.25; break;
                case "idea":     // осенило!
                    Face(2, "!", Yellow); shift.Y = p < 0.4 ? -Math.Sin(p / 0.4 * Math.PI) * hop * 1.4 : 0; break;
                case "snore":    // храпит: «Z» раздувается и лопается
                {
                    Face(4, p < 0.85 ? "Z" : "○", Grey);
                    double s = p < 0.85 ? 0.6 + p : 1.4;
                    mark.RenderTransform = new ScaleTransform(s, s, 0, px * 4);
                    mark.Opacity = p < 0.85 ? 0.8 : 1 - (p - 0.85) * 6;
                    squash.ScaleY = 1 + Math.Sin(t * 2) * 0.03;
                    break;
                }
                case "turn":     // ворочается во сне
                    Face(4, "z", Grey); tilt.Angle = bell * 12; squash.ScaleY = 1 - 0.06 * bell; break;
                case "dream":    // снится что-то хорошее
                    Face(4, "♥", Pink); mark.Opacity = 0.3 + 0.7 * bell; break;
                case "peek":     // приоткрыл глаза, огляделся — и снова спать
                    if (p < 0.15 || p > 0.85) Face(4, "z", Grey); else { Face(0, "", null); LookEyes(p < 0.5 ? -1 : 1, 0); }
                    break;
                case "peekclip": // во сне приоткрыл глаз — не скопировали ли что-нибудь
                    if (p < 0.2 || p > 0.8) Face(4, "z", Grey); else { Face(0, "", null); LookEyes(0, 1); }
                    break;
                case "copydream": // снится, что копирует
                    Face(4, "≡", Grey); mark.Opacity = 0.3 + 0.7 * bell; break;
                case "codedream": // снится код
                    Face(4, "{}", Grey); mark.Opacity = 0.3 + 0.7 * bell; break;
                case "letter":   // снится письмо
                    Face(4, "✉", Grey); mark.Opacity = 0.3 + 0.7 * bell; break;
                case "mumble":   // бормочет во сне
                    Face(4, "~", Grey); shift.Y = Math.Sin(p * Math.PI * 6) * px * 0.3; break;
                case "bell":     // вздрогнул от «звонка» — и снова уснул
                    Face(p < 0.35 ? 2 : 4, p < 0.35 ? "!" : "z", Grey); shift.Y = p < 0.2 ? -Math.Sin(p / 0.2 * Math.PI) * hop : 0; break;
                case "no":       // «не выйдет»: мотает головой
                    Face(1, "×", Brushes.OrangeRed); tilt.Angle = Math.Sin(p * Math.PI * 6) * 11 * (1 - p); break;
                case "wow":      // «ого!»: глаза круглые, подскочил
                    Face(2, "!", Yellow); shift.Y = -bell * hop * 0.7; break;
                case "scratch":  // чешет затылок
                    Face(1, "?", Grey); tilt.Angle = Math.Sin(p * Math.PI * 8) * 5; break;
                case "ring":     // принёс напоминание: звонит в колокольчик — он качается, а зверёк подпрыгивает
                    Face(p < 0.15 ? 2 : 3, BellSign, Yellow);
                    mark.RenderTransform = new RotateTransform(Math.Sin(p * Math.PI * 9) * 24 * (1 - p), mark.ActualWidth / 2, 0);
                    shift.Y = -Math.Abs(Math.Sin(p * Math.PI * 3)) * hop * 0.5;
                    break;
                case "give":     // отдаёт письмо: конверт взлетает и тает, а сам радостно подпрыгивает
                    Face(p < 0.3 ? 2 : 3, "✉", Paper);
                    mark.RenderTransform = new TranslateTransform(px * 2.5 * p, -px * 7 * p);
                    mark.Opacity = Math.Max(0, 1 - p * 1.5);
                    shift.Y = -Math.Abs(Math.Sin(p * Math.PI)) * hop * 0.9;
                    break;
                case "sweat":    // бежит изо всех сил, капля пота
                    Face(1, "·", Drop); shift.X = Math.Sin(t * 10) * hop * 0.8; shift.Y = -Math.Abs(Math.Sin(t * 20)) * hop * 0.7; break;
                case "sprint":   // рывок
                    Face(1, "»", Grey); shift.X = Math.Sin(t * 14) * hop; shift.Y = -Math.Abs(Math.Sin(t * 28)) * hop * 0.5; break;
                case "sip":      // отхлёбывает чай
                    Face(4, "~", Grey); tilt.Angle = -8 * bell; break;
                case "sigh":     // довольно вздыхает
                    Face(4, "…", Grey); squash.ScaleY = 1 - 0.05 * bell; break;
                case "shake":    // отряхивается от дождя
                    Face(4, "·", Drop); shift.X = Math.Sin(p * Math.PI * 20) * hop * 0.5 * (1 - p); break;
                case "lean":     // ветер: наклонился против ветра
                    Face(0, "~", Grey); tilt.Angle = -10 * bell; break;
                case "shiver":   // мёрзнет
                    Face(1, "❄", Ice); shift.X = (rnd.NextDouble() - 0.5) * px * 0.8; break;
                case "catch":    // ловит снежинку или лучик
                    Face(3, "✦", Yellow); shift.Y = -bell * hop * 1.3; break;
                case "sunbathe": // загорает
                    Face(4, "☀", Yellow); squash.ScaleY = 1 - 0.05 * bell; break;
                case "tap":      // постукивает ногой — ждёт
                    Face(0, Prop, PropBrush); shift.Y = -Math.Abs(Math.Sin(p * Math.PI * 6)) * px * 0.5; break;
                case "check":    // поглядывает на таймер
                    Face(0, "?", Grey); LookEyes(-1, 0); break;
                case "peekcmd":
                case "nod":      // кивает тому, что вы пишете
                    Face(0, "", null); LookEyes(-1, 1); shift.Y = Math.Abs(Math.Sin(p * Math.PI * 4)) * hop * 0.35; break;
                default:
                    Face(0, "", null); LookEyes(-1, 1); break;   // заглядывает в строку
            }
        }
        // Свой наряд у этого зверька (в гардеробе — примерка); null — общий, как у всех
        string own;
        public string OwnOutfit { get { return own; } set { own = value; Redress(); } }

        // Переодеться: сняли прежний наряд, надели нынешний (каска прораба, если она надета, остаётся сверху)
        int dressedAs;
        void Redress()
        {
            dressedAs = OutfitVersion;
            foreach (UIElement o in outfit) body.Children.Remove(o);
            outfit.Clear();
            int n = body.Children.Count;
            Dress(body, px, own ?? Outfit);
            for (int i = n; i < body.Children.Count; i++) { outfit.Add(body.Children[i]); body.Children[i].Visibility = hatOn ? Visibility.Collapsed : Visibility.Visible; }
        }

        void Step()
        {
            if (dressedAs != OutfitVersion) Redress();
            int now = Environment.TickCount;
            double t = now / 1000.0, age = unchecked(now - born) / 1000.0;
            shift.X = 0; shift.Y = 0; mark.Opacity = 1;
            squash.ScaleX = squash.ScaleY = 1; tilt.Angle = 0;
            double hop = px * 1.4;
            if (seated)
            {
                // свешенные лапки болтаются — под музыку быстрее
                double a = Math.Sin(t * (mood == "dance" ? 7.4 : 3.1)) * px * 0.55;
                Canvas.SetLeft(legs[1], ((double[])legs[1].Tag)[0] + a);
                Canvas.SetLeft(legs[2], ((double[])legs[2].Tag)[0] - a);
            }
            else if (legs.Count == 4)
            {
                // идёт — лапки переступают парами, иначе стоят на месте
                bool walking = mood == "walk";
                bool stepA = walking && Math.Sin(t * 11) > 0;
                for (int i = 0; i < 4; i++)
                {
                    double[] home = (double[])legs[i].Tag;
                    bool up = walking && ((i % 2 == 0) == stepA);
                    Canvas.SetTop(legs[i], home[1] - (up ? px * 0.6 : 0));
                }
            }
            if (EventTick(now, t, hop)) return;   // случайная сценка поверх обычного поведения
            if (mood != "hello" && mark.RenderTransform != Transform.Identity) mark.RenderTransform = Transform.Identity;
            switch (mood)
            {
                case "fly":     // в прыжке: глаза круглые
                    Face(2, "", null);
                    break;
                case "post":    // несёт письмо — уведомление
                    Face(2, "✉", Paper);
                    shift.Y = -Math.Abs(Math.Sin(t * 7)) * hop * 0.3;
                    break;
                case "bell":    // несёт напоминание — колокольчик
                    Face(2, BellSign, Yellow);
                    shift.Y = -Math.Abs(Math.Sin(t * 7)) * hop * 0.3;
                    break;
                case "walk":    // идёт по краю окна: покачивается в такт шагам, смотрит, куда идёт
                    Face(0, "", null);
                    shift.Y = -Math.Abs(Math.Sin(t * 11)) * px * 0.35;
                    foreach (Rectangle r in eyes[0]) { double[] home = (double[])r.Tag; Canvas.SetLeft(r, home[0] + lookDir * px); }
                    break;
                case "dance":   // играет музыка: покачивается в такт, ноты мигают
                {
                    double beat = t * 2.1 * Math.PI;
                    Face(3, (now / 1400) % 2 == 0 ? "♪" : "♫", Orange);
                    shift.Y = -Math.Abs(Math.Sin(beat)) * hop * 0.55;
                    tilt.Angle = Math.Sin(beat / 2) * 5;
                    mark.Opacity = 0.45 + 0.55 * Math.Abs(Math.Cos(beat));
                    break;
                }
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
