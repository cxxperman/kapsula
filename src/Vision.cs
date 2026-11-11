// Kapsula — «остров» для Windows. Часть исходного кода: Vision.cs
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
    // ============================================================== QR-коды
    // Собственный декодер: ищет QR-код на снимке экрана и читает его. Рассчитан на чёткие коды с экрана
    // (версии 1–10, без исправления ошибок Рида — Соломона: на экране модули не бывают повреждены).
    static class Qr
    {
        struct F { public double X, Y, M; }

        // исправляющих кодовых слов на блок, блоков в группе 1, данных в блоке группы 1, блоков в группе 2 — для уровней L, M, Q, H
        static readonly int[][] Blocks =
        {
            new int[] { 7,1,19,0,   10,1,16,0,  13,1,13,0,  17,1,9,0 },
            new int[] { 10,1,34,0,  16,1,28,0,  22,1,22,0,  28,1,16,0 },
            new int[] { 15,1,55,0,  26,1,44,0,  18,2,17,0,  22,2,13,0 },
            new int[] { 20,1,80,0,  18,2,32,0,  26,2,24,0,  16,4,9,0 },
            new int[] { 26,1,108,0, 24,2,43,0,  18,2,15,2,  22,2,11,2 },
            new int[] { 18,2,68,0,  16,4,27,0,  24,4,19,0,  28,4,15,0 },
            new int[] { 20,2,78,0,  18,4,31,0,  18,2,14,4,  26,4,13,1 },
            new int[] { 24,2,97,0,  22,2,38,2,  22,4,18,2,  26,4,14,2 },
            new int[] { 30,2,116,0, 22,3,36,2,  20,4,16,4,  24,4,12,4 },
            new int[] { 18,2,68,2,  26,4,43,1,  24,6,19,2,  28,6,15,2 }
        };
        static readonly int[][] Align = { new int[0], new int[] { 6, 18 }, new int[] { 6, 22 }, new int[] { 6, 26 }, new int[] { 6, 30 }, new int[] { 6, 34 }, new int[] { 6, 22, 38 }, new int[] { 6, 24, 42 }, new int[] { 6, 26, 46 }, new int[] { 6, 28, 50 } };

        public static string Decode(System.Drawing.Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var gray = new byte[w * h];
            var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[data.Stride];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, data.Stride);
                    int o = y * w;
                    for (int x = 0; x < w; x++) gray[o + x] = (byte)((row[x * 4 + 2] * 77 + row[x * 4 + 1] * 150 + row[x * 4] * 29) >> 8);
                }
            }
            finally { bmp.UnlockBits(data); }

            bool[] dark = Binarize(gray, w, h);
            List<F> fs = Finders(dark, w, h);
            for (int a = 0; a < fs.Count; a++)
                for (int b = 0; b < fs.Count; b++)
                    for (int c = b + 1; c < fs.Count; c++)
                    {
                        if (b == a || c == a) continue;
                        F A = fs[a], B = fs[b], C = fs[c];
                        double bx = B.X - A.X, by = B.Y - A.Y, cx = C.X - A.X, cy = C.Y - A.Y;
                        double lb = Math.Sqrt(bx * bx + by * by), lc = Math.Sqrt(cx * cx + cy * cy);
                        if (lb < 1 || lc < 1 || Math.Min(lb, lc) / Math.Max(lb, lc) < 0.85) continue;
                        if (Math.Abs(bx * cx + by * cy) / (lb * lc) > 0.15) continue; // угол при A должен быть прямым
                        double mMin = Math.Min(A.M, Math.Min(B.M, C.M)), mMax = Math.Max(A.M, Math.Max(B.M, C.M));
                        if (mMax / mMin > 1.5) continue;
                        F TR = bx * cy - by * cx > 0 ? B : C, BL = bx * cy - by * cx > 0 ? C : B;
                        double ms = (A.M + B.M + C.M) / 3;
                        int v0 = (int)Math.Round(((lb + lc) / 2 / ms + 7 - 17) / 4);
                        foreach (int v in new int[] { v0, v0 - 1, v0 + 1 })
                        {
                            if (v < 1 || v > 10) continue;
                            string s = Read(dark, w, h, A, TR, BL, v);
                            if (s != null) return s;
                        }
                    }
            return null;
        }

        // Порог считается по плиткам: середина между самым тёмным и самым светлым. Ровные участки тёмными не считаются.
        static bool[] Binarize(byte[] g, int w, int h)
        {
            const int T = 96;
            int tw = (w + T - 1) / T, th = (h + T - 1) / T;
            var thr = new int[tw * th];
            for (int ty = 0; ty < th; ty++)
                for (int tx = 0; tx < tw; tx++)
                {
                    int min = 255, max = 0;
                    int y1 = Math.Min(h, (ty + 1) * T), x1 = Math.Min(w, (tx + 1) * T);
                    for (int y = ty * T; y < y1; y += 2)
                        for (int x = tx * T; x < x1; x += 2) { int p = g[y * w + x]; if (p < min) min = p; if (p > max) max = p; }
                    thr[ty * tw + tx] = max - min >= 70 ? (min + max) / 2 : -1;
                }
            var d = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                int ty = y / T;
                for (int x = 0; x < w; x++)
                {
                    int t = thr[ty * tw + x / T];
                    // у ровной плитки берём порог соседей, чтобы большой тёмный модуль не «терялся»
                    if (t < 0)
                    {
                        int best = -1;
                        for (int dy = -1; dy <= 1 && best < 0; dy++)
                            for (int dx = -1; dx <= 1 && best < 0; dx++)
                            {
                                int yy = ty + dy, xx = x / T + dx;
                                if (yy >= 0 && yy < th && xx >= 0 && xx < tw && thr[yy * tw + xx] >= 0) best = thr[yy * tw + xx];
                            }
                        t = best;
                    }
                    d[y * w + x] = t >= 0 && g[y * w + x] < t;
                }
            }
            return d;
        }

        static bool Ratio(int[] c)
        {
            int total = c[0] + c[1] + c[2] + c[3] + c[4];
            if (total < 7 || c[0] == 0 || c[1] == 0 || c[2] == 0 || c[3] == 0 || c[4] == 0) return false;
            double m = total / 7.0, v = m / 2;
            return Math.Abs(m - c[0]) < v && Math.Abs(m - c[1]) < v && Math.Abs(3 * m - c[2]) < 3 * v && Math.Abs(m - c[3]) < v && Math.Abs(m - c[4]) < v;
        }

        // Проверка узора 1:1:3:1:1 вдоль прямой через предполагаемый центр; возвращает уточнённую координату центра
        static double Cross(bool[] d, int w, int h, int cx, int cy, bool vertical, int max, int total)
        {
            int len = vertical ? h : w, pos = vertical ? cy : cx;
            Func<int, bool> at = delegate(int p) { return vertical ? d[p * w + cx] : d[cy * w + p]; };
            if (cx < 0 || cy < 0 || cx >= w || cy >= h || !at(pos)) return double.NaN;
            var s = new int[5];
            int i = pos;
            while (i >= 0 && at(i)) { s[2]++; i--; }
            if (i < 0) return double.NaN;
            while (i >= 0 && !at(i) && s[1] <= max) { s[1]++; i--; }
            if (i < 0 || s[1] > max) return double.NaN;
            while (i >= 0 && at(i) && s[0] <= max) { s[0]++; i--; }
            if (s[0] > max) return double.NaN;
            i = pos + 1;
            while (i < len && at(i)) { s[2]++; i++; }
            if (i == len) return double.NaN;
            while (i < len && !at(i) && s[3] < max) { s[3]++; i++; }
            if (i == len || s[3] >= max) return double.NaN;
            while (i < len && at(i) && s[4] < max) { s[4]++; i++; }
            if (s[4] >= max) return double.NaN;
            int tot = s[0] + s[1] + s[2] + s[3] + s[4];
            if (5 * Math.Abs(tot - total) >= 2 * total || !Ratio(s)) return double.NaN;
            return i - s[4] - s[3] - s[2] / 2.0;
        }

        static List<F> Finders(bool[] d, int w, int h)
        {
            var res = new List<F>();
            var c = new int[5];
            for (int y = 0; y < h; y++)
            {
                int st = 0;
                c[0] = c[1] = c[2] = c[3] = c[4] = 0;
                int o = y * w;
                for (int x = 0; x <= w; x++)
                {
                    bool dk = x < w && d[o + x];
                    if (dk) { if ((st & 1) == 1) st++; c[st]++; continue; }
                    if ((st & 1) == 1) { if (x < w) c[st]++; continue; }
                    if (st == 4)
                    {
                        if (Ratio(c))
                        {
                            int total = c[0] + c[1] + c[2] + c[3] + c[4];
                            double cx = x - c[4] - c[3] - c[2] / 2.0;
                            double cy = Cross(d, w, h, (int)cx, y, true, c[2], total);
                            if (!double.IsNaN(cy))
                            {
                                double cx2 = Cross(d, w, h, (int)cx, (int)cy, false, c[2], total);
                                if (!double.IsNaN(cx2))
                                {
                                    double m = total / 7.0;
                                    bool merged = false;
                                    for (int i = 0; i < res.Count; i++)
                                    {
                                        F f = res[i];
                                        if (Math.Abs(f.X - cx2) < 3 * m && Math.Abs(f.Y - cy) < 3 * m)
                                        {
                                            f.X = (f.X + cx2) / 2; f.Y = (f.Y + cy) / 2; f.M = (f.M + m) / 2;
                                            res[i] = f; merged = true; break;
                                        }
                                    }
                                    if (!merged && res.Count < 60) res.Add(new F { X = cx2, Y = cy, M = m });
                                }
                            }
                            st = 0; c[0] = c[1] = c[2] = c[3] = c[4] = 0;
                        }
                        else { c[0] = c[2]; c[1] = c[3]; c[2] = c[4]; c[3] = 1; c[4] = 0; st = 3; }
                    }
                    else if (st != 0 || c[0] != 0) { st++; c[st]++; }
                }
            }
            return res;
        }

        static string Read(bool[] dark, int w, int h, F TL, F TR, F BL, int ver)
        {
            int N = 17 + 4 * ver;
            var mod = new bool[N, N];
            for (int my = 0; my < N; my++)
                for (int mx = 0; mx < N; mx++)
                {
                    double fx = (mx - 3.0) / (N - 7), fy = (my - 3.0) / (N - 7);
                    int px = (int)Math.Round(TL.X + fx * (TR.X - TL.X) + fy * (BL.X - TL.X));
                    int py = (int)Math.Round(TL.Y + fx * (TR.Y - TL.Y) + fy * (BL.Y - TL.Y));
                    if (px < 0 || py < 0 || px >= w || py >= h) return null;
                    mod[mx, my] = dark[py * w + px];
                }

            // Формат: 15 бит возле левого верхнего угла
            int fmt = 0;
            for (int i = 0; i < 6; i++) fmt = (fmt << 1) | (mod[i, 8] ? 1 : 0);
            fmt = (fmt << 1) | (mod[7, 8] ? 1 : 0);
            fmt = (fmt << 1) | (mod[8, 8] ? 1 : 0);
            fmt = (fmt << 1) | (mod[8, 7] ? 1 : 0);
            for (int j = 5; j >= 0; j--) fmt = (fmt << 1) | (mod[8, j] ? 1 : 0);
            int bestD = -1, bestDist = 99;
            for (int dd = 0; dd < 32; dd++)
            {
                int rem = dd << 10;
                for (int i = 14; i >= 10; i--) if (((rem >> i) & 1) != 0) rem ^= 0x537 << (i - 10);
                int code = ((dd << 10) | (rem & 0x3FF)) ^ 0x5412;
                int x = code ^ fmt, dist = 0;
                while (x != 0) { dist += x & 1; x >>= 1; }
                if (dist < bestDist) { bestDist = dist; bestD = dd; }
            }
            if (bestDist > 3) return null;
            int ec = (bestD >> 3) & 3, mask = bestD & 7;
            int level = ec == 1 ? 0 : (ec == 0 ? 1 : (ec == 3 ? 2 : 3));

            // Служебные области, в которых данных нет
            var func = new bool[N, N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    func[x, y] = (x <= 8 && y <= 8) || (x >= N - 8 && y <= 8) || (x <= 8 && y >= N - 8) || x == 6 || y == 6
                        || (ver >= 7 && ((x >= N - 11 && x <= N - 9 && y <= 5) || (y >= N - 11 && y <= N - 9 && x <= 5)));
            int[] al = Align[ver - 1];
            foreach (int ay in al)
                foreach (int ax in al)
                {
                    if ((ax == 6 && ay == 6) || (ax == 6 && ay == N - 7) || (ax == N - 7 && ay == 6)) continue;
                    for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) func[ax + dx, ay + dy] = true;
                }

            // Чтение бит змейкой справа налево
            var bits = new List<bool>();
            bool up = true;
            for (int x = N - 1; x > 0; x -= 2)
            {
                if (x == 6) x--;
                for (int k = 0; k < N; k++)
                {
                    int y = up ? N - 1 - k : k;
                    for (int cc = 0; cc < 2; cc++)
                    {
                        int xx = x - cc;
                        if (func[xx, y]) continue;
                        bool m;
                        switch (mask)
                        {
                            case 0: m = (y + xx) % 2 == 0; break;
                            case 1: m = y % 2 == 0; break;
                            case 2: m = xx % 3 == 0; break;
                            case 3: m = (y + xx) % 3 == 0; break;
                            case 4: m = (y / 2 + xx / 3) % 2 == 0; break;
                            case 5: m = (y * xx) % 2 + (y * xx) % 3 == 0; break;
                            case 6: m = ((y * xx) % 2 + (y * xx) % 3) % 2 == 0; break;
                            default: m = ((y + xx) % 2 + (y * xx) % 3) % 2 == 0; break;
                        }
                        bits.Add(mod[xx, y] ^ m);
                    }
                }
                up = !up;
            }
            var cw = new byte[bits.Count / 8];
            for (int i = 0; i < cw.Length; i++)
            {
                int b = 0;
                for (int k = 0; k < 8; k++) b = (b << 1) | (bits[i * 8 + k] ? 1 : 0);
                cw[i] = (byte)b;
            }

            // Разбор чередования блоков: берём только кодовые слова данных
            int[] t = Blocks[ver - 1];
            int g1 = t[level * 4 + 1], d1 = t[level * 4 + 2], g2 = t[level * 4 + 3];
            int nb = g1 + g2, totalData = g1 * d1 + g2 * (d1 + 1);
            if (cw.Length < totalData) return null;
            var blocks = new byte[nb][];
            for (int b = 0; b < nb; b++) blocks[b] = new byte[b < g1 ? d1 : d1 + 1];
            int pos = 0;
            for (int i = 0; i < d1 + 1; i++)
                for (int b = 0; b < nb; b++)
                    if (i < blocks[b].Length) blocks[b][i] = cw[pos++];
            var dataBytes = new byte[totalData];
            int q = 0;
            foreach (byte[] b in blocks) { Array.Copy(b, 0, dataBytes, q, b.Length); q += b.Length; }
            return Segments(dataBytes, ver);
        }

        static string Segments(byte[] data, int ver)
        {
            int bit = 0, totalBits = data.Length * 8;
            Func<int, int> take = delegate(int n)
            {
                if (bit + n > totalBits) { bit = totalBits + 1; return 0; }
                int v = 0;
                for (int i = 0; i < n; i++) { v = (v << 1) | ((data[bit >> 3] >> (7 - (bit & 7))) & 1); bit++; }
                return v;
            };
            const string AN = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";
            var sb = new StringBuilder();
            var bytes = new List<byte>();
            bool any = false;
            while (bit + 4 <= totalBits)
            {
                int mode = take(4);
                if (mode == 0) break;
                if (mode == 7) { take(8); continue; }
                if (mode == 1)
                {
                    int n = take(ver < 10 ? 10 : 12);
                    while (n >= 3) { int v = take(10); if (v > 999) return null; sb.Append(v.ToString("000")); n -= 3; }
                    if (n == 2) { int v = take(7); if (v > 99) return null; sb.Append(v.ToString("00")); }
                    else if (n == 1) { int v = take(4); if (v > 9) return null; sb.Append(v); }
                }
                else if (mode == 2)
                {
                    int n = take(ver < 10 ? 9 : 11);
                    while (n >= 2) { int v = take(11); if (v >= 45 * 45) return null; sb.Append(AN[v / 45]).Append(AN[v % 45]); n -= 2; }
                    if (n == 1) { int v = take(6); if (v >= 45) return null; sb.Append(AN[v]); }
                }
                else if (mode == 4)
                {
                    int n = take(ver < 10 ? 8 : 16);
                    if (n == 0 || bit + n * 8 > totalBits) return null;
                    bytes.Clear();
                    for (int i = 0; i < n; i++) bytes.Add((byte)take(8));
                    byte[] arr = bytes.ToArray();
                    string s;
                    try { s = new UTF8Encoding(false, true).GetString(arr); }
                    catch { s = Encoding.GetEncoding(28591).GetString(arr); }
                    sb.Append(s);
                }
                else return null;
                if (bit > totalBits) return null;
                any = true;
            }
            if (!any || sb.Length == 0) return null;
            foreach (char ch in sb.ToString()) if (ch < 32 && ch != '\n' && ch != '\r' && ch != '\t') return null;
            return sb.ToString();
        }
    }

    // ====================================================== Текст с экрана
    // Встроенное в Windows распознавание текста; в сеть не ходит. Русский и английский прогоняются оба и сводятся построчно.
    static class Ocr
    {
        static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            int n = 0;
            while (op.Status == Windows.Foundation.AsyncStatus.Started && n++ < 1500) Thread.Sleep(10);
            return op.Status == Windows.Foundation.AsyncStatus.Completed ? op.GetResults() : default(T);
        }

        static List<string> Lines(Windows.Graphics.Imaging.SoftwareBitmap bmp, string tag)
        {
            var lang = new Windows.Globalization.Language(tag);
            if (!Windows.Media.Ocr.OcrEngine.IsLanguageSupported(lang)) return null;
            var eng = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(lang);
            if (eng == null) return null;
            var res = Wait(eng.RecognizeAsync(bmp));
            if (res == null) return null;
            var list = new List<string>();
            foreach (var line in res.Lines) list.Add(line.Text);
            return list;
        }

        static int Cyr(string s)
        {
            int n = 0;
            foreach (char c in s) if ((c >= 'А' && c <= 'я') || c == 'ё' || c == 'Ё') n++;
            return n;
        }

        public static string Recognize(byte[] png)
        {
            var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var writer = new Windows.Storage.Streams.DataWriter(stream);
            writer.WriteBytes(png);
            Wait<uint>(writer.StoreAsync());
            Wait<bool>(writer.FlushAsync());
            writer.DetachStream();
            stream.Seek(0);
            var dec = Wait(Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream));
            if (dec == null) return null;
            var bmp = Wait(dec.GetSoftwareBitmapAsync());
            if (bmp == null) return null;

            List<string> ru = Lines(bmp, "ru"), en = Lines(bmp, "en-US");
            if (ru == null) ru = en;
            if (ru == null) return null;
            // Строку без кириллицы берём из английского распознавания: латиницу оно читает точнее.
            if (en != null && en.Count == ru.Count)
                for (int i = 0; i < ru.Count; i++) if (Cyr(ru[i]) == 0) ru[i] = en[i];
            return string.Join("\r\n", ru.ToArray()).Trim();
        }
    }
}
