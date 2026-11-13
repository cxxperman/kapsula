// Kapsula — «остров» для Windows. Часть исходного кода: Reactions.cs
// Зверёк реагирует на события системы.
// Наушники: Windows переключила звук на наушники или гарнитуру (Bluetooth, провод, USB) — зверёк в капсуле надевает
// наушники и пританцовывает, а капсула их называет. Bluetooth-наушники с зарядом приходят карточкой — туда зверёк
// приносит наушники сам. Отключили — «Звук — Динамики».
// Снимок экрана: файл в папке «Снимки экрана» (Win + PrtScn, «Ножницы») или снимок в буфере (PrtScn, Win + Shift + S) —
// зверёк щурится от вспышки, как после снимка самой Kapsula.
// Интернет (по словам самой Windows): пропал — «Нет интернета», зверёк встревожен; вернулся — радуется.
// Память: панель закрыта две минуты — неиспользуемое отдаём системе.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using WF = System.Windows.Forms;

namespace Kapsula
{
    static class AudioEndpoint
    {
        static PROPERTYKEY NameKey = new PROPERTYKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
        static PROPERTYKEY FormFactorKey = new PROPERTYKEY { fmtid = new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), pid = 0 };
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);
        static readonly int bornAt = Environment.TickCount;

        static IMMDeviceEnumerator enumerator;   // один на всё время: опрашиваем раз в две секунды
        static readonly string Test = Environment.GetEnvironmentVariable("KAPSULA_TEST_HP");   // проверка: через 22 с «подключаются» наушники с таким именем

        static IMMDeviceEnumerator En() { if (enumerator == null) enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorCom()); return enumerator; }

        // id основного устройства вывода — дёшево; имя и вид читаем, только когда он сменился
        public static string DefaultId()
        {
            if (Test != null) return unchecked(Environment.TickCount - bornAt) > 22000 ? "test-headphones" : "test-speakers";
            IMMDevice d = null;
            try
            {
                string id;
                if (En().GetDefaultAudioEndpoint(0, 1, out d) != 0 || d == null || d.GetId(out id) != 0) return null;
                return id;
            }
            catch { return null; }
            finally { if (d != null) try { Marshal.ReleaseComObject(d); } catch { } }
        }

        // Имя устройства и наушники ли это (по виду устройства: «наушники» или «гарнитура»)
        public static void Describe(string id, out string name, out bool headphones)
        {
            name = ""; headphones = false;
            if (Test != null) { headphones = id == "test-headphones"; name = headphones ? Test : "Динамики (Realtek(R) Audio)"; return; }
            object dev = null, store = null;
            try
            {
                IMMDevice d;
                if (En().GetDevice(id, out d) != 0 || d == null) return;
                dev = d;
                IPropertyStore ps;
                if (d.OpenPropertyStore(0, out ps) == 0 && ps != null)
                {
                    store = ps;
                    PROPVARIANT v;
                    PROPERTYKEY k = NameKey;
                    if (ps.GetValue(ref k, out v) == 0)
                        try { if (v.vt == 31 && v.p1 != IntPtr.Zero) name = Marshal.PtrToStringUni(v.p1) ?? ""; }
                        finally { PropVariantClear(ref v); }
                    k = FormFactorKey;
                    if (ps.GetValue(ref k, out v) == 0)
                        try { if (v.vt == 19) { long ff = v.p1.ToInt64() & 0xFFFFFFFF; headphones = ff == 3 || ff == 5; } }   // 3 — наушники, 5 — гарнитура
                        finally { PropVariantClear(ref v); }
                }
            }
            catch { }
            finally
            {
                foreach (object o in new[] { store, dev }) if (o != null) try { Marshal.ReleaseComObject(o); } catch { }
            }
        }

        // «Наушники (AirPods Pro Stereo)» → «AirPods Pro»; «Наушники (Realtek(R) Audio)» — просто наушники (null)
        public static string Pretty(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int a = name.IndexOf('('), b = name.LastIndexOf(')');
            string inner = a >= 0 && b > a ? name.Substring(a + 1, b - a - 1) : name;
            inner = Regex.Replace(inner, @"\s+(Stereo|Hands-Free.*|Стерео|Головной телефон.*)$", "", RegexOptions.IgnoreCase).Trim();
            if (inner.Length == 0 || Regex.IsMatch(inner, @"Realtek|High Definition|Audio Device|USB Audio|^Audio$|Аудио", RegexOptions.IgnoreCase)) return null;
            return inner.Length > 24 ? inner.Substring(0, 23) + "…" : inner;
        }
    }

    partial class IslandWindow
    {
        string hpId, hpPendingName;
        bool hpWas, hpKnown;
        int hpPendingAt, petBudsUntil;

        // Раз в две секунды: куда Windows выводит звук. Переключила на наушники — зверёк их надевает
        void HeadphonesTick()
        {
            string id = AudioEndpoint.DefaultId();
            if (id == null || hpKnown && id == hpId && hpPendingAt == 0) return;   // ничего не сменилось — и читать нечего
            string name; bool hp;
            AudioEndpoint.Describe(id, out name, out hp);
            int now = Environment.TickCount;
            if (!hpKnown) { hpKnown = true; hpId = id; hpWas = hp; return; }   // что было при запуске — не новость
            if (id != hpId)
            {
                bool was = hpWas;
                hpId = id; hpWas = hp;
                // подождём пару секунд: Bluetooth-наушники с зарядом могут прийти карточкой — тогда зверёк понесёт наушники туда
                if (hp) { hpPendingAt = now + 2500; if (hpPendingAt == 0) hpPendingAt = 1; hpPendingName = name; }
                else
                {
                    hpPendingAt = 0;
                    if (was && state == 0 && !StartingUp() && !QuietNow) Toast("", "Звук — " + AudioOut.Short(name), null, -1, null);
                }
            }
            if (hpPendingAt != 0 && unchecked(now - hpPendingAt) >= 0)
            {
                hpPendingAt = 0;
                bool card = budsCardAt != 0 && unchecked(now - budsCardAt) < 15000 || budsAnnounceUntil != 0;
                if (card || state != 0 || StartingUp() || QuietNow) return;
                string pretty = AudioEndpoint.Pretty(hpPendingName);
                petBudsUntil = now + 3600; if (petBudsUntil == 0) petBudsUntil = 1;
                Toast("", pretty != null ? "Наушники · " + pretty : "Наушники подключены", null, -1, null);
                toastUntil = now + 3600;
                RenderCompact();
            }
        }

        // ---- интернет: по словам самой Windows (как значок сети у часов). Пропал — зверёк встревожен и показывает
        // красный значок сети, вернулся — радуется с зелёным. Проверяем через четыре секунды после того, как Windows
        // сообщила о перемене: пока Wi-Fi переподключается, она на миг говорит «нет интернета» — это не новость.
        bool netKnown, netOnline = true;
        int netPendingAt, netCheckedAt, petNetUntil;
        static readonly string TestNet = Environment.GetEnvironmentVariable("KAPSULA_TEST_NET");   // проверка: «22-30» — нет интернета с 22-й по 30-ю секунду
        static readonly int netBorn = Environment.TickCount;

        void NetWatchStart()
        {
            try { Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged += delegate { Dispatcher.BeginInvoke(new Action(delegate { NetSoon(4000); })); }; }
            catch (Exception ex) { Store.LogError("сеть: подписка на перемены", ex); }
            NetSoon(0);   // что сейчас — запомнить без реакции
        }

        void NetSoon(int ms) { netPendingAt = Environment.TickCount + ms; if (netPendingAt == 0) netPendingAt = 1; }

        static bool Online()
        {
            if (TestNet != null)
            {
                string[] p = TestNet.Split('-');
                double s = unchecked(Environment.TickCount - netBorn) / 1000.0, a, b;
                return !(p.Length == 2 && double.TryParse(p[0], out a) && double.TryParse(p[1], out b) && s >= a && s < b);
            }
            try
            {
                var prof = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
                return prof != null && prof.GetNetworkConnectivityLevel() == Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess;
            }
            catch { return true; }   // не знаем — значит, не тревожим
        }

        // Раз в две секунды: пора ли проверить (после сообщения Windows; на всякий случай — и раз в полминуты).
        // Сам вопрос к Windows — в фоне: она может отвечать не сразу, а окно острова ждать не должно.
        int netCheckBusy;

        void NetTick()
        {
            int now = Environment.TickCount;
            if (TestNet != null || unchecked(now - netCheckedAt) > 30000) { if (netPendingAt == 0) NetSoon(0); }
            if (netPendingAt == 0 || unchecked(now - netPendingAt) < 0) return;
            if (System.Threading.Interlocked.Exchange(ref netCheckBusy, 1) != 0) return;   // прежняя проверка ещё идёт
            netPendingAt = 0; netCheckedAt = now;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool on = Online();
                Dispatcher.BeginInvoke(new Action(delegate { netCheckBusy = 0; NetResult(on); }));
            });
        }

        void NetResult(bool on)
        {
            int now = Environment.TickCount;
            if (TestNet != null) WalkLog("сеть: " + (unchecked(now - netBorn) / 1000.0).ToString("0.0") + " с, " + (on ? "есть" : "нет") + ", было " + (netOnline ? "есть" : "нет") + ", состояние " + state + (QuietNow ? ", тихо" : ""));
            if (!netKnown) { netKnown = true; netOnline = on; return; }
            if (on == netOnline) return;
            netOnline = on;
            if (StartingUp() || QuietNow || state != 0) return;
            int len = on ? 3200 : 4500;
            petNetUntil = now + len; if (petNetUntil == 0) petNetUntil = 1;
            Toast("", on ? "Интернет снова есть" : "Нет интернета", null, -1, null);
            toastUntil = now + len;
            RenderCompact();
        }

        // ---- обновления Windows установлены и ждут перезагрузки: раз в день, когда вы за компьютером и на острове тихо,
        // зверёк показывает значок «обновить» и капсула говорит об этом. Только чтение реестра (там Windows сама отмечает,
        // что нужна перезагрузка); в «Не беспокоить» и в играх — молчим.
        int updCheckedAt, petUpdUntil;
        static readonly bool TestUpdate = Environment.GetEnvironmentVariable("KAPSULA_TEST_UPDATE") == "1";

        static bool RebootPending()
        {
            if (TestUpdate) return true;
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                    return k != null;
            }
            catch { return false; }
        }

        // Раз в две секунды заглядываем, а в реестр — раз в десять минут (в проверке — сразу)
        void UpdateTick()
        {
            int now = Environment.TickCount;
            if (updCheckedAt != 0 && unchecked(now - updCheckedAt) < (TestUpdate ? 2000 : 600000)) return;
            if (StartingUp() || state != 0 || toastOn || QuietNow || hiddenFs || gameLite || Native.IdleMs() > 60000) return;   // подождём удобного момента
            updCheckedAt = now | 1;
            string today = DateTime.Today.ToString("yyyyMMdd");
            if (Store.Get("updateToldDay") == today || !RebootPending()) return;
            Store.Set("updateToldDay", today);
            petUpdUntil = now + 4500; if (petUpdUntil == 0) petUpdUntil = 1;
            Toast("", "Windows ждёт перезагрузки", null, -1, null);   // обновления установлены — короткой строкой, чтобы влезла в капсулу
            toastUntil = now + 4500;
            RenderCompact();
        }

        // ---- снимок экрана
        FileSystemWatcher shotWatch;
        int shotSeenAt;

        void ShotWatchStart()
        {
            try
            {
                string dir = Environment.GetEnvironmentVariable("KAPSULA_TEST_SHOTS") ?? ScreenshotsDir();   // проверка — своя папка
                if (dir == null || !Directory.Exists(dir)) return;
                shotWatch = new FileSystemWatcher(dir, "*.png") { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
                shotWatch.Created += delegate { Dispatcher.BeginInvoke(new Action(ShotSeen)); };
                shotWatch.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Store.LogError("папка снимков экрана", ex); }
        }

        // Зверёк щурится от вспышки. Один снимок часто приходит дважды — файлом и в буфер; свой снимок Kapsula и так «вспыхивает»
        void ShotSeen()
        {
            int now = Environment.TickCount;
            if (shotSeenAt != 0 && unchecked(now - shotSeenAt) < 3000 || Until(petFlashUntil)) return;
            shotSeenAt = now | 1;
            petFlashUntil = now + 4500;
            if (state == 0) RenderCompact();
        }

        // Картинка в буфере — похоже на снимок экрана: пришла из «Ножниц» и похожих программ или размером ровно с экран (PrtScn)
        bool ShotLike(int w, int h)
        {
            string src = (ClipSource() ?? "").ToLowerInvariant();
            foreach (string s in new[] { "snipping", "ножниц", "screenclipping", "screensketch", "набросок", "screenshot", "снимок экрана", "lightshot", "sharex", "greenshot", "flameshot" })
                if (src.Contains(s)) return true;
            foreach (WF.Screen sc in WF.Screen.AllScreens) if (sc.Bounds.Width == w && sc.Bounds.Height == h) return true;
            return SystemInformation_Virtual(w, h);
        }

        static bool SystemInformation_Virtual(int w, int h)
        {
            var v = WF.SystemInformation.VirtualScreen;
            return v.Width == w && v.Height == h;
        }

        // ---- память: пока остров долго в покое, неиспользуемое отдаём системе
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr process);

        static void TrimMemory()
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            using (var p = System.Diagnostics.Process.GetCurrentProcess()) EmptyWorkingSet(p.Handle);
        }

        // Раз в полминуты: панель давно закрыта (две минуты) — отдать системе то, что не нужно свёрнутому острову.
        // Остров при этом работает как работал: нужное ему Windows тут же возвращает, остальное — когда раскроют панель.
        // Не чаще раза в десять минут и только если есть что отдавать. В Диспетчере задач — около 40 МБ вместо 120.
        int trimAt, lastOpenAt;

        void TrimTick()
        {
            int now = Environment.TickCount;
            if (state != 0 || StartingUp()) return;
            if (lastOpenAt != 0 && unchecked(now - lastOpenAt) < 120000) return;   // панель недавно открывали — пусть всё будет под рукой
            if (trimAt != 0 && unchecked(now - trimAt) < 600000) return;
            long ws;
            using (var p = System.Diagnostics.Process.GetCurrentProcess()) ws = p.WorkingSet64;
            if (ws < (80L << 20)) return;
            trimAt = now | 1;
            if (TestRider != null) WalkLog("память: отдаём, рабочий набор " + (ws >> 20) + " МБ");
            TrimMemory();
            if (TestRider != null) WalkLog("память: отдали за " + unchecked(Environment.TickCount - now) + " мс");
        }

        // замер (проверка): память до и после
        static string MemNow()
        {
            using (var p = System.Diagnostics.Process.GetCurrentProcess())
                return "рабочий набор " + (p.WorkingSet64 >> 20) + " МБ, частная " + (p.PrivateMemorySize64 >> 20) + " МБ, куча .NET " + (GC.GetTotalMemory(false) >> 20) + " МБ";
        }

        // Известная папка «Снимки экрана» (обычно Изображения\Снимки экрана) — туда их кладут Windows и «Ножницы»
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        static string ScreenshotsDir()
        {
            try
            {
                IntPtr p;
                if (SHGetKnownFolderPath(new Guid("b7bede81-df94-4682-a7d8-57a52620b86f"), 0, IntPtr.Zero, out p) == 0)
                    try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }
    }
}
