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
        public static LoopbackCapture TryCreate()
        {
            var c = new LoopbackCapture();
            IntPtr fmt = IntPtr.Zero;
            try
            {
                var en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorCom());
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return null;
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
                if (c.client.Initialize(0, 0x00020000, 10000000, 0, fmt, IntPtr.Zero) != 0) return null;
                Guid capIid = typeof(IAudioCaptureClient).GUID;
                object co;
                if (c.client.GetService(ref capIid, out co) != 0 || co == null) return null;
                c.capture = (IAudioCaptureClient)co;

                c.PipeName = "kapsula_audio_" + System.Diagnostics.Process.GetCurrentProcess().Id + "_" + Environment.TickCount;
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
