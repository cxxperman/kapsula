// Kapsula — «остров» для Windows. Часть исходного кода: AudioCapture.cs
// Захват системного звука для записи экрана.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Kapsula
{
    [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint flags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    // Спектр того, что сейчас играет: нужен только жидкому краю острова, чтобы он плясал как эквалайзер
    // (слева низкие частоты, справа высокие). Звук разбирается в памяти и никуда не сохраняется и не передаётся.
    static class Spectrum
    {
        const int N = 1024;
        static readonly float[] ring = new float[N];
        static int ringPos, rate = 48000, lastPacketAt, lastUseAt, retryAt;
        static readonly object gate = new object();
        static Thread thread;
        static volatile bool running;
        static double gain = 0.02;
        static readonly double[] re = new double[N], im = new double[N], window = new double[N];

        static Spectrum() { for (int i = 0; i < N; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1)); }

        // Заполняет bands значениями 0…1 (от низких частот к высоким). false — звук снять не удалось или тишина.
        public static bool Read(double[] bands)
        {
            lastUseAt = Environment.TickCount;
            // Не вышло снять звук (нет устройства вывода) — пробуем снова не чаще раза в две секунды.
            if (!running) { if (unchecked(Environment.TickCount - retryAt) >= 0) Start(); return false; }
            if (unchecked(Environment.TickCount - lastPacketAt) > 250) return false;   // давно ничего не играло
            lock (gate) { for (int i = 0; i < N; i++) { re[i] = ring[(ringPos + i) % N] * window[i]; im[i] = 0; } }
            Fft(re, im);
            double top = 0;
            int n = bands.Length;
            for (int b = 0; b < n; b++)
            {
                // границы полосы: от 60 Гц до 9 кГц, равные шаги по логарифму — как слышит ухо
                double f0 = 60 * Math.Pow(150, (double)b / n), f1 = 60 * Math.Pow(150, (double)(b + 1) / n);
                int k0 = Math.Max(1, (int)(f0 * N / rate)), k1 = Math.Max(k0 + 1, (int)(f1 * N / rate));
                double m = 0;
                for (int k = k0; k < k1 && k < N / 2; k++) { double v = Math.Sqrt(re[k] * re[k] + im[k] * im[k]); if (v > m) m = v; }
                m *= 1 + 2.2 * b / n;   // высокие частоты тише низких — приподнимаем, чтобы правая часть не лежала
                bands[b] = m;
                if (m > top) top = m;
            }
            gain = Math.Max(top, gain * 0.99);
            if (gain < 0.02) gain = 0.02;
            for (int b = 0; b < n; b++) bands[b] = Math.Min(1, Math.Pow(bands[b] / gain, 0.7));
            return true;
        }

        public static void Fft(double[] xr, double[] xi)
        {
            int n = xr.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { double t = xr[i]; xr[i] = xr[j]; xr[j] = t; t = xi[i]; xi[i] = xi[j]; xi[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double tr = xr[b] * cr - xi[b] * ci, ti = xr[b] * ci + xi[b] * cr;
                        xr[b] = xr[a] - tr; xi[b] = xi[a] - ti;
                        xr[a] += tr; xi[a] += ti;
                        double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                    }
                }
            }
        }

        static void Start()
        {
            if (running) return;
            running = true;
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Start();
        }

        [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        static void Run()
        {
            IAudioClient client = null;
            IntPtr fmt = IntPtr.Zero;
            try
            {
                var en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorCom());
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return;
                Guid iid = typeof(IAudioClient).GUID;
                object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0 || o == null) return;
                client = (IAudioClient)o;
                if (client.GetMixFormat(out fmt) != 0 || fmt == IntPtr.Zero) return;
                int tag = (ushort)Marshal.ReadInt16(fmt, 0), channels = Marshal.ReadInt16(fmt, 2), align = Marshal.ReadInt16(fmt, 12), bits = Marshal.ReadInt16(fmt, 14);
                rate = Marshal.ReadInt32(fmt, 4);
                if (tag == 0xFFFE) tag = Marshal.ReadInt32(fmt, 24) & 0xFFFF;
                bool isFloat = tag == 3 && bits == 32, isShort = tag == 1 && bits == 16;
                if ((!isFloat && !isShort) || channels < 1 || channels > 8) return;
                if (client.Initialize(0, 0x00020000u, 2000000, 0, fmt, IntPtr.Zero) != 0) return;
                Guid capIid = typeof(IAudioCaptureClient).GUID;
                object co;
                if (client.GetService(ref capIid, out co) != 0 || co == null) return;
                var capture = (IAudioCaptureClient)co;
                if (client.Start() != 0) return;
                var buf = new byte[align * 4096];
                // Работаем, пока спектр кому-то нужен; через три секунды без запросов останавливаемся.
                while (unchecked(Environment.TickCount - lastUseAt) < 3000)
                {
                    uint n;
                    if (capture.GetNextPacketSize(out n) != 0) break;
                    while (n > 0)
                    {
                        IntPtr data; uint frames, flags; ulong p1, p2;
                        if (capture.GetBuffer(out data, out frames, out flags, out p1, out p2) != 0) { n = 0; break; }
                        int bytes = (int)frames * align;
                        bool silent = (flags & 2) != 0 || data == IntPtr.Zero;
                        if (!silent && bytes <= buf.Length) Marshal.Copy(data, buf, 0, bytes);
                        capture.ReleaseBuffer(frames);
                        if (bytes <= buf.Length)
                        {
                            lock (gate)
                            {
                                for (int f = 0; f < (int)frames; f++)
                                {
                                    float s = 0;
                                    if (!silent)
                                    {
                                        int at = f * align;
                                        if (isFloat) { s = BitConverter.ToSingle(buf, at); if (channels > 1) s = (s + BitConverter.ToSingle(buf, at + 4)) * 0.5f; }
                                        else { s = BitConverter.ToInt16(buf, at) / 32768f; if (channels > 1) s = (s + BitConverter.ToInt16(buf, at + 2) / 32768f) * 0.5f; }
                                    }
                                    ring[ringPos] = s;
                                    ringPos = (ringPos + 1) % N;
                                }
                            }
                            if (!silent) lastPacketAt = Environment.TickCount;
                        }
                        if (capture.GetNextPacketSize(out n) != 0) n = 0;
                    }
                    Thread.Sleep(8);
                }
            }
            catch (Exception ex) { Store.LogError("спектр звука", ex); }
            finally
            {
                try { if (client != null) client.Stop(); } catch { }
                if (fmt != IntPtr.Zero) CoTaskMemFree(fmt);
                retryAt = Environment.TickCount + 2000;
                running = false;
            }
        }
    }

    // Снимает то, что играет на устройстве вывода по умолчанию («петля» WASAPI), и отдаёт сырой звук в именованный канал.
    // ffmpeg читает этот канал как второй вход, поэтому звук и видео сводятся в один файл прямо во время записи.
    // Звук никуда, кроме этого канала, не уходит и отдельно не сохраняется.
    class LoopbackCapture : IDisposable
    {
        public string PipeName;      // имя канала без префикса \\.\pipe\
        public int Rate, Channels;
        public bool Float;           // формат отсчётов: 32-битные дробные или 16-битные целые
        public string FfmpegFormat { get { return Float ? "f32le" : "s16le"; } }

        IAudioClient client;
        IAudioCaptureClient capture;
        System.IO.Pipes.NamedPipeServerStream pipe;
        int blockAlign;
        volatile bool stop;
        Thread thread;

        [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);

        // Возвращает null, если звук захватить нельзя (нет устройства вывода, неожиданный формат) — тогда запись пойдёт без звука.
        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        public static LoopbackCapture TryCreate() { return TryCreate(false); }

        // microphone = true — снимаем не то, что играет, а микрофон по умолчанию.
        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        public static LoopbackCapture TryCreate(bool microphone)
        {
            var c = new LoopbackCapture();
            IntPtr fmt = IntPtr.Zero;
            try
            {
                var en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorCom());
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(microphone ? 1 : 0, 1, out dev) != 0 || dev == null) return null;
                Guid iid = typeof(IAudioClient).GUID;
                object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0 || o == null) return null;
                c.client = (IAudioClient)o;
                if (c.client.GetMixFormat(out fmt) != 0 || fmt == IntPtr.Zero) return null;

                int tag = (ushort)Marshal.ReadInt16(fmt, 0);
                c.Channels = Marshal.ReadInt16(fmt, 2);
                c.Rate = Marshal.ReadInt32(fmt, 4);
                c.blockAlign = Marshal.ReadInt16(fmt, 12);
                int bits = Marshal.ReadInt16(fmt, 14);
                if (tag == 0xFFFE) tag = Marshal.ReadInt32(fmt, 24) & 0xFFFF; // расширенный формат: настоящий тип в первом поле подформата
                if (tag == 3 && bits == 32) c.Float = true;
                else if (tag == 1 && bits == 16) c.Float = false;
                else return null;
                if (c.Channels < 1 || c.Channels > 8 || c.Rate < 8000) return null;

                // 0x00020000 — режим «петли»: читаем то, что устройство воспроизводит
                if (c.client.Initialize(0, microphone ? 0u : 0x00020000u, 10000000, 0, fmt, IntPtr.Zero) != 0) return null;
                Guid capIid = typeof(IAudioCaptureClient).GUID;
                object co;
                if (c.client.GetService(ref capIid, out co) != 0 || co == null) return null;
                c.capture = (IAudioCaptureClient)co;

                c.PipeName = "kapsula_" + (microphone ? "mic_" : "audio_") + System.Diagnostics.Process.GetCurrentProcess().Id + "_" + Environment.TickCount;
                c.pipe = new System.IO.Pipes.NamedPipeServerStream(c.PipeName, System.IO.Pipes.PipeDirection.Out, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.None, 0, 1 << 20);
                c.thread = new Thread(c.Run);
                c.thread.IsBackground = true;
                c.thread.Start();
                return c;
            }
            catch (Exception ex) { Store.LogError("захват звука: подготовка", ex); c.Dispose(); return null; }
            finally { if (fmt != IntPtr.Zero) CoTaskMemFree(fmt); }
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        void Run()
        {
            try
            {
                pipe.WaitForConnection();       // ждём, пока ffmpeg откроет канал
                if (stop) return;
                if (client.Start() != 0) return;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                long written = 0;
                var buf = new byte[blockAlign * Rate];
                while (!stop)
                {
                    uint n;
                    if (capture.GetNextPacketSize(out n) != 0) break;
                    while (n > 0)
                    {
                        IntPtr data; uint frames, flags; ulong p1, p2;
                        if (capture.GetBuffer(out data, out frames, out flags, out p1, out p2) != 0) { n = 0; break; }
                        int bytes = (int)frames * blockAlign;
                        if (buf.Length < bytes) buf = new byte[bytes];
                        if ((flags & 2) != 0 || data == IntPtr.Zero) Array.Clear(buf, 0, bytes); else Marshal.Copy(data, buf, 0, bytes);
                        capture.ReleaseBuffer(frames);
                        pipe.Write(buf, 0, bytes);
                        written += frames;
                        if (capture.GetNextPacketSize(out n) != 0) n = 0;
                    }
                    // Когда ничего не играет, Windows не присылает данных вовсе. Дописываем тишину по часам,
                    // иначе звуковая дорожка вышла бы короче видео и всё бы разъехалось.
                    long lag = (long)(watch.Elapsed.TotalSeconds * Rate) - written;
                    if (lag > Rate / 10)
                    {
                        int frames = (int)Math.Min(lag - Rate / 50, Rate);
                        int bytes = frames * blockAlign;
                        if (buf.Length < bytes) buf = new byte[bytes];
                        Array.Clear(buf, 0, bytes);
                        pipe.Write(buf, 0, bytes);
                        written += frames;
                    }
                    Thread.Sleep(8);
                }
            }
            catch (System.IO.IOException) { }            // ffmpeg закрыл канал — запись окончена
            catch (ObjectDisposedException) { }
            catch (Exception ex) { Store.LogError("захват звука", ex); }
            finally { try { if (client != null) client.Stop(); } catch { } }
        }

        public void Dispose()
        {
            stop = true;
            try { if (pipe != null) pipe.Dispose(); } catch { }
            pipe = null;
        }
    }
}
