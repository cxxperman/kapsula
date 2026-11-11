// Kapsula — «остров» для Windows. Часть исходного кода: Media.cs
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
    // ===================================================== Что сейчас играет
    class MediaInfo
    {
        public bool Has, Playing;
        public string Title = "", Artist = "";
        public byte[] Cover;
        public double Pos, Dur;
        public DateTime PosAt = DateTime.UtcNow;

        public double Now()
        {
            double p = Pos;
            if (Playing) p += (DateTime.UtcNow - PosAt).TotalSeconds;
            if (Dur > 0 && p > Dur) p = Dur;
            return p < 0 ? 0 : p;
        }
    }

    class MediaWatcher
    {
        public event Action<MediaInfo> Updated;
        volatile bool stop;

        public void Start() { var t = new Thread(Loop); t.IsBackground = true; t.Start(); }
        public void Stop() { stop = true; }

        static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            int n = 0;
            while (op.Status == Windows.Foundation.AsyncStatus.Started && n++ < 300) Thread.Sleep(10);
            if (op.Status != Windows.Foundation.AsyncStatus.Completed) return default(T);
            return op.GetResults();
        }

        void Loop()
        {
            GlobalSystemMediaTransportControlsSessionManager mgr = null;
            string lastKey = null; byte[] cover = null; int coverTries = 0;
            string sentSig = null;
            while (!stop)
            {
                var info = new MediaInfo();
                try
                {
                    if (mgr == null) mgr = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
                    var s = mgr == null ? null : mgr.GetCurrentSession();
                    if (s != null)
                    {
                        var p = Wait(s.TryGetMediaPropertiesAsync());
                        if (p != null && !string.IsNullOrEmpty(p.Title))
                        {
                            info.Has = true;
                            info.Title = p.Title ?? "";
                            info.Artist = p.Artist ?? "";
                            var pb = s.GetPlaybackInfo();
                            info.Playing = pb != null && pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                            try
                            {
                                var tl = s.GetTimelineProperties();
                                if (tl != null)
                                {
                                    info.Dur = (tl.EndTime - tl.StartTime).TotalSeconds;
                                    info.Pos = (tl.Position - tl.StartTime).TotalSeconds;
                                    info.PosAt = tl.LastUpdatedTime.UtcDateTime;
                                    if (info.PosAt.Year < 2000) info.PosAt = DateTime.UtcNow;
                                }
                            }
                            catch { }
                            string key = info.Title + "|" + info.Artist;
                            if (key != lastKey) { lastKey = key; cover = null; coverTries = 0; }
                            if (cover == null && coverTries < 6 && p.Thumbnail != null) { coverTries++; cover = ReadCover(p.Thumbnail); }
                            info.Cover = cover;
                        }
                    }
                }
                catch { mgr = null; }

                string sig = info.Has + "|" + info.Playing + "|" + info.Title + "|" + info.Artist + "|" + (info.Cover == null ? 0 : info.Cover.Length) + "|" + (long)info.Pos + "|" + (long)info.Dur;
                if (sig != sentSig)
                {
                    sentSig = sig;
                    var h = Updated;
                    if (h != null) h(info);
                }
                Thread.Sleep(1000);
            }
        }

        static byte[] ReadCover(Windows.Storage.Streams.IRandomAccessStreamReference r)
        {
            try
            {
                var stream = Wait(r.OpenReadAsync());
                if (stream == null) return null;
                uint size = (uint)stream.Size;
                if (size == 0 || size > 8 * 1024 * 1024) return null;
                var reader = new Windows.Storage.Streams.DataReader(stream);
                Wait<uint>(reader.LoadAsync(size));
                var buf = new byte[size];
                reader.ReadBytes(buf);
                return buf;
            }
            catch { return null; }
        }
    }

    // ========================================================== Тексты песен
    class LyricLine { public double T; public string Text; }

    // Синхронные тексты с lrclib.net. Отправляются только название, исполнитель и длительность играющего трека.
    static class Lyrics
    {
        static string E(string s) { return Uri.EscapeDataString(s ?? ""); }

        static string Get(string url)
        {
            using (var wc = new System.Net.WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers["User-Agent"] = "Kapsula desktop widget";
                return wc.DownloadString(url);
            }
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as string : null;
        }

        static string Clean(string title)
        {
            string t = System.Text.RegularExpressions.Regex.Replace(title ?? "", @"\s*[\(\[][^\)\]]*[\)\]]", "");
            return t.Trim().Length > 0 ? t.Trim() : title;
        }

        public static List<LyricLine> Fetch(string title, string artist, double dur)
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var js = new System.Web.Script.Serialization.JavaScriptSerializer();
                js.MaxJsonLength = 16 * 1024 * 1024;
                string synced = null;
                try
                {
                    string url = "https://lrclib.net/api/get?track_name=" + E(title) + "&artist_name=" + E(artist) + (dur > 1 ? "&duration=" + (int)Math.Round(dur) : "");
                    synced = Str(js.DeserializeObject(Get(url)) as Dictionary<string, object>, "syncedLyrics");
                }
                catch (System.Net.WebException) { }

                if (string.IsNullOrEmpty(synced))
                {
                    var arr = js.DeserializeObject(Get("https://lrclib.net/api/search?track_name=" + E(Clean(title)) + "&artist_name=" + E(artist))) as object[];
                    double best = double.MaxValue;
                    if (arr != null)
                        foreach (object o in arr)
                        {
                            var d = o as Dictionary<string, object>;
                            string s = Str(d, "syncedLyrics");
                            if (string.IsNullOrEmpty(s)) continue;
                            object du;
                            double diff = dur > 1 && d.TryGetValue("duration", out du) && du != null ? Math.Abs(Convert.ToDouble(du, inv) - dur) : 0;
                            if (diff < best) { best = diff; synced = s; }
                        }
                    // Другая версия трека — строки разъедутся со звуком, лучше не показывать.
                    if (best > 6 && best != double.MaxValue) synced = null;
                }
                var res = Parse(synced);
                Log(title, artist, dur, res == null ? "текст не найден" : "строк: " + res.Count);
                return res;
            }
            catch (Exception ex) { Log(title, artist, dur, "ошибка: " + ex.GetType().Name + " " + ex.Message); return null; }
        }

        // Короткий журнал последних запросов — чтобы было видно, почему текста нет.
        static void Log(string title, string artist, double dur, string outcome)
        {
            try
            {
                string f = System.IO.Path.Combine(Store.Dir, "lyrics.log");
                if (System.IO.File.Exists(f) && new System.IO.FileInfo(f).Length > 20000) System.IO.File.Delete(f);
                System.IO.File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss") + " | " + title + " | " + artist + " | " + Math.Round(dur) + " с | " + outcome + "\r\n", Encoding.UTF8);
            }
            catch { }
        }

        static List<LyricLine> Parse(string lrc)
        {
            if (string.IsNullOrEmpty(lrc)) return null;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var list = new List<LyricLine>();
            var rx = new System.Text.RegularExpressions.Regex(@"\[(\d+):(\d+(?:[\.,]\d+)?)\]");
            foreach (string raw in lrc.Split('\n'))
            {
                var ms = rx.Matches(raw);
                if (ms.Count == 0) continue;
                var last = ms[ms.Count - 1];
                string text = raw.Substring(last.Index + last.Length).Trim();
                if (text.Length == 0) text = "♪";
                foreach (System.Text.RegularExpressions.Match m in ms)
                    list.Add(new LyricLine { T = int.Parse(m.Groups[1].Value, inv) * 60 + double.Parse(m.Groups[2].Value.Replace(',', '.'), inv), Text = text });
            }
            list.Sort(delegate(LyricLine a, LyricLine b) { return a.T.CompareTo(b.T); });
            return list.Count > 0 ? list : null;
        }
    }

    // Лента текста песни: все строки едут вверх, текущая плавно подсвечивается.
    class LyricView
    {
        public Grid Root;
        StackPanel stack;
        TranslateTransform tt = new TranslateTransform();
        List<TextBlock> lines = new List<TextBlock>();
        int applied = -2;
        double dim, fontSize;
        bool wrap;
        Brush brush;

        public LyricView(double width, double height, bool wrap, double dim, Brush brush, double fontSize, bool fadeEdges)
        {
            this.wrap = wrap; this.dim = dim; this.brush = brush; this.fontSize = fontSize;
            Root = new Grid { Width = width, Height = height, ClipToBounds = true, IsHitTestVisible = false };
            // Canvas не обрезает содержимое по своей высоте, поэтому длинная лента прокручивается целиком.
            stack = new StackPanel { Width = width, RenderTransform = tt };
            var canvas = new Canvas();
            canvas.Children.Add(stack);
            Root.Children.Add(canvas);
            if (fadeEdges)
            {
                // Координаты маски — в точках самого окошка, а не в долях. В долях маска растягивалась на всю ленту строк,
                // даже невидимую, и в начале и в конце песни текущая строка попадала в «затухание» и выглядела тусклой.
                var m = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(0, height) };
                m.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
                m.GradientStops.Add(new GradientStop(Colors.Black, 0.26));
                m.GradientStops.Add(new GradientStop(Colors.Black, 0.74));
                m.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
                Root.OpacityMask = m;
            }
        }

        public void SetLines(List<LyricLine> l)
        {
            stack.Children.Clear();
            lines.Clear();
            applied = -2;
            tt.BeginAnimation(TranslateTransform.YProperty, null);
            tt.Y = 0;
            if (l == null) return;
            foreach (LyricLine ll in l)
            {
                var tb = new TextBlock
                {
                    Text = ll.Text, Foreground = brush, FontSize = fontSize, FontWeight = FontWeights.SemiBold, Opacity = dim,
                    TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 0, wrap ? 10 : 0)
                };
                if (wrap) { tb.LineHeight = fontSize + 4.5; tb.MaxHeight = (fontSize + 4.5) * 3; }
                else { tb.Height = Root.Height; }
                lines.Add(tb);
                stack.Children.Add(tb);
            }
        }

        public void Tick(int idx)
        {
            if (idx == applied || lines.Count == 0) return;
            if (idx >= lines.Count) idx = lines.Count - 1;
            double y;
            if (idx < 0) y = wrap ? Root.Height * 0.5 : Root.Height;
            else
            {
                var tb = lines[idx];
                if (tb.ActualHeight <= 0) return; // ещё не разложено — повторим на следующем такте
                y = Root.Height / 2 - (tb.TranslatePoint(new Point(0, 0), stack).Y + tb.ActualHeight / 2);
            }
            bool first = applied == -2;
            int old = applied;
            applied = idx;
            if (first)
            {
                tt.BeginAnimation(TranslateTransform.YProperty, null);
                tt.Y = y;
                for (int i = 0; i < lines.Count; i++) { lines[i].BeginAnimation(UIElement.OpacityProperty, null); lines[i].Opacity = i == idx ? 1 : dim; }
                return;
            }
            var a = new DoubleAnimation(y, TimeSpan.FromMilliseconds(620)) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } };
            tt.BeginAnimation(TranslateTransform.YProperty, a);
            if (old >= 0 && old < lines.Count) Fade(lines[old], dim, 520);
            if (idx >= 0) Fade(lines[idx], 1, 380);
        }

        static void Fade(TextBlock tb, double to, int ms)
        {
            tb.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }
}
