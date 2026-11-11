// Kapsula — «остров» для Windows. Часть исходного кода: Interop.cs
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
    // ================================================================ WinAPI
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }
        [StructLayout(LayoutKind.Sequential)] public struct FILETIME { public uint Low, High; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint thread);
        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        // Сколько миллисекунд не было ввода с клавиатуры и мыши
        public static int IdleMs()
        {
            var li = new LASTINPUTINFO();
            li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            return GetLastInputInfo(ref li) ? unchecked(Environment.TickCount - (int)li.dwTime) : 0;
        }
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, int flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr v);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);
        [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr dc, int index);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("powrprof.dll")] public static extern bool SetSuspendState(bool hibernate, bool force, bool wakeDisabled);

        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        public const long WS_CAPTION = 0x00C00000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        public const int WM_CLIPBOARDUPDATE = 0x031D, WM_HOTKEY = 0x0312;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public static void Key(byte vk, bool up) { keybd_event(vk, 0, up ? 2u : 0u, UIntPtr.Zero); }
        public static void MediaKey(byte vk) { keybd_event(vk, 0, 1, UIntPtr.Zero); keybd_event(vk, 0, 1 | 2, UIntPtr.Zero); }
        public static void PasteIntoActiveWindow() { Key(0x11, false); Key(0x56, false); Key(0x56, true); Key(0x11, true); }
        public static ulong ToU64(FILETIME f) { return ((ulong)f.High << 32) | f.Low; }

        public static int RefreshRate()
        {
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                int hz = GetDeviceCaps(dc, 116);
                ReleaseDC(IntPtr.Zero, dc);
                return hz >= 30 && hz <= 1000 ? hz : 60;
            }
            catch { return 60; }
        }
    }

    // ==================================================== Звук (Core Audio)
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p1, p2; }

    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    [Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
    }

    [Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IConnector
    {
        [PreserveSig] int GetType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector to);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector to);
        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KSPROPERTY { public Guid Set; public uint Id; public uint Flags; }

    [Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KSPROPERTY property, int propertyLength, IntPtr data, int dataLength, out int bytesReturned);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr p);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr p);
        [PreserveSig] int GetChannelCount(out int count);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    // ---- громкость отдельной программы (то же, что ползунки в «Микшере громкости» Windows)
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(IntPtr group, IntPtr context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint pid);
    }

    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute(bool mute, ref Guid context);
        [PreserveSig] int GetMute(out bool mute);
    }

    static class AppVolume
    {
        static List<ISimpleAudioVolume> cached = new List<ISimpleAudioVolume>();
        static string cachedFor;
        static int cachedAt;

        // Подходит ли процесс к названию плеера: «spotify» ↔ «SpotifyMusic», «chrome» ↔ «Chrome»
        public static bool Matches(string process, string player)
        {
            string p = (process ?? "").ToLowerInvariant(), k = (player ?? "").ToLowerInvariant();
            if (p.Length < 3 || k.Length < 3) return false;
            return p == k || p.Contains(k) || k.Contains(p);
        }

        static List<ISimpleAudioVolume> Find(string player)
        {
            int now = Environment.TickCount;
            if (cachedFor == player && unchecked(now - cachedAt) < 2500) return cached;
            var list = new List<ISimpleAudioVolume>();
            try
            {
                var en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorCom());
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) == 0 && dev != null)
                {
                    Guid iid = typeof(IAudioSessionManager2).GUID;
                    object o;
                    if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) == 0 && o != null)
                    {
                        IAudioSessionEnumerator sessions;
                        int n;
                        if (((IAudioSessionManager2)o).GetSessionEnumerator(out sessions) == 0 && sessions != null && sessions.GetCount(out n) == 0)
                            for (int i = 0; i < n; i++)
                            {
                                object s;
                                if (sessions.GetSession(i, out s) != 0 || s == null) continue;
                                uint pid;
                                var c2 = s as IAudioSessionControl2;
                                if (c2 == null || c2.GetProcessId(out pid) != 0 || pid == 0) continue;
                                string name;
                                try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { continue; }
                                var vol = s as ISimpleAudioVolume;
                                if (vol != null && Matches(name, player)) list.Add(vol);
                            }
                    }
                }
            }
            catch { }
            cached = list; cachedFor = player; cachedAt = now;
            return list;
        }

        // Громкость плеера от 0 до 1; -1 — у этого плеера нет своего звука в микшере
        public static double Get(string player)
        {
            try
            {
                double max = -1;
                foreach (var v in Find(player)) { float f; if (v.GetMasterVolume(out f) == 0 && f > max) max = f; }
                return max;
            }
            catch { cachedFor = null; return -1; }
        }

        public static bool Set(string player, double level)
        {
            try
            {
                bool any = false;
                Guid g = Guid.Empty;
                foreach (var v in Find(player)) { if (v.SetMasterVolume((float)Math.Max(0, Math.Min(1, level)), ref g) == 0) any = true; }
                return any;
            }
            catch { cachedFor = null; return false; }
        }
    }

    static class Audio
    {
        static IAudioEndpointVolume[] cache = new IAudioEndpointVolume[2];
        static int[] cachedAt = new int[2];
        static IAudioMeterInformation meter;
        static int meterAt;

        // Текущий пик громкости того, что играет на колонках (0..1). Звук не записывается.
        public static double Peak()
        {
            try
            {
                int now = Environment.TickCount;
                if (meter == null || unchecked(now - meterAt) > 5000)
                {
                    var en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorCom());
                    IMMDevice dev;
                    if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return 0;
                    Guid iid = typeof(IAudioMeterInformation).GUID;
                    object o;
                    if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return 0;
                    meter = o as IAudioMeterInformation;
                    meterAt = now;
                }
                float p;
                if (meter == null || meter.GetPeakValue(out p) != 0) { meter = null; return 0; }
                return p;
            }
            catch { meter = null; return 0; }
        }

        static IAudioEndpointVolume Endpoint(int flow)
        {
            int now = Environment.TickCount;
            if (cache[flow] != null && unchecked(now - cachedAt[flow]) < 5000) return cache[flow];
            cache[flow] = null;
            var en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorCom());
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(flow, flow == 0 ? 1 : 0, out dev) != 0 || dev == null) return null;
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            object o;
            if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return null;
            cache[flow] = o as IAudioEndpointVolume;
            cachedAt[flow] = now;
            return cache[flow];
        }

        public static double GetVolume()
        {
            try { var e = Endpoint(0); if (e == null) return -1; float v; if (e.GetMasterVolumeLevelScalar(out v) != 0) { cache[0] = null; return -1; } return v; }
            catch { cache[0] = null; return -1; }
        }

        public static void SetVolume(double v)
        {
            try { var e = Endpoint(0); if (e == null) return; Guid g = Guid.Empty; e.SetMasterVolumeLevelScalar((float)Math.Max(0, Math.Min(1, v)), ref g); }
            catch { cache[0] = null; }
        }

        public static bool MicMuted()
        {
            try { var e = Endpoint(1); if (e == null) return false; bool m; e.GetMute(out m); return m; }
            catch { cache[1] = null; return false; }
        }

        public static void SetMicMuted(bool m)
        {
            try { var e = Endpoint(1); if (e == null) return; Guid g = Guid.Empty; e.SetMute(m, ref g); }
            catch { cache[1] = null; }
        }
    }
}
