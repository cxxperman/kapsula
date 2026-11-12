// Kapsula — «остров» для Windows. Часть исходного кода: Calendar.cs
// Ближайшие события из календаря Windows (того же, что показывает приложение «Календарь» и панель часов).
using System;
using System.Collections.Generic;
using System.Threading;
using Windows.ApplicationModel.Appointments;

namespace Kapsula
{
    class CalEvent { public string Subject, Location; public DateTime Start; public bool AllDay; }

    // Читается только то, что уже лежит в календаре Windows на этом компьютере; в сеть Kapsula за ним не ходит.
    static class Calendar
    {
        static List<CalEvent> events = new List<CalEvent>();
        static int loadedAt, running;
        public static volatile bool Failed;

        static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            var done = new ManualResetEvent(false);
            op.Completed = delegate { done.Set(); };
            if (!done.WaitOne(8000)) throw new TimeoutException();
            return op.GetResults();
        }

        public static List<CalEvent> Events() { lock (typeof(Calendar)) return new List<CalEvent>(events); }

        // Перечитать события на ближайшие двое суток (не чаще раза в пять минут)
        public static void Refresh(bool force)
        {
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
                        foreach (Appointment a in Wait(store.FindAppointmentsAsync(DateTimeOffset.Now.AddMinutes(-5), TimeSpan.FromHours(48))))
                        {
                            if (a.IsCanceledMeeting) continue;
                            list.Add(new CalEvent { Subject = string.IsNullOrEmpty(a.Subject) ? "Без названия" : a.Subject, Location = a.Location ?? "", Start = a.StartTime.LocalDateTime, AllDay = a.AllDay });
                        }
                    list.Sort(delegate(CalEvent x, CalEvent y) { return x.Start.CompareTo(y.Start); });
                    lock (typeof(Calendar)) events = list;
                    Failed = false;
                }
                catch { Failed = true; }
                finally { running = 0; }
            });
        }

        // «сегодня в 15:00», «завтра в 9:30», «сегодня, весь день»
        public static string When(CalEvent e, DateTime now)
        {
            string day = e.Start.Date == now.Date ? "сегодня" : e.Start.Date == now.Date.AddDays(1) ? "завтра" : e.Start.ToString("d MMMM");
            return e.AllDay ? day + ", весь день" : day + " в " + e.Start.ToString("H:mm");
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
