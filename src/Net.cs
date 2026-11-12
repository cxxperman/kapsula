// Kapsula — «остров» для Windows. Часть исходного кода: Net.cs
// Сети Wi-Fi (через встроенную в Windows утилиту netsh) и выбор устройства вывода звука.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Kapsula
{
    class WifiNet { public string Ssid = ""; public int Signal; public bool Secure, Connected, Known; }

    // Wi-Fi через netsh: список сетей, подключение с паролем и отключение. Пароль уходит только в netsh —
    // он сохраняет его в профиль Windows, как если бы сеть подключили из её собственного меню.
    static class Wifi
    {
        public static string Error;   // почему списка нет: нет адаптера, нет разрешения на местоположение и т. п.
        public static bool NeedsLocation;

        static string Netsh(string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("netsh", args)
                {
                    CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    string s = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(8000);
                    return s;
                }
            }
            catch { return ""; }
        }

        // «  Имя  : значение» — значение после первого двоеточия
        static string Val(string line)
        {
            int i = line.IndexOf(':');
            return i < 0 ? "" : line.Substring(i + 1).Trim();
        }

        static string Key(string line)
        {
            int i = line.IndexOf(':');
            return (i < 0 ? line : line.Substring(0, i)).Trim();
        }

        // Разбор вывода «netsh wlan show networks mode=bssid». Вывод на языке Windows, поэтому опираемся не на слова,
        // а на форму: «SSID 1 : имя», процент сигнала, тип защиты (Open/Открыт… — без пароля).
        public static List<WifiNet> ParseNetworks(string text)
        {
            var list = new List<WifiNet>();
            WifiNet cur = null;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                string k = Key(line);
                if (k.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !k.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                {
                    cur = new WifiNet { Ssid = Val(line), Secure = true };
                    if (cur.Ssid.Length > 0) list.Add(cur); else cur = null;
                    continue;
                }
                if (cur == null) continue;
                string v = Val(line);
                if (v.EndsWith("%"))
                {
                    int sig;
                    if (int.TryParse(v.TrimEnd('%').Trim(), out sig) && sig > cur.Signal) cur.Signal = sig;
                }
                else if (raw.StartsWith("    ") && !raw.StartsWith("        ") && (v.Equals("Open", StringComparison.OrdinalIgnoreCase) || v.StartsWith("Откр", StringComparison.OrdinalIgnoreCase)))
                    cur.Secure = false;
            }
            list.Sort(delegate(WifiNet a, WifiNet b) { return b.Signal.CompareTo(a.Signal); });
            return list;
        }

        // Имя сети, к которой подключён компьютер, из «netsh wlan show interfaces»
        public static string ParseCurrent(string text)
        {
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string k = Key(raw);
                if (k.Equals("SSID", StringComparison.OrdinalIgnoreCase)) { string v = Val(raw); if (v.Length > 0) return v; }
            }
            return null;
        }

        public static List<string> ParseProfiles(string text)
        {
            var list = new List<string>();
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.IndexOf(':') < 0) continue;
                string v = Val(raw), k = Key(raw);
                if (v.Length > 0 && k.Length > 4 && raw.StartsWith("    ")) list.Add(v);
            }
            return list;
        }

        public static List<WifiNet> List()
        {
            Error = null; NeedsLocation = false;
            string nets = Netsh("wlan show networks mode=bssid");
            if (nets.IndexOf("privacy-location", StringComparison.OrdinalIgnoreCase) >= 0 || nets.IndexOf("местополож", StringComparison.OrdinalIgnoreCase) >= 0 || nets.IndexOf("location", StringComparison.OrdinalIgnoreCase) >= 0 && nets.IndexOf("SSID", StringComparison.Ordinal) < 0)
            {
                NeedsLocation = true;
                Error = "Windows показывает список сетей только программам с доступом к местоположению.";
                return new List<WifiNet>();
            }
            var list = ParseNetworks(nets);
            if (list.Count == 0 && nets.IndexOf("SSID", StringComparison.Ordinal) < 0)
                Error = "Сети Wi-Fi не найдены. Если адаптер выключен — включите его переключателем выше.";
            string cur = ParseCurrent(Netsh("wlan show interfaces"));
            List<string> known = ParseProfiles(Netsh("wlan show profiles"));
            foreach (WifiNet n in list)
            {
                n.Connected = cur != null && n.Ssid == cur;
                n.Known = known.Contains(n.Ssid);
            }
            return list;
        }

        static string Xml(string s) { return System.Security.SecurityElement.Escape(s); }

        // Подключиться. Пароль нужен, только если сеть защищена и Windows её ещё не знает.
        public static bool Connect(WifiNet n, string password)
        {
            if (!n.Known || !string.IsNullOrEmpty(password))
            {
                string auth = n.Secure ? "WPA2PSK" : "open", enc = n.Secure ? "AES" : "none";
                string key = n.Secure ? "<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>" + Xml(password ?? "") + "</keyMaterial></sharedKey>" : "";
                string xml = "<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>" + Xml(n.Ssid) + "</name>" +
                    "<SSIDConfig><SSID><name>" + Xml(n.Ssid) + "</name></SSID></SSIDConfig><connectionType>ESS</connectionType><connectionMode>auto</connectionMode>" +
                    "<MSM><security><authEncryption><authentication>" + auth + "</authentication><encryption>" + enc + "</encryption><useOneX>false</useOneX></authEncryption>" + key + "</security></MSM></WLANProfile>";
                // файл с паролем живёт доли секунды: netsh читает его и сразу удаляется
                string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kapsula-wifi-" + Guid.NewGuid().ToString("N") + ".xml");
                try
                {
                    System.IO.File.WriteAllText(file, xml, new UTF8Encoding(false));
                    Netsh("wlan add profile filename=\"" + file + "\" user=current");
                }
                finally { try { System.IO.File.Delete(file); } catch { } }
            }
            Netsh("wlan connect name=\"" + n.Ssid.Replace("\"", "") + "\" ssid=\"" + n.Ssid.Replace("\"", "") + "\"");
            for (int i = 0; i < 20; i++)
            {
                System.Threading.Thread.Sleep(500);
                if (ParseCurrent(Netsh("wlan show interfaces")) == n.Ssid) return true;
            }
            return false;
        }

        public static void Disconnect() { Netsh("wlan disconnect"); }
    }

    // ---- устройства вывода звука и выбор основного
    class AudioOutDevice { public string Id, Name; public bool Default; }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr f);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int def, IntPtr f);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr a, IntPtr b);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int def, IntPtr a, IntPtr b);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr a);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr a);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr a);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr v);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr v);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class PolicyConfigClient { }

    static class AudioOut
    {
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);
        static PROPERTYKEY FriendlyName = new PROPERTYKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };

        static string Name(IMMDevice d)
        {
            IPropertyStore ps;
            if (d.OpenPropertyStore(0, out ps) != 0 || ps == null) return null;
            PROPVARIANT v;
            PROPERTYKEY k = FriendlyName;
            if (ps.GetValue(ref k, out v) != 0) return null;
            try { return v.vt == 31 && v.p1 != IntPtr.Zero ? Marshal.PtrToStringUni(v.p1) : null; }
            finally { PropVariantClear(ref v); }
        }

        // Включённые устройства вывода; основное отмечено
        public static List<AudioOutDevice> List() { return List(0); }

        // flow 0 — вывод (динамики), 1 — ввод (микрофоны)
        public static List<AudioOutDevice> List(int flow)
        {
            var res = new List<AudioOutDevice>();
            try
            {
                var en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorCom());
                string defId = null;
                IMMDevice def;
                if (en.GetDefaultAudioEndpoint(flow, 1, out def) == 0 && def != null) def.GetId(out defId);
                IMMDeviceCollection col;
                if (en.EnumAudioEndpoints(flow, 1, out col) != 0 || col == null) return res;
                uint n;
                col.GetCount(out n);
                for (uint i = 0; i < n; i++)
                {
                    IMMDevice d;
                    if (col.Item(i, out d) != 0 || d == null) continue;
                    string id;
                    if (d.GetId(out id) != 0 || id == null) continue;
                    res.Add(new AudioOutDevice { Id = id, Name = Name(d) ?? "Устройство", Default = id == defId });
                }
            }
            catch (Exception ex) { Store.LogError("Звук: список устройств", ex); }
            return res;
        }

        // Сделать устройство основным — для всего: музыки, фильмов и звонков
        public static bool SetDefault(string id)
        {
            try
            {
                var pc = (IPolicyConfig)(new PolicyConfigClient());
                bool ok = true;
                for (int role = 0; role < 3; role++) ok &= pc.SetDefaultEndpoint(id, role) == 0;
                return ok;
            }
            catch (Exception ex) { Store.LogError("Звук: выбор устройства", ex); return false; }
        }

        // «Динамики (Realtek(R) Audio)» → «Динамики»
        public static string Short(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Звук";
            int i = name.IndexOf(" (");
            string s = i > 0 ? name.Substring(0, i) : name;
            return s.Length > 18 ? s.Substring(0, 17) + "…" : s;
        }
    }
}
