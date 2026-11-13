// Kapsula — «остров» для Windows. Часть исходного кода: Habits.cs
// Наряды за привычки. Зверёк получает аксессуары за то, что вы и так делаете: отошли передохнуть после долгой
// работы — повязка, отметили задачи на «Сегодня» — бабочка, слушали музыку — наушники, крутили помодоро — корона.
// Новый наряд приносит карточка, и зверёк сразу его надевает — везде: в капсуле, на краю панели, на окнах.
// Выбрать, что носить, можно в настройках («Внешний вид → Наряд зверька»).
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Kapsula
{
    partial class IslandWindow
    {
        public static readonly string[] OutfitIds = PetView.HabitIds;
        public static readonly string[] OutfitNames = { "Повязка", "Бабочка", "Наушники", "Корона" };
        static readonly string[] HabitKeys = { "habitBreaks", "habitTasks", "habitMusic", "habitFocus" };
        static readonly int[] HabitGoal = { 3, 10, 600, 10 };   // перерывы, задачи, минуты музыки, круги помодоро

        // Проверки (KAPSULA_HABITS «habitFocus=9,habitMusic=600»): счёт живёт только в памяти, настройки не трогаются
        Dictionary<string, string> habitMem;
        readonly List<UIElement> petOutfit = new List<UIElement>();   // наряд зверька в капсуле
        Canvas petCanvas;
        int petDressedAs, musicMs, outfitPending = -1;
        bool onBreak;

        void HabitsInit()
        {
            string t = Environment.GetEnvironmentVariable("KAPSULA_HABITS");
            if (t != null)
            {
                habitMem = new Dictionary<string, string>();
                foreach (string kv in t.Split(','))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0) habitMem[kv.Substring(0, eq).Trim()] = kv.Substring(eq + 1).Trim();
                }
            }
            ApplyOutfit();
        }

        string HGet(string key) { if (habitMem == null) return Store.Get(key); string v; return habitMem.TryGetValue(key, out v) ? v : ""; }
        void HSet(string key, string v) { if (habitMem == null) Store.Set(key, v); else habitMem[key] = v; }

        int HabitCount(int i) { int n; return int.TryParse(HGet(HabitKeys[i]), out n) ? n : 0; }
        public bool OutfitHave(int i) { return HabitCount(i) >= HabitGoal[i]; }

        // За что даётся наряд — для карточки и настроек
        public static string OutfitFor(int i)
        {
            switch (i) { case 0: return "3 перерыва после долгой работы"; case 1: return "10 выполненных задач"; case 2: return "10 часов музыки"; default: return "10 кругов помодоро"; }
        }

        // «2 из 3», «4 ч из 10» или «есть»
        public string OutfitProgress(int i)
        {
            if (OutfitHave(i)) return "есть";
            int n = HabitCount(i);
            return i == 2 ? n / 60 + " ч из 10" : Math.Max(0, n) + " из " + HabitGoal[i];
        }

        void HabitAdd(int i, int by)
        {
            int was = HabitCount(i), now = Math.Max(0, was + by);
            HSet(HabitKeys[i], now.ToString());
            if (was < HabitGoal[i] && now >= HabitGoal[i]) OutfitUnlocked(i);
        }

        void OutfitUnlocked(int i)
        {
            HSet("outfitLast", OutfitIds[i]);
            ApplyOutfit();      // «Сам» — надевает только что полученный
            outfitPending = i;  // карточку покажем, как только на острове будет спокойно
            OutfitCardTry();
        }

        // Что сейчас носит зверёк: выбранное в настройках, а «Сам» — последний полученный наряд или сезонный
        string OutfitWearing()
        {
            string pick = HGet("petOutfit");
            if (pick == "none") return "none";
            if (pick == "season") return "";
            int k = Array.IndexOf(OutfitIds, pick);
            if (k >= 0 && OutfitHave(k)) return pick;
            k = Array.IndexOf(OutfitIds, HGet("outfitLast"));
            return k >= 0 && OutfitHave(k) ? OutfitIds[k] : "";
        }

        public string OutfitChoice() { return HGet("petOutfit"); }

        public void SetOutfit(string pick) { HSet("petOutfit", pick ?? ""); ApplyOutfit(); }

        public void ApplyOutfit()
        {
            PetView.Outfit = OutfitWearing();
            PetView.OutfitVersion++;   // все зверьки переоденутся на своём следующем шаге
            if (roomPet != null && page == PPet) RenderPetPage();   // уголок зверька открыт — обновить гардероб
        }

        // Зверёк в капсуле нарисован отдельно — переодеваем его сами
        void PetRedress()
        {
            petDressedAs = PetView.OutfitVersion;
            if (petCanvas == null) return;
            foreach (UIElement o in petOutfit) petCanvas.Children.Remove(o);
            petOutfit.Clear();
            int n = petCanvas.Children.Count;
            PetView.Dress(petCanvas, PetPx);
            for (int i = n; i < petCanvas.Children.Count; i++) petOutfit.Add(petCanvas.Children[i]);
        }

        // Раз в полсекунды: минуты музыки (сохраняются раз в пять минут) и отложенная карточка с нарядом
        void HabitTick()
        {
            if (media.Has && media.Playing)
            {
                musicMs += 500;
                if (musicMs >= 300000) { musicMs -= 300000; HabitAdd(2, 5); }
            }
            if (outfitPending >= 0) OutfitCardTry();
        }

        void OutfitCardTry() { OutfitCardTry(false); }

        void OutfitCardTry(bool now)   // now — для проверок: не ждать, пока Kapsula «проснётся» после запуска
        {
            if (outfitPending < 0 || state != 0 || toastOn || hiddenFs || QuietNow || StartingUp() && !now) return;
            int i = outfitPending;
            outfitPending = -1;
            ShowCardEx(null, "", "Новый наряд: " + OutfitNames[i].ToLower(), "За " + OutfitFor(i) + " · зверёк уже примерил", null, null, null, false);
            if (state != 3) return;
            HideCardButton();
            CardPet("happy");   // зверёк на карточке — уже в обновке
            Happy(4000);
            cardUntil = Environment.TickCount + 7000;
        }
    }
}
