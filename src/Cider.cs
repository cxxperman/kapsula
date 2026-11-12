// Kapsula — «остров» для Windows. Часть исходного кода: Cider.cs
// «В избранное» и «Не интересует» для Cider (клиент Apple Music). Стандартное управление музыкой в Windows умеет только
// играть, ставить на паузу и листать — оценок в нём нет. Cider же даёт программам на этом компьютере свой API
// (127.0.0.1:10767) по ключу, который пользователь создаёт в самом Cider и вставляет в настройки Kapsula.
// Ключ хранится только на этом компьютере, зашифрованный Windows, и отправляется только Cider на этот же компьютер.
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Kapsula
{
    static class Cider
    {
        const string Base = "http://127.0.0.1:10767/api/v1/playback/";

        public static string Token { get { return Secrets.GetKey("ciderKey", "CIDER_TOKEN").Trim(); } }

        // Играет ли сейчас Cider (по имени программы, которую видит Windows)
        public static bool IsCider(string source) { return !string.IsNullOrEmpty(source) && source.IndexOf("cider", StringComparison.OrdinalIgnoreCase) >= 0; }

        // null — Cider не ответил или ключ не подошёл
        static string Call(string method, string path, string json, out int status)
        {
            status = 0;
            try
            {
                var rq = (HttpWebRequest)WebRequest.Create(Base + path);
                rq.Method = method; rq.Timeout = 2500; rq.ReadWriteTimeout = 2500; rq.Proxy = null;
                string tok = Token;
                if (tok.Length > 0) rq.Headers["apptoken"] = tok;
                if (json != null)
                {
                    byte[] b = Encoding.UTF8.GetBytes(json);
                    rq.ContentType = "application/json"; rq.ContentLength = b.Length;
                    using (var s = rq.GetRequestStream()) s.Write(b, 0, b.Length);
                }
                using (var r = (HttpWebResponse)rq.GetResponse())
                using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                { status = (int)r.StatusCode; return sr.ReadToEnd(); }
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                status = r != null ? (int)r.StatusCode : 0;
                return null;
            }
            catch { return null; }
        }

        // В избранном ли текущий трек: true/false, null — неизвестно (Cider не отвечает или нужен ключ)
        public static bool? IsFavorite(out int status)
        {
            string s = Call("GET", "now-playing", null, out status);
            if (s == null) return null;
            int i = s.IndexOf("\"inFavorites\"", StringComparison.Ordinal);
            if (i < 0) return null;
            int colon = s.IndexOf(':', i);
            return colon > 0 && s.Substring(colon + 1).TrimStart().StartsWith("true");
        }

        // В избранное (заодно в медиатеку) или убрать оценку
        public static bool SetFavorite(bool on, out int status)
        {
            if (on) Call("POST", "add-to-library", null, out status);
            Call("POST", "set-rating", "{\"rating\":" + (on ? 1 : 0) + "}", out status);
            return status >= 200 && status < 300;
        }

        // «Не интересует»: предлагать меньше такого и сразу дальше
        public static bool Dislike(out int status)
        {
            Call("POST", "set-rating", "{\"rating\":-1}", out status);
            bool ok = status >= 200 && status < 300;
            int st2;
            if (ok) Call("POST", "next", null, out st2);
            return ok;
        }
    }
}
