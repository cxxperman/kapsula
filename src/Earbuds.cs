// Kapsula — «остров» для Windows. Часть исходного кода: Earbuds.cs
// Заряд наушников Apple (AirPods, AirPods Pro, AirPods Max, Beats): они сами несколько раз в секунду сообщают его
// по Bluetooth всем устройствам рядом. Kapsula только слушает эти сообщения; к наушникам она не подключается,
// ничего им не отправляет и в сеть ничего не передаёт.
using System;
using System.Collections.Generic;
using Windows.Devices.Bluetooth.Advertisement;

namespace Kapsula
{
    class EarbudsState
    {
        public string Model = "";          // «AirPods Max», «AirPods Pro»…
        public int Left = -1, Right = -1;  // заряд в процентах, -1 — неизвестно
        public int Case = -1;              // футляр
        public int Single = -1;            // у накладных наушников одна батарея
        public bool Charging, InEar;
        public int Rssi;
        public int At;                     // когда пришло последнее сообщение

        // Самый низкий из известных зарядов: по нему решаем, пора ли предупреждать
        public int Lowest
        {
            get
            {
                int m = 101;
                foreach (int v in new int[] { Single, Left, Right }) if (v >= 0 && v < m) m = v;
                return m == 101 ? -1 : m;
            }
        }

        public string Text
        {
            get
            {
                if (Single >= 0) return Single + "%" + (Charging ? " · заряжаются" : "");
                var parts = new List<string>();
                if (Left >= 0) parts.Add("Л " + Left + "%");
                if (Right >= 0) parts.Add("П " + Right + "%");
                if (Case >= 0) parts.Add("футляр " + Case + "%");
                return parts.Count == 0 ? "заряд неизвестен" : string.Join(" · ", parts.ToArray());
            }
        }
    }

    // Заряд подключённых Bluetooth-наушников любой марки — тот, что Windows сама узнаёт от устройства
    // (его же она показывает в «Параметрах»). Многие наушники его сообщают, но не все.
    static class BtBattery
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct DEVPROPKEY { public Guid fmtid; public uint pid; }

