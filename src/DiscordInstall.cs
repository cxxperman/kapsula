// Kapsula — «остров» для Windows. Часть исходного кода: DiscordInstall.cs
// «Подключить Discord» одной кнопкой: ставит в Discord нашу сборку Vencord — это официальный Vencord плюс плагин
// Kapsula (github.com/cxxperman/kapsula-vencord; она сама собирается из каждого обновления Vencord). Если Vencord
// уже стоит, меняются только его файлы, а прежние сохраняются рядом — «Отключить» возвращает их на место. Если
// Vencord нет, Kapsula делает то же, что его установщик. Ваши плагины и настройки Vencord не трогаются. Заработает
// после перезапуска Discord — сам Discord Kapsula не перезапускает.
using System;
using System.Collections.Generic;
using System.IO;

namespace Kapsula
{
    static class DiscordInstall
    {
        public const string Repo = "cxxperman/kapsula-vencord";
        static readonly string[] Files = { "patcher.js", "preload.js", "renderer.js", "renderer.css" };

        // Для проверок (tests.ps1): поддельные папки AppData и готовая сборка из папки вместо скачивания
        static string RoamingRoot { get { return Environment.GetEnvironmentVariable("KAPSULA_TEST_APPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        static string LocalRoot { get { return Environment.GetEnvironmentVariable("KAPSULA_TEST_LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        static string Roaming(string name) { return Path.Combine(RoamingRoot, name); }
        static string Dist { get { return Path.Combine(Roaming("Vencord"), "dist"); } }
        static string Backup { get { return Path.Combine(Roaming("Vencord"), "dist-before-kapsula"); } }
        static string Marker { get { return Path.Combine(Dist, "kapsula.txt"); } }

        public static bool Installed { get { return File.Exists(Marker); } }

        // Наш плагин действительно в сборке: официальный установщик Vencord («Repair») мог вернуть свою
        public static bool PluginInBuild
        {
            get
            {
                try { string f = Path.Combine(Dist, "renderer.js"); return File.Exists(f) && File.ReadAllText(f).Contains("\"Kapsula\""); }
                catch { return false; }
            }
        }

        // Папки resources у установленных Discord (обычный, PTB, Canary) — у самой новой версии каждого
        static List<string> DiscordResources()
        {
            var list = new List<string>();
            string local = LocalRoot;
            foreach (string name in new[] { "Discord", "DiscordPTB", "DiscordCanary" })
            {
                string root = Path.Combine(local, name);
                if (!Directory.Exists(root)) continue;
                string best = null; Version bestV = null;
                foreach (string d in Directory.GetDirectories(root, "app-*"))
                {
                    Version v;
                    if (!Version.TryParse(Path.GetFileName(d).Substring(4), out v)) continue;
                    if (bestV == null || v > bestV) { bestV = v; best = d; }
                }
                if (best != null && Directory.Exists(Path.Combine(best, "resources"))) list.Add(Path.Combine(best, "resources"));
            }
            return list;
        }

        static byte[] Get(string url)
        {
            System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
            using (var wc = new System.Net.WebClient())
            {
                wc.Headers["User-Agent"] = "Kapsula";
                return wc.DownloadData(url);
            }
        }

        static string JsString(string s) { return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }

        // null — получилось; иначе — что пошло не так (для настроек)
        public static string Install()
        {
            if (Directory.Exists(Roaming("BetterDiscord"))) return "у вас стоит BetterDiscord — вместе с Vencord он не уживается";
            List<string> res = DiscordResources();
            if (res.Count == 0) return "Discord не найден";
            // 1. Сначала скачать сборку целиком — и только потом что-то менять
            var data = new Dictionary<string, byte[]>();
            foreach (string f in Files)
            {
                byte[] b;
                string testDir = Environment.GetEnvironmentVariable("KAPSULA_TEST_VENCORD_DIR");
                try { b = testDir != null ? File.ReadAllBytes(Path.Combine(testDir, f)) : Get("https://github.com/" + Repo + "/releases/download/devbuild/" + f); }
                catch (Exception ex) { return "не удалось скачать сборку (" + ex.Message + ")"; }
                if (b == null || b.Length < 100) return "скачалась пустая сборка";
                data[f] = b;
            }
            try
            {
                // 2. Прежнюю сборку Vencord — в сторонку. Один раз: повторное подключение резерв не перезаписывает
                Directory.CreateDirectory(Dist);
                bool hadVencord = false;
                foreach (string f in Files) if (File.Exists(Path.Combine(Dist, f))) hadVencord = true;
                if (hadVencord && !Installed && !Directory.Exists(Backup))
                {
                    Directory.CreateDirectory(Backup);
                    foreach (string file in Directory.GetFiles(Dist)) File.Copy(file, Path.Combine(Backup, Path.GetFileName(file)), true);
                }
                bool ours = !hadVencord || Installed && File.ReadAllText(Marker).Contains("vencord=kapsula");
                // 3. Наша сборка
                foreach (var kv in data) File.WriteAllBytes(Path.Combine(Dist, kv.Key), kv.Value);
                // 4. Discord без Vencord — встроить так же, как его установщик: исходный app.asar → _app.asar,
                //    а на его место — папка app, которая загружает Vencord из его папки
                var injected = new List<string>();
                foreach (string r in res)
                {
                    if (File.Exists(Path.Combine(r, "_app.asar"))) continue;   // уже со встроенным Vencord
                    string asar = Path.Combine(r, "app.asar");
                    if (!File.Exists(asar)) continue;
                    try { File.Move(asar, Path.Combine(r, "_app.asar")); }
                    catch (IOException) { return "закройте Discord (и в трее у часов) и нажмите ещё раз"; }
                    string app = Path.Combine(r, "app");
                    Directory.CreateDirectory(app);
                    File.WriteAllText(Path.Combine(app, "index.js"), "require(" + JsString(Path.Combine(Dist, "patcher.js")) + ");\n");
                    File.WriteAllText(Path.Combine(app, "package.json"), "{\"name\":\"discord\",\"main\":\"index.js\"}\n");
                    injected.Add(r);
                }
                string old = Installed ? File.ReadAllText(Marker) : "";
                var sb = new System.Text.StringBuilder();
                sb.Append("Kapsula · ").Append(DateTime.Now.ToString("s")).Append("\r\n");
                sb.Append(ours ? "vencord=kapsula\r\n" : "vencord=before\r\n");
                foreach (string line in old.Split('\n')) if (line.StartsWith("injected=")) sb.Append(line.TrimEnd('\r')).Append("\r\n");
                foreach (string r in injected) sb.Append("injected=").Append(r).Append("\r\n");
                File.WriteAllText(Marker, sb.ToString());
                return null;
            }
            catch (Exception ex) { Store.LogError("подключение Discord", ex); return ex.Message; }
        }

        // Вернуть всё как было: прежнюю сборку Vencord (или убрать Vencord, если его ставили мы)
        public static string Uninstall()
        {
            if (!Installed) return null;
            try
            {
                string[] lines = File.ReadAllLines(Marker);
                if (Directory.Exists(Backup))
                {
                    foreach (string f in Files) { string p = Path.Combine(Dist, f); if (File.Exists(p)) File.Delete(p); }
                    foreach (string file in Directory.GetFiles(Backup)) File.Copy(file, Path.Combine(Dist, Path.GetFileName(file)), true);
                    Directory.Delete(Backup, true);
                }
                else foreach (string f in Files) { string p = Path.Combine(Dist, f); if (File.Exists(p)) File.Delete(p); }
                foreach (string line in lines)
                {
                    if (!line.StartsWith("injected=")) continue;
                    string r = line.Substring(9).Trim();
                    string app = Path.Combine(r, "app"), orig = Path.Combine(r, "_app.asar"), asar = Path.Combine(r, "app.asar");
                    try
                    {
                        if (Directory.Exists(app)) Directory.Delete(app, true);
                        if (File.Exists(orig) && !File.Exists(asar)) File.Move(orig, asar);
                    }
                    catch (IOException) { return "закройте Discord (и в трее у часов) и нажмите ещё раз"; }
                }
                File.Delete(Marker);
                return null;
            }
            catch (Exception ex) { Store.LogError("отключение Discord", ex); return ex.Message; }
        }
    }
}
