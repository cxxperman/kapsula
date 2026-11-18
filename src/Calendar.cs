// Kapsula — «остров» для Windows. Часть исходного кода: Calendar.cs
// Ближайшие события из календаря Windows (того же, что показывает приложение «Календарь» и панель часов).
using System;
using System.Collections.Generic;
using System.Threading;
using Windows.ApplicationModel.Appointments;

namespace Kapsula
{
    // название и место — не null: без места (событие из проверки, день рождения) подсказки и список дня не падают
    // End — когда событие кончается; у выдуманных событий из проверок его нет (значение по умолчанию)
    class CalEvent { public string Subject = "", Location = ""; public DateTime Start, End; public bool AllDay; }

    // Читается только то, что уже лежит в календаре Windows на этом компьютере; в сеть Kapsula за ним не ходит.
    static class Calendar
    {
        static List<CalEvent> events = new List<CalEvent>();
        static int loadedAt, running;
        public static volatile bool Failed;
        // Календарь прочитан: Windows дала к нему доступ (или события подставлены проверкой). Без этого остров не говорит
        // другим приложениям «событий нет» — он просто не знает
        public static volatile bool Loaded;
        // Растёт после каждой удачной загрузки: открытая вкладка «Сегодня» по нему видит, что события дочитались
        public static int Version;
        static readonly System.Globalization.CultureInfo Ru = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");

        static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            var done = new ManualResetEvent(false);
            op.Completed = delegate { done.Set(); };
            if (!done.WaitOne(8000)) throw new TimeoutException();
            return op.GetResults();
        }

        public static List<CalEvent> Events() { lock (typeof(Calendar)) return new List<CalEvent>(events); }

        // Только для проверок (отдельная папка настроек): выдуманные события вместо настоящего календаря
        public static void TestSet(List<CalEvent> list) { if (!Store.Test) return; lock (typeof(Calendar)) events = new List<CalEvent>(list); Loaded = true; Interlocked.Increment(ref Version); }

        // Перечитать события — с запасом на месяц назад и три вперёд, для календаря во вкладке «Сегодня» (не чаще раза в пять минут)
        public static void Refresh(bool force)
        {
            if (Store.Test) return;   // в проверках настоящий календарь не читаем — на снимках только выдуманное
            int now = Environment.TickCount;
            if (!force && loadedAt != 0 && unchecked(now - loadedAt) < 5 * 60000) return;
            if (Interlocked.Exchange(ref running, 1) != 0) return;
            loadedAt = now | 1;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    AppointmentStore store = Wait(AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly));
                    var list = new List<CalEvent>();
                    if (store != null)
                        foreach (Appointment a in Wait(store.FindAppointmentsAsync(DateTimeOffset.Now.Date.AddDays(-40), TimeSpan.FromDays(110))))
                        {
                            if (a.IsCanceledMeeting) continue;
                            // название и место бывают пробелом или адресом в несколько строк (вставлен из почты) — одной строкой:
                            // без пустой строки под названием и без висячего «·» в подсказках; пустое название — «Без названия»
                            string subj = System.Text.RegularExpressions.Regex.Replace(a.Subject ?? "", "\\s+", " ").Trim();
                            string loc = System.Text.RegularExpressions.Regex.Replace(a.Location ?? "", "\\s+", " ").Trim();
                            list.Add(new CalEvent { Subject = subj.Length > 0 ? subj : "Без названия", Location = loc, Start = a.StartTime.LocalDateTime, End = a.StartTime.LocalDateTime + a.Duration, AllDay = a.AllDay });
                        }
                    list.Sort(delegate(CalEvent x, CalEvent y) { return x.Start.CompareTo(y.Start); });
                    lock (typeof(Calendar)) events = list;
                    Loaded = store != null;
                    Interlocked.Increment(ref Version);
                    Failed = false;
                }
                catch { Failed = true; }
                finally { running = 0; }
            });
        }

        // «сегодня в 15:00», «завтра в 9:30», «сегодня, весь день», «15 ноября в 10:00» — месяц всегда по-русски,
        // как и остальной остров, даже если в Windows английский или украинский формат дат
        public static string When(CalEvent e, DateTime now)
        {
            string day = e.Start.Date == now.Date ? "сегодня" : e.Start.Date == now.Date.AddDays(1) ? "завтра" : e.Start.ToString("d MMMM", Ru);
            return e.AllDay ? day + ", весь день" : day + " в " + e.Start.ToString("H:mm", Ru);
        }

        // «через 10 мин», «через 1 ч 5 мин», «сейчас»
        public static string In(DateTime start, DateTime now)
        {
            int min = (int)Math.Ceiling((start - now).TotalMinutes);
            if (min <= 0) return "сейчас";
            if (min < 60) return "через " + min + " мин";
            return "через " + (min / 60) + " ч" + (min % 60 > 0 ? " " + (min % 60) + " мин" : "");
        }
    }
}
