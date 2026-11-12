// Kapsula — «остров» для Windows. Часть исходного кода: Devices.cs
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
    class BtDevice { public string Name; public bool Connected; public List<string> KsIds = new List<string>(); }

    // Bluetooth-наушники и колонки: Windows видит их как звуковые устройства и умеет подключать по запросу.
    static class BtAudio
    {
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);
        static PROPERTYKEY IfaceName = new PROPERTYKEY { fmtid = new Guid("026E516E-B814-414B-83CD-856D6FEF4822"), pid = 2 };
        static PROPERTYKEY DevName = new PROPERTYKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
        static readonly Guid BtAudioSet = new Guid("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");

        static string Prop(IMMDevice d, PROPERTYKEY key)
        {
            IPropertyStore ps;
            if (d.OpenPropertyStore(0, out ps) != 0 || ps == null) return null;
            PROPVARIANT v;
            if (ps.GetValue(ref key, out v) != 0) return null;
            try { return v.vt == 31 && v.p1 != IntPtr.Zero ? Marshal.PtrToStringUni(v.p1) : null; }
            finally { PropVariantClear(ref v); }
        }

        static string Clean(string name)
        {
            foreach (string suffix in new string[] { " Hands-Free AG Audio", " Hands-Free AG", " Hands-Free", " Stereo", " Avrcp Transport", " - Find My" })
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - suffix.Length);
            return name.Trim();
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        public static List<BtDevice> List()
        {
            var res = new List<BtDevice>();
            try
            {
                var en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorCom());
                IMMDeviceCollection col;
                if (en.EnumAudioEndpoints(2, 0xF, out col) != 0 || col == null) return res;
                uint n;
                col.GetCount(out n);
                for (uint i = 0; i < n; i++)
                {
                    IMMDevice d;
                    if (col.Item(i, out d) != 0 || d == null) continue;
                    Guid iid = typeof(IDeviceTopology).GUID;
                    object o;
                    if (d.Activate(ref iid, 23, IntPtr.Zero, out o) != 0 || o == null) continue;
                    IConnector c;
                    if (((IDeviceTopology)o).GetConnector(0, out c) != 0 || c == null) continue;
                    string ksId;
                    if (c.GetDeviceIdConnectedTo(out ksId) != 0 || ksId == null) continue;
                    if (ksId.IndexOf("BTH", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int state;
                    d.GetState(out state);
                    string name = Clean(Prop(d, IfaceName) ?? Prop(d, DevName) ?? "Bluetooth");
                    BtDevice dev = res.Find(delegate(BtDevice b) { return b.Name == name; });
                    if (dev == null) { dev = new BtDevice { Name = name }; res.Add(dev); }
                    if (state == 1) dev.Connected = true;
                    if (!dev.KsIds.Contains(ksId)) dev.KsIds.Add(ksId);
                }
            }
            catch (Exception ex) { Store.LogError("Bluetooth: список устройств", ex); }
            return res;
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        public static bool SetConnected(BtDevice dev, bool connect)
        {
            bool ok = false;
            try
            {
                var en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorCom());
                foreach (string id in dev.KsIds)
                {
                    IMMDevice kd;
                    if (en.GetDevice(id, out kd) != 0 || kd == null) continue;
                    Guid iid = typeof(IKsControl).GUID;
                    object o;
                    if (kd.Activate(ref iid, 23, IntPtr.Zero, out o) != 0 || o == null) continue;
                    var p = new KSPROPERTY { Set = BtAudioSet, Id = connect ? 0u : 1u, Flags = 1 };
                    int ret;
                    if (((IKsControl)o).KsProperty(ref p, Marshal.SizeOf(typeof(KSPROPERTY)), IntPtr.Zero, 0, out ret) == 0) ok = true;
                }
            }
            catch (Exception ex) { Store.LogError("Bluetooth: подключение " + dev.Name, ex); }
            return ok;
        }
    }

    // Радиомодули Windows (Bluetooth, Wi-Fi): состояние и включение
    static class Radios
    {
        static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            int n = 0;
            while (op.Status == Windows.Foundation.AsyncStatus.Started && n++ < 400) Thread.Sleep(10);
            return op.Status == Windows.Foundation.AsyncStatus.Completed ? op.GetResults() : default(T);
        }

        static Windows.Devices.Radios.Radio Find(Windows.Devices.Radios.RadioKind kind)
        {
            var list = Wait(Windows.Devices.Radios.Radio.GetRadiosAsync());
            if (list == null) return null;
            foreach (var r in list) if (r.Kind == kind) return r;
            return null;
        }

        // null — такого радиомодуля нет
        public static bool? Get(bool wifi)
        {
            try
            {
                var r = Find(wifi ? Windows.Devices.Radios.RadioKind.WiFi : Windows.Devices.Radios.RadioKind.Bluetooth);
                if (r == null) return null;
                return r.State == Windows.Devices.Radios.RadioState.On;
            }
            catch { return null; }
        }

        public static bool Set(bool wifi, bool on)
        {
            try
            {
                var r = Find(wifi ? Windows.Devices.Radios.RadioKind.WiFi : Windows.Devices.Radios.RadioKind.Bluetooth);
                if (r == null) return false;
                Wait(Windows.Devices.Radios.Radio.RequestAccessAsync());
                return Wait(r.SetStateAsync(on ? Windows.Devices.Radios.RadioState.On : Windows.Devices.Radios.RadioState.Off)) == Windows.Devices.Radios.RadioAccessStatus.Allowed;
            }
            catch { return false; }
        }
    }

    // VPN: Cloudflare WARP через его собственную утилиту командной строки
    static class Warp
    {
        static readonly string Cli = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Cloudflare\Cloudflare WARP\warp-cli.exe");
        public static bool Exists { get { return System.IO.File.Exists(Cli); } }

        static string Run(string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(Cli, args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    string s = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(6000);
                    return s;
                }
            }
            catch { return ""; }
        }

        public static bool? Status()
        {
            if (!Exists) return null;
            string s = Run("status");
            return s.IndexOf("Connected", StringComparison.OrdinalIgnoreCase) >= 0 && s.IndexOf("Disconnected", StringComparison.OrdinalIgnoreCase) < 0;
        }

        public static void Set(bool on) { Run(on ? "connect" : "disconnect"); }
    }

    class ControlState
    {
        public bool NetUp, HasWifi;
        public string NetName = "Сеть";
        public bool? WifiOn, BtOn, VpnOn;
        public List<BtDevice> Bt = new List<BtDevice>();
        public Dictionary<string, int> BtBattery = new Dictionary<string, int>();   // заряд по имени устройства, если Windows его знает
    }
}
