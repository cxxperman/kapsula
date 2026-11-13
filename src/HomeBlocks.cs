// Kapsula — «остров» для Windows. Часть исходного кода: HomeBlocks.cs
// Блоки на выбор для «Главной»: ближайшее событие календаря, сеть со скоростью загрузки и отдачи, заряд батареи
// ноутбука. По умолчанию спрятаны: карандаш в углу панели → «+ Календарь», «+ Сеть», «+ Батарея» (сначала
// освободите для них место — например, спрячьте ярлыки или пару плиток). Щелчок по блоку открывает его вкладку.
using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Kapsula
{
    partial class IslandWindow
    {
        TextBlock hbCalTitle, hbCalSub, hbNetGlyph, hbNetName, hbNetSpeed, hbBatGlyph, hbBatPct, hbBatSub;
        long netRx = -1, netTx = -1;
        int netAt, netSampleAt;
        bool netBusy;
        CalEvent testCal;

        // Блок «Главной» в стиле плиток: значок в кружке и две строки. Широкий — значок слева, узкий — сверху.
        Border HomeBlock(string glyph, bool wide, out TextBlock glyphTb, out TextBlock title, out TextBlock sub, Action click)
        {
            glyphTb = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 13.5, Foreground = Tile.Light, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var badge = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Tile.Badge, Child = glyphTb,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = wide ? VerticalAlignment.Center : VerticalAlignment.Top };
            title = new TextBlock { Foreground = Fg, FontSize = wide ? 12.5 : 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            if (wide) { title.TextWrapping = TextWrapping.Wrap; title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; title.LineHeight = 15.5; title.MaxHeight = 32; }   // длинное название события — в две строки
            sub = new TextBlock { Foreground = Dim, FontSize = 11, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            var texts = new StackPanel { VerticalAlignment = wide ? VerticalAlignment.Center : VerticalAlignment.Bottom, Margin = new Thickness(wide ? 38 : 0, 0, 0, 0) };
            texts.Children.Add(title); texts.Children.Add(sub);
            var g = new Grid { Margin = new Thickness(wide ? 12 : 10, 9, 10, 8) };
            g.Children.Add(badge); g.Children.Add(texts);
            var b = new Border { CornerRadius = new CornerRadius(16), Background = Tile.Off, BorderBrush = Tile.Line, BorderThickness = new Thickness(1), Child = g, Cursor = Cursors.Hand };
            b.MouseEnter += delegate { b.Background = Tile.Hover; };
            b.MouseLeave += delegate { b.Background = Tile.Off; };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            Fx.Press(b, 0.96);
            return b;
        }

        // Регистрация в конструкторе «Главной» (вызывается до загрузки раскладки)
        void AddHomeBlocks(Board hb)
        {
            TextBlock calGlyph;
            hb.Add("calendar", "Календарь", HomeBlock(((char)0xE787).ToString(), true, out calGlyph, out hbCalTitle, out hbCalSub, delegate { ShowPage(PToday); }), 0, 6, 4, 2, true);
            hb.Add("net", "Сеть", HomeBlock(((char)0xE701).ToString(), true, out hbNetGlyph, out hbNetName, out hbNetSpeed, delegate { ShowPage(PSystem); }), 4, 6, 4, 2, true);
            if (BatteryPresent()) hb.Add("battery", "Батарея", HomeBlock(BatGlyph(100, false), false, out hbBatGlyph, out hbBatPct, out hbBatSub, delegate { ShowPage(PSystem); }), 8, 6, 2, 2, true);
        }

        // Пока «Главная» на экране (вместе с остальным живым на ней)
        void RenderHomeBlocks()
        {
            if (homeBoard == null) return;
            if (hbCalTitle != null && homeBoard.IsShown("calendar")) RenderCalBlock();
            if (hbBatPct != null && homeBoard.IsShown("battery") && batHas)
            {
                hbBatGlyph.Text = BatGlyph(batPct, batPlugged);
                hbBatGlyph.Foreground = batPlugged ? (Brush)ClaudeGreen : batPct <= 20 ? (Brush)RecRed : Tile.Light;
                hbBatPct.Text = batPct + "%";
                hbBatSub.Text = batPlugged ? (batPct >= 100 ? "заряжена" : "заряжается") : batLeftMin > 0 ? "≈ " + LeftText(batLeftMin) : "от батареи";
            }
            if (hbNetName != null && homeBoard.IsShown("net") && unchecked(Environment.TickCount - netSampleAt) >= 1000) { netSampleAt = Environment.TickCount; NetSample(); }
        }

        // Ближайшее событие: идущее (началось не больше четверти часа назад) или следующее
        void RenderCalBlock()
        {
            DateTime now = DateTime.Now;
            CalEvent next = testCal;   // для проверок — выдуманное событие, а не ваш календарь
            if (next == null && Store.Get("calendar") != "0")
                foreach (CalEvent e in Calendar.Events())
                {
                    if (e.AllDay || e.Start < now.AddMinutes(-15)) continue;
                    if (next == null || e.Start < next.Start) next = e;
                }
            if (next == null) { hbCalTitle.Text = "Событий нет"; hbCalSub.Text = Store.Get("calendar") == "0" ? "календарь выключен в настройках" : "в календаре Windows пусто"; return; }
            hbCalTitle.Text = next.Subject.Length > 0 ? next.Subject : "Событие";
            hbCalSub.Text = next.Start <= now ? "идёт сейчас" : (next.Start - now).TotalHours < 3 ? Calendar.In(next.Start, now) : Calendar.When(next, now);
        }

        // Скорость сети: раз в секунду, в фоне — сумма по включённым подключениям (без петли и туннелей)
        void NetSample()
        {
            if (netBusy) return;
            netBusy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                long rx = 0, tx = 0; string name = null; bool wifi = false;
                try
                {
                    foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != OperationalStatus.Up) continue;
                        NetworkInterfaceType t = ni.NetworkInterfaceType;
                        if (t == NetworkInterfaceType.Loopback || t == NetworkInterfaceType.Tunnel) continue;
                        IPv4InterfaceStatistics st = ni.GetIPv4Statistics();
                        rx += st.BytesReceived; tx += st.BytesSent;
                        if (name == null && ni.GetIPProperties().GatewayAddresses.Count > 0) { wifi = t == NetworkInterfaceType.Wireless80211; name = wifi ? "Wi-Fi" : "Ethernet"; }
                    }
                }
                catch { }
                int at = Environment.TickCount;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    netBusy = false;
                    string speed = "";
                    double sec = unchecked(at - netAt) / 1000.0;
                    if (netRx >= 0 && sec > 0.2 && rx >= netRx && tx >= netTx) speed = "↓ " + Rate((rx - netRx) / sec) + "  ↑ " + Rate((tx - netTx) / sec);
                    netRx = rx; netTx = tx; netAt = at;
                    hbNetGlyph.Text = ((char)(name == null ? 0xF384 : wifi ? 0xE701 : 0xE839)).ToString();
                    hbNetName.Text = name ?? "Нет сети";
                    hbNetSpeed.Text = name == null ? "нет подключения" : speed.Length > 0 ? speed : "считаю скорость…";
                }));
            });
        }

        // Байты в секунду — по-человечески: «0 КБ/с», «850 КБ/с», «2,4 МБ/с»
        static string Rate(double bps)
        {
            if (bps < 1024) return "0 КБ/с";
            if (bps < 1024 * 1024) return Math.Round(bps / 1024) + " КБ/с";
            return (bps / 1048576).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " МБ/с";
        }
    }
}
