// Kapsula Camera — виртуальная камера Windows (фильтр DirectShow), которую видят Discord, Zoom, браузеры и OBS.
// Картинку берёт из общей памяти «Local\KapsulaCam», куда Kapsula кладёт кадры с камеры телефона.
// Если Kapsula не запущена — показывает тёмный кадр. Собирается отдельной библиотекой (AnyCPU), чтобы работать
// и в 64-, и в 32-битных программах. Регистрирует её сама Kapsula (настройки → «Камера телефона»).
using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyTitle("Kapsula Camera")]

namespace KapsulaCam
{
    // ------------------------------------------------------------------ структуры
    [StructLayout(LayoutKind.Sequential)]
    struct AMMediaType
    {
        public Guid majortype, subtype;
        public int bFixedSizeSamples, bTemporalCompression;
        public int lSampleSize;
        public Guid formattype;
        public IntPtr pUnk;
        public int cbFormat;
        public IntPtr pbFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct AllocProps { public int cBuffers, cbBuffer, cbAlign, cbPrefix; }

    static class G
    {
        public static readonly Guid IID_IPin = new Guid("56a86891-0ad4-11ce-b03a-0020af0ba770");
        public static readonly Guid IID_IBaseFilter = new Guid("56a86895-0ad4-11ce-b03a-0020af0ba770");
        public static readonly Guid MEDIATYPE_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid RGB32 = new Guid("e436eb7e-524f-11ce-9f53-0020af0ba770");
        public static readonly Guid FORMAT_VideoInfo = new Guid("05589f80-c356-11ce-bf01-00aa0055595a");
        public static readonly Guid AMPROPSETID_Pin = new Guid("9b00f101-1567-11d1-b3f1-00aa003761c5");
        public static readonly Guid PIN_CATEGORY_CAPTURE = new Guid("fb6c4281-0353-11d1-905f-0000c0cc16ba");
        public const int S_OK = 0, S_FALSE = 1, E_POINTER = unchecked((int)0x80004003), E_NOTIMPL = unchecked((int)0x80004001), E_FAIL = unchecked((int)0x80004005),
            E_INVALIDARG = unchecked((int)0x80070057), VFW_E_NOT_CONNECTED = unchecked((int)0x80040209), VFW_E_ALREADY_CONNECTED = unchecked((int)0x80040204),
            VFW_E_NO_ACCEPTABLE_TYPES = unchecked((int)0x80040207), VFW_E_NOT_FOUND = unchecked((int)0x80040216), VFW_S_NO_MORE_ITEMS = 0x00040103,
            VFW_S_CANT_CUE = 0x00040268, VFW_E_NOT_STOPPED = unchecked((int)0x80040224), E_PROP_SET_UNSUPPORTED = unchecked((int)0x80070492),
            E_PROP_ID_UNSUPPORTED = unchecked((int)0x80070490), VFW_E_TIMEOUT = unchecked((int)0x8004022E);

        // Варианты, которые камера предлагает программам: ширина, высота, кадров в секунду
        public static readonly int[][] Modes = { new[] { 1280, 720, 30 }, new[] { 1920, 1080, 30 }, new[] { 1920, 1080, 60 }, new[] { 1280, 720, 60 }, new[] { 3840, 2160, 30 }, new[] { 640, 360, 30 } };

        // Форматы кадра: I420 и YUY2 нужны голосовым движкам на WebRTC (Discord, Zoom, Teams), RGB — остальным.
        // subtype, бит на точку, FOURCC (0 — RGB без сжатия, снизу вверх)
        public static readonly Guid[] FmtGuid = { new Guid("30323449-0000-0010-8000-00AA00389B71"), new Guid("32595559-0000-0010-8000-00AA00389B71"), new Guid("e436eb7d-524f-11ce-9f53-0020af0ba770"), RGB32 };
        public static readonly int[] FmtBits = { 12, 16, 24, 32 };
        public static readonly int[] FmtFourcc = { 0x30323449, 0x32595559, 0, 0 };
        public static int FrameSize(int w, int h, int f) { return w * h * FmtBits[f] / 8; }

        // Все варианты: каждый режим во всех форматах
        public static int[][] Ordered()
        {
            var res = new List<int[]>();
            foreach (int[] m in OrderedModes()) for (int f = 0; f < FmtGuid.Length; f++) res.Add(new[] { m[0], m[1], m[2], f });
            return res.ToArray();
        }

        // Первым идёт режим, выбранный в Kapsula (настройки → «Камера телефона» → качество)
        public static int[][] OrderedModes()
        {
            int pref = 0;
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Kapsula\Camera"))
                    if (k != null) pref = Convert.ToInt32(k.GetValue("Mode", 0));
            }
            catch { }
            if (pref < 0 || pref >= Modes.Length) pref = 0;
            var list = new List<int[]>(); list.Add(Modes[pref]);
            // выбрано 60 кадров — предлагаем только режимы на 60: иначе браузеры и сайты, которые не просят частоту, берут 30
            bool only60 = Modes[pref][2] == 60;
            for (int i = 0; i < Modes.Length; i++) if (i != pref && (!only60 || Modes[i][2] == 60)) list.Add(Modes[i]);
            return list.ToArray();
        }

