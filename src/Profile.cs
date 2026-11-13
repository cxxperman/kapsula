// Kapsula — «остров» для Windows. Часть исходного кода: Profile.cs
// Замер нагрузки (только для проверки, KAPSULA_PROFILE=1): сколько процессора берёт каждый поток Kapsula и сколько
// времени уходит на опрос мыши, медленный такт и кадры анимации. С 30-й по 60-ю секунду после запуска — отчёт
// profile.txt в папке настроек. Без ключа ничего не делает.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Kapsula
{
    static class Prof
    {
        public static readonly bool On = Environment.GetEnvironmentVariable("KAPSULA_PROFILE") == "1";
        static readonly Dictionary<string, long[]> acc = new Dictionary<string, long[]>();   // такты Stopwatch, вызовы
        static Dictionary<int, TimeSpan> threadsAt = new Dictionary<int, TimeSpan>();
        static long markedAt;
        public static uint UiThread;

        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

        public static long T() { return On ? Stopwatch.GetTimestamp() : 0; }

        public static void Add(string name, long t0)
        {
            if (!On) return;
            long dt = Stopwatch.GetTimestamp() - t0;
            lock (acc)
            {
                long[] a;
                if (!acc.TryGetValue(name, out a)) acc[name] = a = new long[2];
                a[0] += dt; a[1]++;
            }
        }

        // начало окна замера: запомнить процессорное время всех потоков и обнулить счётчики
        public static void Mark()
        {
            var at = new Dictionary<int, TimeSpan>();
            using (var p = Process.GetCurrentProcess())
                foreach (ProcessThread t in p.Threads) try { at[t.Id] = t.TotalProcessorTime; } catch { }
            threadsAt = at;
            lock (acc) acc.Clear();
            markedAt = Stopwatch.GetTimestamp();
        }

        public static string Report()
        {
            double seconds = (Stopwatch.GetTimestamp() - markedAt) / (double)Stopwatch.Frequency;
            var sb = new StringBuilder();
            sb.Append("Замер за ").Append(seconds.ToString("0.0")).Append(" с\r\n");
            double total = 0;
            var rows = new List<KeyValuePair<int, double>>();
            using (var p = Process.GetCurrentProcess())
                foreach (ProcessThread t in p.Threads)
                    try
                    {
                        TimeSpan before;
                        threadsAt.TryGetValue(t.Id, out before);
                        double ms = (t.TotalProcessorTime - before).TotalMilliseconds;
                        total += ms;
                        if (ms > 0.5) rows.Add(new KeyValuePair<int, double>(t.Id, ms));
                    }
                    catch { }
            rows.Sort(delegate(KeyValuePair<int, double> a, KeyValuePair<int, double> b) { return b.Value.CompareTo(a.Value); });
            sb.Append("всего: ").Append((total / seconds / 10).ToString("0.00")).Append(" % одного ядра\r\n");
            foreach (var r in rows)
                sb.Append("  поток ").Append(r.Key).Append(r.Key == UiThread ? " (окно острова)" : "").Append(": ").Append((r.Value / seconds / 10).ToString("0.00")).Append(" %\r\n");
            lock (acc)
            {
                var names = new List<string>(acc.Keys);
                names.Sort(delegate(string a, string b) { return acc[b][0].CompareTo(acc[a][0]); });
                foreach (string n in names)
                {
                    long[] a = acc[n];
                    sb.Append(n).Append(": ").Append((a[0] * 1000.0 / Stopwatch.Frequency / seconds).ToString("0.00")).Append(" мс в секунду, ")
                      .Append((a[1] / seconds).ToString("0.0")).Append(" раз в секунду\r\n");
                }
            }
            return sb.ToString();
        }
    }
}
