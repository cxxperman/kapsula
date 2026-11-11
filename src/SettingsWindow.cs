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
    // ============================================================= Настройки
    class SettingsWindow : Window
    {
        IslandWindow island;
        static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x18));
        static readonly Brush RowBg = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x23));
        static readonly Brush Txt = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6));
        static readonly Brush Sub = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96));
        static readonly Brush ChipBg = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x31));
        static readonly Brush OnBg = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6));
        static readonly Brush OnTxt = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x14));
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
            Width = 560; Height = Math.Min(700, SystemParameters.WorkArea.Height - 16);   // на небольших экранах окно не должно уходить за край
            Background = Bg;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            UseLayoutRounding = true;

            var sp = new StackPanel { Margin = new Thickness(22, 16, 22, 22) };
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

            sp.Children.Add(Header("Внешний вид"));
            sp.Children.Add(Row("Цвет острова", "Наведите на цвет — остров сверху сразу покажет его. Клик сохраняет.", BuildSwatches(), true));
            sp.Children.Add(Row("Как в Windows", "Светлый остров при светлой теме Windows, тёмный — при тёмной.", Toggle(island.FollowWindows, delegate(bool v) { island.SetFlag("followWin", v); }), false));
            double[] scales = { 0.8, 1, 1.25, 1.5, 1.75, 2 };
            int si = 1;
            for (int i = 0; i < scales.Length; i++) if (Math.Abs(scales[i] - island.Scale) < 0.01) si = i;
            sp.Children.Add(Row("Размер", "Для больших мониторов.", Chips(new string[] { "80%", "100%", "125%", "150%", "175%", "200%" }, si, delegate(int i) { island.ApplyScale(scales[i]); }), true));

            sp.Children.Add(Row("Автоскрытие", "Остров уезжает за край и возвращается, когда курсор у верхней кромки экрана или есть что показать.", Toggle(island.AutoHide, delegate(bool v) { island.SetFlag("autoHide", v); }), false));
            sp.Children.Add(Row("Глаза в капсуле", "Следят за курсором, моргают и засыпают, когда вы отошли.", Toggle(island.ShowEyes, delegate(bool v) { island.SetFlag("eyes", v); }), false));
            if (island.MonitorCount > 1)
            {
                var names = new string[island.MonitorCount];
                for (int i = 0; i < names.Length; i++) names[i] = "Монитор " + (i + 1);
                sp.Children.Add(Row("Монитор", "На каком экране живёт остров.", Chips(names, Math.Max(0, Math.Min(names.Length - 1, island.MonitorIndex)), delegate(int i) { island.SetMonitor(i); }), true));
            }

            sp.Children.Add(Header("Музыка"));
            sp.Children.Add(Row("Капсула во время музыки", "Что показывать в свёрнутом виде.", Chips(new string[] { "Время и погода", "Название трека" }, island.PillTitle ? 1 : 0, delegate(int i) { island.SetFlag("pillTitle", i == 1); }), true));
            sp.Children.Add(Row("Мини-плеер при наведении", "Во время музыки сначала открывается мини-плеер, клик по нему — вся панель.", Toggle(island.HoverMini, delegate(bool v) { island.SetFlag("hoverMini", v); }), false));
            sp.Children.Add(Row("Жидкий край", "Нижний край пляшет под звук.", Toggle(island.LiquidOn, delegate(bool v) { island.SetFlag("liquid", v); }), false));
            sp.Children.Add(Row("Тексты песен", "Загружаются с lrclib.net для играющего трека.", Toggle(island.LyricsEnabled, delegate(bool v) { island.SetFlag("lyrics", v); }), false));

            double[] offs = { -1, -0.5, 0, 0.5, 1 };
            int oi = 2;
            for (int i = 0; i < offs.Length; i++) if (Math.Abs(offs[i] - island.LyricOffset) < 0.01) oi = i;
            sp.Children.Add(Row("Сдвиг текста песни", "Если строки отстают от звука — сдвиньте вперёд, если спешат — назад.", Chips(new string[] { "−1 с", "−0,5 с", "Без сдвига", "+0,5 с", "+1 с" }, oi, delegate(int i) { island.SetLyricOffset(offs[i]); }), true));

            sp.Children.Add(Header("ИИ-чат (Claude)"));
            sp.Children.Add(Row("Ключ Claude API", "Нужен свой ключ: console.anthropic.com → API Keys. Он хранится только на этом компьютере, зашифрованный Windows, и отправляется только на api.anthropic.com.", BuildKeyEditor(), true));
            sp.Children.Add(Row("Модель", "Opus 5.5 — умнее; Sonnet 5.5 — быстрее и дешевле.", Chips(new string[] { "Opus 5.5", "Sonnet 5.5" }, ClaudeChat.Model() == "claude-sonnet-5-5" ? 1 : 0, delegate(int i) { island.SetClaudeModel(i == 1 ? "claude-sonnet-5-5" : "claude-opus-5-5"); }), true));

            sp.Children.Add(Header("Прочее"));
            sp.Children.Add(Row("Секунды в часах", null, Toggle(island.ShowSeconds, delegate(bool v) { island.SetFlag("seconds", v); }), false));
            sp.Children.Add(Row("Запись экрана со звуком", "В запись попадает то, что играет на колонках или в наушниках. Микрофон не записывается.", Toggle(Store.Get("recAudio") != "0", delegate(bool v) { island.SetFlag("recAudio", v); }), false));
            sp.Children.Add(Row("Показывать остров на скриншотах", "По умолчанию он скрыт от снимков, записи экрана и трансляций.", Toggle(island.ShowInCapture, delegate(bool v) { island.SetFlag("showInCapture", v); }), false));
            sp.Children.Add(Row("Запускать вместе с Windows", null, Toggle(Autostart.IsOn(), delegate(bool v) { Autostart.Set(v); }), false));
            sp.Children.Add(Row("Автообновление", "Kapsula сама скачивает новые версии с GitHub и сверяет контрольную сумму файла. Сейчас установлена версия " + Updater.CurrentText + ".", Toggle(Store.Get("autoUpdate") != "0", delegate(bool v) { island.SetFlag("autoUpdate", v); }), false));

            sp.Children.Add(Row("Показывать смену раскладки", "Короткая подсказка «Раскладка: RU» при переключении языка.", Toggle(island.ShowLayout, delegate(bool v) { island.SetFlag("layout", v); }), false));
            sp.Children.Add(Row("Уведомления Windows на острове", "Новые уведомления других программ показываются карточкой. Нигде не сохраняются.", Toggle(island.ShowNotifications, delegate(bool v) { island.SetFlag("notifications", v); }), false));

            sp.Children.Add(Header("Горячие клавиши"));
            sp.Children.Add(new TextBlock { Foreground = Sub, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 0, 8), Text = "Нажмите на сочетание и введите новое. Backspace отключает, правый клик возвращает исходное. Красным отмечены сочетания, занятые другой программой." });
            foreach (Hot hk in island.Hots) sp.Children.Add(Row(hk.Name, null, HotChip(hk), false));
            PreviewKeyDown += OnCaptureKey;
            Closed += delegate { if (capturing != null) { capturing = null; island.SuspendHotkeys(false); } };
            scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
            Content = scroller;
        }

        FrameworkElement Header(string text)
        {
            return new TextBlock { Text = text, Foreground = Sub, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 12, 0, 8) };
        }

        FrameworkElement Row(string title, string desc, FrameworkElement control, bool below)
        {
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = title, Foreground = Txt, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(desc)) texts.Children.Add(new TextBlock { Text = desc, Foreground = Sub, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
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
            return new Border { Background = RowBg, CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8), Child = body };
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

        FrameworkElement Chips(string[] labels, int selected, Action<int> pick)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var chips = new List<Border>();
            Action<int> paint = delegate(int sel)
            {
                for (int i = 0; i < chips.Count; i++)
                {
                    chips[i].Background = i == sel ? OnBg : ChipBg;
                    ((TextBlock)chips[i].Child).Foreground = i == sel ? OnTxt : Txt;
                }
            };
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var c = new Border { Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 0, 14, 1), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand, Child = new TextBlock { Text = labels[i], FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } };
                c.MouseLeftButtonDown += delegate { paint(idx); pick(idx); };
                chips.Add(c);
                row.Children.Add(c);
            }
            paint(selected);
            return row;
        }

        ScrollViewer scroller;
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
}
