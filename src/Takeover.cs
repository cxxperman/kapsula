// Kapsula — «остров» для Windows. Часть исходного кода: Takeover.cs
// Новая версия, скачанная и запущенная из другой папки, сменяет старую:
// закрывает работающую старую Kapsula, перенаправляет автозапуск Windows на себя, а старый Kapsula.exe кладёт в Корзину.
// Раньше было наоборот: старая копия из первых версий оставалась в автозапуске, а новая молча закрывалась.
using System;
using System.Diagnostics;
using System.IO;

namespace Kapsula
{
    static class Takeover
    {
        static Version Mine { get { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version; } }
        static string MyPath { get { return System.Reflection.Assembly.GetExecutingAssembly().Location; } }

        static Version VersionOf(string exe)
        {
            try
            {
                var fv = FileVersionInfo.GetVersionInfo(exe);
                if (fv.ProductName != "Kapsula" && Path.GetFileNameWithoutExtension(exe).IndexOf("Kapsula", StringComparison.OrdinalIgnoreCase) < 0) return null;
                return new Version(fv.FileMajorPart, fv.FileMinorPart, fv.FileBuildPart, fv.FilePrivatePart);
            }
            catch { return null; }
        }

        static bool Same(string a, string b) { try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); } catch { return false; } }

        // Закрыть работающую Kapsula старее этой. true — закрыли хотя бы одну.
        public static bool CloseOlder()
        {
            bool any = false;
            int me = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcessesByName("Kapsula"))
            {
                try
                {
                    if (p.Id == me) continue;
                    string path = p.MainModule.FileName;
                    if (Same(path, MyPath)) continue;
                    Version v = VersionOf(path);
                    if (v == null || v >= Mine) continue;
                    p.Kill();
                    p.WaitForExit(4000);
                    any = true;
                }
                catch { }
                finally { p.Dispose(); }
            }
            return any;
        }

        // Автозапуск указывает на старую версию в другой папке — переводим его на себя, старый файл — в Корзину
        public static void FixAutostart()
        {
            try
            {
                string target = null;
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (k == null) return;
                    target = k.GetValue("Kapsula") as string;
                }
                if (string.IsNullOrEmpty(target)) return;
                string old = target.Trim().Trim('"');
                if (Same(old, MyPath)) return;
                Version v = File.Exists(old) ? VersionOf(old) : null;
                if (v != null && v >= Mine) return;   // там версия не старее — ничего не трогаем
                Autostart.Set(true);                  // теперь автозапуск — эта копия
                Store.LogError("автозапуск", new Exception("перенаправлен на новую версию " + Mine + (v != null ? " вместо " + v : "")));
                if (v != null && File.Exists(old) && Path.GetExtension(old).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(old, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        foreach (string extra in new string[] { Path.ChangeExtension(old, ".old.exe"), Path.ChangeExtension(old, ".new.exe") })
                            if (File.Exists(extra)) try { File.Delete(extra); } catch { }
                    }
                    catch (Exception ex) { Store.LogError("старая версия", ex); }
                }
            }
            catch (Exception ex) { Store.LogError("автозапуск", ex); }
        }
    }
}
