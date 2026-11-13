// Kapsula — «остров» для Windows. Часть исходного кода: Flow.cs
// Карточка → панель. Колёсико вниз над карточкой (или «ручка» внизу карточки) — остров перетекает из карточки
// прямо в панель, без возврата в капсулу: карточка опускается и тает, панель раскрывается, зверёк перепрыгивает
// с края карточки на край панели. Уведомление при этом не пропадает, а «улетает» в историю: значок и текст
// карточки плавно переезжают в верхнюю строку вкладки «Уведомления» и становятся ею. Колёсико вверх — смахнуть
// карточку в капсулу.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Kapsula
{
    partial class IslandWindow
    {
        int cardPage = -1;          // вкладка, к которой относится карточка (-1 — та, что была открыта в прошлый раз)
        bool cardNote;              // карточка — уведомление из истории: при раскрытии улетает в её верхнюю строку
        bool cardFlow;              // карточка уходит в панель: опускается и тает, а не сжимается к капсуле
        readonly TranslateTransform cardShift = new TranslateTransform();
        int wheelMuteUntil;         // после жеста колёсиком остаток прокрутки не должен крутить громкость на «Главной»
        Border cardGrab;

        // «Ручка» внизу карточки: появляется, когда курсор на карточке, щелчок — раскрыть в панель
        void BuildCardGrab()
        {
            var bar = new Border { Width = 30, Height = 4, CornerRadius = new CornerRadius(2), Background = Dim, Opacity = 0.55, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            cardGrab = new Border { Width = 76, Height = 14, Background = Brushes.Transparent, Child = bar, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2), Cursor = Cursors.Hand, Opacity = 0, ToolTip = "Раскрыть в панель (или колёсико вниз)" };
            cardGrab.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; CardExpand(); };
            cardGrab.MouseEnter += delegate { bar.Opacity = 0.95; };
            cardGrab.MouseLeave += delegate { bar.Opacity = 0.55; };
            cardHost.Children.Add(cardGrab);
            cardHost.MouseEnter += delegate { CardGrabShow(true); };
            cardHost.MouseLeave += delegate { CardGrabShow(false); };
        }

        void CardGrabShow(bool on)
        {
            if (cardGrab == null) return;
            on = on && CardCanExpand();
            cardGrab.IsHitTestVisible = on;
            cardGrab.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 160 : 120)));
        }

        // Раскрыть можно карточку-сообщение. Карточку с выбором (вопрос Claude Code, приглашение, таймер) — нет:
        // на неё нужно ответить, а в панели этих кнопок не будет.
        bool CardCanExpand()
        {
            if (state != 3 || cardHost == null) return false;
            if (claudeNow != null) return false;
            if (cardNo != null && cardNo.Visibility == Visibility.Visible) return false;
            for (int i = 0; i < 2; i++) if (cardAlt[i] != null && cardAlt[i].Visibility == Visibility.Visible) return false;
            return true;
        }

        void CardExpand()
        {
            if (!CardCanExpand()) return;
            int p = cardPage >= 0 && TabAvailable(cardPage) ? cardPage : page;
            bool hero = cardNote && p == PNotif && !ReducedMotion && HeroCapture();
            if (p != page) ShowPage(p);
            else if (p == PNotif) RenderNotifPage();
            hoverBlock = false;
            holdOpenUntil = Environment.TickCount + 1200;   // курсор ещё на месте карточки — панель не закрывается сразу
            SetState(2);
            if (hero) HeroStart();
        }

        // Колёсико над карточкой: вниз — раскрыть в панель, вверх — смахнуть в капсулу
        bool CardWheel(MouseWheelEventArgs e)
        {
            if (state != 3) return false;
            wheelMuteUntil = Environment.TickCount + 450;
            if (e.Delta < 0) { if (CardCanExpand()) CardExpand(); }
            else { hoverBlock = true; SetState(0); }
            return true;
        }

        // ============================================================ перелёт карточки в историю
        // Три копии летят каждая в своё место строки: значок — в значок, заголовок — в заголовок, текст — в текст.
        // Так в конце копия ложится ровно на настоящую строку и ничего не двоится.
        Canvas heroLayer;
        readonly Image[] heroImg = new Image[3];
        readonly FrameworkElement[] heroFrom = new FrameworkElement[3], heroTo = new FrameworkElement[3];
        readonly Rect[] heroAtRect = new Rect[3];
        int heroAt;
        bool heroOn;
        const int HeroMs = 480;

        void BuildHero()
        {
            heroLayer = new Canvas { IsHitTestVisible = false };
            for (int i = 0; i < 3; i++)
            {
                heroImg[i] = new Image { Visibility = Visibility.Collapsed, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(heroImg[i], BitmapScalingMode.HighQuality);
                heroLayer.Children.Add(heroImg[i]);
            }
            clipHost.Children.Add(heroLayer);
        }

        // Снять значок, заголовок и текст карточки, пока она ещё на экране целиком. Только если верхняя строка истории — это она.
        bool HeroCapture()
        {
            if (heroLayer == null || seenNotes.Count == 0) return false;
            Seen last = seenNotes[seenNotes.Count - 1];
            if (last.Title != cardTitle.Text) return false;
            heroFrom[0] = cardLogo != null && cardLogo.Visibility == Visibility.Visible ? (FrameworkElement)cardLogo : cardThumbBox;
            heroFrom[1] = cardTitle;
            heroFrom[2] = cardSub.Text.Length > 0 ? cardSub : null;
            for (int i = 0; i < 3; i++)
            {
                heroImg[i].Source = null;
                if (heroFrom[i] == null) continue;
                ImageSource s = Snap(heroFrom[i], HeroPad(i), out heroAtRect[i]);
                if (s == null) return false;
                heroImg[i].Source = s;
            }
            return true;
        }

        static double HeroPad(int i) { return i == 0 ? 8 : 0; }

        void HeroStart()
        {
            for (int i = 0; i < 3; i++) heroTo[i] = null;
            pages[PNotif].UpdateLayout();
            var b = notifList.Children.Count > 0 ? notifList.Children[0] as Border : null;
            var row = b != null ? b.Child as Grid : null;
            var texts = row != null && row.Children.Count >= 2 ? row.Children[1] as StackPanel : null;
            if (texts == null || texts.Children.Count == 0) return;
            heroTo[0] = row.Children[0] as FrameworkElement;
            heroTo[1] = texts.Children[0] as FrameworkElement;
            heroTo[2] = texts.Children.Count > 1 ? texts.Children[1] as FrameworkElement : null;
            if (heroTo[0] == null || heroTo[1] == null) return;
            for (int i = 0; i < 3; i++)
            {
                if (heroFrom[i] == null || heroImg[i].Source == null) continue;
                if (heroTo[i] != null) heroTo[i].Opacity = 0;   // строка ждёт, пока в неё «прилетит» карточка
                heroFrom[i].Opacity = 0;                          // а на самой карточке их уже нет — летят копии
                Place(heroImg[i], heroAtRect[i]);
                heroImg[i].Opacity = 1;
                heroImg[i].Visibility = Visibility.Visible;
            }
            heroAt = Environment.TickCount;
            heroOn = true;
            Wake();
        }

        // Кадр перелёта: копии едут к своим местам в строке (строка сама ещё двигается вместе с раскрывающейся панелью)
        void HeroFrame()
        {
            if (!heroOn) return;
            if (state != 2 || page != PNotif) { HeroEnd(); return; }
            double p = Clamp01(unchecked(Environment.TickCount - heroAt) / (double)HeroMs);
            double e = 1 - Math.Pow(1 - p, 3);
            double f = Clamp01((p - 0.8) / 0.2);   // в конце копии растворяются в настоящей строке
            for (int i = 0; i < 3; i++)
            {
                if (heroImg[i].Visibility != Visibility.Visible) continue;
                Rect to;
                if (heroTo[i] == null || !Bounds(heroTo[i], out to)) { heroImg[i].Opacity = 1 - e; continue; }   // своего места нет — просто тает
                // тот же масштаб по обеим осям: высота — как у места в строке; значок — по центру своего квадрата, текст — от левого края
                Rect a = heroAtRect[i];
                double pad = HeroPad(i), inner = a.Height - 2 * pad;
                double k = inner > 0 ? to.Height / inner : 1;
                double w = a.Width * k, h = a.Height * k;
                var fit = new Rect(i == 0 ? to.Left + (to.Width - w) / 2 : to.Left - pad * k, to.Top - pad * k, w, h);
                Place(heroImg[i], Lerp(a, fit, e));
                heroImg[i].Opacity = 1 - f;
                heroTo[i].Opacity = f;
            }
            if (p >= 1) HeroEnd();
        }

        void HeroEnd()
        {
            if (!heroOn) return;
            heroOn = false;
            for (int i = 0; i < 3; i++)
            {
                heroImg[i].Visibility = Visibility.Collapsed; heroImg[i].Source = null;
                if (heroTo[i] != null) heroTo[i].Opacity = 1;
            }
        }

        // Новая карточка: значок и текст, которые улетали в историю, снова на месте
        void CardContentBack()
        {
            if (cardThumbBox != null) cardThumbBox.Opacity = 1;
            if (cardLogo != null) cardLogo.Opacity = 1;
            if (cardTitle != null) cardTitle.Opacity = 1;
            if (cardSub != null) cardSub.Opacity = 1;
        }

        static Rect Lerp(Rect a, Rect b, double e)
        {
            return new Rect(a.Left + (b.Left - a.Left) * e, a.Top + (b.Top - a.Top) * e, Math.Max(0, a.Width + (b.Width - a.Width) * e), Math.Max(0, a.Height + (b.Height - a.Height) * e));
        }

        static void Place(FrameworkElement el, Rect r)
        {
            Canvas.SetLeft(el, r.Left); Canvas.SetTop(el, r.Top);
            el.Width = r.Width; el.Height = r.Height;
        }

        // Где элемент сейчас на острове — со всеми сдвигами и масштабами по дороге (раскрытие панели, «всплытие» страницы)
        bool Bounds(FrameworkElement el, out Rect r)
        {
            r = Rect.Empty;
            if (el == null || el.ActualWidth <= 0 || !clipHost.IsAncestorOf(el)) return false;
            try { r = el.TransformToVisual(clipHost).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight)); return !r.IsEmpty; }
            catch { return false; }
        }

        // Снимок элемента в его настоящем размере на экране — чтобы копия была чёткой. pad — запас по краям
        // (у значка карточки в углу торчит значок программы). Окно кисти задано явно: иначе VisualBrush берёт
        // только нарисованное (у короткого заголовка — пару букв) и растягивает это на всю ширину.
        ImageSource Snap(FrameworkElement el, double pad, out Rect at)
        {
            at = Rect.Empty;
            if (!Bounds(el, out at) || el.ActualHeight <= 0) return null;
            at.Inflate(pad, pad);
            try
            {
                double k = Math.Max(1, dpi * Scale);
                double bw = el.ActualWidth + 2 * pad, bh = el.ActualHeight + 2 * pad;
                int w = (int)Math.Ceiling(bw * k), h = (int)Math.Ceiling(bh * k);
                if (w <= 0 || h <= 0 || w > 4000 || h > 4000) return null;
                // кисть рисует элемент вместе с его сдвигом внутри родителя (у второй строки текста — отступ сверху)
                Vector off = VisualTreeHelper.GetOffset(el);
                var dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                    dc.DrawRectangle(new VisualBrush(el) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(off.X - pad, off.Y - pad, bw, bh) }, null, new Rect(0, 0, bw, bh));
                var rtb = new RenderTargetBitmap(w, h, 96 * k, 96 * k, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                return rtb;
            }
            catch { return null; }
        }
    }
}