        public static IntPtr NewMediaType(int w, int h, int fps, int f)
        {
            IntPtr p = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(AMMediaType)));
            Marshal.StructureToPtr(Make(w, h, fps, f), p, false);
            return p;
        }

        public static AMMediaType Make(int w, int h, int fps, int f)
        {
            int size = FrameSize(w, h, f);
            var mt = new AMMediaType { majortype = MEDIATYPE_Video, subtype = FmtGuid[f], bFixedSizeSamples = 1, lSampleSize = size, formattype = FORMAT_VideoInfo, cbFormat = 88 };
            mt.pbFormat = Marshal.AllocCoTaskMem(88);
            var b = new byte[88];
            WriteI(b, 8, w); WriteI(b, 12, h);          // rcSource
            WriteI(b, 24, w); WriteI(b, 28, h);         // rcTarget
            WriteI(b, 32, (int)Math.Min(int.MaxValue, (long)size * 8 * fps));   // dwBitRate
            WriteL(b, 40, 10000000L / fps);             // AvgTimePerFrame
            WriteI(b, 48, 40); WriteI(b, 52, w); WriteI(b, 56, h);   // BITMAPINFOHEADER: размер, ширина, высота (снизу вверх)
            b[60] = 1; b[62] = (byte)FmtBits[f];        // плоскостей 1, бит на точку
            WriteI(b, 64, FmtFourcc[f]);                // biCompression
            WriteI(b, 68, size);
            Marshal.Copy(b, 0, mt.pbFormat, 88);
            return mt;
        }

        public static void WriteI(byte[] b, int o, int v) { BitConverter.GetBytes(v).CopyTo(b, o); }
        public static void WriteL(byte[] b, int o, long v) { BitConverter.GetBytes(v).CopyTo(b, o); }

        // Разбор чужого типа: подходит ли он нам и какие у него размеры
        public static bool Parse(IntPtr pmt, out int w, out int h, out int fps, out int f)
        {
            w = h = fps = 0; f = -1;
            if (pmt == IntPtr.Zero) return false;
            var mt = (AMMediaType)Marshal.PtrToStructure(pmt, typeof(AMMediaType));
            if (mt.majortype != MEDIATYPE_Video) return false;
            f = Array.IndexOf(FmtGuid, mt.subtype);
            if (f < 0) return false;
            if (mt.formattype != FORMAT_VideoInfo || mt.pbFormat == IntPtr.Zero || mt.cbFormat < 88) return false;
            w = Marshal.ReadInt32(mt.pbFormat, 52); h = Math.Abs(Marshal.ReadInt32(mt.pbFormat, 56));
            long tpf = Marshal.ReadInt64(mt.pbFormat, 40);
            fps = tpf > 0 ? (int)Math.Round(10000000.0 / tpf) : 30;
            if (fps <= 0 || fps > 120) fps = 30;
            if (Marshal.ReadInt16(mt.pbFormat, 62) != FmtBits[f]) return false;
            foreach (int[] m in Modes) if (m[0] == w && m[1] == h) return true;
            return false;
        }

        public static void FreeContents(ref AMMediaType mt)
        {
            if (mt.pbFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(mt.pbFormat);
            if (mt.pUnk != IntPtr.Zero) Marshal.Release(mt.pUnk);
            mt.pbFormat = IntPtr.Zero; mt.pUnk = IntPtr.Zero;
        }

        public static void WriteName(IntPtr dst, string s)
        {
            var c = new char[128];
            for (int i = 0; i < s.Length && i < 127; i++) c[i] = s[i];
            Marshal.Copy(c, 0, dst, 128);
        }
    }

    // ------------------------------------------------------------------ прямые вызовы чужих объектов
    // Через обёртки .NET вызов из потока кадров ушёл бы в главный поток программы и мог бы ждать вечно,
    // поэтому методы получателя вызываются напрямую по таблице методов, как это делают обычные драйверы.
    static class Raw
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int F0(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int F1(IntPtr self, IntPtr a);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int F2(IntPtr self, IntPtr a, IntPtr b);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int FPI(IntPtr self, IntPtr a, int b);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int FI(IntPtr self, int a);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int FOut(IntPtr self, out IntPtr a);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int FBuf(IntPtr self, out IntPtr s, IntPtr a, IntPtr b, int f);

        public static T Slot<T>(IntPtr obj, int i) where T : class
        {
            IntPtr vt = Marshal.ReadIntPtr(obj);
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, i * IntPtr.Size), typeof(T)) as T;
        }

        public static IntPtr QI(IntPtr obj, Guid iid)
        {
            IntPtr r;
            return Marshal.QueryInterface(obj, ref iid, out r) == 0 ? r : IntPtr.Zero;
        }

        public static readonly Guid IID_IMemInputPin = new Guid("56a8689d-0ad4-11ce-b03a-0020af0ba770");
        public static readonly Guid IID_IMediaSample = new Guid("56a8689a-0ad4-11ce-b03a-0020af0ba770");
    }

    // ------------------------------------------------------------------ интерфейсы DirectShow
    [ComImport, Guid("0000010c-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersist { [PreserveSig] int GetClassID(out Guid clsid); }

    [ComImport, Guid("56a86899-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMediaFilter
    {
        [PreserveSig] int GetClassID(out Guid clsid);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Run(long tStart);
        [PreserveSig] int GetState(int msTimeout, out int state);
        [PreserveSig] int SetSyncSource(IntPtr clock);
        [PreserveSig] int GetSyncSource(out IntPtr clock);
    }

    [ComImport, Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IBaseFilter
    {
        [PreserveSig] int GetClassID(out Guid clsid);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Run(long tStart);
        [PreserveSig] int GetState(int msTimeout, out int state);
        [PreserveSig] int SetSyncSource(IntPtr clock);
        [PreserveSig] int GetSyncSource(out IntPtr clock);
        [PreserveSig] int EnumPins(out IntPtr ppEnum);
        [PreserveSig] int FindPin([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr ppPin);
        [PreserveSig] int QueryFilterInfo(IntPtr pInfo);
        [PreserveSig] int JoinFilterGraph(IntPtr graph, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int QueryVendorInfo(out IntPtr info);
    }

    [ComImport, Guid("2dd74950-a890-11d1-abe8-00a0c905f375"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAMFilterMiscFlags { [PreserveSig] int GetMiscFlags(); }

    [ComImport, Guid("56a86891-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPin
    {
        [PreserveSig] int Connect(IntPtr pReceivePin, IntPtr pmt);
        [PreserveSig] int ReceiveConnection(IntPtr pConnector, IntPtr pmt);
        [PreserveSig] int Disconnect();
        [PreserveSig] int ConnectedTo(out IntPtr pPin);
        [PreserveSig] int ConnectionMediaType(IntPtr pmt);
        [PreserveSig] int QueryPinInfo(IntPtr pInfo);
        [PreserveSig] int QueryDirection(out int dir);
        [PreserveSig] int QueryId(out IntPtr id);
        [PreserveSig] int QueryAccept(IntPtr pmt);
        [PreserveSig] int EnumMediaTypes(out IntPtr ppEnum);
        [PreserveSig] int QueryInternalConnections(IntPtr apPin, ref int nPin);
        [PreserveSig] int EndOfStream();
        [PreserveSig] int BeginFlush();
        [PreserveSig] int EndFlush();
        [PreserveSig] int NewSegment(long start, long stop, double rate);
    }

    [ComImport, Guid("56a86892-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumPins
    {
        [PreserveSig] int Next(int c, IntPtr ppPins, IntPtr pFetched);
        [PreserveSig] int Skip(int c);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IntPtr ppEnum);
    }

    [ComImport, Guid("89c31040-846b-11ce-97d3-00aa0055595a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumMediaTypes
    {
        [PreserveSig] int Next(int c, IntPtr ppTypes, IntPtr pFetched);
        [PreserveSig] int Skip(int c);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IntPtr ppEnum);
    }

    [ComImport, Guid("C6E13340-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAMStreamConfig
    {
        [PreserveSig] int SetFormat(IntPtr pmt);
        [PreserveSig] int GetFormat(out IntPtr ppmt);
        [PreserveSig] int GetNumberOfCapabilities(out int count, out int size);
        [PreserveSig] int GetStreamCaps(int index, out IntPtr ppmt, IntPtr pSCC);
    }

    [ComImport, Guid("31EFAC30-515C-11d0-A9AA-00AA0061BE93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IKsPropertySet
    {
        [PreserveSig] int Set(ref Guid set, int id, IntPtr inst, int cbInst, IntPtr data, int cbData);
        [PreserveSig] int Get(ref Guid set, int id, IntPtr inst, int cbInst, IntPtr data, int cbData, out int returned);
        [PreserveSig] int QuerySupported(ref Guid set, int id, out int support);
    }

    [ComImport, Guid("56a8689d-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMemInputPin
    {
        [PreserveSig] int GetAllocator(out IntPtr ppAllocator);
        [PreserveSig] int NotifyAllocator(IntPtr pAllocator, int readOnly);
        [PreserveSig] int GetAllocatorRequirements(IntPtr pProps);
        [PreserveSig] int Receive(IntPtr pSample);
        [PreserveSig] int ReceiveMultiple(IntPtr pSamples, int n, out int processed);
        [PreserveSig] int ReceiveCanBlock();
    }

    [ComImport, Guid("56a8689c-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMemAllocator
    {
        [PreserveSig] int SetProperties(IntPtr pRequest, IntPtr pActual);
        [PreserveSig] int GetProperties(IntPtr pProps);
        [PreserveSig] int Commit();
        [PreserveSig] int Decommit();
        [PreserveSig] int GetBuffer(out IntPtr ppBuffer, IntPtr pStart, IntPtr pEnd, int flags);
        [PreserveSig] int ReleaseBuffer(IntPtr pBuffer);
    }

    [ComImport, Guid("56a8689a-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMediaSample
    {
        [PreserveSig] int GetPointer(out IntPtr ppBuffer);
        [PreserveSig] int GetSize();
        [PreserveSig] int GetTime(out long start, out long end);
        [PreserveSig] int SetTime(IntPtr pStart, IntPtr pEnd);
        [PreserveSig] int IsSyncPoint();
        [PreserveSig] int SetSyncPoint(int b);
        [PreserveSig] int IsPreroll();
        [PreserveSig] int SetPreroll(int b);
        [PreserveSig] int GetActualDataLength();
        [PreserveSig] int SetActualDataLength(int len);
        [PreserveSig] int GetMediaType(out IntPtr ppmt);
        [PreserveSig] int SetMediaType(IntPtr pmt);
        [PreserveSig] int IsDiscontinuity();
        [PreserveSig] int SetDiscontinuity(int b);
        [PreserveSig] int GetMediaTime(out long start, out long end);
        [PreserveSig] int SetMediaTime(IntPtr pStart, IntPtr pEnd);
    }

    // ------------------------------------------------------------------ фильтр
    [ComVisible(true), Guid("4B415053-554C-4143-414D-000000000001"), ClassInterface(ClassInterfaceType.None)]
    public class CamFilter : IBaseFilter, IMediaFilter, IPersist, IAMFilterMiscFlags
    {
        public static readonly Guid Clsid = new Guid("4B415053-554C-4143-414D-000000000001");
        public const string Name = "Kapsula Camera";
        internal int state;          // 0 стоп, 1 пауза, 2 работа
        internal IntPtr graph;
        internal readonly CamPin pin;
        internal IntPtr clock;

        public CamFilter() { pin = new CamPin(this); }

        public int GetClassID(out Guid clsid) { clsid = Clsid; return G.S_OK; }
        public int Stop() { pin.StopStream(); state = 0; return G.S_OK; }
        public int Pause() { if (state == 2) pin.StopStream(); state = 1; return G.S_OK; }
        public int Run(long tStart) { state = 2; pin.StartStream(); return G.S_OK; }
        public int GetState(int ms, out int s) { s = state; return state == 1 ? G.VFW_S_CANT_CUE : G.S_OK; }
        public int SetSyncSource(IntPtr c)
        {
            if (clock != IntPtr.Zero) Marshal.Release(clock);
            clock = c;
            if (clock != IntPtr.Zero) Marshal.AddRef(clock);
            return G.S_OK;
        }
        public int GetSyncSource(out IntPtr c) { c = clock; if (c != IntPtr.Zero) Marshal.AddRef(c); return G.S_OK; }

        public int EnumPins(out IntPtr e) { e = Marshal.GetComInterfaceForObject(new PinEnum(this, 0), typeof(IEnumPins)); return G.S_OK; }
        public int FindPin(string id, out IntPtr p)
        {
            p = IntPtr.Zero;
            if (id != "Capture" && id != "Захват") return G.VFW_E_NOT_FOUND;
            p = Marshal.GetComInterfaceForObject(pin, typeof(IPin));
            return G.S_OK;
        }
        public int QueryFilterInfo(IntPtr info)
        {
            if (info == IntPtr.Zero) return G.E_POINTER;
            G.WriteName(info, Name);
            Marshal.WriteIntPtr(info, 256, graph);
            if (graph != IntPtr.Zero) Marshal.AddRef(graph);
            return G.S_OK;
        }
        public int JoinFilterGraph(IntPtr g, string name) { graph = g; return G.S_OK; }   // граф держит фильтр, а не наоборот
        public int QueryVendorInfo(out IntPtr info) { info = IntPtr.Zero; return G.E_NOTIMPL; }
        public int GetMiscFlags() { return 2; }   // источник
    }

    // ------------------------------------------------------------------ выход фильтра
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public class CamPin : IPin, IAMStreamConfig, IKsPropertySet
    {
        readonly CamFilter filter;
        IntPtr connected;            // IPin получателя
        IntPtr input;                // его IMemInputPin
        int w = 1280, h = 720, fps = 30, fmt = 0;
        bool formatSet;
        CamAllocator ourAlloc;
        IntPtr theirAlloc;           // IMemAllocator получателя
        Thread thread;
        volatile bool running;

        internal CamPin(CamFilter f) { filter = f; int[] m = G.Ordered()[0]; w = m[0]; h = m[1]; fps = m[2]; fmt = m[3]; }

        // --- соединение
        public int Connect(IntPtr pReceive, IntPtr pmt)
        {
            if (pReceive == IntPtr.Zero) return G.E_POINTER;
            if (connected != IntPtr.Zero) return G.VFW_E_ALREADY_CONNECTED;
            if (filter.state != 0) return G.VFW_E_NOT_STOPPED;
            var tries = new List<int[]>();
            int pw, ph, pf, pfm;
            if (pmt != IntPtr.Zero && G.Parse(pmt, out pw, out ph, out pf, out pfm)) tries.Add(new[] { pw, ph, pf, pfm });
            else if (formatSet) tries.Add(new[] { w, h, fps, fmt });
            else tries.AddRange(G.Ordered());
            foreach (int[] m in tries)
            {
                AMMediaType mt = G.Make(m[0], m[1], m[2], m[3]);
                IntPtr p = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(AMMediaType)));
                Marshal.StructureToPtr(mt, p, false);
                IntPtr self = Marshal.GetComInterfaceForObject(this, typeof(IPin));
                int hr = Raw.Slot<Raw.F2>(pReceive, 4)(pReceive, self, p);   // IPin::ReceiveConnection
                Marshal.Release(self);
                Marshal.FreeCoTaskMem(p);
                G.FreeContents(ref mt);
                if (hr != 0) continue;
                w = m[0]; h = m[1]; fps = m[2]; fmt = m[3];
                Marshal.AddRef(pReceive);
                connected = pReceive;
                input = Raw.QI(pReceive, Raw.IID_IMemInputPin);
                if (input == IntPtr.Zero || !DecideAllocator()) { Raw.Slot<Raw.F0>(pReceive, 5)(pReceive); Disconnect(); return G.E_FAIL; }
                return G.S_OK;
            }
            return G.VFW_E_NO_ACCEPTABLE_TYPES;
        }

        bool DecideAllocator()
        {
            int size = G.FrameSize(w, h, fmt);
            ourAlloc = new CamAllocator(size);
            IntPtr a = Marshal.GetComInterfaceForObject(ourAlloc, typeof(IMemAllocator));
            int hr = Raw.Slot<Raw.FPI>(input, 4)(input, a, 0);   // IMemInputPin::NotifyAllocator
            Marshal.Release(a);
            if (hr == 0) { theirAlloc = IntPtr.Zero; return true; }
            ourAlloc = null;
            // получатель хочет свой распределитель памяти
            IntPtr ta;
            if (Raw.Slot<Raw.FOut>(input, 3)(input, out ta) != 0 || ta == IntPtr.Zero) return false;
            theirAlloc = ta;
            IntPtr req = Marshal.AllocCoTaskMem(16), act = Marshal.AllocCoTaskMem(16);
            Marshal.StructureToPtr(new AllocProps { cBuffers = 3, cbBuffer = size, cbAlign = 1 }, req, false);
            hr = Raw.Slot<Raw.F2>(ta, 3)(ta, req, act);
            Marshal.FreeCoTaskMem(req); Marshal.FreeCoTaskMem(act);
            if (hr != 0) return false;
            return Raw.Slot<Raw.FPI>(input, 4)(input, ta, 0) == 0;
        }

        public int ReceiveConnection(IntPtr c, IntPtr pmt) { return G.E_NOTIMPL; }   // это выход, к нему не подключаются
        public int Disconnect()
        {
            if (connected == IntPtr.Zero) return G.S_FALSE;
            StopStream();
            Marshal.Release(connected);
            connected = IntPtr.Zero;
            if (input != IntPtr.Zero) Marshal.Release(input);
            if (theirAlloc != IntPtr.Zero) Marshal.Release(theirAlloc);
            input = IntPtr.Zero; theirAlloc = IntPtr.Zero; ourAlloc = null;
            return G.S_OK;
        }
        public int ConnectedTo(out IntPtr p) { p = connected; if (p == IntPtr.Zero) return G.VFW_E_NOT_CONNECTED; Marshal.AddRef(p); return G.S_OK; }
        public int ConnectionMediaType(IntPtr pmt)
        {
            if (pmt == IntPtr.Zero) return G.E_POINTER;
            if (connected == IntPtr.Zero) return G.VFW_E_NOT_CONNECTED;
            Marshal.StructureToPtr(G.Make(w, h, fps, fmt), pmt, false);
            return G.S_OK;
        }
        public int QueryPinInfo(IntPtr info)
        {
            if (info == IntPtr.Zero) return G.E_POINTER;
            IntPtr f = Marshal.GetComInterfaceForObject(filter, typeof(IBaseFilter));
            Marshal.WriteIntPtr(info, 0, f);
            Marshal.WriteInt32(info, IntPtr.Size, 1);   // выход
            G.WriteName(new IntPtr(info.ToInt64() + IntPtr.Size + 4), "Capture");
            return G.S_OK;
        }
        public int QueryDirection(out int dir) { dir = 1; return G.S_OK; }
        public int QueryId(out IntPtr id) { id = Marshal.StringToCoTaskMemUni("Capture"); return G.S_OK; }
        public int QueryAccept(IntPtr pmt) { int a, b, c, d; return G.Parse(pmt, out a, out b, out c, out d) ? G.S_OK : G.S_FALSE; }
        public int EnumMediaTypes(out IntPtr e) { e = Marshal.GetComInterfaceForObject(new TypeEnum(formatSet ? new int[][] { new[] { w, h, fps, fmt } } : G.Ordered(), 0), typeof(IEnumMediaTypes)); return G.S_OK; }
        public int QueryInternalConnections(IntPtr ap, ref int n) { return G.E_NOTIMPL; }
        public int EndOfStream() { return G.S_OK; }
        public int BeginFlush() { return G.S_OK; }
        public int EndFlush() { return G.S_OK; }
        public int NewSegment(long a, long b, double r) { return G.S_OK; }

        // --- IAMStreamConfig: программа выбирает размер и частоту кадров
        public int SetFormat(IntPtr pmt)
        {
            if (pmt == IntPtr.Zero) { formatSet = false; return G.S_OK; }
            int pw, ph, pf, pfm;
            if (!G.Parse(pmt, out pw, out ph, out pf, out pfm)) return G.VFW_E_NO_ACCEPTABLE_TYPES;
            if (connected != IntPtr.Zero && (pw != w || ph != h || pfm != fmt)) return G.VFW_E_ALREADY_CONNECTED;
            w = pw; h = ph; fps = pf; fmt = pfm; formatSet = true;
            return G.S_OK;
        }
        public int GetFormat(out IntPtr ppmt) { ppmt = G.NewMediaType(w, h, fps, fmt); return G.S_OK; }
        public int GetNumberOfCapabilities(out int count, out int size) { count = G.Ordered().Length; size = 128; return G.S_OK; }
        public int GetStreamCaps(int index, out IntPtr ppmt, IntPtr caps)
        {
            ppmt = IntPtr.Zero;
            int[][] list = G.Ordered();
            if (index < 0 || index >= list.Length) return G.S_FALSE;
            int[] m = list[index];
            ppmt = G.NewMediaType(m[0], m[1], m[2], m[3]);
            int bps = (int)Math.Min(int.MaxValue, (long)G.FrameSize(m[0], m[1], m[3]) * 8 * m[2]);
            if (caps != IntPtr.Zero)
            {
                var b = new byte[128];
                G.FORMAT_VideoInfo.ToByteArray().CopyTo(b, 0);
                G.WriteI(b, 20, m[0]); G.WriteI(b, 24, m[1]);                     // InputSize
                G.WriteI(b, 28, m[0]); G.WriteI(b, 32, m[1]); G.WriteI(b, 36, m[0]); G.WriteI(b, 40, m[1]);   // кадрирование
                G.WriteI(b, 44, 1); G.WriteI(b, 48, 1); G.WriteI(b, 52, 1); G.WriteI(b, 56, 1);
                G.WriteI(b, 60, m[0]); G.WriteI(b, 64, m[1]); G.WriteI(b, 68, m[0]); G.WriteI(b, 72, m[1]);   // размер на выходе
                G.WriteI(b, 76, 1); G.WriteI(b, 80, 1);
                G.WriteL(b, 104, 10000000L / m[2]); G.WriteL(b, 112, 10000000L / m[2]);
                G.WriteI(b, 120, bps); G.WriteI(b, 124, bps);
                Marshal.Copy(b, 0, caps, 128);
            }
            return G.S_OK;
        }

        // --- IKsPropertySet: «я — выход захвата»
        public int Set(ref Guid set, int id, IntPtr inst, int cbInst, IntPtr data, int cbData) { return G.E_NOTIMPL; }
        public int Get(ref Guid set, int id, IntPtr inst, int cbInst, IntPtr data, int cbData, out int returned)
        {
            returned = 0;
            if (set != G.AMPROPSETID_Pin) return G.E_PROP_SET_UNSUPPORTED;
            if (id != 0) return G.E_PROP_ID_UNSUPPORTED;
            returned = 16;
            if (data == IntPtr.Zero) return G.S_OK;
            if (cbData < 16) return G.E_INVALIDARG;
            Marshal.Copy(G.PIN_CATEGORY_CAPTURE.ToByteArray(), 0, data, 16);
            return G.S_OK;
        }
        public int QuerySupported(ref Guid set, int id, out int support)
        {
            support = 0;
            if (set != G.AMPROPSETID_Pin) return G.E_PROP_SET_UNSUPPORTED;
            if (id != 0) return G.E_PROP_ID_UNSUPPORTED;
            support = 1;
            return G.S_OK;
        }

        // --- поток кадров
        internal void StartStream()
        {
            if (running || input == IntPtr.Zero) return;
            if (theirAlloc != IntPtr.Zero) Raw.Slot<Raw.F0>(theirAlloc, 5)(theirAlloc);   // Commit
            running = true;
            thread = new Thread(Pump) { IsBackground = true, Name = "Kapsula Camera" };
            thread.Start();
        }

        internal void StopStream()
        {
            running = false;
            var t = thread;
            if (t != null && t != Thread.CurrentThread) t.Join(2000);
            thread = null;
            if (theirAlloc != IntPtr.Zero) Raw.Slot<Raw.F0>(theirAlloc, 6)(theirAlloc);   // Decommit
        }

        [DllImport("winmm.dll")] static extern int timeBeginPeriod(int ms);
        [DllImport("winmm.dll")] static extern int timeEndPeriod(int ms);

        void Pump()
        {
            timeBeginPeriod(1);   // паузы с точностью до миллисекунды — кадры идут ровно, а не пачками по 15 мс
            var src = new FrameSource();
            long interval = 10000000L / fps, n = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int size = G.FrameSize(w, h, fmt);
            var receive = Raw.Slot<Raw.F1>(input, 6);                 // IMemInputPin::Receive
            Raw.FBuf getBuffer = theirAlloc != IntPtr.Zero ? Raw.Slot<Raw.FBuf>(theirAlloc, 7) : null;
            IntPtr times = Marshal.AllocCoTaskMem(16);
            while (running)
            {
                long due = n * interval / 10000;   // миллисекунды
                long wait = due - sw.ElapsedMilliseconds;
                if (wait > 1) Thread.Sleep((int)Math.Min(wait - 1, 50));
                else if (wait > 0) Thread.Sleep(0);
                if (sw.ElapsedMilliseconds < due) continue;
                if (sw.ElapsedMilliseconds - due > 500) n = sw.ElapsedMilliseconds * 10000 / interval;   // отстали — догоняем без пачки кадров
                IntPtr sample = IntPtr.Zero;
                CamSample ours = null;
                try
                {
                    if (ourAlloc != null)
                    {
                        ours = ourAlloc.Next();
                        if (ours == null) { Thread.Sleep(2); continue; }
                        sample = Marshal.GetComInterfaceForObject(ours, typeof(IMediaSample));
                    }
                    else if (getBuffer(theirAlloc, out sample, IntPtr.Zero, IntPtr.Zero, 0) != 0 || sample == IntPtr.Zero) { sample = IntPtr.Zero; Thread.Sleep(5); continue; }
                    IntPtr buf;
                    Raw.Slot<Raw.FOut>(sample, 3)(sample, out buf);                         // GetPointer
                    if (Raw.Slot<Raw.F0>(sample, 4)(sample) >= size) src.Fill(buf, w, h, fmt);   // GetSize
                    Raw.Slot<Raw.FI>(sample, 12)(sample, size);              // SetActualDataLength
                    Raw.Slot<Raw.FI>(sample, 8)(sample, 1);                  // SetSyncPoint
                    Marshal.WriteInt64(times, 0, n * interval); Marshal.WriteInt64(times, 8, (n + 1) * interval);
                    Raw.Slot<Raw.F2>(sample, 6)(sample, times, new IntPtr(times.ToInt64() + 8));   // SetTime
                    Raw.Slot<Raw.FI>(sample, 16)(sample, n == 0 ? 1 : 0);    // SetDiscontinuity
                    int hr = receive(input, sample);
                    if (hr < 0) Thread.Sleep(20);
                }
                catch { Thread.Sleep(20); }
                finally
                {
                    if (sample != IntPtr.Zero) Marshal.Release(sample);
                    if (ours != null) ourAlloc.Done(ours);
                }
                n++;
            }
            Marshal.FreeCoTaskMem(times);
            src.Dispose();
            timeEndPeriod(1);
        }
    }
    // ------------------------------------------------------------------ кадры из Kapsula
    // Общая память: [0] метка 'KCAM', [4] ширина, [8] высота, [12] номер записи (нечётный — идёт запись),
    // [16] время записи (UTC, такты); с 64-го байта — кадр BGRA сверху вниз.
    class FrameSource : IDisposable
    {
        MemoryMappedFile map;
        MemoryMappedViewAccessor view;
        int lastTry;
        int[] xmap; int xmapFor;
        IntPtr tmp; int tmpSize;   // кадр BGRA сверху вниз нужного размера, из него — нужный формат
        int lastSeq = -1;          // номер последнего показанного кадра Kapsula
        bool haveFrame;

        unsafe public void Fill(IntPtr dst, int w, int h, int fmt)
        {
            if (tmpSize != w * h * 4) { if (tmp != IntPtr.Zero) Marshal.FreeHGlobal(tmp); tmpSize = w * h * 4; tmp = Marshal.AllocHGlobal(tmpSize); haveFrame = false; lastSeq = -1; }
            uint* bgra = (uint*)tmp;
            bool got = false;
            if (view == null && unchecked(Environment.TickCount - lastTry) > 1000)
            {
                lastTry = Environment.TickCount;
                try { map = MemoryMappedFile.OpenExisting(@"Local\KapsulaCam2", MemoryMappedFileRights.Read); view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read); }
                catch { view = null; if (map != null) { map.Dispose(); map = null; } }
            }
            if (view != null)
            {
                byte* s = null;
                view.SafeMemoryMappedViewHandle.AcquirePointer(ref s);
                try
                {
                    s += view.PointerOffset;
                    // Два кадра по очереди: Kapsula пишет в один, свежий — второй; его никто не трогает, пока мы читаем
                    const long SlotBytes = 3840L * 2160 * 4;
                    if (*(int*)s == 0x3241434B && view.Capacity >= 64 + 2 * SlotBytes)
                    {
                        int seq = *(int*)(s + 12);
                        int slot = *(int*)(s + 24) & 1;
                        int sw = *(int*)(s + 32 + slot * 8), sh = *(int*)(s + 36 + slot * 8);
                        if (sw > 0 && sh > 0 && (long)sw * sh * 4 <= SlotBytes)
                        {
                            if (seq != lastSeq || !haveFrame) { Scale((uint*)(s + 64 + slot * SlotBytes), sw, sh, bgra, w, h); lastSeq = seq; haveFrame = true; }
                            got = true;
                        }
                    }
                }
                finally { view.SafeMemoryMappedViewHandle.ReleasePointer(); }
            }
            if (!got) for (int i = 0, n = w * h; i < n; i++) bgra[i] = 0xFF101014;   // Kapsula не запущена: тёмный кадр
            Convert(bgra, w, h, (byte*)dst, fmt);
        }

        // Вписываем кадр источника в кадр камеры (с полями, без искажений), сверху вниз
        unsafe void Scale(uint* s, int sw, int sh, uint* d, int w, int h)
        {
            double k = Math.Min((double)w / sw, (double)h / sh);
            int fw = Math.Max(1, (int)(sw * k)), fh = Math.Max(1, (int)(sh * k));
            int ox = (w - fw) / 2, oy = (h - fh) / 2;
            int key = sw * 7919 + w * 31 + fw;
            if (xmap == null || xmapFor != key) { xmap = new int[fw]; for (int x = 0; x < fw; x++) xmap[x] = Math.Min(sw - 1, (int)(x / k)); xmapFor = key; }
            for (int y = 0; y < h; y++)
            {
                uint* row = d + (long)y * w;
                int fy = y - oy;
                if (fy < 0 || fy >= fh) { for (int x = 0; x < w; x++) row[x] = 0xFF000000; continue; }
                uint* srow = s + (long)Math.Min(sh - 1, (int)(fy / k)) * sw;
                for (int x = 0; x < ox; x++) row[x] = 0xFF000000;
                if (fw == sw) { for (int x = 0; x < fw; x++) row[ox + x] = srow[x] | 0xFF000000; }
                else { for (int x = 0; x < fw; x++) row[ox + x] = srow[xmap[x]] | 0xFF000000; }
                for (int x = ox + fw; x < w; x++) row[x] = 0xFF000000;
            }
        }

        // BT.601, ограниченный диапазон — так ждут веб-камеры
        static int Y(int r, int g, int b) { return ((66 * r + 129 * g + 25 * b + 128) >> 8) + 16; }
        static int U(int r, int g, int b) { return ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128; }
        static int V(int r, int g, int b) { return ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128; }

        unsafe static void Convert(uint* src, int w, int h, byte* d, int fmt)
        {
            if (fmt == 3)   // RGB32, снизу вверх
            {
                for (int y = 0; y < h; y++) { uint* a = src + (long)(h - 1 - y) * w; uint* b = (uint*)d + (long)y * w; for (int x = 0; x < w; x++) b[x] = a[x]; }
                return;
            }
            if (fmt == 2)   // RGB24, снизу вверх
            {
                for (int y = 0; y < h; y++)
                {
                    uint* a = src + (long)(h - 1 - y) * w; byte* b = d + (long)y * w * 3;
                    for (int x = 0; x < w; x++) { uint p = a[x]; b[0] = (byte)p; b[1] = (byte)(p >> 8); b[2] = (byte)(p >> 16); b += 3; }
                }
                return;
            }
            if (fmt == 1)   // YUY2: Y0 U Y1 V на каждые две точки
            {
                for (int y = 0; y < h; y++)
                {
                    uint* a = src + (long)y * w; byte* b = d + (long)y * w * 2;
                    for (int x = 0; x < w; x += 2)
                    {
                        uint p0 = a[x], p1 = a[x + 1];
                        int r0 = (int)(p0 >> 16) & 255, g0 = (int)(p0 >> 8) & 255, b0 = (int)p0 & 255;
                        int r1 = (int)(p1 >> 16) & 255, g1 = (int)(p1 >> 8) & 255, b1 = (int)p1 & 255;
                        int r = (r0 + r1) >> 1, g = (g0 + g1) >> 1, bb = (b0 + b1) >> 1;
                        b[0] = (byte)Y(r0, g0, b0); b[1] = (byte)U(r, g, bb); b[2] = (byte)Y(r1, g1, b1); b[3] = (byte)V(r, g, bb);
                        b += 4;
                    }
                }
                return;
            }
            // I420: плоскость Y, затем U и V в четверть размера
            byte* yp = d, up = d + (long)w * h, vp = up + (long)w * h / 4;
            for (int y = 0; y < h; y++)
            {
                uint* a = src + (long)y * w; byte* yr = yp + (long)y * w;
                for (int x = 0; x < w; x++) { uint p = a[x]; yr[x] = (byte)Y((int)(p >> 16) & 255, (int)(p >> 8) & 255, (int)p & 255); }
            }
            int cw = w / 2;
            for (int y = 0; y < h / 2; y++)
            {
                uint* a0 = src + (long)(y * 2) * w, a1 = a0 + w;
                for (int x = 0; x < cw; x++)
                {
                    uint p00 = a0[x * 2], p01 = a0[x * 2 + 1], p10 = a1[x * 2], p11 = a1[x * 2 + 1];
                    int r = (int)(((p00 >> 16) & 255) + ((p01 >> 16) & 255) + ((p10 >> 16) & 255) + ((p11 >> 16) & 255)) >> 2;
                    int g = (int)(((p00 >> 8) & 255) + ((p01 >> 8) & 255) + ((p10 >> 8) & 255) + ((p11 >> 8) & 255)) >> 2;
                    int b = (int)((p00 & 255) + (p01 & 255) + (p10 & 255) + (p11 & 255)) >> 2;
                    up[(long)y * cw + x] = (byte)U(r, g, b); vp[(long)y * cw + x] = (byte)V(r, g, b);
                }
            }
        }

        public void Dispose()
        {
            if (view != null) view.Dispose();
            if (map != null) map.Dispose();
            if (tmp != IntPtr.Zero) { Marshal.FreeHGlobal(tmp); tmp = IntPtr.Zero; }
        }
    }
    // ------------------------------------------------------------------ собственный распределитель памяти
    // Получатели вроде Chrome и ffmpeg копируют кадр прямо в Receive, поэтому буферы просто ходят по кругу.
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public class CamAllocator : IMemAllocator
    {
        readonly CamSample[] samples;
        readonly bool[] busy;
        int next;
        readonly int size;
        internal CamAllocator(int size)
        {
            this.size = size;
            samples = new CamSample[3]; busy = new bool[3];
            for (int i = 0; i < 3; i++) samples[i] = new CamSample(this, size);
        }
        internal CamSample Next()
        {
            lock (this)
            {
                for (int i = 0; i < 3; i++) { int j = (next + i) % 3; if (!busy[j]) { busy[j] = true; next = (j + 1) % 3; return samples[j]; } }
                return null;
            }
        }
        internal void Done(CamSample s) { lock (this) for (int i = 0; i < 3; i++) if (samples[i] == s) busy[i] = false; }

        public int SetProperties(IntPtr req, IntPtr act) { if (act != IntPtr.Zero) Marshal.StructureToPtr(new AllocProps { cBuffers = 3, cbBuffer = size, cbAlign = 1 }, act, false); return G.S_OK; }
        public int GetProperties(IntPtr p) { if (p != IntPtr.Zero) Marshal.StructureToPtr(new AllocProps { cBuffers = 3, cbBuffer = size, cbAlign = 1 }, p, false); return G.S_OK; }
        public int Commit() { return G.S_OK; }
        public int Decommit() { return G.S_OK; }
        public int GetBuffer(out IntPtr pp, IntPtr a, IntPtr b, int flags)
        {
            CamSample s = Next();
            if (s == null) { pp = IntPtr.Zero; return G.VFW_E_TIMEOUT; }
            Done(s);
            pp = Marshal.GetComInterfaceForObject(s, typeof(IMediaSample));
            return G.S_OK;
        }
        public int ReleaseBuffer(IntPtr p) { return G.S_OK; }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public class CamSample : IMediaSample
    {
        readonly IntPtr buf; readonly int size;
        int actual; long tStart, tEnd; bool hasTime, sync, disc;
        internal CamSample(CamAllocator a, int size) { this.size = size; buf = Marshal.AllocHGlobal(size); actual = size; }
        ~CamSample() { Marshal.FreeHGlobal(buf); }
        public int GetPointer(out IntPtr p) { p = buf; return G.S_OK; }
        public int GetSize() { return size; }
        public int GetTime(out long s, out long e) { s = tStart; e = tEnd; return hasTime ? G.S_OK : unchecked((int)0x80040249); }
        public int SetTime(IntPtr s, IntPtr e)
        {
            if (s == IntPtr.Zero) { hasTime = false; return G.S_OK; }
            tStart = Marshal.ReadInt64(s); tEnd = e != IntPtr.Zero ? Marshal.ReadInt64(e) : tStart + 1; hasTime = true;
            return G.S_OK;
        }
        public int IsSyncPoint() { return sync ? G.S_OK : G.S_FALSE; }
        public int SetSyncPoint(int b) { sync = b != 0; return G.S_OK; }
        public int IsPreroll() { return G.S_FALSE; }
        public int SetPreroll(int b) { return G.S_OK; }
        public int GetActualDataLength() { return actual; }
        public int SetActualDataLength(int len) { if (len > size) return G.E_INVALIDARG; actual = len; return G.S_OK; }
        public int GetMediaType(out IntPtr p) { p = IntPtr.Zero; return G.S_FALSE; }
        public int SetMediaType(IntPtr p) { return G.S_OK; }
        public int IsDiscontinuity() { return disc ? G.S_OK : G.S_FALSE; }
        public int SetDiscontinuity(int b) { disc = b != 0; return G.S_OK; }
        public int GetMediaTime(out long s, out long e) { s = e = 0; return unchecked((int)0x80040251); }
        public int SetMediaTime(IntPtr s, IntPtr e) { return G.S_OK; }
    }

    // ------------------------------------------------------------------ перечислители
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public class PinEnum : IEnumPins
    {
        readonly CamFilter f; int pos;
        internal PinEnum(CamFilter f, int pos) { this.f = f; this.pos = pos; }
        public int Next(int c, IntPtr pp, IntPtr fetched)
        {
            int n = 0;
            if (c > 0 && pos == 0 && pp != IntPtr.Zero) { Marshal.WriteIntPtr(pp, Marshal.GetComInterfaceForObject(f.pin, typeof(IPin))); pos = 1; n = 1; }
            if (fetched != IntPtr.Zero) Marshal.WriteInt32(fetched, n);
            return n == c ? G.S_OK : G.S_FALSE;
        }
        public int Skip(int c) { pos += c; return pos <= 1 ? G.S_OK : G.S_FALSE; }
        public int Reset() { pos = 0; return G.S_OK; }
        public int Clone(out IntPtr e) { e = Marshal.GetComInterfaceForObject(new PinEnum(f, pos), typeof(IEnumPins)); return G.S_OK; }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public class TypeEnum : IEnumMediaTypes
    {
        readonly int[][] modes; int pos;
        internal TypeEnum(int[][] modes, int pos) { this.modes = modes; this.pos = pos; }
        public int Next(int c, IntPtr pp, IntPtr fetched)
        {
            int n = 0;
            while (n < c && pos < modes.Length && pp != IntPtr.Zero)
            {
                Marshal.WriteIntPtr(pp, n * IntPtr.Size, G.NewMediaType(modes[pos][0], modes[pos][1], modes[pos][2], modes[pos][3]));
                n++; pos++;
            }
            if (fetched != IntPtr.Zero) Marshal.WriteInt32(fetched, n);
            return n == c ? G.S_OK : G.S_FALSE;
        }
        public int Skip(int c) { pos += c; return pos <= modes.Length ? G.S_OK : G.S_FALSE; }
        public int Reset() { pos = 0; return G.S_OK; }
        public int Clone(out IntPtr e) { e = Marshal.GetComInterfaceForObject(new TypeEnum(modes, pos), typeof(IEnumMediaTypes)); return G.S_OK; }
    }
}
