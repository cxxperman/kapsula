// Kapsula — «остров» для Windows. Часть исходного кода: Battery.cs
// Батарея ноутбука. Подключили зарядку — в капсуле полоска заряда, как у громкости, а зверёк радуется
// с молнией; отключили — надпись «От батареи» и сколько осталось. Батарея садится (20 % и 10 %) —
// карточка, а зверёк «устаёт» вместе с ней. Зарядилась полностью — короткая надпись. На вкладке «Система» рядом
// с нагрузкой — значок и проценты заряда. На компьютере без батареи ничего этого нет.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WF = System.Windows.Forms;

namespace Kapsula
{
    partial class IslandWindow
    {
        bool batKnown, batHas, batPlugged, batFullShown;
        int batPct = -1, batLeftMin = -1, batWarned = 100;
        int toastCharge;                 // 1 — надпись «Заряжается»: зверёк в капсуле радуется с молнией
        string batTest = Environment.GetEnvironmentVariable("KAPSULA_BATTERY");   // «проценты,1|0» — для проверок вместо настоящей батареи
        TextBlock batGlyph, batText;
        FrameworkElement batChip;

        bool ReadBattery(out bool has, out int pct, out bool plugged, out int leftMin)
        {
            has = false; pct = -1; plugged = false; leftMin = -1;
            if (!string.IsNullOrEmpty(batTest))
            {
                string[] p = batTest.Split(',');
                int v;
                if (!int.TryParse(p[0], out v)) return false;
                has = true; pct = Math.Max(0, Math.Min(100, v)); plugged = p.Length > 1 && p[1] == "1"; leftMin = plugged ? -1 : pct * 4;
                return true;
            }
            try
            {
                WF.PowerStatus ps = WF.SystemInformation.PowerStatus;
                if ((ps.BatteryChargeStatus & WF.BatteryChargeStatus.NoSystemBattery) != 0 || ps.BatteryChargeStatus == WF.BatteryChargeStatus.Unknown) return true;
                has = true;
                pct = (int)Math.Round(Math.Max(0, Math.Min(1, ps.BatteryLifePercent)) * 100);
                plugged = ps.PowerLineStatus == WF.PowerLineStatus.Online;
                leftMin = !plugged && ps.BatteryLifeRemaining > 0 ? ps.BatteryLifeRemaining / 60 : -1;
                return true;
            }
            catch { return false; }
        }

        // Раз в секунду
        void BatteryTick()
        {
            bool has, plugged; int pct, left;
            if (!ReadBattery(out has, out pct, out plugged, out left)) return;
            if (!has) { if (batHas) { batHas = false; RenderBattery(); } batKnown = true; return; }
            bool first = !batKnown || !batHas, wasPlugged = batPlugged;
            batKnown = true; batHas = true; batPct = pct; batPlugged = plugged; batLeftMin = left;
            RenderBattery();
            if (first)
            {
                // при запуске — без надписей: только запомнить, о чём уже предупреждали
                batWarned = plugged ? 100 : pct <= 10 ? 10 : pct <= 20 ? 20 : 100;
                batFullShown = pct >= 100;
                return;
            }
            if (plugged != wasPlugged)
            {
                if (plugged) { batWarned = 100; batFullShown = pct >= 100; }
                BatteryToast(plugged);
                return;
            }
            if (plugged && pct >= 100 && !batFullShown) { batFullShown = true; BatteryToast(true); return; }
            if (!plugged && pct <= 10 && batWarned > 10) { batWarned = 10; BatteryLowCard(); }
            else if (!plugged && pct <= 20 && batWarned > 20) { batWarned = 20; BatteryLowCard(); }
        }

        // Подключили — полоска заряда и зверёк с молнией; отключили или зарядилась — надпись (сколько осталось — словами)
        void BatteryToast(bool on)
        {
            if (on && batPct < 100)
            {
                Toast(BatGlyph(batPct, true), "Заряжается", batPct + "%", batPct / 100.0, ClaudeGreen);
                toastCharge = 1;   // Toast сбросил отметку — ставим после
                return;
            }
            string text = on ? "Заряжено полностью" : "От батареи" + (batLeftMin > 0 ? " · осталось " + LeftText(batLeftMin) : "");
            Toast(BatGlyph(batPct, on), text, batPct + "%", -1, on ? (Brush)ClaudeGreen : batPct <= 20 ? (Brush)RecRed : null);
        }

        void BatteryLowCard()
        {
            string sub = "Подключите зарядку" + (batLeftMin > 0 ? " · осталось " + LeftText(batLeftMin) : "");
            ShowCardEx(null, BatGlyph(batPct, false), "Батарея садится · " + batPct + "%", sub, null, null, null, false);
            petLowUntil = Environment.TickCount + 15000;   // зверёк в капсуле «устаёт» вместе с батареей
            if (state != 3) return;   // панель открыта — вместо карточки была короткая надпись
            HideCardButton();
            cardPage = PSystem;       // раскрыть карточку — на вкладку «Система», где виден заряд
            cardUntil = Environment.TickCount + 7000;
        }

        static string LeftText(int min)
        {
            if (min < 60) return min + " мин";
            return min / 60 + " ч" + (min % 60 > 0 ? " " + (min % 60).ToString("00") + " мин" : "");
        }

        // Значок батареи из шрифта Windows: десять делений, отдельный ряд — с молнией
        static string BatGlyph(int pct, bool charging)
        {
            int i = Math.Max(0, Math.Min(10, (pct + 5) / 10));
            if (charging) return ((char)(i == 10 ? 0xEA93 : i == 9 ? 0xE83E : 0xE85A + i)).ToString();
            return ((char)(i == 10 ? 0xE83F : 0xE850 + i)).ToString();
        }

        // Значок и проценты на вкладке «Система», рядом с нагрузкой
        FrameworkElement BuildBatChip()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), Visibility = Visibility.Collapsed };
            batGlyph = new TextBlock { FontFamily = Icons, FontSize = 16, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center };
            batText = new TextBlock { Foreground = Fg, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            sp.Children.Add(batGlyph); sp.Children.Add(batText);
            batChip = sp;
            return sp;
        }

        // Есть ли батарея прямо сейчас (при сборке вкладки «Система»: оставить ли место под заряд)
        bool BatteryPresent()
        {
            bool has, plugged; int pct, left;
            return ReadBattery(out has, out pct, out plugged, out left) && has;
        }

        void RenderBattery()
        {
            if (batChip == null) return;
            batChip.Visibility = batHas ? Visibility.Visible : Visibility.Collapsed;
            if (!batHas) return;
            Brush c = batPlugged ? (Brush)ClaudeGreen : batPct <= 20 ? (Brush)RecRed : Fg;
            batGlyph.Text = BatGlyph(batPct, batPlugged);
            batGlyph.Foreground = c;
            batText.Text = batPct + "%";
            batChip.ToolTip = "Батарея: " + batPct + "%" + (batPlugged ? (batPct >= 100 ? " · заряжена" : " · заряжается") : batLeftMin > 0 ? " · осталось " + LeftText(batLeftMin) : "");
        }
    }
}
