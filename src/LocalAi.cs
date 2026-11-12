// Kapsula — «остров» для Windows. Часть исходного кода: LocalAi.cs
// Qwen прямо на этом компьютере — без ключей и без интернета во время разговора.
// По кнопке в настройках Kapsula скачивает две вещи: llama.cpp (открытый движок для запуска нейросетей,
// github.com/ggml-org/llama.cpp, сборка с Vulkan — работает на видеокартах NVIDIA, AMD и Intel, а без них на процессоре)
// и саму модель Qwen 3.5 в формате GGUF (huggingface.co/unsloth). Всё лежит в %LOCALAPPDATA%\Kapsula\ai.
// Kapsula запускает llama-server (только на 127.0.0.1 — снаружи к нему не подключиться) на первом вопросе в чате
// и полностью выгружает по кнопке «Очистить»; на всякий случай — и после 15 минут без вопросов. Вместе с Kapsula закрывается и он.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Kapsula
{
    static class QwenLocal
    {
        public class ModelInfo
        {
            public string Id, Name, Repo, File, Proj, Note;
            public double SizeGb;    // модель + картинки (mmproj)
            public double RamGb;     // сколько памяти нужно, чтобы работала нормально
        }

        public static readonly ModelInfo[] Models =
        {
            new ModelInfo { Id = "qwen3.5-0.8b", Name = "0.8B", Repo = "unsloth/Qwen3.5-0.8B-GGUF", File = "Qwen3.5-0.8B-Q4_K_M.gguf", Proj = "mmproj-F16.gguf", SizeGb = 0.7, RamGb = 2, Note = "самая маленькая и быстрая, для слабых ПК — простые вопросы и перевод" },
            new ModelInfo { Id = "qwen3.5-2b", Name = "2B", Repo = "unsloth/Qwen3.5-2B-GGUF", File = "Qwen3.5-2B-Q4_K_M.gguf", Proj = "mmproj-F16.gguf", SizeGb = 1.8, RamGb = 4, Note = "быстрая, для ноутбуков без видеокарты" },
            new ModelInfo { Id = "qwen3.5-4b", Name = "4B", Repo = "unsloth/Qwen3.5-4B-GGUF", File = "Qwen3.5-4B-Q4_K_M.gguf", Proj = "mmproj-F16.gguf", SizeGb = 3.2, RamGb = 8, Note = "золотая середина: заметно умнее, всё ещё быстрая" },
            new ModelInfo { Id = "qwen3.5-9b", Name = "9B", Repo = "unsloth/Qwen3.5-9B-GGUF", File = "Qwen3.5-9B-Q4_K_M.gguf", Proj = "mmproj-F16.gguf", SizeGb = 6.2, RamGb = 12, Note = "самая умная из небольших, лучше с видеокартой от 8 ГБ" },
            new ModelInfo { Id = "qwen3.5-35b-a3b", Name = "35B-A3B", Repo = "unsloth/Qwen3.5-35B-A3B-GGUF", File = "Qwen3.5-35B-A3B-Q4_K_M.gguf", Proj = "mmproj-F16.gguf", SizeGb = 21.4, RamGb = 32, Note = "большая, для мощных ПК с 32 ГБ памяти и больше; отвечает быстро для своего размера" },
        };

        static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kapsula", "ai"); } }
        static string EngineDir { get { return Path.Combine(Root, "llama"); } }
        static string ModelsDir { get { return Path.Combine(Root, "models"); } }
        static string ServerExe { get { return Path.Combine(EngineDir, "llama-server.exe"); } }
        static string LogFile { get { return Path.Combine(Store.Dir, "qwen.log"); } }

        public static ModelInfo Chosen
        {
            get
            {
                string id = Store.Get("qwenModel");
                foreach (ModelInfo m in Models) if (m.Id == id) return m;
                return Models[Recommended()];
            }
        }
        public static void Choose(string id) { Store.Set("qwenModel", id); }

        static string ModelPath(ModelInfo m) { return Path.Combine(ModelsDir, m.Id, m.File); }
        static string ProjPath(ModelInfo m) { return Path.Combine(ModelsDir, m.Id, m.Proj); }

        public static bool EngineReady { get { return File.Exists(ServerExe); } }
        public static bool Downloaded(ModelInfo m) { return File.Exists(ModelPath(m)); }
        public static bool Ready { get { return EngineReady && Downloaded(Chosen); } }

        // Сколько оперативной памяти у ПК (ГБ) — чтобы посоветовать размер модели
        public static double RamGb()
        {
            try { return new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / 1073741824.0; } catch { return 8; }
        }

        public static int Recommended()
        {
            double ram = RamGb();
            int best = 0;
            for (int i = 0; i < Models.Length - 1; i++) if (ram >= Models[i].RamGb + 4) best = i;   // самую большую не советуем сами
            return best;
        }

        // ---------------------------------------------------------------- скачивание
        public static volatile bool Busy;
        public static volatile string Status = "";
        public static long Done, Total;
        public static volatile string Error;
        static volatile bool cancel;
        public static event Action Finished;

        public static void Cancel() { cancel = true; }

        public static void Download(ModelInfo m)
        {
            if (Busy) return;
            Busy = true; cancel = false; Error = null; Done = 0; Total = 0; Status = "Готовлюсь…";
            var th = new Thread(delegate()
            {
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                    if (!EngineReady) GetEngine(WantedEngine());
                    Directory.CreateDirectory(Path.Combine(ModelsDir, m.Id));
                    if (!File.Exists(ProjPath(m))) Fetch(HfUrl(m.Repo, m.Proj), ProjPath(m), "Скачиваю зрение модели");
                    if (!File.Exists(ModelPath(m))) Fetch(HfUrl(m.Repo, m.File), ModelPath(m), "Скачиваю Qwen " + m.Name);
                    Status = "Готово";
                }
                catch (Exception ex)
                {
                    Error = cancel ? "Скачивание отменено" : ex.Message;
                    Status = "";
                    if (!cancel) Store.LogError("qwen скачивание", ex);
                }
                finally
                {
                    Busy = false;
                    var f = Finished; if (f != null) try { f(); } catch { }
                }
            });
            th.IsBackground = true;
            th.Start();
        }

        static string HfUrl(string repo, string file) { return "https://huggingface.co/" + repo + "/resolve/main/" + file + "?download=true"; }

        // ---- какой движок нужен этому ПК. На видеокартах NVIDIA сборка на CUDA в разы быстрее Vulkan
        // (на RTX 3080 с Qwen 3.5 9B — 92 слова в секунду против 7), хоть и весит около 600 МБ.
        static int nvDriver = -1;
        static int NvidiaDriver()
        {
            if (nvDriver >= 0) return nvDriver;
            nvDriver = 0;
            try
            {
                string smi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
                if (!File.Exists(smi)) return 0;
                var psi = new ProcessStartInfo(smi, "--query-gpu=driver_version --format=csv,noheader") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (var p = Process.Start(psi))
                {
                    string s = p.StandardOutput.ReadToEnd(); p.WaitForExit(5000);
                    int dot = s.IndexOf('.'); int v;
                    if (dot > 0 && int.TryParse(s.Substring(0, dot).Trim(), out v)) nvDriver = v;
                }
            }
            catch { }
            return nvDriver;
        }

        // "cuda-13" — новые драйверы NVIDIA, "cuda-12" — постарше, "vulkan" — AMD, Intel и всё остальное
        public static string WantedEngine() { int d = NvidiaDriver(); return d >= 580 ? "cuda-13" : d >= 528 ? "cuda-12" : "vulkan"; }
        static string KindFile { get { return Path.Combine(EngineDir, "kapsula-engine.txt"); } }
        public static string EngineKind { get { try { return File.Exists(KindFile) ? File.ReadAllText(KindFile).Trim() : EngineReady ? "vulkan" : ""; } catch { return ""; } } }
        public static bool IsNvidia { get { return WantedEngine() != "vulkan"; } }
        // Уже скачан Vulkan, а видеокарта NVIDIA — можно ускорить
        public static bool CanSpeedUp { get { return EngineReady && EngineKind == "vulkan" && IsNvidia; } }
        public static string EngineSizeText { get { return IsNvidia ? "около 600 МБ (CUDA для NVIDIA)" : "около 40 МБ"; } }

        public static void SpeedUp()
        {
            if (Busy) return;
            Busy = true; cancel = false; Error = null; Done = 0; Total = 0; Status = "Готовлюсь…";
            var th = new Thread(delegate()
            {
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; GetEngine(WantedEngine()); Status = "Готово"; }
                catch (Exception ex) { Error = cancel ? "Скачивание отменено" : ex.Message; Status = ""; if (!cancel) Store.LogError("qwen движок", ex); }
                finally { Busy = false; var f = Finished; if (f != null) try { f(); } catch { } }
            });
            th.IsBackground = true; th.Start();
        }

        // Свежая сборка llama.cpp с GitHub: ищем в последних выпусках архив для Windows (и библиотеки CUDA, если нужны)
        static void GetEngine(string kind)
        {
            Status = "Ищу llama.cpp…";
            string url, rt;
            FindEngine(kind, out url, out rt);
            DownloadEngine(kind, url, rt);
        }

        // Ссылки на архив llama.cpp (и библиотеки CUDA) в последних выпусках на GitHub
        public static void FindEngine(string kind, out string url, out string rt)
        {
            string json = GetString("https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=8");
            var rel = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as object[];
            string cudaVer = kind == "cuda-13" ? "-bin-win-cuda-13." : "-bin-win-cuda-12.";
            url = null; rt = null;
            if (rel != null)
                foreach (object r in rel)
                {
                    var assets = Get(r as Dictionary<string, object>, "assets") as object[];
                    if (assets == null) continue;
                    url = null; rt = null;
                    foreach (object a in assets)
                    {
                        var ad = a as Dictionary<string, object>;
                        string name = Get(ad, "name") as string ?? "", link = Get(ad, "browser_download_url") as string;
                        if (!name.EndsWith("-x64.zip")) continue;
                        if (kind == "vulkan") { if (name.StartsWith("llama-") && name.EndsWith("-bin-win-vulkan-x64.zip")) url = link; }
                        else if (name.StartsWith("llama-") && name.Contains(cudaVer)) url = link;
                        else if (name.StartsWith("cudart-llama") && name.Contains(cudaVer)) rt = link;
                    }
                    if (url != null && (kind == "vulkan" || rt != null)) break;
                }
            if (url == null || kind != "vulkan" && rt == null) throw new Exception("не нашёл сборку llama.cpp для Windows на GitHub");
        }

        static void DownloadEngine(string kind, string url, string rt)
        {
            Directory.CreateDirectory(Root);
            string tmp = EngineDir + ".new";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            string zip = Path.Combine(Root, "llama.zip"), zip2 = Path.Combine(Root, "cudart.zip");
            Fetch(url, zip, "Скачиваю llama.cpp" + (kind == "vulkan" ? "" : " для NVIDIA"));
            if (rt != null) Fetch(rt, zip2, "Скачиваю библиотеки CUDA");
            Status = "Распаковываю llama.cpp…";
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, tmp);
            // в архиве файлы могут лежать во вложенной папке — берём ту, где llama-server.exe
            string[] found = Directory.GetFiles(tmp, "llama-server.exe", SearchOption.AllDirectories);
            if (found.Length == 0) throw new Exception("в архиве llama.cpp нет llama-server.exe");
            string src = Path.GetDirectoryName(found[0]);
            if (rt != null)
                using (var za = System.IO.Compression.ZipFile.OpenRead(zip2))
                    foreach (var en in za.Entries)
                        if (en.Name.Length > 0) System.IO.Compression.ZipFileExtensions.ExtractToFile(en, Path.Combine(src, en.Name), true);   // библиотеки CUDA — рядом с llama-server.exe
            File.WriteAllText(Path.Combine(src, "kapsula-engine.txt"), kind);
            Stop();
            if (Directory.Exists(EngineDir)) Directory.Delete(EngineDir, true);
            Directory.Move(src, EngineDir);
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); File.Delete(zip); if (File.Exists(zip2)) File.Delete(zip2); } catch { }
        }
        static string GetString(string url)
        {
            var rq = (HttpWebRequest)WebRequest.Create(url);
            rq.UserAgent = "Kapsula";
            rq.Timeout = 20000;
            using (var r = rq.GetResponse())
            using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
        }

        // Скачивание в файл .part с докачкой после обрыва; готовый файл появляется только целиком
        static void Fetch(string url, string dest, string what)
        {
            string part = dest + ".part";
            for (int attempt = 0; ; attempt++)
            {
                long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                try
                {
                    var rq = (HttpWebRequest)WebRequest.Create(url);
                    rq.UserAgent = "Kapsula";
                    rq.Timeout = 30000; rq.ReadWriteTimeout = 60000;
                    rq.AllowAutoRedirect = true;
                    if (have > 0) rq.AddRange(have);
                    using (var resp = (HttpWebResponse)rq.GetResponse())
                    {
                        bool resumed = resp.StatusCode == HttpStatusCode.PartialContent;
                        if (!resumed) have = 0;
                        long len = resp.ContentLength;
                        Total = len > 0 ? have + len : 0;
                        Done = have;
                        Status = what;
                        using (var s = resp.GetResponseStream())
                        using (var fs = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                        {
                            byte[] buf = new byte[1 << 20];
                            int n;
                            while ((n = s.Read(buf, 0, buf.Length)) > 0)
                            {
                                if (cancel) throw new OperationCanceledException();
                                fs.Write(buf, 0, n);
                                Interlocked.Add(ref Done, n);
                            }
                        }
                    }
                    if (Total > 0 && new FileInfo(part).Length < Total) throw new IOException("файл скачался не полностью");
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(part, dest);
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    if (cancel) throw new OperationCanceledException();
                    if (attempt >= 4) throw;
                    Status = what + " — связь прервалась, продолжаю…";
                    Thread.Sleep(2000 * (attempt + 1));
                }
            }
        }

        public static void Delete(ModelInfo m)
        {
            if (Busy) return;
            if (loaded == m.Id) Stop();
            try { Directory.Delete(Path.Combine(ModelsDir, m.Id), true); } catch (Exception ex) { Store.LogError("qwen удаление", ex); }
        }

        // Сколько места занято всем, что скачано для Qwen
        public static double UsedGb()
        {
            try
            {
                if (!Directory.Exists(Root)) return 0;
                long sum = 0;
                foreach (string f in Directory.GetFiles(Root, "*", SearchOption.AllDirectories)) sum += new FileInfo(f).Length;
                return sum / 1073741824.0;
            }
            catch { return 0; }
        }

        // ---------------------------------------------------------------- сервер
        static Process server;
        static int port;
        static string loaded;
        static bool loadedVision;
        static int lastUse;
        static Timer idle;
        static readonly object gate = new object();
        static IntPtr job;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int cls, ref JobInfo info, int len);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr proc);
        [StructLayout(LayoutKind.Sequential)]
        struct JobInfo
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public int LimitFlags; public UIntPtr MinWs, MaxWs; public int ActiveProcessLimit; public UIntPtr Affinity; public int PriorityClass, SchedulingClass;
            public ulong r1, r2, r3, r4, r5, r6; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        // llama-server закрывается вместе с Kapsula, даже если она упала
        static void TieToUs(Process p)
        {
            try
            {
                if (job == IntPtr.Zero)
                {
                    job = CreateJobObject(IntPtr.Zero, null);
                    var info = new JobInfo { LimitFlags = 0x2000 };   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    SetInformationJobObject(job, 9, ref info, Marshal.SizeOf(typeof(JobInfo)));
                }
                AssignProcessToJobObject(job, p.Handle);
            }
            catch { }
        }

        public static bool Running { get { try { return server != null && !server.HasExited; } catch { return false; } } }

        static int FreePort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start(); int p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
            return p;
        }

        // Запускает сервер с выбранной моделью и ждёт, пока он загрузит её. null — готово, иначе текст ошибки.
        // vision — нужен ли разбор картинок: «зрение» модели (ещё ~1 ГБ) грузим только тогда
        static string Ensure(Action<string> progress) { return Ensure(progress, false); }
        static string Ensure(Action<string> progress, bool vision)
        {
            lock (gate)
            {
                ModelInfo m = Chosen;
                if (!EngineReady || !Downloaded(m)) return "Qwen ещё не скачан — настройки, «Нейросети».";
                vision = vision && File.Exists(ProjPath(m));
                if (Running && loaded == m.Id && (!vision || loadedVision)) return null;
                string err = Start(m, vision, true, progress);
                if (err == "args") err = Start(m, vision, false, progress);   // старая или новая сборка не знает какой-то ключ — запускаем с самыми простыми
                return err;
            }
        }

        static string Start(ModelInfo m, bool vision, bool lean, Action<string> progress)
        {
            {
                Stop();
                port = FreePort();
                var sb = new StringBuilder();
                sb.Append("-m \"").Append(ModelPath(m)).Append("\"");
                if (vision) sb.Append(" --mmproj \"").Append(ProjPath(m)).Append("\"");
                sb.Append(" --host 127.0.0.1 --port ").Append(port).Append(" -c 8192");
                // одно окно разговора вместо четырёх и загрузка без отображения файла в память:
                // в «Диспетчере задач» ~1 ГБ вместо 6-7 ГБ (сама модель — в видеопамяти)
                // --cache-ram 0: не держать копии прошлых разговоров в оперативной памяти
                if (lean) sb.Append(" -np 1 -lm none --no-ui --cache-ram 0");
                var psi = new ProcessStartInfo(ServerExe, sb.ToString())
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = EngineDir,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                var tail = new Queue<string>();
                DataReceivedEventHandler keep = delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (tail) { tail.Enqueue(e.Data); while (tail.Count > 40) tail.Dequeue(); }
                };
                try
                {
                    server = Process.Start(psi);
                    TieToUs(server);
                    try { server.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }   // игры и всё остальное — важнее
                    server.OutputDataReceived += keep; server.ErrorDataReceived += keep;
                    server.BeginOutputReadLine(); server.BeginErrorReadLine();
                }
                catch (Exception ex) { server = null; return "Не запустился llama.cpp: " + ex.Message; }
                loaded = m.Id; loadedVision = vision;
                if (progress != null) progress("Qwen просыпается…");
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 180000)
                {
                    if (!Running)
                    {
                        string log; lock (tail) log = string.Join("\n", tail.ToArray());
                        try { File.WriteAllText(LogFile, log, Encoding.UTF8); } catch { }
                        server = null; loaded = null;
                        if (lean && log.IndexOf("invalid argument", StringComparison.OrdinalIgnoreCase) >= 0) return "args";
                        return log.IndexOf("out of memory", StringComparison.OrdinalIgnoreCase) >= 0 || log.IndexOf("failed to allocate", StringComparison.OrdinalIgnoreCase) >= 0
                            ? "Qwen " + m.Name + " не помещается в память этого ПК — выберите модель поменьше."
                            : "llama.cpp закрылся при запуске (подробности — в qwen.log в папке настроек Kapsula).";
                    }
                    try
                    {
                        var rq = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/health");
                        rq.Timeout = 1500; rq.Proxy = null;
                        using (var r = (HttpWebResponse)rq.GetResponse()) if ((int)r.StatusCode == 200) { Touch(); return null; }
                    }
                    catch { }
                    Thread.Sleep(300);
                }
                Stop();
                return "Qwen слишком долго загружается.";
            }
        }

        static volatile bool warming;
        // Загрузить модель заранее (в фоне), если она ещё не загружена
        public static void Warm()
        {
            if (warming || Busy || forgotten || (Running && loaded == Chosen.Id)) return;
            warming = true;
            ThreadPool.QueueUserWorkItem(delegate { try { Ensure(null); } catch { } finally { warming = false; } });
        }

        static void Touch()
        {
            lastUse = Environment.TickCount;
            if (idle == null)
                idle = new Timer(delegate
                {
                    if (Running && unchecked(Environment.TickCount - lastUse) > 15 * 60 * 1000) Stop();   // 15 минут без вопросов — освобождаем память
                }, null, 60000, 60000);
        }

        // «Очистить» в чате: модель забывает разговор целиком — процесс закрывается вместе со всей своей памятью
        // (и видеопамятью) и сам больше не запускается: только когда зададут новый вопрос (около 2 секунд на запуск)
        static volatile bool forgotten;
        public static void Forget()
        {
            forgotten = true;
            ThreadPool.QueueUserWorkItem(delegate { lock (gate) Stop(); });
        }

        public static void Stop()
        {
            try { if (server != null && !server.HasExited) { server.Kill(); server.WaitForExit(3000); } } catch { }
            server = null; loaded = null;
        }

        // ---------------------------------------------------------------- разговор
        const string SystemPrompt = "Ты — помощник внутри небольшого виджета на рабочем столе Windows. Отвечай по делу, без вступлений и повторов вопроса. Пиши простым текстом без Markdown-разметки: виджет её не отображает, так что списки оформляй обычными строками. Отвечай на языке пользователя.";

        public static string Label { get { return "Qwen 3.5 · " + Chosen.Name + " на этом ПК"; } }

        // Возвращает null при успехе, иначе текст ошибки. Текст ответа приходит кусками через onText.
        public static string Send(List<ChatMsg> history, string system, Action<string> onText)
        {
            forgotten = false;   // новый вопрос — модель снова можно держать наготове
            bool vision = false;
            foreach (ChatMsg h in history) if (h.ImageB64 != null) vision = true;
            string err = Ensure(null, vision);
            if (err != null) return err;
            try
            {
                var msgs = new List<object>();
                msgs.Add(new Dictionary<string, object> { { "role", "system" }, { "content", system ?? SystemPrompt } });
                foreach (ChatMsg h in history)
                {
                    object content = h.Text ?? "";
                    if (h.ImageB64 != null)
                        content = new object[]
                        {
                            new Dictionary<string, object> { { "type", "image_url" }, { "image_url", new Dictionary<string, object> { { "url", "data:image/png;base64," + h.ImageB64 } } } },
                            new Dictionary<string, object> { { "type", "text" }, { "text", h.Text ?? "" } }
                        };
                    msgs.Add(new Dictionary<string, object> { { "role", h.Role == "assistant" ? "assistant" : "user" }, { "content", content } });
                }
                var body = new Dictionary<string, object>
                {
                    { "messages", msgs }, { "stream", true }, { "temperature", 0.7 }, { "top_p", 0.8 },
                    { "chat_template_kwargs", new Dictionary<string, object> { { "enable_thinking", false } } }   // без долгих «размышлений»: виджету нужен быстрый ответ
                };
                var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                byte[] payload = Encoding.UTF8.GetBytes(js.Serialize(body));
                var rq = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/v1/chat/completions");
                rq.Method = "POST"; rq.ContentType = "application/json"; rq.Proxy = null;
                rq.Timeout = 120000; rq.ReadWriteTimeout = 120000;
                using (var s = rq.GetRequestStream()) s.Write(payload, 0, payload.Length);
                bool any = false, inThink = false;
                using (var resp = rq.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        Touch();
                        if (!line.StartsWith("data:")) continue;
                        string j = line.Substring(5).Trim();
                        if (j == "[DONE]") break;
                        Dictionary<string, object> ev;
                        try { ev = js.DeserializeObject(j) as Dictionary<string, object>; } catch { continue; }
                        var ch = Get(ev, "choices") as object[];
                        if (ch == null || ch.Length == 0) continue;
                        string piece = Get(Get(ch[0] as Dictionary<string, object>, "delta") as Dictionary<string, object>, "content") as string;
                        if (string.IsNullOrEmpty(piece)) continue;
                        // на всякий случай вырезаем «размышления», если модель всё же их прислала текстом
                        if (piece.Contains("<think>")) { inThink = true; piece = piece.Substring(0, piece.IndexOf("<think>")); }
                        if (inThink) { int e = piece.IndexOf("</think>"); if (e < 0) continue; inThink = false; piece = piece.Substring(e + 8).TrimStart(); }
                        if (!any) piece = piece.TrimStart();
                        if (piece.Length == 0) continue;
                        any = true; onText(piece);
                    }
                }
                return any ? null : "Qwen не прислал ответа.";
            }
            catch (WebException ex)
            {
                var hr = ex.Response as HttpWebResponse;
                if (hr == null) return "Qwen не ответил: " + ex.Message;
                string msg = "";
                try { using (var sr = new StreamReader(hr.GetResponseStream(), Encoding.UTF8)) msg = sr.ReadToEnd(); } catch { }
                if (msg.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0) return "Эта модель Qwen не принимает картинки.";
                return "Ошибка Qwen " + (int)hr.StatusCode + (msg.Length > 0 ? ": " + (msg.Length > 200 ? msg.Substring(0, 200) : msg) : "");
            }
            catch (Exception ex) { return "Не удалось получить ответ Qwen: " + ex.Message; }
        }

        // Проверка (ключ --qwentest модель файл): скачать, запустить, задать два вопроса, записать ход в файл
        public static int Test(string id, string outFile)
        {
            var log = new StringBuilder();
            Action<string> w = delegate(string s) { lock (log) { log.AppendLine(DateTime.Now.ToString("HH:mm:ss ") + s); try { File.WriteAllText(outFile, log.ToString(), Encoding.UTF8); } catch { } } };
            ModelInfo m = null; foreach (ModelInfo x in Models) if (x.Id == id) m = x;
            if (m == null) { w("нет модели " + id); return 2; }
            Store.Set("qwenModel", m.Id);
            var done = new ManualResetEvent(false);
            Finished += delegate { done.Set(); };
            Download(m);
            string last = "";
            while (!done.WaitOne(5000)) { string s = Status + " " + (Total > 0 ? (100.0 * Interlocked.Read(ref Done) / Total).ToString("0") + "%" : ""); if (s != last) { w(s); last = s; } }
            if (Error != null) { w("ошибка: " + Error); return 1; }
            w("скачано, занято " + UsedGb().ToString("0.00") + " ГБ");
            foreach (string q in new string[] { "Привет! Ответь одним предложением: столица Франции?", "Переведи на английский: «Сегодня отличная погода»." })
            {
                var sw = Stopwatch.StartNew();
                var ans = new StringBuilder(); long first = -1;
                string err = Send(new List<ChatMsg> { new ChatMsg { Role = "user", Text = q } }, null, delegate(string p) { if (first < 0) first = sw.ElapsedMilliseconds; ans.Append(p); });
                w("вопрос: " + q);
                w(err != null ? "ошибка: " + err : "ответ за " + sw.ElapsedMilliseconds + " мс (первые слова через " + first + " мс): " + ans);
            }
            try { server.Refresh(); w("движок " + EngineKind + ", память в диспетчере задач ~" + (server.WorkingSet64 / 1048576) + " МБ"); } catch { }
            Stop();
            return 0;
        }

        static object Get(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v : null; }
    }
}