        [System.Runtime.InteropServices.DllImport("cfgmgr32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int CM_Get_Device_ID_List_Size(out uint len, string filter, uint flags);
        [System.Runtime.InteropServices.DllImport("cfgmgr32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int CM_Get_Device_ID_List(string filter, char[] buffer, uint len, uint flags);
        [System.Runtime.InteropServices.DllImport("cfgmgr32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int CM_Locate_DevNode(out uint devInst, string id, uint flags);
        [System.Runtime.InteropServices.DllImport("cfgmgr32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int CM_Get_DevNode_Property(uint devInst, ref DEVPROPKEY key, out uint type, byte[] buffer, ref uint size, uint flags);

        static DEVPROPKEY Key(string guid, uint pid) { return new DEVPROPKEY { fmtid = new Guid(guid), pid = pid }; }
        static readonly DEVPROPKEY KBattery = Key("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);
        static readonly DEVPROPKEY KConnected = Key("83DA6326-97A6-4088-9453-A1923F573B29", 15);
        static readonly DEVPROPKEY KName = Key("a45c254e-df1c-4efd-8020-67d146a850e0", 14);

        static byte[] Prop(uint dev, DEVPROPKEY key)
        {
            uint type, size = 0;
            DEVPROPKEY k = key;
            CM_Get_DevNode_Property(dev, ref k, out type, null, ref size, 0);
            if (size == 0 || size > 4096) return null;
            var buf = new byte[size];
            return CM_Get_DevNode_Property(dev, ref k, out type, buf, ref size, 0) == 0 ? buf : null;
        }

        public class Device { public string Name = ""; public int Battery = -1; public bool Connected; }

        // Подключённые сейчас Bluetooth-устройства (классический Bluetooth) с зарядом, если он известен
        public static List<Device> Connected()
        {
            var list = new List<Device>();
            try
            {
                uint len;
                if (CM_Get_Device_ID_List_Size(out len, "BTHENUM", 0x1 | 0x100) != 0 || len < 2 || len > 1000000) return list;   // по перечислителю, только присутствующие
                var buf = new char[len];
                if (CM_Get_Device_ID_List("BTHENUM", buf, len, 0x1 | 0x100) != 0) return list;
                var byName = new Dictionary<string, Device>();
                foreach (string id in new string(buf).Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    uint dev;
                    if (CM_Locate_DevNode(out dev, id, 0) != 0) continue;
                    byte[] nameBytes = Prop(dev, KName);
                    string name = nameBytes == null ? "" : System.Text.Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');
                    byte[] bat = Prop(dev, KBattery);
                    byte[] con = Prop(dev, KConnected);
                    bool connected = con != null && con.Length > 0 && con[0] != 0;
                    int battery = bat != null && bat.Length > 0 && bat[0] <= 100 ? bat[0] : -1;
                    if (!connected && battery < 0) continue;
                    // У одного устройства несколько узлов (само устройство, гарнитура, управление): склеиваем по началу имени
                    string key = name;
                    foreach (string cut in new string[] { " Hands-Free", " Avrcp", " AVRCP", " Stereo", " Hands-free" }) { int i = key.IndexOf(cut, StringComparison.OrdinalIgnoreCase); if (i > 0) key = key.Substring(0, i); }
                    Device d;
                    if (!byName.TryGetValue(key, out d)) { d = new Device { Name = key }; byName[key] = d; }
                    if (connected) d.Connected = true;
                    if (battery >= 0) d.Battery = battery;
                }
                foreach (Device d in byName.Values) if (d.Connected && d.Name.Length > 0) list.Add(d);
            }
            catch { }
            return list;
        }
    }

    static class Earbuds
    {
        static BluetoothLEAdvertisementWatcher watcher;
        static EarbudsState state;
        static readonly object gate = new object();
        public static volatile string Error;
        public static bool TestActive;
        public static bool Logging;                     // проверочный режим: копить сырые сообщения
        public static readonly List<string> Log = new List<string>();

        public static bool Enabled { get { return Store.Get("earbuds") != "0"; } }

        // Последнее состояние, если наушники слышно в последние 20 секунд
        public static EarbudsState Current
        {
            get
            {
                lock (gate)
                {
                    if (state == null || unchecked(Environment.TickCount - state.At) > 20000) return null;
                    return state;
                }
            }
        }

        static string ModelName(int id)
        {
            switch (id)
            {
                case 0x0220: return "AirPods";
                case 0x0F20: return "AirPods 2";
                case 0x1320: return "AirPods 3";
                case 0x1920: return "AirPods 4";
                case 0x1B20: return "AirPods 4";
                case 0x0E20: return "AirPods Pro";
                case 0x1420: return "AirPods Pro 2";
                case 0x2420: return "AirPods Pro 2";
                case 0x0A20: return "AirPods Max";
                case 0x1F20: return "AirPods Max";
                case 0x0320: return "Powerbeats 3";
                case 0x0B20: return "Powerbeats Pro";
                case 0x0520: return "BeatsX";
                case 0x0620: return "Beats Solo 3";
                case 0x0920: return "Beats Studio 3";
                case 0x1120: return "Beats Studio Buds";
                case 0x1020: return "Beats Flex";
                default: return "";
            }
        }

        static int Pct(int nibble) { return nibble <= 10 ? nibble * 10 : -1; }

        // Разбор сообщения «я рядом» от наушников Apple. data — содержимое блока производителя (без кода Apple).
        // Возвращает null, если это не оно.
        public static EarbudsState Parse(byte[] d, int rssi)
        {
            if (d == null || d.Length < 9 || d[0] != 0x07 || d[1] != 0x19) return null;
            int model = (d[3] << 8) | d[4];
            string name = ModelName(model);
            if (name.Length == 0) return null;
            int status = d[5];
            bool flipped = (status & 0x20) == 0;          // какой из двух зарядов относится к левому наушнику
            int hi = d[6] >> 4, lo = d[6] & 0x0F;
            int flags = d[7] >> 4, caseNibble = d[7] & 0x0F;
            var s = new EarbudsState { Model = name, Rssi = rssi, At = Environment.TickCount };
            if (name == "AirPods Max" || name.StartsWith("Beats S") || name == "BeatsX" || name == "Beats Flex" || name == "Powerbeats 3")
            {
                // Одна батарея: её заряд лежит в одной из двух половинок, вторая помечена как «неизвестно»
                s.Single = Pct(lo) >= 0 ? Pct(lo) : Pct(hi);
                // У AirPods Max с USB-C половинка даёт только десятки, а точный процент лежит в следующем байте.
                // Берём его, только если он согласуется с десятками (сверено на настоящих наушниках: десятки 0, байт 5 → 5%).
                if (model == 0x1F20 && d.Length > 8 && d[8] <= 100 && s.Single >= 0 && d[8] / 10 == s.Single / 10) s.Single = d[8];
                s.Charging = (flags & 0x03) != 0;
                s.InEar = (status & 0x02) != 0;
            }
            else
            {
                s.Left = Pct(flipped ? hi : lo);
                s.Right = Pct(flipped ? lo : hi);
                s.Case = Pct(caseNibble);
                s.Charging = (flags & 0x03) != 0;
                s.InEar = (status & 0x0A) != 0;
            }
            return s;
        }

        public static void Start()
        {
            lock (gate)
            {
                if (watcher != null || !Enabled) return;
                try
                {
                    var w = new BluetoothLEAdvertisementWatcher();
                    w.ScanningMode = TestActive ? BluetoothLEScanningMode.Active : BluetoothLEScanningMode.Passive;   // обычно только слушаем
                    w.AdvertisementFilter.Advertisement.ManufacturerData.Add(new BluetoothLEManufacturerData { CompanyId = 0x004C });
                    w.Received += OnReceived;
                    w.Stopped += delegate(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs e)
                    {
                        if (e.Error != Windows.Devices.Bluetooth.BluetoothError.Success) Error = "Bluetooth выключен или недоступен";
                    };
                    w.Start();
                    watcher = w;
                    Error = null;
                }
                catch (Exception ex) { Error = "Bluetooth недоступен"; Store.LogError("наушники", ex); }
            }
        }

        public static void Stop()
        {
            lock (gate)
            {
                if (watcher == null) return;
                try { watcher.Stop(); } catch { }
                watcher = null; state = null;
            }
        }

        static void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs e)
        {
            try
            {
                foreach (BluetoothLEManufacturerData md in e.Advertisement.ManufacturerData)
                {
                    if (md.CompanyId != 0x004C) continue;
                    var rd = Windows.Storage.Streams.DataReader.FromBuffer(md.Data);
                    byte[] d = new byte[rd.UnconsumedBufferLength];
                    rd.ReadBytes(d);
                    if (Logging && d.Length >= 2) lock (Log) { if (Log.Count < 200) Log.Add(e.RawSignalStrengthInDBm + " " + BitConverter.ToString(d)); }
                    EarbudsState s = Parse(d, e.RawSignalStrengthInDBm);
                    if (s == null) continue;
                    // Рядом могут быть чужие наушники: берём самые «громкие» и только те, что совсем близко
                    if (s.Rssi < -72) continue;
                    lock (gate)
                    {
                        bool fresh = state == null || unchecked(Environment.TickCount - state.At) > 6000;
                        if (fresh || s.Rssi >= state.Rssi - 8) state = s;
                    }
                }
            }
            catch { }
        }
    }
}
