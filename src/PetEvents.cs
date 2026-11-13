// Kapsula — «остров» для Windows. Часть исходного кода: PetEvents.cs
// Зверёк откликается на жизнь вокруг: в ваш голосовой канал Discord зашли — машет «привет», вышли — машет вслед;
// отметили последнюю задачу на сегодня — праздник с конфетти; за минуту пришло пять уведомлений — зверька
// завалило письмами, и он выкапывается. И он же — на пульте в телефоне, с тем же настроением, что на острове.
using System;
using System.Collections.Generic;

namespace Kapsula
{
    partial class IslandWindow
    {
        int petFriendUntil, petPartyUntil, friendToastAt;
        bool petFriendJoin;

        // Кто-то зашёл в ваш голосовой канал или вышел из него
        void FriendWave(string name, bool join, int count)
        {
            int now = Environment.TickCount;
            petFriendUntil = now + 2800; if (petFriendUntil == 0) petFriendUntil = 1;
            petFriendJoin = join;
            if (rider != null && rOut && !rFly) rider.Play(join ? "wave" : "bye", 1600);   // на раскрытом острове машет и попутчик
            // имя — короткой надписью (в большом канале люди ходят часто — там без надписей)
            if (name.Length == 0 || count > 8 || state != 0 || unchecked(now - friendToastAt) < 2500) return;
            friendToastAt = now;
            string who = name.Length > 18 ? name.Substring(0, 17) + "…" : name;
            Toast("", (join ? "Привет, " : "Пока, ") + who + "!", null, -1, null);   // от лица зверька: он и машет
        }

        // Последнюю задачу на сегодня отметили — праздник (раз в день и если задач было хотя бы две)
        void PartyCheck(List<DayTask> cur, bool plusOne)
        {
            DateTime today = DateTime.Today;
            string key = today.ToString("yyyyMMdd");
            if (Store.Get("partyDay") == key) return;
            int open = 0, done = plusOne ? 1 : 0;
            foreach (DayTask t in cur)
            {
                if (t.Day == today) { if (t.Done) done++; else open++; }
                else if (t.Day < today && !t.Done) open++;   // просроченное тоже должно быть сделано
            }
            if (open > 0 || done < 2) return;
            Store.Set("partyDay", key);
            if (rider != null && rOut && !rFly) rider.Play("party", 2600);   // на краю панели празднует попутчик
            partyPendingUntil = Environment.TickCount + 15000;   // надпись и конфетти в капсуле — когда панель закроют (если в ближайшие 15 секунд)
            PartyTick();
        }

        int partyPendingUntil;

        // Раз в полсекунды: панель закрыли — в капсуле «Всё на сегодня сделано!» и зверёк с конфетти
        void PartyTick()
        {
            if (partyPendingUntil == 0) return;
            int now = Environment.TickCount;
            if (unchecked(now - partyPendingUntil) >= 0) { partyPendingUntil = 0; return; }
            if (state != 0) return;
            partyPendingUntil = 0;
            petPartyUntil = now + 4200; if (petPartyUntil == 0) petPartyUntil = 1;
            Toast("", "Всё на сегодня сделано!", null, -1, accent);
        }

        // ---- завалило письмами: пять уведомлений за минуту — попутчик на карточке выкапывается из конвертов
        readonly List<int> noteTimes = new List<int>();
        int buriedAt;
        bool riderDigNext;

        void LetterCounted()
        {
            int now = Environment.TickCount;
            noteTimes.Add(now);
            noteTimes.RemoveAll(delegate(int x) { return unchecked(now - x) > 60000; });
            if (noteTimes.Count < 5 || QuietNow || buriedAt != 0 && unchecked(now - buriedAt) < 600000) return;   // не чаще раза в десять минут
            buriedAt = now | 1; noteTimes.Clear();
            riderDigNext = true;   // следующее письмо он не отдаёт, а выкапывается из кучи
        }

        // ---- зверёк на пульте в телефоне: страница раз в несколько секунд спрашивает, что он сейчас делает
        public string PhonePetMood()
        {
            if (!PetProps()) return "";
            if (Recorder.Active) return "rec";
            if (CallVisible()) return "call";
            if (Until(petLoveUntil)) return "love";
            if (Until(petPartyUntil)) return "party";
            if (claudeState == 2) return "ask";
            if (claudeState == 1 || chatBusy) return "work";
            if (unchecked(Environment.TickCount - happyUntil) < 0) return "happy";
            if (media.Has && media.Playing) return "dance";
            if (Native.IdleMs() > 120000) return "sleep";
            if (PetView.NightNow()) return "night";
            return "idle";
        }

        // Погладили зверька на экране телефона — он радуется и здесь
        public void PhonePetTap()
        {
            int now = Environment.TickCount;
            petLoveUntil = now + 12000; if (petLoveUntil == 0) petLoveUntil = 1;
            if (walkerOut && walker != null && walkPhase == 2) walker.Pet.Extend("purr", 1400);
            else if (rider != null && rOut && !rFly) rider.Extend("purr", 1400);
            else if (state == 0) PetAnimate();
        }
    }
}
