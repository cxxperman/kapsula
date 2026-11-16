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
            // имя — короткой надписью (в большом канале люди ходят часто — там без надписей).
            // Разность — без знака: ноль в начале и давняя метка — «давно» и тогда, когда счётчик Windows ушёл в минус
            if (name.Trim().Length == 0 || count > 8 || state != 0 || unchecked((uint)(now - friendToastAt)) < 2500u) return;
            friendToastAt = now;
            Toast("", (join ? "Привет, " : "Пока, ") + FriendShortName(name) + "!", null, -1, null);   // от лица зверька: он и машет
        }

        // Длинное имя — первыми словами, сколько влезает (обычно это и есть имя), а не обрубком с «…». Значок или тег
        // клана впереди («🎮 Геймер…», «[RU] Дима…») пропускаем — имя начинается с первого настоящего слова; на разделителе
        // («Дима | стример») останавливаемся, чтобы он не повис в конце. Одно очень длинное слово капсула дорежет сама
        static string FriendShortName(string name)
        {
            string who = (name ?? "").Trim();
            if (who.Length <= 20) return who;
            string[] words = who.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int first = 0;
            while (first < words.Length && FriendWordKind(words[first]) != 0) first++;
            if (first == words.Length) first = 0;   // имя из одних значков — как есть, с начала
            who = words[first];
            for (int k = first + 1; k < words.Length && FriendWordKind(words[k]) != 2 && who.Length + 1 + words[k].Length <= 20; k++) who += " " + words[k];
            return who;
        }

        // Слово из имени: 0 — часть имени, 1 — значок или тег в скобках («🎮», «★», «[RU]», «(CLAN)»), 2 — разделитель («|», «—», «·», «/»)
        static int FriendWordKind(string w)
        {
            bool letters = false, punct = true;
            foreach (char c in w)
            {
                if (char.IsLetterOrDigit(c)) letters = true;
                if (!char.IsPunctuation(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.MathSymbol) punct = false;
            }
            if (!letters) return punct ? 2 : 1;
            return w.Length > 2 && "[({<【「|".IndexOf(w[0]) >= 0 && "])}>】」|".IndexOf(w[w.Length - 1]) >= 0 ? 1 : 0;
        }

        // Последнюю задачу на сегодня отметили — праздник (раз в день и если задач было хотя бы две)
        void PartyCheck(List<DayTask> cur, bool plusOne)
        {
            DateTime today = DateTime.Today;
            string key = today.ToString("yyyyMMdd");
            if (Store.Get("partyDay") == key) return;
            // Отмеченная повторяющаяся задача уезжает на следующий раз и из списка дня пропадает — её сегодняшние
            // разы считаем отдельно (plusOne — только что отмеченный), иначе день из одних повторяющихся не празднуется
            int open = 0, done = DayCount(Store.Get("tasksDoneDay"), key);
            if (plusOne) { done++; Store.Set("tasksDoneDay", key + ":" + done); }
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

        // Счётчик за день вида «20261114:3»: сколько раз за этот день; запись за другой день (или пустая) — ноль
        static int DayCount(string stored, string day)
        {
            int n;
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith(day + ":", StringComparison.Ordinal) || !int.TryParse(stored.Substring(day.Length + 1), out n)) return 0;
            return Math.Max(0, n);
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
        int riderDigUntil;   // до какого момента следующее письмо — «из кучи»: не отдал его сразу — потом, ради одного письма, раскопки не к месту

        void LetterCounted()
        {
            int now = Environment.TickCount;
            noteTimes.Add(now);
            noteTimes.RemoveAll(delegate(int x) { return unchecked(now - x) > 60000; });
            if (noteTimes.Count < 5 || QuietNow || buriedAt != 0 && unchecked(now - buriedAt) < 600000) return;   // не чаще раза в десять минут
            buriedAt = now | 1; noteTimes.Clear();
            riderDigUntil = now + 20000; if (riderDigUntil == 0) riderDigUntil = 1;   // следующее письмо он не отдаёт, а выкапывается из кучи
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
            if (Until(happyUntil)) return "happy";
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
