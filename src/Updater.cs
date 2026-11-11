// Kapsula — «остров» для Windows. Часть исходного кода: Updater.cs
// Автообновление из релизов GitHub.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Kapsula
{
    // Раз в несколько часов спрашивает у GitHub список релизов. Если есть версия новее — скачивает Kapsula.exe,
    // сверяет контрольную сумму SHA-256 и подменяет файл программы. Новая версия включается после перезапуска.
    // Кроме запроса списка релизов и самого файла, в сеть ничего не уходит: ни настроек, ни идентификаторов.
    static class Updater
    {
        // Репозиторий на GitHub в виде «владелец/название». Если оставить пустым — автообновление выключено.
        public const string Repo = "cxxperman/kapsula";

        // Только для проверки на своём компьютере: адрес вида http://127.0.0.1:порт/..., отдающий тот же JSON, что GitHub.
        public static string FeedOverride;

        static IslandWindow island;

        public static Version Current { get { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version; } }
        public static string CurrentText { get { Version v = Current; return v.Major + "." + v.Minor + "." + v.Build; } }
        public static bool Configured { get { return Repo != "" || FeedOverride != null; } }
        public static bool Enabled { get { return Configured && Store.Get("autoUpdate") != "0"; } }

        // Ключ --noapi: для проверки запасного пути, как будто GitHub не отдал список релизов.
        public static bool SkipApi;

        static readonly AutoResetEvent wake = new AutoResetEvent(false);
        static volatile bool manual, installed;
        static volatile string status = "";

        // Чем кончилась последняя проверка — показывается в настройках, чтобы было видно, работает ли обновление.
        public static string StatusText { get { return status; } }
        static void SetStatus(string text) { status = "Проверено в " + DateTime.Now.ToString("HH:mm") + ": " + text; }
        static void Say(string text) { if (island != null) island.Dispatcher.BeginInvoke(new Action(delegate { island.UpdateToast(text); })); }

        // Кнопка «Проверить обновления»: не ждём очередного часа.
        public static void CheckNow()
        {
            if (!Configured) return;
            if (installed) { island.RestartForUpdate(); return; }
            manual = true;
            wake.Set();
        }

        static string ExePath { get { return System.Reflection.Assembly.GetExecutingAssembly().Location; } }

        public static void Start(IslandWindow w)
        {
            island = w;
            // Прошлая версия остаётся рядом под именем Kapsula.old.exe, пока работает; при следующем запуске убираем её.
            try { string old = System.IO.Path.ChangeExtension(ExePath, ".old.exe"); if (System.IO.File.Exists(old)) System.IO.File.Delete(old); } catch { }
            if (!Configured) return;
            var t = new Thread(delegate()
            {
                wake.WaitOne(FeedOverride != null ? 2000 : 20000);
                int fails = 0;
                while (true)
                {
                    int wait = 60 * 60 * 1000;   // проверяем раз в час
                    bool byHand = manual; manual = false;
                    if (Enabled || byHand)
                    {
                        try
                        {
                            if (Check()) break;
                            fails = 0;
                            SetStatus("у вас последняя версия");
                            if (byHand) Say("У вас последняя версия — " + CurrentText);
                        }
                        catch (Exception ex)
                        {
                            // Сразу после включения компьютера сети часто ещё нет — первые попытки повторяем через пару минут.
                            if (fails == 0 || byHand) Store.LogError("обновление", ex);
                            fails++;
                            if (fails <= 5) wait = 2 * 60 * 1000;
                            SetStatus("не удалось проверить: " + ex.Message);
                            if (byHand) Say("Не удалось проверить обновления");
                        }
                    }
                    wake.WaitOne(wait);
                }
                SetStatus("скачана новая версия, включится после перезапуска");
                // Новая версия уже на диске. Перезапускаемся сами, как только это никому не помешает.
                int idleNeeded = FeedOverride != null ? 0 : 120000;
                while (true)
                {
                    Thread.Sleep(FeedOverride != null ? 3000 : 15000);
                    if (!Enabled) continue;
                    try
                    {
                        bool ok = (bool)island.Dispatcher.Invoke(new Func<bool>(delegate { return island.CanRestartNow(idleNeeded); }));
                        if (ok) { island.Dispatcher.Invoke(new Action(delegate { island.RestartForUpdate(); })); return; }
                    }
                    catch (Exception ex) { Store.LogError("перезапуск после обновления", ex); return; }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // «v0.2.1», «0.2.1-alpha», «0.2» → версия. Всё после дефиса отбрасывается.
        public static bool TryParseVersion(string tag, out Version v)
        {
            v = null;
            if (tag == null) return false;
            string s = tag.Trim().TrimStart('v', 'V');
            int dash = s.IndexOf('-');
            if (dash >= 0) s = s.Substring(0, dash);
            string[] parts = s.Split('.');
            if (parts.Length < 2 || parts.Length > 3) return false;
            int[] n = new int[3];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], out n[i]) || n[i] < 0 || n[i] > 65000) return false;
            v = new Version(n[0], n[1], n[2], 0);
            return true;
        }

        // Выбирает из ответа GitHub самый новый релиз, в котором есть файл Kapsula.exe.
        // sha — контрольная сумма из поля digest; если её нет, shaUrl — адрес файла Kapsula.exe.sha256.
        public static bool PickRelease(string json, out Version version, out string url, out string sha, out string shaUrl)
        {
            version = null; url = null; sha = null; shaUrl = null;
            object root = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.DeserializeObject(json);
            var releases = new List<object>();
            if (root is object[]) releases.AddRange((object[])root); else releases.Add(root);
            foreach (object r in releases)
            {
                var rel = r as Dictionary<string, object>;
                if (rel == null) continue;
                object draft;
                if (rel.TryGetValue("draft", out draft) && draft is bool && (bool)draft) continue;
                object tag, assets;
                Version v;
                if (!rel.TryGetValue("tag_name", out tag) || !TryParseVersion(tag as string, out v)) continue;
                if (version != null && v <= version) continue;
                if (!rel.TryGetValue("assets", out assets) || !(assets is object[])) continue;
                string u = null, h = null, hu = null;
                foreach (object a in (object[])assets)
                {
                    var asset = a as Dictionary<string, object>;
                    if (asset == null) continue;
                    object name, link, digest;
                    asset.TryGetValue("name", out name);
                    asset.TryGetValue("browser_download_url", out link);
                    asset.TryGetValue("digest", out digest);
                    string nm = (name as string ?? "").ToLowerInvariant();
                    if (nm == "kapsula.exe")
                    {
                        u = link as string;
                        string d = digest as string ?? "";
                        if (d.StartsWith("sha256:")) h = d.Substring(7).Trim().ToLowerInvariant();
                    }
                    else if (nm == "kapsula.exe.sha256") hu = link as string;
                }
                if (u == null) continue;
                version = v; url = u; sha = h; shaUrl = hu;
            }
            return version != null;
        }

        // Запасной путь без списка релизов: номер версии из version.txt, файлы — по известным адресам релиза.
        static bool FindDirect(out Version v, out string url, out string shaUrl)
        {
            url = null; shaUrl = null;
            string text = System.Text.Encoding.ASCII.GetString(Get("https://raw.githubusercontent.com/" + Repo + "/main/version.txt"));
            if (!TryParseVersion(text, out v)) return false;
            string tag = "v" + v.Major + "." + v.Minor + "." + v.Build;
            url = "https://github.com/" + Repo + "/releases/download/" + tag + "/Kapsula.exe";
            shaUrl = url + ".sha256";
            return true;
        }

        static bool AllowedUrl(string url)
        {
            if (url == null) return false;
            if (FeedOverride != null) return url.StartsWith("http://127.0.0.1:");
            return url.StartsWith("https://github.com/" + Repo + "/releases/download/");
        }

        static byte[] Get(string url)
        {
            System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
            using (var wc = new System.Net.WebClient())
            {
                wc.Headers["User-Agent"] = "Kapsula-Updater";
                wc.Headers["Accept"] = url.Contains("api.github.com") ? "application/vnd.github+json" : "*/*";
                return wc.DownloadData(url);
            }
        }

        static string Sha256(byte[] data)
        {
            using (var s = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(s.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        // true — новая версия поставлена, проверять дальше незачем
        static bool Check()
        {
            string feed = FeedOverride ?? "https://api.github.com/repos/" + Repo + "/releases?per_page=10";
            if (FeedOverride != null && !feed.StartsWith("http://127.0.0.1:")) return false;
            Version v; string url, sha, shaUrl;
            try
            {
                if (SkipApi) throw new System.Net.WebException("список релизов отключён ключом --noapi");
                string json = System.Text.Encoding.UTF8.GetString(Get(feed));
                if (!PickRelease(json, out v, out url, out sha, out shaUrl)) return false;
            }
            catch (System.Net.WebException)
            {
                // GitHub ограничивает число запросов списка релизов с одного адреса (общий Wi-Fi, VPN, мобильная сеть),
                // а где-то этот адрес недоступен вовсе. Тогда узнаём версию по файлу version.txt в репозитории.
                if (FeedOverride != null) throw;
                if (!FindDirect(out v, out url, out shaUrl)) return false;
                sha = null;
            }
            if (v <= Current) return false;
            if (!AllowedUrl(url)) throw new Exception("неожиданный адрес файла: " + url);

            if (sha == null && shaUrl != null && AllowedUrl(shaUrl))
            {
                string text = System.Text.Encoding.ASCII.GetString(Get(shaUrl)).Trim();
                if (text.Length >= 64) sha = text.Substring(0, 64).ToLowerInvariant();
            }
            if (sha == null || sha.Length != 64) throw new Exception("у релиза " + v + " нет контрольной суммы, обновление пропущено");

            byte[] data = Get(url);
            if (data.Length < 50000 || data.Length > 100 * 1024 * 1024 || data[0] != 'M' || data[1] != 'Z') throw new Exception("скачанный файл не похож на программу");
            if (Sha256(data) != sha) throw new Exception("контрольная сумма не совпала, обновление отброшено");

            string exe = ExePath;
            string fresh = System.IO.Path.ChangeExtension(exe, ".new.exe");
            string old = System.IO.Path.ChangeExtension(exe, ".old.exe");
            System.IO.File.WriteAllBytes(fresh, data);
            // Версия внутри файла должна быть новее нашей: иначе мы бы «обновлялись» по кругу.
            Version inside = System.Reflection.AssemblyName.GetAssemblyName(fresh).Version;
            if (inside <= Current) { System.IO.File.Delete(fresh); throw new Exception("в релизе " + v + " лежит файл версии " + inside); }

            // Работающую программу нельзя перезаписать, но можно переименовать — так и подменяем.
            if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
            System.IO.File.Move(exe, old);
            try { System.IO.File.Move(fresh, exe); }
            catch { System.IO.File.Move(old, exe); throw; }

            installed = true;
            string text2 = inside.Major + "." + inside.Minor + "." + inside.Build;
            if (island != null) island.Dispatcher.BeginInvoke(new Action(delegate { island.UpdateInstalled(text2); }));
            return true;
        }

        public static void Restart()
        {
            try { System.Diagnostics.Process.Start(ExePath, "--wait --updated"); } catch (Exception ex) { Store.LogError("перезапуск", ex); return; }
            System.Windows.Application.Current.Shutdown();
        }
    }
}
