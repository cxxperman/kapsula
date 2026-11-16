// Kapsula — «остров» для Windows. Часть исходного кода: DiscordInstall.cs
// «Подключить Discord» одной кнопкой: ставит в Discord нашу сборку Vencord — это официальный Vencord плюс плагин
// Kapsula (github.com/cxxperman/kapsula-vencord; она сама собирается из каждого обновления Vencord). Если Vencord
// уже стоит, меняются только его файлы, а прежние сохраняются рядом — «Отключить» возвращает их на место. Если
// Vencord нет, Kapsula делает то же, что его установщик. Ваши плагины и настройки Vencord не трогаются. Пока Discord
// запущен, ничего не меняется — Kapsula просит его закрыть; заработает при следующем запуске Discord (сам Discord
// Kapsula не закрывает и не запускает).
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
        public static bool PluginInBuild { get { return OurBuild(Dist); } }

        // В этой папке — наша сборка Vencord (с плагином Kapsula), а не официальная
        static bool OurBuild(string dir)
        {
            try { string f = Path.Combine(dir, "renderer.js"); return File.Exists(f) && File.ReadAllText(f).Contains("\"Kapsula\""); }
            catch { return false; }
        }

        // Discord запущен (хоть в трее у часов): свой app.asar он держит открытым — встроиться не выйдет.
        // На поддельных папках проверок (tests.ps1) настоящий Discord ни при чём
        static bool DiscordRunning()
        {
            if (Environment.GetEnvironmentVariable("KAPSULA_TEST_LOCALAPPDATA") != null) return false;
            try
            {
                foreach (string name in new[] { "Discord", "DiscordPTB", "DiscordCanary" })
                {
                    System.Diagnostics.Process[] ps = System.Diagnostics.Process.GetProcessesByName(name);
                    foreach (System.Diagnostics.Process p in ps) p.Dispose();
                    if (ps.Length > 0) return true;
                }
            }
            catch { }
            return false;
        }

        // Ответ, когда Discord открыт: настройки по нему предлагают «Закрыть Discord и подключить»
        public const string NeedClose = "закройте Discord (и в трее у часов) и нажмите ещё раз";
        public static bool Running() { return DiscordRunning(); }

        // Закрыть Discord (и в трее у часов) — только по кнопке «Закрыть Discord и …» в настройках, которую нажал сам
        // человек. Возвращает, какие из Discord были открыты: их потом запускаем снова (StartDiscord)
        public static List<string> CloseDiscord()
        {
            var was = new List<string>();
            if (Environment.GetEnvironmentVariable("KAPSULA_TEST_LOCALAPPDATA") != null) return was;
            foreach (string name in new[] { "Discord", "DiscordPTB", "DiscordCanary" })
            {
                System.Diagnostics.Process[] ps;
                try { ps = System.Diagnostics.Process.GetProcessesByName(name); } catch { continue; }
                if (ps.Length > 0) was.Add(name);
                foreach (System.Diagnostics.Process p in ps)
                {
                    try { p.Kill(); p.WaitForExit(5000); } catch { }
                    finally { p.Dispose(); }
                }
            }
            if (was.Count > 0) System.Threading.Thread.Sleep(1000);   // app.asar отпускается не сразу после выхода
            return was;
        }

        // Запустить снова те Discord, что закрыли, — так же, как их ярлык: Update.exe --processStart
        public static void StartDiscord(List<string> names)
        {
            if (names == null) return;
            foreach (string name in names)
            {
                try
                {
                    string upd = Path.Combine(Path.Combine(LocalRoot, name), "Update.exe");
                    if (File.Exists(upd)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(upd, "--processStart " + name + ".exe") { UseShellExecute = false });
                }
                catch (Exception ex) { Store.LogError("запуск Discord после подключения", ex); }
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

        // Все версии Discord (обычный, PTB, Canary): Vencord сам переносит заглушку в новую версию после обновления
        static List<string> AllResources()
        {
            var list = new List<string>();
            foreach (string name in new[] { "Discord", "DiscordPTB", "DiscordCanary" })
            {
                string root = Path.Combine(LocalRoot, name);
                if (!Directory.Exists(root)) continue;
                foreach (string d in Directory.GetDirectories(root, "app-*"))
                    if (Directory.Exists(Path.Combine(d, "resources"))) list.Add(Path.Combine(d, "resources"));
            }
            return list;
        }

        // Заглушка app.asar — точно как у установщика Vencord: крошечный архив asar, где index.js загружает Vencord из
        // его папки. Настоящий Discord лежит рядом в _app.asar: Vencord находит его сам, а после обновления Discord сам
        // переносит заглушку в новую версию. (Формат asar: заголовок-«pickle» с JSON-оглавлением, потом файлы подряд.)
        public static byte[] StubAsar()
        {
            byte[] idx = System.Text.Encoding.UTF8.GetBytes("require(" + JsString(Path.Combine(Dist, "patcher.js")) + ")");
            byte[] pkg = System.Text.Encoding.UTF8.GetBytes("{\n\t\"name\": \"discord\",\n\t\"main\": \"index.js\"\n}");
            byte[] json = System.Text.Encoding.UTF8.GetBytes("{\"files\":{\"index.js\":{\"size\":" + idx.Length + ",\"offset\":\"0\"},\"package.json\":{\"size\":" + pkg.Length + ",\"offset\":\"" + idx.Length + "\"}}}");
            int pad = (4 - json.Length % 4) % 4;
            int payload = 4 + json.Length + pad;
            using (var ms = new MemoryStream())
            {
                var w = new BinaryWriter(ms);
                w.Write(4); w.Write(4 + payload); w.Write(payload); w.Write(json.Length);
                w.Write(json); w.Write(new byte[pad]); w.Write(idx); w.Write(pkg);
                w.Flush();
                return ms.ToArray();
            }
        }

        // app.asar — заглушка Vencord, а не сам Discord: маленький файл, который загружает patcher.js
        static bool IsStub(string asar)
        {
            try
            {
                var fi = new FileInfo(asar);
                return fi.Exists && fi.Length < 8192 && System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(asar)).Contains("patcher.js");
            }
            catch { return false; }
        }

        // Папка app с загрузчиком Vencord — так встраивала Kapsula 0.6.1–0.6.2. Рядом с переименованным _app.asar это
        // не работает: Vencord из папки app ищет исходный Discord в app.asar, а его там нет — Discord не запускался
        // («Cannot find module …\app.asar\package.json»). Узнаём только свою папку: index.js и package.json, и всё.
        static bool OldLoader(string app)
        {
            try
            {
                if (!Directory.Exists(app) || Directory.GetDirectories(app).Length > 0) return false;
                foreach (string f in Directory.GetFiles(app))
                {
                    string n = Path.GetFileName(f);
                    if (n != "index.js" && n != "package.json") return false;
                }
                string idx = Path.Combine(app, "index.js");
                return File.Exists(idx) && File.ReadAllText(idx).Contains("patcher.js");
            }
            catch { return false; }
        }

        // При запуске Kapsula: Discord, сломанный подключением из 0.6.1–0.6.2, — починить. Папку app убираем, на место
        // app.asar — заглушка (или сам Discord, если сборки Vencord уже нет). true — что-то починили
        public static bool RepairBroken()
        {
            if (!Installed) return false;
            bool done = false;
            bool haveVencord = File.Exists(Path.Combine(Dist, "patcher.js"));
            foreach (string r in AllResources())
            {
                string app = Path.Combine(r, "app"), orig = Path.Combine(r, "_app.asar"), asar = Path.Combine(r, "app.asar");
                if (!OldLoader(app) || !File.Exists(orig) || File.Exists(asar)) continue;
                try
                {
                    Directory.Delete(app, true);
                    if (haveVencord) File.WriteAllBytes(asar, StubAsar()); else File.Move(orig, asar);
                    done = true;
                }
                catch (Exception ex) { Store.LogError("починка подключения Discord", ex); }
            }
            return done;
        }

        // null — получилось; иначе — что пошло не так (для настроек). Всё или ничего: не вышло на полпути — всё
        // возвращается как было, иначе следующая попытка приняла бы нашу же сборку за прежний Vencord
        public static string Install()
        {
            if (Directory.Exists(Roaming("BetterDiscord"))) return "у вас стоит BetterDiscord — вместе с Vencord он не уживается";
            List<string> res = DiscordResources();
            if (res.Count == 0) return "Discord не найден";
            if (DiscordRunning()) return NeedClose;
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
            // Что было до нас: файлы сборки в dist и папки — чтобы откатить, если что-то не выйдет
            var before = new Dictionary<string, byte[]>();
            var injected = new List<string>();
            bool vencordMade = !Directory.Exists(Roaming("Vencord")), distMade = !Directory.Exists(Dist), backupMade = false, wrote = false;
            Action undo = delegate
            {
                // Discord — как при «Отключить». Не вышло — сборку не трогаем: без неё заглушка не запустит Discord
                foreach (string r in injected)
                {
                    string asar = Path.Combine(r, "app.asar"), orig = Path.Combine(r, "_app.asar");
                    try
                    {
                        if (File.Exists(orig) && (!File.Exists(asar) || IsStub(asar)))
                        {
                            if (File.Exists(asar)) File.Delete(asar);
                            File.Move(orig, asar);
                        }
                    }
                    catch (Exception ex) { Store.LogError("откат подключения Discord", ex); return; }
                }
                try
                {
                    if (wrote)
                        foreach (string f in Files)
                        {
                            string p = Path.Combine(Dist, f);
                            byte[] prev;
                            if (before.TryGetValue(f, out prev)) File.WriteAllBytes(p, prev);
                            else if (File.Exists(p)) File.Delete(p);
                        }
                    if (backupMade) Directory.Delete(Backup, true);
                }
                catch (Exception ex) { Store.LogError("откат подключения Discord", ex); return; }
                // пустые папки, которые создали сами: иначе Kapsula решила бы, что Vencord стоит
                try { if (distMade) Directory.Delete(Dist); if (vencordMade) Directory.Delete(Roaming("Vencord")); } catch { }
            };
            try
            {
                // 2. Прежнюю сборку Vencord — в сторонку. Один раз: повторное подключение резерв не перезаписывает
                Directory.CreateDirectory(Dist);
                bool hadVencord = false;
                foreach (string f in Files)
                {
                    string p = Path.Combine(Dist, f);
                    if (File.Exists(p)) { hadVencord = true; before[f] = File.ReadAllBytes(p); }
                }
                // Наша же сборка без метки — остаток неудачной попытки прежней версии Kapsula, а не прежний Vencord
                if (hadVencord && !Installed && !Directory.Exists(Backup) && PluginInBuild) hadVencord = false;
                if (hadVencord && !Installed && !Directory.Exists(Backup))
                {
                    Directory.CreateDirectory(Backup);
                    backupMade = true;
                    foreach (string file in Directory.GetFiles(Dist)) File.Copy(file, Path.Combine(Backup, Path.GetFileName(file)), true);
                }
                bool ours = !hadVencord || Installed && File.ReadAllText(Marker).Contains("vencord=kapsula");
                // 3. Наша сборка
                wrote = true;
                foreach (var kv in data) File.WriteAllBytes(Path.Combine(Dist, kv.Key), kv.Value);
                // 4. Discord без Vencord — встроить так же, как его установщик: исходный app.asar → _app.asar,
                //    а на его место — заглушка app.asar, которая загружает Vencord из его папки
                foreach (string r in res)
                {
                    string asar = Path.Combine(r, "app.asar"), orig = Path.Combine(r, "_app.asar"), app = Path.Combine(r, "app");
                    try
                    {
                        if (OldLoader(app)) Directory.Delete(app, true);   // так встраивала 0.6.1–0.6.2 — с этим Discord не запускался
                        if (File.Exists(orig))
                        {
                            // Vencord уже встроен; заглушки нет (после 0.6.1–0.6.2) — поставить
                            if (!File.Exists(asar)) { File.WriteAllBytes(asar, StubAsar()); injected.Add(r); }
                            continue;
                        }
                        if (!File.Exists(asar)) continue;
                        File.Move(asar, orig);
                        try { File.WriteAllBytes(asar, StubAsar()); }
                        catch { File.Move(orig, asar); throw; }   // без заглушки Discord не запустится — вернуть как было
                        injected.Add(r);
                    }
                    catch (IOException) { undo(); return NeedClose; }
                    catch (UnauthorizedAccessException) { undo(); return NeedClose; }
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
            catch (Exception ex) { Store.LogError("подключение Discord", ex); undo(); return ex.Message; }
        }

        // Вернуть всё как было: прежнюю сборку Vencord (или убрать Vencord, если его ставили мы)
        public static string Uninstall()
        {
            if (!Installed) return null;
            try
            {
                string[] lines = File.ReadAllLines(Marker);
                // В резерве — наша же сборка: так бывало после неудачного первого «Подключить» в прежних версиях.
                // Прежнего Vencord тогда не было — убираем всё, как за собой
                bool fakeBackup = Directory.Exists(Backup) && OurBuild(Backup);
                bool ours = fakeBackup || Array.Exists(lines, l => l.Trim() == "vencord=kapsula");
                // 1. Сначала Discord — пока он запущен, ничего не меняем: иначе он остался бы без сборки, которую загружает.
                //    Где встраивали мы, вернуть исходный app.asar; если и Vencord ставили мы — то же во всех версиях
                //    Discord (Vencord сам переносит заглушку в новую версию после обновления)
                var places = new List<string>();
                foreach (string line in lines) if (line.StartsWith("injected=")) places.Add(line.Substring(9).Trim());
                if (ours) foreach (string r in AllResources()) if (!places.Contains(r)) places.Add(r);
                foreach (string r in places)
                {
                    string app = Path.Combine(r, "app"), orig = Path.Combine(r, "_app.asar"), asar = Path.Combine(r, "app.asar");
                    try
                    {
                        if (OldLoader(app)) Directory.Delete(app, true);
                        if (File.Exists(orig) && (!File.Exists(asar) || IsStub(asar)))
                        {
                            if (File.Exists(asar)) File.Delete(asar);
                            File.Move(orig, asar);
                        }
                    }
                    catch (IOException) { return NeedClose; }
                    catch (UnauthorizedAccessException) { return NeedClose; }
                }
                // 2. Сборка Vencord: прежняя — на место, иначе убрать нашу
                if (Directory.Exists(Backup) && !fakeBackup)
                {
                    foreach (string f in Files) { string p = Path.Combine(Dist, f); if (File.Exists(p)) File.Delete(p); }
                    foreach (string file in Directory.GetFiles(Backup)) File.Copy(file, Path.Combine(Dist, Path.GetFileName(file)), true);
                    Directory.Delete(Backup, true);
                }
                else
                {
                    foreach (string f in Files) { string p = Path.Combine(Dist, f); if (File.Exists(p)) File.Delete(p); }
                    if (fakeBackup) Directory.Delete(Backup, true);
                }
                File.Delete(Marker);
                return null;
            }
            catch (Exception ex) { Store.LogError("отключение Discord", ex); return ex.Message; }
        }
    }
}
