// Kapsula — «остров» для Windows. Часть исходного кода: PhoneCam.cs
// Телефон как веб-камера. Телефон открывает защищённую страницу по QR-коду (камера в браузере работает только по https),
// снимает видео в выбранном качестве и шлёт кадры сюда. Kapsula кладёт их в общую память, а драйвер «Kapsula Camera»
// (отдельная библиотека из папки cam) отдаёт их Discord, Zoom, браузерам и OBS как обычная камера.
// Драйвер ставится один раз с правами администратора: Windows не загружает такие камеры, записанные только для одного пользователя.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Kapsula
{
    static class PhoneCam
    {
        public const string Clsid = "{4B415053-554C-4143-414D-000000000001}";
        const string Category = "{860BB310-5D01-11D0-BD3B-00A0C911CE86}";
        public const int Port = 42074;
        // в том же порядке, что и режимы в драйвере
        public static readonly string[] ModeNames = { "720p · 30", "1080p · 30", "1080p · 60", "720p · 60", "4K · 30" };
        static readonly int[][] Modes = { new[] { 1280, 720, 30 }, new[] { 1920, 1080, 30 }, new[] { 1920, 1080, 60 }, new[] { 1280, 720, 60 }, new[] { 3840, 2160, 30 } };

        static IslandWindow island;
        static TcpListener listener;
        static X509Certificate2 cert;
        static MemoryMappedFile map;
        static MemoryMappedViewAccessor view;
        // Общая память камеры: заголовок и ДВА кадра. Kapsula пишет в один, пока драйвер читает другой, готовый, —
        // так кадр никогда не бывает «наполовину новым» (разрывы и полосы). Тот же приём у softcam.
        // [0] метка KCA2, [12] номер кадра, [16] время, [24] какой кадр свежий (0/1), [32+8*n] ширина и высота кадра n
        const long Slot = 3840L * 2160 * 4;
        const long MapSize = 64 + 2 * Slot;
        public const long MapBytes = 64 + Slot;   // буфер приёмника WebRTC: один кадр

        // Пишем в тот кадр, который сейчас не свежий, и только потом объявляем его свежим
        unsafe delegate void FillSlot(byte* dst);
        unsafe static void PutFrame(int w, int h, FillSlot fill)
        {
            lock (writeLock)
            {
                byte* p = null;
                view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
                try
                {
                    p += view.PointerOffset;
                    int target = (*(int*)p == 0x3241434B ? *(int*)(p + 24) & 1 : 1) ^ 1;
                    fill(p + 64 + target * Slot);
                    *(int*)(p + 32 + target * 8) = w; *(int*)(p + 36 + target * 8) = h;
                    *(long*)(p + 16) = DateTime.UtcNow.Ticks;
                    Thread.MemoryBarrier();
                    *(int*)(p + 24) = target;
                    *(int*)p = 0x3241434B;                  // метка KCA2
                    Thread.MemoryBarrier();
                    *(int*)(p + 12) = *(int*)(p + 12) + 2;  // новый кадр готов
                }
                finally { view.SafeMemoryMappedViewHandle.ReleasePointer(); }
            }
        }
        static volatile int clients;
        static int lastFrameAt;
        public static string LastInfo = "";

        // ---------------------------------------------------------------- качество
        public static int Mode
        {
            get { int m; return int.TryParse(Store.Get("camMode"), out m) && m >= 0 && m < Modes.Length ? m : 1; }
            set
            {
                Store.Set("camMode", value.ToString());
                try { using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Kapsula\Camera")) k.SetValue("Mode", value, RegistryValueKind.DWord); } catch { }
            }
        }

        // ---------------------------------------------------------------- драйвер
        static byte[] DriverBytes()
        {
            using (var s = typeof(PhoneCam).Assembly.GetManifestResourceStream("Kapsula.camera.KapsulaCam.dll"))
            {
                if (s == null) return null;
                var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
            }
        }

        // Версия драйвера: метка его исходника, записанная при сборке
        static string Hash8(byte[] b)
        {
            using (var s = typeof(PhoneCam).Assembly.GetManifestResourceStream("Kapsula.camera.version"))
                if (s != null) using (var r = new StreamReader(s)) { string v = r.ReadToEnd().Trim(); if (v.Length > 0) return v; }
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b), 0, 4).Replace("-", "").ToLowerInvariant();
        }

        static string InstalledPath()
        {
            try
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"Software\Classes\CLSID\" + Clsid + @"\InprocServer32"))
                {
                    string cb = k == null ? null : k.GetValue("CodeBase") as string;
                    if (cb == null) return null;
                    return cb.StartsWith("file:///") ? Uri.UnescapeDataString(cb.Substring(8)).Replace('/', '\\') : cb;
                }
            }
            catch { return null; }
        }

        public static bool Installed { get { string p = InstalledPath(); return p != null && File.Exists(p); } }

        public static bool UpToDate
        {
            get
            {
                string p = InstalledPath();
                byte[] b = DriverBytes();
                return p != null && b != null && Path.GetFileName(Path.GetDirectoryName(p)) == Hash8(b) && File.Exists(p);
            }
        }

        // Запуск себя же с правами администратора: Windows покажет окно «Разрешить этому приложению вносить изменения?»
        public static bool Setup(bool install)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, "--camsetup " + (install ? "install" : "remove")) { UseShellExecute = true, Verb = "runas" };
                using (var p = System.Diagnostics.Process.Start(psi)) { p.WaitForExit(60000); return p.HasExited && p.ExitCode == 0; }
            }
            catch (System.ComponentModel.Win32Exception) { return false; }   // отказались в окне UAC
            catch (Exception ex) { Store.LogError("Камера: установка", ex); return false; }
        }

        // Выполняется в процессе с правами администратора (ключ --camsetup)
        public static int RunSetup(bool install)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Kapsula Camera");
                foreach (RegistryView v in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, v))
                    {
                        try { root.DeleteSubKeyTree(@"Software\Classes\CLSID\" + Clsid, false); } catch { }
                        try { root.DeleteSubKeyTree(@"Software\Classes\CLSID\" + Category + @"\Instance\" + Clsid, false); } catch { }
                    }
                if (!install)
                {
                    try { Directory.Delete(dir, true); } catch { }
                    return 0;
                }
                byte[] b = DriverBytes();
                if (b == null) return 2;
                // каждая версия — в своей папке: старую может держать открытой Discord или Zoom
                string verDir = Path.Combine(dir, Hash8(b));
                Directory.CreateDirectory(verDir);
                string path = Path.Combine(verDir, "KapsulaCam.dll");
                if (!File.Exists(path)) File.WriteAllBytes(path, b);
                foreach (string f in Directory.GetFiles(dir)) try { File.Delete(f); } catch { }
                foreach (string d in Directory.GetDirectories(dir)) if (!string.Equals(d, verDir, StringComparison.OrdinalIgnoreCase)) try { Directory.Delete(d, true); } catch { }
                // путь без %20: загрузчик .NET читает CodeBase как обычный путь
                string codeBase = "file:///" + path.Replace('\\', '/');
                foreach (RegistryView v in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, v))
                    {
                        using (var k = root.CreateSubKey(@"Software\Classes\CLSID\" + Clsid))
                        {
                            k.SetValue("", "Kapsula Camera");
                            foreach (string sub in new[] { "InprocServer32", @"InprocServer32\1.0.0.0" })
                                using (var ip = k.CreateSubKey(sub))
                                {
                                    if (sub == "InprocServer32") { ip.SetValue("", "mscoree.dll"); ip.SetValue("ThreadingModel", "Both"); }
                                    ip.SetValue("Class", "KapsulaCam.CamFilter");
                                    ip.SetValue("Assembly", "KapsulaCam, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
                                    ip.SetValue("RuntimeVersion", "v4.0.30319");
                                    ip.SetValue("CodeBase", codeBase);
                                }
                        }
                        using (var i = root.CreateSubKey(@"Software\Classes\CLSID\" + Category + @"\Instance\" + Clsid))
                        {
                            i.SetValue("CLSID", Clsid);
                            i.SetValue("FriendlyName", "Kapsula Camera");
                        }
                    }
                return 0;
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "kapsula-camsetup.log"), DateTime.Now + " " + ex + "\r\n"); } catch { }
                return 1;
            }
        }

        // ---------------------------------------------------------------- сервер
        public static string Url()
        {
            string ip = Phone.LanIp();
            return ip == null ? null : "https://" + ip + ":" + Port + "/?k=" + Phone.Key;
        }

        public static void Start(IslandWindow w)
        {
            island = w;
            if (listener != null) return;
            try
            {
                Mode = Mode;   // драйвер читает выбранное качество из реестра
                cert = Certificate();
                OpenMap();
                listener = new TcpListener(IPAddress.Any, Port);
                listener.Start();
                new Thread(Loop) { IsBackground = true, Name = "PhoneCam" }.Start();
                WritePlaceholder();
            }
            catch (Exception ex) { listener = null; Store.LogError("Камера телефона: сервер", ex); }
        }

        public static void Stop()
        {
            try { if (listener != null) listener.Stop(); } catch { }
            listener = null;
        }

        public static bool Streaming { get { return clients > 0 && unchecked(Environment.TickCount - lastFrameAt) < 3000; } }

        // Свой сертификат для https: телефон один раз предупредит, что он не от известного центра
        static X509Certificate2 Certificate()
        {
            string ip = Phone.LanIp() ?? "127.0.0.1";
            string file = Path.Combine(Store.Dir, "camera.pfx");
            string pass = Store.Get("camPfxPass");
            if (File.Exists(file) && pass.Length > 0 && Store.Get("camPfxIp") == ip)
                try { return new X509Certificate2(File.ReadAllBytes(file), pass, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable); } catch { }
            var rnd = new byte[18];
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(rnd);
            pass = Convert.ToBase64String(rnd);
            using (var rsa = new RSACng(2048))
            {
                var req = new CertificateRequest("CN=Kapsula", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var san = new SubjectAlternativeNameBuilder();
                san.AddIpAddress(IPAddress.Parse(ip));
                san.AddDnsName("kapsula.local");
                req.CertificateExtensions.Add(san.Build());
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
                using (var c = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(800)))
                {
                    byte[] pfx = c.Export(X509ContentType.Pfx, pass);
                    File.WriteAllBytes(file, pfx);
                    Store.Set("camPfxPass", pass);
                    Store.Set("camPfxIp", ip);
                    return new X509Certificate2(pfx, pass, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
                }
            }
        }

        static void OpenMap()
        {
            if (map != null) return;
            map = MemoryMappedFile.CreateOrOpen(@"Local\KapsulaCam2", MapSize, MemoryMappedFileAccess.ReadWrite);
            view = map.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);
        }

        static void Loop()
        {
            var l = listener;
            while (l != null && listener == l)
            {
                TcpClient c;
                try { c = l.AcceptTcpClient(); } catch { return; }
                var ep = c.Client.RemoteEndPoint as IPEndPoint;
                byte[] b = ep == null ? new byte[4] : ep.Address.MapToIPv4().GetAddressBytes();
                bool lan = b[0] == 127 || b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] >= 16 && b[1] <= 31;
                if (!lan) { c.Close(); continue; }
                new Thread(delegate() { Serve(c); }) { IsBackground = true }.Start();
            }
        }

        static void Serve(TcpClient c)
        {
            try
            {
                c.ReceiveTimeout = 15000; c.SendTimeout = 15000; c.NoDelay = true;
                using (var ssl = new SslStream(c.GetStream(), false))
                {
                    ssl.AuthenticateAsServer(cert, false, System.Security.Authentication.SslProtocols.Tls12, false);
                    string head = ReadHead(ssl);
                    if (head == null) return;
                    string first = head.Substring(0, Math.Max(0, head.IndexOf('\r')));
                    string[] parts = first.Split(' ');
                    if (parts.Length < 2) return;
                    string target = parts[1], path = target, key = "";
                    int q = target.IndexOf('?');
                    if (q >= 0)
                    {
                        path = target.Substring(0, q);
                        foreach (string kv in target.Substring(q + 1).Split('&')) if (kv.StartsWith("k=")) key = Uri.UnescapeDataString(kv.Substring(2));
                    }
                    if (key != Phone.Key) { Send(ssl, "403 Forbidden", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Нет доступа: откройте страницу по QR-коду из Kapsula")); return; }
                    if (path == "/") { Send(ssl, "200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page())); return; }
                    if (path == "/offer")
                    {
                        // телефон прислал предложение WebRTC — отвечаем от приёмника в Kapsula
                        int len; int.TryParse(Header(head, "Content-Length") ?? "0", out len);
                        if (len <= 0 || len > 200000) { Send(ssl, "400 Bad Request", "text/plain", new byte[0]); return; }
                        var body = new byte[len];
                        ReadExact(ssl, body, len);
                        PhoneRtc.Start();
                        string ans = PhoneRtc.Answer(Encoding.UTF8.GetString(body));
                        if (ans == null) { Send(ssl, "503 Service Unavailable", "text/plain", new byte[0]); return; }
                        Send(ssl, "200 OK", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(ans));
                        return;
                    }
                    if (path == "/ws")
                    {
                        string wsKey = Header(head, "Sec-WebSocket-Key");
                        if (wsKey == null) return;
                        string accept;
                        using (var sha = SHA1.Create()) accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(wsKey.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                        byte[] h = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
                        ssl.Write(h, 0, h.Length);
                        c.ReceiveTimeout = 10000;
                        Socket(ssl);
                        return;
                    }
                    Send(ssl, "404 Not Found", "text/plain", new byte[0]);
                }
            }
            catch { }
            finally { try { c.Close(); } catch { } }
        }

        static string ReadHead(Stream s)
        {
            var sb = new StringBuilder();
            var one = new byte[1];
            while (sb.Length < 16384)
            {
                if (s.Read(one, 0, 1) <= 0) return null;
                sb.Append((char)one[0]);
                if (sb.Length >= 4 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\r' && sb[sb.Length - 3] == '\n' && sb[sb.Length - 4] == '\r') return sb.ToString();
            }
            return null;
        }

        static string Header(string head, string name)
        {
            foreach (string line in head.Split('\n'))
            {
                int i = line.IndexOf(':');
                if (i > 0 && line.Substring(0, i).Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return line.Substring(i + 1).Trim();
            }
            return null;
        }

        static void Send(Stream s, string status, string type, byte[] body)
        {
            byte[] h = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            s.Write(h, 0, h.Length); s.Write(body, 0, body.Length);
        }

        static void ReadExact(Stream s, byte[] b, int n)
        {
            int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) throw new EndOfStreamException(); got += r; }
        }

        // ---------------------------------------------------------------- WebSocket: двоичные сообщения — кадры JPEG
        static void Socket(Stream s)
        {
            Interlocked.Increment(ref clients);
            Log("телефон подключился");
            Notify(true);
            var hdr = new byte[14];
            var msg = new MemoryStream();
            int msgOp = 0;
            try
            {
                while (true)
                {
                    ReadExact(s, hdr, 2);
                    bool fin = (hdr[0] & 0x80) != 0;
                    int op = hdr[0] & 0x0F;
                    bool masked = (hdr[1] & 0x80) != 0;
                    long len = hdr[1] & 0x7F;
                    if (len == 126) { ReadExact(s, hdr, 2); len = hdr[0] << 8 | hdr[1]; }
                    else if (len == 127) { ReadExact(s, hdr, 8); len = 0; for (int i = 0; i < 8; i++) len = len << 8 | hdr[i]; }
                    if (len > 24 * 1024 * 1024) return;
                    var mask = new byte[4];
                    if (masked) ReadExact(s, mask, 4);
                    var data = new byte[len];
                    ReadExact(s, data, (int)len);
                    if (masked) for (int i = 0; i < data.Length; i++) data[i] ^= mask[i & 3];
                    if (op == 8) { WsSend(s, 8, new byte[0]); return; }           // закрытие: отвечаем тем же
                    if (op == 9) { WsSend(s, 0xA, data); continue; }            // ping → pong
                    if (op == 0xA) continue;
                    if (op != 0) { msg.SetLength(0); msgOp = op; }
                    msg.Write(data, 0, data.Length);
                    if (!fin) continue;
                    if (msgOp == 2) Frame(msg.ToArray());
                    else if (msgOp == 1) LastInfo = Encoding.UTF8.GetString(msg.ToArray());
                    msg.SetLength(0);
                }
            }
            catch { }
            finally
            {
                Interlocked.Decrement(ref clients);
                Log("телефон отключился");
                Notify(false);
            }
        }

        static void WsSend(Stream s, int op, byte[] data)
        {
            if (data.Length > 125) return;
            var f = new byte[2 + data.Length];
            f[0] = (byte)(0x80 | op); f[1] = (byte)data.Length;
            data.CopyTo(f, 2);
            lock (s) s.Write(f, 0, f.Length);
        }

        static void Notify(bool on)
        {
            var w = island;
            if (w == null) return;
            w.Dispatcher.BeginInvoke(new Action(delegate
            {
                w.PhoneCamChanged(on);
                if (on || clients > 0) return;
                // заставку — только если телефон не вернулся за 3 секунды (смена объектива или качества переподключает поток)
                var wait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                wait.Tick += delegate { wait.Stop(); if (clients == 0) WritePlaceholder(); };
                wait.Start();
            }));
        }

        // Кадры раскодируются по одному: пока один разбирается, новые заменяют друг друга — копится только последний
        static byte[] pending;
        static int decoding;

        static void Frame(byte[] jpeg)
        {
            Interlocked.Exchange(ref pending, jpeg);
            if (Interlocked.CompareExchange(ref decoding, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    byte[] j;
                    while ((j = Interlocked.Exchange(ref pending, null)) != null)
                    {
                        try
                        {
                            var dec = new JpegBitmapDecoder(new MemoryStream(j), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                            BitmapSource f = dec.Frames[0];
                            if (f.Format != PixelFormats.Bgra32 && f.Format != PixelFormats.Bgr32) f = new FormatConvertedBitmap(f, PixelFormats.Bgra32, null, 0);
                            Diag(f);
                            WriteFrame(f);
                            lastFrameAt = Environment.TickCount;
                        }
                        catch { }
                    }
                }
                finally { decoding = 0; }
                byte[] left = Interlocked.Exchange(ref pending, null);
                if (left != null) Frame(left);
            });
        }

        // ---- журнал камеры (%APPDATA%\Kapsula\camera.log): сколько кадров, были ли тёмные, смены размера, переподключения
        static int dFrames, dDark, dJumps, dLast = -1, dAt = Environment.TickCount;
        static string dSize = "";
        public static void Log(string s) { try { File.AppendAllText(Path.Combine(Store.Dir, "camera.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n"); } catch { } }

        static void Diag(BitmapSource f)
        {
            try
            {
                int w = f.PixelWidth, h = f.PixelHeight;
                string size = w + "x" + h;
                if (size != dSize) { Log("размер кадра " + size); dSize = size; }
                // средняя яркость по сетке 8×8 точек
                long sum = 0; var px = new byte[4];
                for (int gy = 0; gy < 8; gy++) for (int gx = 0; gx < 8; gx++)
                {
                    f.CopyPixels(new Int32Rect(w * (2 * gx + 1) / 16, h * (2 * gy + 1) / 16, 1, 1), px, 4, 0);
                    sum += (px[0] + px[1] * 2 + px[2]) / 4;
                }
                int lum = (int)(sum / 64);
                dFrames++;
                if (lum < 6) dDark++;
                if (dLast >= 0 && Math.Abs(lum - dLast) > 60) { dJumps++; Log("резкая смена яркости " + dLast + " → " + lum); }
                dLast = lum;
                if (unchecked(Environment.TickCount - dAt) >= 5000)
                {
                    Log("за 5 с: кадров " + dFrames + " (" + (dFrames / 5) + " к/с), тёмных " + dDark + ", скачков яркости " + dJumps + ", яркость " + lum + (LastInfo.Length > 0 ? ", телефон: " + LastInfo : ""));
                    dFrames = dDark = dJumps = 0; dAt = Environment.TickCount;
                }
            }
            catch { }
        }

        static readonly object writeLock = new object();

        // Кадр от приёмника WebRTC: BGRA (или RGBA) сверху вниз, строка = w*4 байт
        public unsafe static void WriteRaw(IntPtr src, int w, int h, bool rgba)
        {
            if (view == null || (long)w * h * 4 > Slot) return;
            DiagRaw((byte*)src, w, h, rgba);
            long n = (long)w * h * 4;
            PutFrame(w, h, delegate(byte* d)
            {
                if (!rgba) Buffer.MemoryCopy((void*)src, d, Slot, n);
                else
                {
                    uint* s = (uint*)src, o = (uint*)d;
                    for (long i = 0, c = (long)w * h; i < c; i++) { uint v = s[i]; o[i] = (v & 0xFF00FF00) | ((v & 0xFF) << 16) | ((v >> 16) & 0xFF); }
                }
            });
            lastFrameAt = Environment.TickCount;
        }

        // Телефон подключился или отключился по WebRTC — как и для старого способа
        public static void RtcChanged(bool on)
        {
            if (on) { Interlocked.Increment(ref clients); Log("телефон подключился (WebRTC, H.264)"); }
            else { if (Interlocked.Decrement(ref clients) < 0) Interlocked.Exchange(ref clients, 0); Log("телефон отключился (WebRTC)"); }
            Notify(on);
        }

        unsafe static void DiagRaw(byte* p, int w, int h, bool rgba)
        {
            string size = w + "x" + h;
            if (size != dSize) { Log("размер кадра " + size); dSize = size; }
            long sum = 0;
            for (int gy = 0; gy < 8; gy++) for (int gx = 0; gx < 8; gx++)
            {
                byte* q = p + ((long)(h * (2 * gy + 1) / 16) * w + w * (2 * gx + 1) / 16) * 4;
                sum += (q[0] + q[1] * 2 + q[2]) / 4;
            }
            int lum = (int)(sum / 64);
            dFrames++;
            if (lum < 6) dDark++;
            if (dLast >= 0 && Math.Abs(lum - dLast) > 60) { dJumps++; Log("резкая смена яркости " + dLast + " → " + lum); }
            dLast = lum;
            if (unchecked(Environment.TickCount - dAt) >= 5000)
            {
                Log("за 5 с: кадров " + dFrames + " (" + (dFrames / 5) + " к/с), тёмных " + dDark + ", скачков яркости " + dJumps + ", яркость " + lum + (LastInfo.Length > 0 ? ", телефон: " + LastInfo : ""));
                dFrames = dDark = dJumps = 0; dAt = Environment.TickCount;
            }
        }

        unsafe static void WriteFrame(BitmapSource f)
        {
            if (view == null) return;
            int w = f.PixelWidth, h = f.PixelHeight;
            if ((long)w * h * 4 > Slot) return;
            PutFrame(w, h, delegate(byte* d) { f.CopyPixels(Int32Rect.Empty, new IntPtr(d), (int)Slot, w * 4); });
        }

        // Пока телефон не подключён, камера показывает подсказку
        static void WritePlaceholder()
        {
            var w = island;
            if (w == null || view == null) return;
            w.Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    var g = new Grid { Width = 1280, Height = 720, Background = new LinearGradientBrush(Color.FromRgb(0x15, 0x15, 0x1A), Color.FromRgb(0x0B, 0x0B, 0x0E), 90) };
                    var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    sp.Children.Add(new TextBlock { Text = ((char)0xE722).ToString(), FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 64, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)), HorizontalAlignment = HorizontalAlignment.Center });
                    sp.Children.Add(new TextBlock { Text = "Kapsula Camera", Foreground = Brushes.White, FontSize = 40, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) });
                    sp.Children.Add(new TextBlock { Text = "Телефон не подключён · кнопка камеры слева в раскрытом острове Kapsula", Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA3)), FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
                    g.Children.Add(sp);
                    g.Measure(new Size(1280, 720)); g.Arrange(new Rect(0, 0, 1280, 720)); g.UpdateLayout();
                    var rtb = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(g);
                    rtb.Freeze();
                    ThreadPool.QueueUserWorkItem(delegate { if (!Streaming) { Log("показана заставка"); WriteFrame(rtb); } });
                }
                catch (Exception ex) { Store.LogError("Камера телефона: заставка", ex); }
            }));
        }

        // для проверки драйвера без телефона: бегущая цветная полоса (ключ --camtest)
        public static void TestPattern(IslandWindow w)
        {
            island = w;
            OpenMap();
            new Thread(delegate()
            {
                int n = 0;
                var px = new byte[1280 * 720 * 4];
                while (true)
                {
                    for (int y = 0; y < 720; y++)
                        for (int x = 0; x < 1280; x++)
                        {
                            int i = (y * 1280 + x) * 4;
                            bool bar = ((x + n * 8) / 80) % 2 == 0;
                            px[i] = (byte)(bar ? 255 : x / 5); px[i + 1] = (byte)(y / 3); px[i + 2] = (byte)(bar ? 40 : 200); px[i + 3] = 255;
                        }
                    var bmp = BitmapSource.Create(1280, 720, 96, 96, PixelFormats.Bgra32, null, px, 1280 * 4);
                    bmp.Freeze();
                    WriteFrame(bmp);
                    lastFrameAt = Environment.TickCount;
                    n++;
                    Thread.Sleep(33);
                }
            }) { IsBackground = true }.Start();
        }

        // ---------------------------------------------------------------- страница на телефоне
        static string Page()
        {
            return @"<!doctype html><html lang=ru><head><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1,user-scalable=no'>
<title>Kapsula · камера</title><style>
:root{color-scheme:dark}*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
body{margin:0;background:#0b0b0d;color:#f2f2f5;font:15px system-ui,-apple-system,Segoe UI,sans-serif;padding:14px 16px 30px}
h1{font-size:20px;margin:4px 0 2px}.sub{color:#8a8a93;font-size:13px;margin-bottom:12px}
.seg{display:flex;gap:4px;background:#18181c;border-radius:14px;padding:4px;margin-bottom:10px;overflow-x:auto}
.seg b{flex:1;text-align:center;padding:9px 6px;border-radius:10px;color:#9a9aa3;font-weight:600;font-size:13px;white-space:nowrap}
.seg b.on{background:#2a2a30;color:#fff}
#v{width:100%;border-radius:18px;background:#000;aspect-ratio:16/9;object-fit:contain;display:block;margin:6px 0 12px}
#v.mirror{transform:scaleX(-1)}
button{background:#f2f2f5;color:#0b0b0d;border:0;border-radius:16px;padding:17px;font:700 16px system-ui;width:100%}
button.stop{background:#3a1d1d;color:#ff8a80}
.st{color:#8a8a93;font-size:13px;margin-top:10px;text-align:center;min-height:18px}
.tip{color:#6a6a72;font-size:12.5px;margin-top:14px;line-height:1.45}
</style></head><body>
<h1>Камера для компьютера</h1><div class=sub>В Discord, Zoom и браузере выберите камеру «Kapsula Camera»</div>
<div class=seg id=q></div>
<div class=seg id=f><b class=on>Основная</b><b>Фронтальная</b></div>
<video id=v playsinline muted autoplay></video>
<button id=go>Включить камеру</button>
<div class=st id=st></div>
<div class=tip>Держите телефон горизонтально — так кадр заполнит весь экран собеседника. Экран телефона не гаснет, пока идёт трансляция; заблокируете телефон — камера остановится.</div>
<script>
var K=new URLSearchParams(location.search).get('k');
var M=[[1280,720,30,'720p · 30'],[1920,1080,30,'1080p · 30'],[1920,1080,60,'1080p · 60'],[1280,720,60,'720p · 60'],[3840,2160,30,'4K · 30']];
var mode=" + Mode + @",stream=null,ws=null,run=false,lock=null,cv=document.createElement('canvas'),cx=cv.getContext('2d',{alpha:false}),busy=0;
var cams=[],cam=null,seq=0,sentSeq=-1;
try{cam=localStorage.getItem('kcam')}catch(e){}
var v=document.getElementById('v'),st=document.getElementById('st'),go=document.getElementById('go');
function seg(id,list,cur,pick){var el=document.getElementById(id);el.innerHTML='';list.forEach(function(t,i){var b=document.createElement('b');b.textContent=t;if(i===cur)b.className='on';
b.onclick=function(){pick(i);[].forEach.call(el.children,function(x){x.className=''});b.className='on'};el.appendChild(b)})}
seg('q',M.map(function(m){return m[3]}),mode,function(i){mode=i;if(run)restart()});
// Объективы. Виртуальные «двойная/тройная» камеры сами прыгают между объективами (макро, сверхширокий) — их не берём по умолчанию
function kind(l){l=(l||'').toLowerCase();
 if(/front|фронт|передн|user/.test(l))return 0;
 if(/dual|triple|двум|двойн|тремя|тройн/.test(l))return 4;
 if(/ultra|сверхшир|ультра/.test(l))return 2;
 if(/tele|теле/.test(l))return 3;
 return 1}
function nice(c,i){var k=kind(c.label);return k===0?'Фронтальная':k===1?'Основная':k===2?'Широкий угол':k===3?'Зум':'Авто (сам переключается)'}
function pickDefault(){for(var i=0;i<cams.length;i++)if(kind(cams[i].label)===1)return cams[i].deviceId;return cams.length?cams[0].deviceId:null}
async function listCams(){
 var l=(await navigator.mediaDevices.enumerateDevices()).filter(function(d){return d.kind==='videoinput'&&d.label&&kind(d.label)!==4});   // без «авто»: они сами прыгают между объективами
 // одинаковые названия (Android: «camera2 0», «camera2 2») — нумеруем
 cams=l;var names=l.map(nice),seen={};names=names.map(function(n){seen[n]=(seen[n]||0)+1;return seen[n]>1?n+' '+seen[n]:n});
 if(!cam||!l.some(function(c){return c.deviceId===cam}))cam=pickDefault();
 var cur=-1;l.forEach(function(c,i){if(c.deviceId===cam)cur=i});
 seg('f',names,cur,function(i){cam=cams[i].deviceId;try{localStorage.setItem('kcam',cam)}catch(e){}if(run)restart()});
}
var pc=null,sender=null,how='';
function stopAll(){run=false;if(stream)stream.getTracks().forEach(function(t){t.stop()});stream=null;if(ws){try{ws.close()}catch(e){}}ws=null;
 if(pc){try{pc.close()}catch(e){}}pc=null;sender=null;if(lock){lock.release();lock=null}
 go.textContent='Включить камеру';go.className='';st.textContent=''}
// смена объектива или качества: в WebRTC просто подменяем дорожку, соединение не рвётся
function restart(){var old=stream;openCam().then(function(ok){if(!ok){stopAll();return}
 if(old&&old!==stream)old.getTracks().forEach(function(t){t.stop()});
 if(sender){sender.replaceTrack(stream.getVideoTracks()[0]);tune()}})}
async function openCam(){
 var m=M[mode];
 var vc={width:{ideal:m[0]},height:{ideal:m[1]},frameRate:{ideal:m[2]}};
 if(cam)vc.deviceId={exact:cam};else vc.facingMode={ideal:'environment'};
 var s;
 try{s=await navigator.mediaDevices.getUserMedia({audio:false,video:vc})}
 catch(e){if(cam){cam=null;return openCam()}st.textContent='Нет доступа к камере: '+e.name;return false}
 var tr=s.getVideoTracks()[0];
 if(!cams.length){await listCams();var id=tr.getSettings().deviceId;if(cam&&id&&id!==cam){tr.stop();return openCam()}}
 try{tr.contentHint='motion'}catch(e){}
 stream=s;
 var fm=(tr.getSettings().facingMode||'');v.srcObject=stream;v.className=(fm==='user'||kind(tr.label)===0)?'mirror':'';try{await v.play()}catch(e){}
 return true}
// битрейт под качество: как в рабочих проектах — с запасом, чтобы картинка не рассыпалась
function tune(){if(!sender)return;var m=M[mode];try{var p=sender.getParameters();if(!p.encodings||!p.encodings.length)p.encodings=[{}];
 p.encodings[0].maxBitrate=m[0]>=3840?25000000:m[0]>=1920?(m[2]>=60?12000000:8000000):(m[2]>=60?6000000:4000000);
 p.encodings[0].maxFramerate=m[2];p.encodings[0].scaleResolutionDownBy=1;p.degradationPreference='maintain-resolution';sender.setParameters(p).catch(function(){})}catch(e){}}
async function startRtc(){
 if(!window.RTCPeerConnection)return false;
 pc=new RTCPeerConnection({iceServers:[]});var my=pc;
 sender=pc.addTrack(stream.getVideoTracks()[0],stream);
 // H.264 первым: его телефоны сжимают аппаратно
 try{var tc=pc.getTransceivers()[0],cs=RTCRtpSender.getCapabilities('video').codecs;
  var h=cs.filter(function(c){return /h264/i.test(c.mimeType)}),o=cs.filter(function(c){return !/h264/i.test(c.mimeType)});if(h.length)tc.setCodecPreferences(h.concat(o))}catch(e){}
 var off=await pc.createOffer();
 // сразу высокая скорость, без разгона с маленького разрешения (Chrome и Android; Safari пропустит)
 var pts=[];off.sdp.replace(/a=rtpmap:(\d+) H264\/90000/gi,function(a,p){pts.push(p);return a});
 off.sdp=off.sdp.replace(/a=fmtp:(\d+) ([^\r\n]*)/g,function(a,p,rest){return pts.indexOf(p)>=0&&rest.indexOf('x-google')<0?a+';x-google-start-bitrate='+(M[mode][0]>=1920?6000:3000)+';x-google-min-bitrate='+(M[mode][0]>=1920?2500:1200):a});
 await pc.setLocalDescription(off);
 await new Promise(function(r){if(my.iceGatheringState==='complete')return r();var t=setTimeout(r,2000);my.onicegatheringstatechange=function(){if(my.iceGatheringState==='complete'){clearTimeout(t);r()}}});
 var res=await fetch('/offer?k='+K,{method:'POST',body:my.localDescription.sdp});if(!res.ok)return false;
 await my.setRemoteDescription({type:'answer',sdp:await res.text()});
 var ok=await new Promise(function(r){if(my.connectionState==='connected')return r(true);var t=setTimeout(function(){r(false)},8000);
  my.onconnectionstatechange=function(){if(my.connectionState==='connected'){clearTimeout(t);r(true)}else if(my.connectionState==='failed'){clearTimeout(t);r(false)}}});
 if(!ok){try{my.close()}catch(e){}pc=null;sender=null;return false}
 tune();
 my.onconnectionstatechange=function(){if(run&&my===pc&&(my.connectionState==='failed'||my.connectionState==='closed')){st.textContent='Связь с компьютером потеряна';stopAll()}};
 how='WebRTC';stats();return true}
async function stats(){if(!run||!pc)return;try{var r=await pc.getStats(),out=null;r.forEach(function(s){if(s.type==='outbound-rtp'&&s.kind==='video')out=s});
 if(out){var codec='';r.forEach(function(s){if(s.id===out.codecId)codec=(s.mimeType||'').replace('video/','')});
  st.textContent='Идёт трансляция · '+(out.frameWidth||'?')+'×'+(out.frameHeight||'?')+' · '+Math.round(out.framesPerSecond||0)+' к/с · '+codec+(out.encoderImplementation?' ('+out.encoderImplementation+')':'')+(out.qualityLimitationReason&&out.qualityLimitationReason!=='none'?' · упор в '+(out.qualityLimitationReason==='bandwidth'?'Wi-Fi':out.qualityLimitationReason==='cpu'?'процессор':out.qualityLimitationReason):'')}}catch(e){}
 setTimeout(stats,1000)}
async function start(){
 if(!navigator.mediaDevices){st.textContent='Браузер не даёт камеру: откройте ссылку в Chrome или Safari';return}
 if(!(await openCam()))return;
 run=true;go.textContent='Остановить';go.className='stop';
 try{lock=await navigator.wakeLock.request('screen')}catch(e){}
 st.textContent='Подключаюсь…';
 var ok=false;try{ok=await startRtc()}catch(e){ok=false}
 if(!run)return;
 if(ok)return;
 // запасной способ: кадры JPEG по защищённому соединению
 how='JPEG';st.textContent='WebRTC не соединился — запасной режим';
 ws=new WebSocket('wss://'+location.host+'/ws?k='+K);ws.binaryType='arraybuffer';
 ws.onopen=function(){var s=stream.getVideoTracks()[0].getSettings();ws.send('cam '+s.width+'x'+s.height+' '+Math.round(s.frameRate||30));tick()};
 ws.onclose=function(){if(run){st.textContent='Связь с компьютером потеряна';stopAll()}};
}
var last=performance.now(),cnt=0;
function tick(){if(!run||!ws)return;
 if(v.requestVideoFrameCallback)v.requestVideoFrameCallback(send);else setTimeout(send,1000/M[mode][2])}
function send(){if(!run||!ws||ws.readyState!==1)return;
 if(busy>=2||ws.bufferedAmount>2500000){tick();return}   // до двух кадров в работе: пока один летит, следующий уже сжимается
 var m=M[mode],w=v.videoWidth,h=v.videoHeight;if(!w||v.readyState<2||v.paused){tick();return}   // видео ещё не готово — пустой кадр не шлём
 var k=Math.min(1,Math.max(m[0],m[1])/Math.max(w,h));w=Math.round(w*k);h=Math.round(h*k);
 if(cv.width!==w||cv.height!==h){cv.width=w;cv.height=h}
 cx.drawImage(v,0,0,w,h);busy++;var my=seq++;
 cv.toBlob(function(b){busy--;
  // кадр сжался позже следующего — не отправляем, иначе картинка дёрнется назад
  if(b&&my>sentSeq&&ws&&ws.readyState===1){ws.send(b);sentSeq=my;cnt++}
  var now=performance.now();if(now-last>1000){st.textContent='Запасной режим · '+w+'×'+h+' · '+Math.round(cnt*1000/(now-last))+' к/с';cnt=0;last=now}
  },'image/jpeg',m[0]>=3840?0.72:m[0]>=1920?0.8:0.85);tick()}go.onclick=function(){if(run)stopAll();else start()};
document.addEventListener('visibilitychange',function(){if(document.visibilityState==='visible'&&run&&!lock){navigator.wakeLock&&navigator.wakeLock.request('screen').then(function(l){lock=l}).catch(function(){})}});
</script></body></html>";
        }

        // ---------------------------------------------------------------- проверка: видит ли камеру Chromium (как Discord)
        // ключ --camprobe файл: невидимое окно с WebView2 сам разрешает себе камеру и пишет в файл, что увидел
        public static void Probe(string outFile)
        {
            var win = new Window { Width = 400, Height = 300, Left = -4000, Top = -4000, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false };
            var view = new Microsoft.Web.WebView2.Wpf.WebView2();
            win.Content = view;
            win.Show();
            Action<string> done = delegate(string s) { try { File.WriteAllText(outFile, s); } catch { } Environment.Exit(0); };
            var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(40) };
            timeout.Tick += delegate { done("TIMEOUT"); };
            timeout.Start();
            Microsoft.Web.WebView2.Core.CoreWebView2Environment.SetLoaderDllFolderPath(WebFiles.Dir);
            string data = Path.Combine(Path.GetTempPath(), "kapsula-camprobe");
            var ui = System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext();
            Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, data, null).ContinueWith(delegate(System.Threading.Tasks.Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment> te)
            {
                if (te.IsFaulted) { done("ENV " + te.Exception.InnerException.Message); return; }
                view.EnsureCoreWebView2Async(te.Result).ContinueWith(delegate(System.Threading.Tasks.Task t2)
                {
                    if (t2.IsFaulted) { done("CORE " + t2.Exception.InnerException.Message); return; }
                    var core = view.CoreWebView2;
                    core.PermissionRequested += delegate(object o, Microsoft.Web.WebView2.Core.CoreWebView2PermissionRequestedEventArgs e) { e.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow; };
                    core.WebMessageReceived += delegate(object o, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e) { done(e.TryGetWebMessageAsString()); };
                    core.SetVirtualHostNameToFolderMapping("probe.kapsula", Path.GetTempPath(), Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "kapsula-camprobe.html"), @"<!doctype html><body><video id=v muted autoplay playsinline></video><script>
async function run(){ var out=[];
 try{ var s=await navigator.mediaDevices.getUserMedia({video:true}); out.push('default: '+s.getVideoTracks()[0].label); s.getTracks().forEach(function(t){t.stop()}); }catch(e){ out.push('gum: '+e.name+' '+e.message); }
 var l=(await navigator.mediaDevices.enumerateDevices()).filter(function(d){return d.kind=='videoinput'});
 out.push('cams: '+l.map(function(d){return d.label}).join(' | '));
 var k=l.filter(function(d){return d.label.indexOf('Kapsula')>=0})[0];
 if(k){ try{ var s2=await navigator.mediaDevices.getUserMedia({video:{deviceId:{exact:k.deviceId},width:1280,height:720}}); var v=document.getElementById('v'); v.srcObject=s2; await v.play();
   await new Promise(function(r){setTimeout(r,2500)}); var c=document.createElement('canvas'); c.width=v.videoWidth; c.height=v.videoHeight; var x=c.getContext('2d'); x.drawImage(v,0,0);
   var px=x.getImageData(c.width/2|0, (c.height*0.42)|0, 1, 1).data; var st=s2.getVideoTracks()[0].getSettings();
   out.push('kapsula default: '+c.width+'x'+c.height+' fps='+st.frameRate+' px='+px[0]+','+px[1]+','+px[2]); s2.getTracks().forEach(function(t){t.stop()}); }catch(e){ out.push('kapsula open: '+e.name+' '+e.message); }
   try{ var s3=await navigator.mediaDevices.getUserMedia({video:{deviceId:{exact:k.deviceId},width:1920,height:1080,frameRate:{ideal:60}}}); var v3=document.getElementById('v'); v3.srcObject=s3; await v3.play(); await new Promise(function(r){setTimeout(r,800)});
     var n=0,t0=performance.now(); await new Promise(function(r){ function cb(){ n++; if(performance.now()-t0<3000) v3.requestVideoFrameCallback(cb); else r(); } v3.requestVideoFrameCallback(cb); });
     out.push('kapsula ask60: '+s3.getVideoTracks()[0].getSettings().frameRate+' setting, '+Math.round(n/3)+' frames/s real'); s3.getTracks().forEach(function(t){t.stop()}); }catch(e){ out.push('ask60: '+e.name); }
   try{ var s5=await navigator.mediaDevices.getUserMedia({video:{deviceId:{exact:k.deviceId}}}); var v5=document.getElementById('v'); v5.srcObject=s5; await v5.play(); await new Promise(function(r){setTimeout(r,800)});
     var cc=document.createElement('canvas'); cc.width=64; cc.height=36; var cx5=cc.getContext('2d',{willReadFrequently:true}); var lum=[], t5=performance.now();
     await new Promise(function(r){ function cb(){ cx5.drawImage(v5,0,0,64,36); var d=cx5.getImageData(0,0,64,36).data, s=0; for(var i=0;i<d.length;i+=4) s+=(d[i]+2*d[i+1]+d[i+2])/4; lum.push(Math.round(s/(d.length/4))); if(performance.now()-t5<4000) v5.requestVideoFrameCallback(cb); else r(); } v5.requestVideoFrameCallback(cb); });
     out.push('browser frames '+lum.length+' ('+s5.getVideoTracks()[0].getSettings().width+'x'+s5.getVideoTracks()[0].getSettings().height+'@'+Math.round(s5.getVideoTracks()[0].getSettings().frameRate)+'): '+lum.join(' ')); s5.getTracks().forEach(function(t){t.stop()}); }catch(e){ out.push('lum: '+e.name); }
   try{ var s4=await navigator.mediaDevices.getUserMedia({video:{deviceId:{exact:k.deviceId}}}); out.push('kapsula plain: '+JSON.stringify(s4.getVideoTracks()[0].getSettings().frameRate)+' fps '+s4.getVideoTracks()[0].getSettings().width); out.push('caps: '+JSON.stringify(s4.getVideoTracks()[0].getCapabilities().frameRate)); s4.getTracks().forEach(function(t){t.stop()}); }catch(e){} }
 chrome.webview.postMessage(out.join('\n')); }
run();
</script></body>", new UTF8Encoding(false));
                    core.Navigate("https://probe.kapsula/kapsula-camprobe.html");
                }, ui);
            }, ui);
        }
        // ---------------------------------------------------------------- окно с QR-кодом
        public static void ShowQr()
        {
            string url = Url();
            if (url == null) { MessageBox.Show("Компьютер не подключён к домашней сети — телефону не к чему подключиться.", "Kapsula"); return; }
            if (island == null) return;
            PhoneRtc.Start();   // приёмник видео (~100 МБ памяти) запускаем, только когда камера понадобилась
            island.ShowQrInIsland(url, "Наведите камеру телефона", new string[]
            {
                "Телефон — в той же сети Wi-Fi. Браузер скажет «Подключение не защищено»: это наш собственный сертификат для домашней сети, связь всё равно шифруется. Нажмите «Дополнительно» → «Перейти на сайт».",
                "Выберите качество и нажмите «Включить камеру». Эта панель закроется сама. В Discord или Zoom выберите «Kapsula Camera»."
            });
        }
    }
}
