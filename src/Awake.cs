// Kapsula — «остров» для Windows. Часть исходного кода: Awake.cs
// «Не засыпать» — как PowerToys Awake в Windows или Amphetamine на Mac: пока режим включён, компьютер не уснёт
// и экран не погаснет (скачивается большой файл, идёт презентация, читаете с экрана). Включается чашкой
// на вкладке «Погода и система», командой «не засыпать» в строке острова (можно на время: «не засыпать 2 ч»,
// «кофе 30 мин») и в меню значка у часов. Пока режим включён, в капсуле стоит маленькая чашка с паром.
// Сам выключается, когда выйдет время, и на батарее ниже 10% — чтобы ноутбук не сел. После перезапуска
// Kapsula режим не возвращается: компьютер не должен незаметно остаться без сна навсегда.
using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Kapsula
{
    partial class IslandWindow
    {
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
        const uint EsContinuous = 0x80000000, EsSystem = 0x1, EsDisplay = 0x2;

        bool awakeOn;
        DateTime awakeUntil = DateTime.MinValue;   // MinValue — пока не выключат
        FrameworkElement awakeDot;                  // чашка в капсуле
        Action awakeRepaint;                        // кнопка на вкладке «Система»
        static readonly Brush AwakeOnBrush = Glass.Tinted(Color.FromRgb(0xFF, 0x9F, 0x0A));

        // Чашка с паром — свой рисунок из простых линий
        static readonly Geometry CupGeometry = Geometry.Parse("M3,7 H12 V10.5 A3.5,3.5 0 0 1 8.5,14 H6.5 A3.5,3.5 0 0 1 3,10.5 Z M12,8 H13 A1.75,1.75 0 0 1 13,11.5 H12 M6,1.5 C7,2.6 5,3.6 6,4.8 M9,1.5 C10,2.6 8,3.6 9,4.8");

        static Path CupIcon(double size, Brush stroke)
        {
            return new Path
            {
                Data = CupGeometry, Stroke = stroke, StrokeThickness = 1.35, Stretch = Stretch.Uniform, Width = size, Height = size,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
            };
        }

        // Чашка для капсулы — рядом с точками микрофона и камеры
        FrameworkElement BuildAwakeDot()
        {
            var cup = CupIcon(12, Fg);
            cup.Opacity = 0.85;
            awakeDot = new Border { Child = cup, Background = Brushes.Transparent, Margin = new Thickness(4, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, ToolTip = "Не засыпать: компьютер не уснёт" };
            return awakeDot;
        }

        // «не засыпать», «не засыпай 2 ч», «не спать 1 час», «кофе 30 мин», «awake», «caffeinate 2h».
        // Возвращает false, если это не команда; span — на сколько (Zero — пока не выключат).
        static bool AwakeParse(string q, out TimeSpan span)
        {
            span = TimeSpan.Zero;
            var m = Regex.Match((q ?? "").Trim(), @"^(?:не\s+засып\w*|не\s+спать|кофе|awake|caffeinate)(?:\s+(?:на\s+)?(.+))?$", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            if (!m.Groups[1].Success) return true;
            return Calc.TryTimer(m.Groups[1].Value.Trim(), out span);
        }

        // Строка в командной строке острова
        void FillAwakeCmd(string q)
        {
            TimeSpan span;
            if (!AwakeParse(q, out span)) return;
            if (awakeOn)
            {
                AddCmd("Выключить «Не засыпать»", awakeUntil == DateTime.MinValue ? "сейчас компьютер не засыпает" : "включено до " + awakeUntil.ToString("H:mm"), "", null, delegate { SetAwake(false, TimeSpan.Zero, true); });
                return;
            }
            if (span > TimeSpan.Zero)
                AddCmd("Не засыпать " + Fmt(span), "до " + (DateTime.Now + span).ToString("H:mm") + ", потом компьютер снова сможет уснуть", "", null, delegate { SetAwake(true, span, true); });
            else
                AddCmd("Не засыпать", "компьютер не уснёт и экран не погаснет, пока не выключите", "", null, delegate { SetAwake(true, TimeSpan.Zero, true); });
        }

        void SetAwake(bool on, TimeSpan span, bool say)
        {
            awakeOn = on;
            awakeUntil = on && span > TimeSpan.Zero ? DateTime.Now + span : DateTime.MinValue;
            try { SetThreadExecutionState(on ? EsContinuous | EsSystem | EsDisplay : EsContinuous); } catch { }
            if (awakeDot != null) awakeDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (awakeRepaint != null) awakeRepaint();
            if (say) Toast("", on ? (awakeUntil == DateTime.MinValue ? "Не засыпать — компьютер не уснёт" : "Не засыпать до " + awakeUntil.ToString("H:mm")) : "Компьютер снова может уснуть", null, -1, null);
            if (state == 0) { RenderCompact(); SetTargets(); }
        }

        // Из OnPoll: вышло время или батарея садится — режим выключается сам
        void AwakeTick()
        {
            if (!awakeOn) return;
            if (awakeUntil != DateTime.MinValue && DateTime.Now >= awakeUntil) { SetAwake(false, TimeSpan.Zero, false); Toast("", "Время вышло — компьютер снова может уснуть", null, -1, null); return; }
            if (batHas && !batPlugged && batPct <= 10) { SetAwake(false, TimeSpan.Zero, false); Toast("", "Батарея садится — компьютер снова может уснуть", null, -1, RecRed); }
        }

        // Кнопка-чашка для вкладки «Погода и система»: включено — оранжевое стекло, как переключатели «Пункта управления»
        Border AwakeBtn(double size)
        {
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            Action paint = delegate
            {
                b.Background = awakeOn ? AwakeOnBrush : b.IsMouseOver ? (Brush)Glass.Hover : Brushes.Transparent;
                b.BorderBrush = awakeOn || b.IsMouseOver ? (Brush)Glass.Rim : Brushes.Transparent;
                b.Child = CupIcon(15, awakeOn ? Brushes.White : Fg);
                b.ToolTip = awakeOn
                    ? (awakeUntil == DateTime.MinValue ? "Не засыпать: включено · нажмите, чтобы выключить" : "Не засыпать до " + awakeUntil.ToString("H:mm") + " · нажмите, чтобы выключить")
                    : "Не засыпать: компьютер не уснёт и экран не погаснет (на время — команда «не засыпать 2 ч»)";
            };
            paint();
            b.MouseEnter += delegate { paint(); };
            b.MouseLeave += delegate { paint(); };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; SetAwake(!awakeOn, TimeSpan.Zero, true); };
            awakeRepaint = paint;
            Fx.Press(b, 0.88);
            return b;
        }
    }
}
