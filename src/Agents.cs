// Kapsula — «остров» для Windows. Часть исходного кода: Agents.cs
// ИИ-агенты на острове: контекст сессий, запуск Claude Code из строки команд,
// уведомления и вопросы от любых агентов по MCP.
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Kapsula
{
    partial class IslandWindow
    {
        static string Plural(int n, string one, string few, string many)
        {
            int m10 = n % 10, m100 = n % 100;
            return n + " " + (m10 == 1 && m100 != 11 ? one : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? few : many);
        }

        // ---- открыть папку проекта в VS Code (или в Проводнике, если редактора нет)
        void OpenInEditor(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) { Toast(ClaudeGlyph, "Папка проекта не найдена", null, -1, null); return; }
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c code \"" + dir + "\"") { CreateNoWindow = true, UseShellExecute = false };
                var p = System.Diagnostics.Process.Start(psi);
                p.WaitForExit(4000);
                if (p.HasExited && p.ExitCode != 0) System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); } catch { } }
        }

        // ---- запустить Claude Code с заданием в папке проекта (новая сессия в терминале)
        void RunClaude(string task, string dir)
        {
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string arg = task.Replace("\"", "'");
            // Windows Terminal делит команду по каждой «;», даже в кавычках: «\;» для него — просто точка с запятой
            string wtArg = arg.Replace(";", "\\;");
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("wt.exe", "-d " + WtQuoted(dir) + " claude " + WtQuoted(wtArg)) { UseShellExecute = true }); }
            catch
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/k claude \"" + arg + "\"") { WorkingDirectory = dir, UseShellExecute = true }); }
                catch { Toast(ClaudeGlyph, "Не нашёл Claude Code в системе", null, -1, null); return; }
            }
            Toast(ClaudeGlyph, "Claude Code получил задание", null, -1, accent);
        }

        // В кавычках для wt.exe. «\» перед закрывающей кавычкой экранирует её («D:\"» сбивает все аргументы) — удваиваем
        static string WtQuoted(string s)
        {
            int n = 0;
            while (n < s.Length && s[s.Length - 1 - n] == '\\') n++;
            return "\"" + s + new string('\\', n) + "\"";
        }

        static string ProjectName(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            string t = dir.TrimEnd('\\', '/');
            int i = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
            return i >= 0 ? t.Substring(i + 1) : t;
        }

        // ---- полоса заполнения контекста сессии
        FrameworkElement ContextBar(ClaudeBridge.Session s)
        {
            long t = ClaudeBridge.ContextTokens(s.Transcript);
            if (t <= 0) return null;
            long cap = t > 200000 ? 1000000 : 200000;   // длинный контекст — до миллиона
            double f = Math.Min(1, t / (double)cap);
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0), Height = 14 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.Children.Add(new TextBlock { Text = "Контекст", Foreground = Faint, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center });
            var track = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Bar.TrackBrush });
            Brush fill = f > 0.85 ? (Brush)ClaudeOrange : accent;
            var bar = new Border { CornerRadius = new CornerRadius(2), Background = fill, HorizontalAlignment = HorizontalAlignment.Left };
            track.SizeChanged += delegate { bar.Width = Math.Max(3, track.ActualWidth * f); };
            track.Children.Add(bar);
            Grid.SetColumn(track, 1); g.Children.Add(track);
            var v = new TextBlock { Text = ClaudeUsage.Short(t) + " из " + (cap >= 1000000 ? "1M" : "200k"), Foreground = Dim, FontSize = 10.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(v, 2); g.Children.Add(v);
            g.ToolTip = "Сколько контекста сессии занято: " + Math.Round(f * 100) + "%. Ближе к краю Claude Code начнёт сжимать разговор.";
            return g;
        }

        // ---- уведомления и вопросы от других агентов (MCP)
        void InitAgents()
        {
            ClaudeBridge.McpNotified += delegate(string agent, string title, string text)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (QuietNow) return;
                    ShowCardEx(null, ((char)0xE99A).ToString(), agent + " · " + (title.Length > 0 ? title : "уведомление"), text.Length > 0 ? text : " ", delegate { }, null, null, true);
                    CardPet("done");
                    cardUntil = Environment.TickCount + 7000;
                }));
            };
            ClaudeBridge.McpAsked += delegate(ClaudeBridge.McpQuestion q)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (QuietNow) { q.Say(McpQuiet); return; }   // «Не беспокоить»: не показываем, и агент узнаёт об этом сразу
                    mcpQueue.Add(q);
                    McpPump();
                }));
            };
        }

        // ---- вопросы агентов — по одному, как запросы Claude Code: ждут, пока остров свободен (панель закрыта, другой
        // карточки нет). Карточку вопроса сменила другая или панель — вопрос вернётся, когда остров освободится
        readonly List<ClaudeBridge.McpQuestion> mcpQueue = new List<ClaudeBridge.McpQuestion>();
        ClaudeBridge.McpQuestion mcpNow;   // вопрос, чья карточка сейчас на острове
        Action mcpNo;                      // её крестик: пока он у карточки, на острове всё ещё она
        System.Windows.Threading.DispatcherTimer mcpTimer;
        const string McpQuiet = "Пользователь включил «Не беспокоить» — спросите позже.";

        void McpPump()
        {
            mcpQueue.RemoveAll(delegate(ClaudeBridge.McpQuestion x) { return x.Answered; });
            if (mcpNow != null)
            {
                bool shown = object.ReferenceEquals(cardNoAction, mcpNo);
                if (mcpNow.Answered) { if (shown && state == 3) SetState(0); mcpNow = null; }   // ответ ушёл, а карточка висит — значит, ответил таймер: её кнопки уже ничего не отправят
                else if (shown && state == 3) return;                  // вопрос на острове — ждём ответа
                else if (shown && state == 0) mcpNow = null;           // карточку закрыли или её время вышло — агенту ответит таймер
                else { mcpQueue.Insert(0, mcpNow); mcpNow = null; }   // её сменила другая карточка или панель — покажем снова
            }
            if (mcpQueue.Count == 0) { if (mcpTimer != null) mcpTimer.Stop(); return; }
            if (QuietNow) { foreach (ClaudeBridge.McpQuestion w in mcpQueue) w.Say(McpQuiet); mcpQueue.Clear(); if (mcpTimer != null) mcpTimer.Stop(); return; }
            if (mcpTimer == null)
            {
                mcpTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                mcpTimer.Tick += delegate { McpPump(); };
            }
            mcpTimer.Start();   // пока есть вопросы — смотрим раз в полсекунды, не освободился ли остров
            if (state != 0 || claudeQueue.Count > 0) return;   // остров занят (или своей очереди ждёт Claude Code) — подождём
            ClaudeBridge.McpQuestion q = mcpQueue[0];
            mcpQueue.RemoveAt(0);
            // вопрос мог долго ждать очереди: карточка держится только до 115 с от вопроса, через 120 с агенту ответит таймер
            int left = 115000 - unchecked(Environment.TickCount - q.At);
            if (left < 3000) { q.Answer(null); return; }   // времени почти не осталось — показывать незачем
            var acts = new Action[q.Options.Length];
            for (int i = 0; i < q.Options.Length; i++) { string opt = q.Options[i]; acts[i] = delegate { q.Answer(opt); }; }
            Action no = delegate { q.Answer(""); };   // крестик — отказ, а не «не ответил за две минуты»
            ShowCardOptions(((char)0xE99A).ToString(), q.Agent + " спрашивает", q.Question, q.Options, acts, no, Math.Min(110000, left));
            if (state != 3) { mcpQueue.Insert(0, q); return; }
            mcpNow = q; mcpNo = no;
            CardPet("ask");
            ClaudeSound();
        }

        // Строки командной строки про агентов: «claude …» и «открыть проект в VS Code»
        void FillAgentCmd(string q, string pq)
        {
            string dir = ClaudeBridge.LastProject;
            string proj = ProjectName(dir);
            foreach (string w in new string[] { "claude ", "клод " })
                if (pq.StartsWith(w) && q.Length > w.Length + 1)
                {
                    string task = q.Substring(w.Length).Trim();
                    AddCmd("Спросить Claude Code: «" + (task.Length > 44 ? task.Substring(0, 43) + "…" : task) + "»", proj.Length > 0 ? "новая сессия · " + proj : "новая сессия в папке пользователя", ClaudeGlyph, null, delegate { RunClaude(task, dir); });
                    return;
                }
            if (proj.Length > 0 && Store.Get("vscodeButton") == "1" && (pq.StartsWith("vs") || pq.StartsWith("код") || pq.StartsWith("откр") || pq.StartsWith("code") || pq.StartsWith(proj.ToLowerInvariant())))
                AddCmd("Открыть " + proj + " в VS Code", "последний проект Claude Code", ((char)0xE943).ToString(), null, delegate { OpenInEditor(dir); });
        }
    }
}
