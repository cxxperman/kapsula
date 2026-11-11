// Kapsula — «остров» для Windows. Часть исходного кода: Services.cs
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
    static class Store
    {
        public static readonly string Dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kapsula");
        static Dictionary<string, string> data = new Dictionary<string, string>();

        public static void Load()
        {
            try
            {
                System.IO.Directory.CreateDirectory(Dir);
                string f = System.IO.Path.Combine(Dir, "settings.txt");
                if (!System.IO.File.Exists(f)) return;
                foreach (string line in System.IO.File.ReadAllLines(f, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) data[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch { }
        }

        public static string Get(string key) { string v; return data.TryGetValue(key, out v) ? v : ""; }

        public static void Set(string key, string value)
        {
            data[key] = value ?? "";
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in data) sb.Append(kv.Key).Append('=').Append(kv.Value.Replace("\r", " ").Replace("\n", " ")).Append("\r\n");
                System.IO.File.WriteAllText(System.IO.Path.Combine(Dir, "settings.txt"), sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static void LogError(string where, Exception ex)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Dir, "error.log"), DateTime.Now + " [" + where + "] " + ex + "\r\n"); } catch { }
        }

        public static string NotePath { get { return System.IO.Path.Combine(Dir, "note.txt"); } }
    }

    // ================================================================ Погода
    class WeatherData
    {
        public string City = "", Error;
        public double Temp, Max, Min, Feels, Wind, WindDir, Humidity, Pressure, Uv, RainProb;
        public string Sunrise = "", Sunset = "";
        public bool IsDay = true;
        public int Code;
        public List<string[]> Hours = new List<string[]>(); // время, значок, температура
        public List<string[]> Days = new List<string[]>();  // день, значок, макс, мин
    }

    static class Weather
    {
        public static string Describe(int c)
        {
            if (c == 0) return "Ясно";
            if (c == 1) return "Малооблачно";
            if (c == 2) return "Переменная облачность";
            if (c == 3) return "Пасмурно";
            if (c == 45 || c == 48) return "Туман";
            if (c >= 51 && c <= 57) return "Морось";
            if (c >= 61 && c <= 67) return "Дождь";
            if (c >= 71 && c <= 77) return "Снег";
            if (c >= 80 && c <= 82) return "Ливень";
            if (c == 85 || c == 86) return "Снегопад";
            if (c >= 95) return "Гроза";
            return "—";
        }

        public static string Symbol(int c)
        {
            if (c <= 1) return "☀";
            if (c == 2) return "⛅";
            if (c == 3 || c == 45 || c == 48) return "☁";
            if ((c >= 51 && c <= 67) || (c >= 80 && c <= 82)) return "☂";
            if ((c >= 71 && c <= 77) || c == 85 || c == 86) return "❄";
            if (c >= 95) return "⚡";
            return "☁";
        }

        static string T(object o) { return Math.Round(Convert.ToDouble(o, System.Globalization.CultureInfo.InvariantCulture)).ToString("0").Replace("-", "−") + "°"; }
        static object[] Arr(object o) { return o as object[] ?? new object[0]; }

        static double Num(Dictionary<string, object> d, string key, double fallback)
        {
            object v;
            try { return d.TryGetValue(key, out v) && v != null ? Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture) : fallback; }
            catch { return fallback; }
        }

        static double First(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v)) return 0;
            object[] a = Arr(v);
            try { return a.Length > 0 && a[0] != null ? Convert.ToDouble(a[0], System.Globalization.CultureInfo.InvariantCulture) : 0; }
            catch { return 0; }
        }

        static string Clock(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v)) return "";
            object[] a = Arr(v);
            string s = a.Length > 0 ? Convert.ToString(a[0]) : "";
            return s.Length >= 16 ? s.Substring(11, 5) : "";
        }

        public static string WindName(double deg)
        {
            string[] n = { "С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ" };
            return n[(int)Math.Round(((deg % 360) + 360) % 360 / 45.0) % 8];
        }

        // Цвета неба для карточки: зависят от погоды и времени суток.
        public static Color[] Sky(int code, bool day)
        {
            if (!day) return new Color[] { Color.FromRgb(0x10, 0x1B, 0x33), Color.FromRgb(0x2B, 0x4A, 0x6F) };
            if (code <= 1) return new Color[] { Color.FromRgb(0x2A, 0x7D, 0xE8), Color.FromRgb(0x6C, 0xC4, 0xF2) };
            if (code == 2) return new Color[] { Color.FromRgb(0x3A, 0x7B, 0xC8), Color.FromRgb(0x8F, 0xB8, 0xD8) };
            if (code == 3 || code == 45 || code == 48) return new Color[] { Color.FromRgb(0x4A, 0x5D, 0x73), Color.FromRgb(0x7D, 0x8F, 0xA3) };
            if ((code >= 71 && code <= 77) || code == 85 || code == 86) return new Color[] { Color.FromRgb(0x5B, 0x7F, 0xA8), Color.FromRgb(0xA9, 0xC4, 0xDE) };
            return new Color[] { Color.FromRgb(0x2C, 0x3E, 0x55), Color.FromRgb(0x55, 0x6D, 0x88) };
        }

        public static WeatherData Fetch(string city)
        {
            var w = new WeatherData();
            try
            {
                System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var js = new System.Web.Script.Serialization.JavaScriptSerializer();
                string lat = Store.Get("lat"), lon = Store.Get("lon");
                w.City = Store.Get("cityName");
                if (Store.Get("cityQuery") != city || lat == "" || lon == "")
                {
                    string g = Download("https://geocoding-api.open-meteo.com/v1/search?count=1&language=ru&name=" + Uri.EscapeDataString(city));
                    var gr = (Dictionary<string, object>)js.DeserializeObject(g);
                    object res;
                    if (!gr.TryGetValue("results", out res) || Arr(res).Length == 0) { w.Error = "Город не найден"; return w; }
                    var first = (Dictionary<string, object>)Arr(res)[0];
                    lat = Convert.ToDouble(first["latitude"], inv).ToString("0.####", inv);
                    lon = Convert.ToDouble(first["longitude"], inv).ToString("0.####", inv);
                    w.City = Convert.ToString(first["name"]);
                    Store.Set("lat", lat); Store.Set("lon", lon); Store.Set("cityName", w.City); Store.Set("cityQuery", city);
                }
                string f = Download("https://api.open-meteo.com/v1/forecast?latitude=" + lat + "&longitude=" + lon +
                    "&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m,wind_direction_10m,relative_humidity_2m,surface_pressure" +
                    "&hourly=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,uv_index_max,precipitation_probability_max" +
                    "&wind_speed_unit=ms&timezone=auto&forecast_days=6");
                var r = (Dictionary<string, object>)js.DeserializeObject(f);
                var cur = (Dictionary<string, object>)r["current"];
                w.Temp = Convert.ToDouble(cur["temperature_2m"], inv);
                w.Code = Convert.ToInt32(cur["weather_code"], inv);
                w.Feels = Num(cur, "apparent_temperature", w.Temp);
                w.IsDay = Num(cur, "is_day", 1) > 0.5;
                w.Wind = Num(cur, "wind_speed_10m", 0);
                w.WindDir = Num(cur, "wind_direction_10m", 0);
                w.Humidity = Num(cur, "relative_humidity_2m", 0);
                w.Pressure = Num(cur, "surface_pressure", 0) * 0.750062; // гПа → мм рт. ст.
                string curTime = Convert.ToString(cur["time"]);
                string hourPrefix = curTime.Length >= 13 ? curTime.Substring(0, 13) : curTime;

                var hourly = (Dictionary<string, object>)r["hourly"];
                object[] ht = Arr(hourly["time"]), htemp = Arr(hourly["temperature_2m"]), hcode = Arr(hourly["weather_code"]);
                int start = 0;
                for (int i = 0; i < ht.Length; i++) if (string.CompareOrdinal(Convert.ToString(ht[i]), hourPrefix) >= 0) { start = i; break; }
                for (int i = start + 1; i < ht.Length && w.Hours.Count < 8; i += 3)
                {
                    string t = Convert.ToString(ht[i]);
                    w.Hours.Add(new string[] { t.Length >= 16 ? t.Substring(11, 5) : t, Symbol(Convert.ToInt32(hcode[i], inv)), T(htemp[i]) });
                }

                var daily = (Dictionary<string, object>)r["daily"];
                object[] dt = Arr(daily["time"]), dmax = Arr(daily["temperature_2m_max"]), dmin = Arr(daily["temperature_2m_min"]), dcode = Arr(daily["weather_code"]);
                var ru = new System.Globalization.CultureInfo("ru-RU");
                for (int i = 0; i < dt.Length && i < 6; i++)
                {
                    DateTime d = DateTime.ParseExact(Convert.ToString(dt[i]), "yyyy-MM-dd", inv);
                    if (i == 0)
                    {
                        w.Max = Convert.ToDouble(dmax[i], inv); w.Min = Convert.ToDouble(dmin[i], inv);
                        w.Sunrise = Clock(daily, "sunrise"); w.Sunset = Clock(daily, "sunset");
                        w.Uv = First(daily, "uv_index_max"); w.RainProb = First(daily, "precipitation_probability_max");
                    }
                    w.Days.Add(new string[] { i == 0 ? "Сегодня" : ru.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek), Symbol(Convert.ToInt32(dcode[i], inv)), T(dmax[i]), T(dmin[i]) });
                }
            }
            catch (Exception ex) { w.Error = "Нет связи: " + ex.Message; }
            return w;
        }

        static string Download(string url)
        {
            using (var wc = new System.Net.WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers["User-Agent"] = "Kapsula";
                return wc.DownloadString(url);
            }
        }

        public static string Temp(double t) { return Math.Round(t).ToString("0").Replace("-", "−") + "°"; }
    }

    // ================================================= Преобразования текста
    static class TextTools
    {
        const string En = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./~QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?";
        const string Ru = "ёйцукенгшщзхъфывапролджэячсмитьбю.ЁЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,";

        public static string FixLayout(string s)
        {
            int cyr = 0, lat = 0;
            foreach (char c in s) { if (c >= 'А' && c <= 'я' || c == 'ё' || c == 'Ё') cyr++; else if (c < 128 && char.IsLetter(c)) lat++; }
            string from = cyr > lat ? Ru : En, to = cyr > lat ? En : Ru;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) { int i = from.IndexOf(c); sb.Append(i >= 0 ? to[i] : c); }
            return sb.ToString();
        }

        static readonly string[] TrFrom = { "а","б","в","г","д","е","ё","ж","з","и","й","к","л","м","н","о","п","р","с","т","у","ф","х","ц","ч","ш","щ","ъ","ы","ь","э","ю","я" };
        static readonly string[] TrTo = { "a","b","v","g","d","e","yo","zh","z","i","y","k","l","m","n","o","p","r","s","t","u","f","kh","ts","ch","sh","shch","","y","","e","yu","ya" };

        public static string Translit(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                string lower = char.ToLowerInvariant(c).ToString();
                int i = Array.IndexOf(TrFrom, lower);
                if (i < 0) { sb.Append(c); continue; }
                string t = TrTo[i];
                if (char.IsUpper(c) && t.Length > 0) t = char.ToUpperInvariant(t[0]) + t.Substring(1);
                sb.Append(t);
            }
            return sb.ToString();
        }

        public static string Title(string s)
        {
            var sb = new StringBuilder(s.ToLower());
            bool start = true;
            for (int i = 0; i < sb.Length; i++)
            {
                if (char.IsLetter(sb[i])) { if (start) sb[i] = char.ToUpper(sb[i]); start = false; }
                else start = true;
            }
            return sb.ToString();
        }

        public static string B64Enc(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s)); }
        public static string B64Dec(string s) { return Encoding.UTF8.GetString(Convert.FromBase64String(s.Trim())); }
    }

    class ClipItem { public string Text; public bool Pinned; public BitmapSource Image; public byte[] Png; public uint Hash; }

    class ChatMsg { public string Role, Text, ImageB64; }

    class Hot
    {
        public int Id;
        public string Key, Name;
        public uint Mods, Vk, DefMods, DefVk;
        public bool Ok;
        public Action Run;
    }

    // ========================================================== Запись экрана
    // Запись ведёт ffmpeg, уже установленный на компьютере (захват gdigrab, видео H.264 в MP4, без звука).
    static class Recorder
    {
        static System.Diagnostics.Process proc;
        public static string FilePath;
        public static DateTime Started;
        public static bool Active { get { try { return proc != null && !proc.HasExited; } catch { return false; } } }

        static string Search(string dir, int depth, ref int budget)
        {
            if (depth < 0 || budget-- <= 0 || string.IsNullOrEmpty(dir)) return null;
            try
            {
                string f = System.IO.Path.Combine(dir, "ffmpeg.exe");
                if (System.IO.File.Exists(f)) return f;
                foreach (string s in System.IO.Directory.GetDirectories(dir))
                {
                    if ((System.IO.File.GetAttributes(s) & System.IO.FileAttributes.ReparsePoint) != 0) continue;
                    string r = Search(s, depth - 1, ref budget);
                    if (r != null) return r;
                }
            }
            catch { }
            return null;
        }

        public static string FindFfmpeg()
        {
            string saved = Store.Get("ffmpeg");
            if (saved != "" && System.IO.File.Exists(saved)) return saved;
            string found = null;
            foreach (string d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { string f = System.IO.Path.Combine(d.Trim(), "ffmpeg.exe"); if (d.Trim() != "" && System.IO.File.Exists(f)) { found = f; break; } } catch { }
            }
            if (found == null)
            {
                int budget = 40000;
                foreach (Environment.SpecialFolder sf in new Environment.SpecialFolder[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
                {
                    found = Search(Environment.GetFolderPath(sf), 5, ref budget);
                    if (found != null) break;
                }
            }
            if (found != null) Store.Set("ffmpeg", found);
            return found;
        }

        static LoopbackCapture audio;
        public static bool HasAudio;

        public static bool Start(string ffmpeg, string path, int x, int y, int w, int h, string codec, bool withAudio)
        {
            DropAudio();
            try
            {
                string audioIn = "", audioOut = "";
                if (withAudio)
                {
                    audio = LoopbackCapture.TryCreate();
                    if (audio == null) return false;
                    // Второй вход ffmpeg — сырой звук из канала. Время звука отсчитывается по числу отсчётов с нуля:
                    // захват начинается в момент, когда ffmpeg открыл канал, то есть вместе с первым кадром видео.
                    audioIn = " -f " + audio.FfmpegFormat + " -ar " + audio.Rate + " -ac " + audio.Channels +
                              " -thread_queue_size 4096 -i \\\\.\\pipe\\" + audio.PipeName;
                    audioOut = " -c:a aac -b:a 192k";
                }
                // Звук идёт первым входом: ffmpeg открывает входы по очереди, и захват экрана стартует сразу следом,
                // так что дорожки начинаются почти одновременно. В обратном порядке звук отставал от начала видео на секунду.
                string args = "-hide_banner -loglevel error -y" + audioIn +
                              " -f gdigrab -framerate 30 -draw_mouse 1 -offset_x " + x + " -offset_y " + y + " -video_size " + (w & ~1) + "x" + (h & ~1) +
                              " -thread_queue_size 1024 -i desktop " + codec + " -pix_fmt yuv420p" + audioOut + " -movflags +faststart \"" + path + "\"";
                var psi = new System.Diagnostics.ProcessStartInfo(ffmpeg, args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardInput = true };
                proc = System.Diagnostics.Process.Start(psi);
                FilePath = path;
                Started = DateTime.Now;
                HasAudio = withAudio;
                return true;
            }
            catch { proc = null; DropAudio(); return false; }
        }

        public static void DropAudio()
        {
            var a = audio;
            audio = null;
            if (a != null) a.Dispose();
        }

        public static void Stop()
        {
            var p = proc;
            proc = null;
            if (p == null) { DropAudio(); return; }
            try
            {
                if (!p.HasExited)
                {
                    p.StandardInput.Write("q");
                    p.StandardInput.Flush();
                    if (!p.WaitForExit(8000)) p.Kill();
                }
            }
            catch { try { p.Kill(); } catch { } }
            DropAudio(); // канал закрываем после того, как ffmpeg дописал файл
        }
    }

    // ============================================================ ИИ-чат (Claude)
    // Ключ Claude API хранится в настройках в зашифрованном виде (DPAPI: расшифровать может только этот пользователь Windows).
    static class Secrets
    {
        public static void SetKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) { Store.Set("claudeKey", ""); return; }
            byte[] enc = System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            Store.Set("claudeKey", Convert.ToBase64String(enc));
        }

        public static string GetKey()
        {
            try
            {
                string s = Store.Get("claudeKey");
                if (s != "") return Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(s), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
            }
            catch { }
            return Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
        }

        public static bool Saved { get { return Store.Get("claudeKey") != ""; } }
    }

    // Запрос к Claude Messages API обычным HTTP с потоковым ответом (SSE).
    // Официальной библиотекой Anthropic для C# здесь не воспользоваться: проект собирается компилятором .NET Framework без менеджера пакетов.
    static class ClaudeChat
    {
        public const string DefaultModel = "claude-opus-5-5";
        const string SystemPrompt = "Ты — помощник внутри небольшого виджета на рабочем столе Windows. Отвечай по делу, без вступлений и повторов вопроса. Пиши простым текстом без Markdown-разметки: виджет её не отображает, так что списки оформляй обычными строками. Отвечай на языке пользователя.";

        public static string Model()
        {
            string m = Store.Get("claudeModel");
            return m == "claude-sonnet-5-5" ? m : DefaultModel;
        }

        // Возвращает null при успехе, иначе текст ошибки для показа пользователю. Текст ответа приходит кусками через onText.
        public static string Send(string key, string model, List<ChatMsg> history, string system, Action<string> onText)
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
                var js = new System.Web.Script.Serialization.JavaScriptSerializer();
                js.MaxJsonLength = int.MaxValue;
                var msgs = new List<object>();
                foreach (ChatMsg h in history)
                {
                    object content = h.Text;
                    if (h.ImageB64 != null)
                    {
                        // Сообщение с картинкой: сначала изображение, затем текст вопроса.
                        content = new object[]
                        {
                            new Dictionary<string, object> { { "type", "image" }, { "source", new Dictionary<string, object> { { "type", "base64" }, { "media_type", "image/png" }, { "data", h.ImageB64 } } } },
                            new Dictionary<string, object> { { "type", "text" }, { "text", h.Text } }
                        };
                    }
                    msgs.Add(new Dictionary<string, object> { { "role", h.Role }, { "content", content } });
                }
                var body = new Dictionary<string, object>
                {
                    { "model", model },
                    { "max_tokens", 64000 },
                    { "stream", true },
                    { "system", system ?? SystemPrompt },
                    // На Opus 5.5 размышление включено всегда; глубину задаёт effort.
                    { "output_config", new Dictionary<string, object> { { "effort", "medium" } } },
                    // Если модель отклонит запрос по соображениям безопасности, сервер сам попробует запасную модель.
                    { "fallbacks", "default" },
                    { "messages", msgs }
                };
                byte[] payload = Encoding.UTF8.GetBytes(js.Serialize(body));

                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("https://api.anthropic.com/v1/messages");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Headers["x-api-key"] = key;
                req.Headers["anthropic-version"] = "2026-10-21";
                req.Headers["anthropic-beta"] = "server-side-fallback-2026-07-01";
                req.Timeout = 60000;
                req.ReadWriteTimeout = 600000;
                using (var rs = req.GetRequestStream()) rs.Write(payload, 0, payload.Length);

                string stop = null;
                using (var resp = req.GetResponse())
                using (var reader = new System.IO.StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!line.StartsWith("data:")) continue;
                        string json = line.Substring(5).Trim();
                        if (json.Length == 0) continue;
                        var ev = js.DeserializeObject(json) as Dictionary<string, object>;
                        if (ev == null) continue;
                        string type = Str(ev, "type");
                        if (type == "content_block_delta")
                        {
                            var d = Obj(ev, "delta");
                            if (Str(d, "type") == "text_delta") { string t = Str(d, "text"); if (!string.IsNullOrEmpty(t)) onText(t); }
                        }
                        else if (type == "message_delta")
                        {
                            string sr = Str(Obj(ev, "delta"), "stop_reason");
                            if (sr != null) stop = sr;
                        }
                        else if (type == "error")
                            return "Ошибка сервиса: " + (Str(Obj(ev, "error"), "message") ?? "без описания");
                    }
                }
                if (stop == "refusal") return "Модель отклонила этот запрос.";
                if (stop == "max_tokens") onText("\n… (ответ обрезан по длине)");
                return null;
            }
            catch (System.Net.WebException ex)
            {
                var hr = ex.Response as System.Net.HttpWebResponse;
                if (hr == null) return "Нет связи с api.anthropic.com: " + ex.Message;
                string msg = null;
                try
                {
                    using (var reader = new System.IO.StreamReader(hr.GetResponseStream(), Encoding.UTF8))
                    {
                        var js = new System.Web.Script.Serialization.JavaScriptSerializer();
                        msg = Str(Obj(js.DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>, "error"), "message");
                    }
                }
                catch { }
                int code = (int)hr.StatusCode;
                if (code == 401) return "Ключ не принят (401). Проверьте его в настройках.";
                if (code == 403) return "Доступ запрещён (403): " + (msg ?? "у ключа нет прав на этот запрос");
                if (code == 404) return "Модель недоступна для этого ключа (404).";
                if (code == 429) return "Слишком много запросов или исчерпан лимит (429). Попробуйте позже.";
                if (code == 400) return "Запрос отклонён (400): " + (msg ?? "неверный запрос");
                if (code >= 500) return "Сервис перегружен (" + code + "). Попробуйте ещё раз.";
                return "Ошибка " + code + ": " + (msg ?? ex.Message);
            }
            catch (Exception ex) { return "Не удалось получить ответ: " + ex.Message; }
        }

        static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as string : null;
        }
    }

    class CmdItem { public string Title, Sub, Glyph, IconPath; public Action Run; }

    class Reminder { public DateTime At; public string Text; public bool Daily; }

    // Значки программ и ярлыков
    static class Shell
    {
        static object wsh;
        static Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static string LnkTarget(string lnk)
        {
            try
            {
                if (wsh == null) wsh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                object sc = wsh.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, wsh, new object[] { lnk });
                return sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null) as string;
            }
            catch { return null; }
        }

        public static ImageSource IconFor(string path)
        {
            ImageSource hit;
            if (cache.TryGetValue(path, out hit)) return hit;
            ImageSource res = null;
            try
            {
                string src = path;
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    string t = LnkTarget(path);
                    if (!string.IsNullOrEmpty(t) && System.IO.File.Exists(t)) src = t;
                }
                using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(src))
                {
                    if (ico != null)
                    {
                        var bs = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bs.Freeze();
                        res = bs;
                    }
                }
            }
            catch { }
            cache[path] = res;
            return res;
        }
    }

    // Калькулятор, перевод единиц и разбор фраз для командной строки
    static class Calc
    {
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        static string Num(double d) { return Math.Round(d, 6).ToString("0.######", Inv).Replace('.', ','); }

        public static bool TryCalc(string q, out string res)
        {
            res = null;
            string e = q.Replace(',', '.').Replace('×', '*').Replace('÷', '/').TrimEnd('=').Trim();
            e = System.Text.RegularExpressions.Regex.Replace(e, @"(\d+(?:\.\d+)?)\s*%", "($1/100.0)");
            if (!System.Text.RegularExpressions.Regex.IsMatch(e, @"^[\d\.\+\-\*\/\(\)\s]+$")) return false;
            if (!System.Text.RegularExpressions.Regex.IsMatch(e, @"[\d\)]\s*[\+\-\*\/]\s*[\d\(]")) return false;
            try
            {
                // Целые делим как дробные: 5/2 = 2,5
                e = System.Text.RegularExpressions.Regex.Replace(e, @"(?<![\d\.])(\d+)(?![\d\.])", "$1.0");
                double d = Convert.ToDouble(new System.Data.DataTable().Compute(e, null), Inv);
                if (double.IsNaN(d) || double.IsInfinity(d)) return false;
                res = Num(d);
                return true;
            }
            catch { return false; }
        }

        static readonly Dictionary<string, double[]> Units = BuildUnits();

        static Dictionary<string, double[]> BuildUnits()
        {
            var u = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            Action<int, double, string> add = delegate(int kind, double f, string names) { foreach (string n in names.Split(' ')) u[n] = new double[] { kind, f }; };
            add(0, 1000, "км km километр километра километров");
            add(0, 1, "м m метр метра метров");
            add(0, 0.01, "см cm сантиметр сантиметра сантиметров");
            add(0, 0.001, "мм mm");
            add(0, 1609.344, "миля мили миль mi mile miles");
            add(0, 0.3048, "фут фута футов ft");
            add(0, 0.0254, "дюйм дюйма дюймов inch inches");
            add(0, 0.9144, "ярд ярда ярдов yd");
            add(1, 1, "кг kg килограмм килограмма килограммов");
            add(1, 0.001, "г g грамм грамма граммов");
            add(1, 0.45359237, "фунт фунта фунтов lb lbs");
            add(1, 0.028349523, "унция унции унций oz");
            add(1, 1000, "т t тонна тонны тонн");
            return u;
        }

        public static bool TryConvert(string q, out string res)
        {
            res = null;
            var m = System.Text.RegularExpressions.Regex.Match(q.Trim(), @"^(\d+(?:[\.,]\d+)?)\s*([a-zа-яё]+)\s+(?:в|to)\s+([a-zа-яё]+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            double[] a, b;
            if (!Units.TryGetValue(m.Groups[2].Value, out a) || !Units.TryGetValue(m.Groups[3].Value, out b) || a[0] != b[0]) return false;
            double v = double.Parse(m.Groups[1].Value.Replace(',', '.'), Inv);
            res = Num(v) + " " + m.Groups[2].Value + " = " + Math.Round(v * a[1] / b[1], 3).ToString("0.###", Inv).Replace('.', ',') + " " + m.Groups[3].Value;
            return true;
        }

        // Разбор фразы напоминания: «через 10 мин чай», «в 18:30 созвон», «завтра в 9 позвонить», «каждый день в 23:00 таблетки».
        public static bool TryReminder(string q, DateTime now, out DateTime at, out string text, out bool daily)
        {
            at = now; text = ""; daily = false;
            if (string.IsNullOrWhiteSpace(q)) return false;
            var opt = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
            string s = " " + q.Trim() + " ";
            System.Text.RegularExpressions.Match m;
            bool ok = false;

            m = System.Text.RegularExpressions.Regex.Match(s, @"\sкажд\w+ день(?: в)? (\d{1,2})(?:[:\.](\d{2}))?(?=\s)", opt);
            if (m.Success && TimeOf(m, 1, now.Date, out at)) { daily = true; if (at <= now) at = at.AddDays(1); ok = true; }
            if (!ok)
            {
                m = System.Text.RegularExpressions.Regex.Match(s, @"\sчерез (\d+)\s*(сек\w*|мин\w*|час\w*|ч|м|с)(?=\s)", opt);
                if (m.Success)
                {
                    int n = int.Parse(m.Groups[1].Value, Inv);
                    string u = m.Groups[2].Value.ToLowerInvariant();
                    at = u.StartsWith("с") ? now.AddSeconds(n) : (u.StartsWith("ч") ? now.AddHours(n) : now.AddMinutes(n));
                    ok = n > 0;
                }
            }
            if (!ok)
            {
                m = System.Text.RegularExpressions.Regex.Match(s, @"\sзавтра(?: в (\d{1,2})(?:[:\.](\d{2}))?)?(?=\s)", opt);
                if (m.Success)
                {
                    if (m.Groups[1].Success) ok = TimeOf(m, 1, now.Date.AddDays(1), out at);
                    else { at = now.Date.AddDays(1).AddHours(9); ok = true; }
                }
            }
            if (!ok)
            {
                m = System.Text.RegularExpressions.Regex.Match(s, @"\s(?:сегодня )?в (\d{1,2})(?:[:\.](\d{2}))?(?=\s)", opt);
                if (m.Success && TimeOf(m, 1, now.Date, out at)) { if (at <= now) at = at.AddDays(1); ok = true; }
            }
            if (!ok) return false;

            text = (s.Substring(0, m.Index) + " " + s.Substring(m.Index + m.Length)).Trim();
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^(напомни(ть)?( мне)?|напоминание)[\s:,]*", "", opt).Trim();
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s{2,}", " ");
            if (text.Length == 0) text = "Напоминание";
            else text = char.ToUpper(text[0]) + text.Substring(1);
            return true;
        }

        static bool TimeOf(System.Text.RegularExpressions.Match m, int g, DateTime day, out DateTime at)
        {
            at = day;
            int h = int.Parse(m.Groups[g].Value, Inv);
            int min = m.Groups[g + 1].Success ? int.Parse(m.Groups[g + 1].Value, Inv) : 0;
            if (h > 23 || min > 59) return false;
            at = day.AddHours(h).AddMinutes(min);
            return true;
        }

        public static bool TryTimer(string q, out TimeSpan ts)
        {
            ts = TimeSpan.Zero;
            var m = System.Text.RegularExpressions.Regex.Match(q.Trim(), @"^(?:(таймер|timer)\s+)?(\d+)\s*(сек\w*|с|sec\w*|s|мин\w*|м|min\w*|m|час\w*|ч|h\w*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            int n = int.Parse(m.Groups[2].Value, Inv);
            string u = m.Groups[3].Value.ToLowerInvariant();
            if (n <= 0 || n > 100000) return false;
            if (u.StartsWith("с") || u.StartsWith("s")) ts = TimeSpan.FromSeconds(n);
            else if (u.StartsWith("ч") || u.StartsWith("h")) ts = TimeSpan.FromHours(n);
            else ts = TimeSpan.FromMinutes(n);
            return true;
        }
    }

    static class Autostart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsOn()
        {
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key)) return k != null && k.GetValue("Kapsula") != null; }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue("Kapsula", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"");
                    else k.DeleteValue("Kapsula", false);
                }
            }
            catch { }
        }
    }
}
