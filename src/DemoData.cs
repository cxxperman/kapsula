// Kapsula — «остров» для Windows. Часть исходного кода: DemoData.cs
// Демо-режим (ключ --demo) — для снимков в описание: на всех вкладках выдуманные данные, а не ваши.
// Буфер обмена не читается (в истории — придуманные записи), погода не скачивается (выдуманный город
// и прогноз), сети Wi-Fi и устройства Bluetooth — тоже придуманные. Так снимок любой вкладки можно
// выкладывать: ничего личного на него не попадёт.
using System;
using System.Collections.Generic;

namespace Kapsula
{
    partial class IslandWindow
    {
        const string DemoText = "kapsula.txt — остров для Windows: музыка, заметки, буфер и зверёк в одной капсуле.";

        // KAPSULA_DEMOLONG вместе с --demo — проверка вёрстки «неудобными» данными (по ним проходят проверки
        // KAPSULA_RIDEPET=11, 12 и 13): =1 — очень длинные тексты (трек, буфер, город, сети, устройства, события),
        // чтобы было видно, где текст обрезается или вылезает; =2 — короткие и необычные: имя из одной буквы, эмодзи,
        // пустой исполнитель и встреча без темы, трек на два часа, мороз и штиль; =3 — пустые списки
        static readonly string DemoStress = Environment.GetEnvironmentVariable("KAPSULA_DEMOLONG") ?? "";
        static readonly bool DemoLong = DemoStress == "1", DemoEdge = DemoStress == "2", DemoEmpty = DemoStress == "3";

        // Из блока --demo при запуске: история буфера и погода
        void DemoSeed()
        {
            clips.Clear();
            DateTime now = DateTime.Now;
            // встреча — через пять минут от запуска: в капсуле всегда виден отсчёт, когда бы проверку ни запустили
            DateTime soon = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(5);
            if (DemoLong)
            {
                media.Title = "Очень длинное название трека, которое не помещается в одну строку (Live at Kyiv Arena 2026)";
                media.Artist = "Оркестр имени длинного названия feat. Ещё один исполнитель и друзья";
                RenderMedia();
                clips.Add(new ClipItem { Text = "Позвонить в сервисный центр по поводу гарантийного ремонта ноутбука и уточнить, когда его можно забрать, — номер заявки 2026-11-0457", Source = "Почта — входящие от сервисного центра", At = now.AddMinutes(-1) });
                clips.Add(new ClipItem { Text = "https://www.example.com/очень/длинный/адрес/страницы/с/параметрами?utm_source=kapsula&utm_campaign=проверка_вёрстки", Source = "Браузер", At = now.AddMinutes(-7) });
                clips.Add(new ClipItem { Text = "Суперкалифраджилистикэкспиалидоцистическийтекстбезпробелов", Source = "Заметки", At = now.AddHours(-3) });
                Calendar.TestSet(new List<CalEvent>
                {
                    new CalEvent { Subject = "Еженедельная встреча команды разработки по планированию следующего выпуска", Location = "Большая переговорная на третьем этаже", Start = soon },
                    new CalEvent { Subject = "День рождения Константина Константиновича", Start = now.Date.AddDays(1), AllDay = true }
                });
            }
            if (DemoEdge)
            {
                // без обложки и исполнителя, а трек — на два часа (время с часами)
                media = new MediaInfo { Has = true, Playing = true, Title = "Я", Artist = "", Pos = 65, Dur = 7265, PosAt = DateTime.UtcNow };
                RenderMedia();
                var many = new System.Text.StringBuilder();
                for (int i = 0; i < 70; i++) many.Append("очень много текста ");
                clips.Add(new ClipItem { Text = "1", Source = "Калькулятор", At = now.AddMinutes(-1) });
                clips.Add(new ClipItem { Text = "ок", Source = "Telegram", At = now.AddMinutes(-3) });
                clips.Add(new ClipItem { Text = "🔥🔥🔥", Source = "Discord", At = now.AddMinutes(-5) });
                clips.Add(new ClipItem { Text = "Двадцать один символ!", Source = "Заметки", At = now.AddMinutes(-8) });
                clips.Add(new ClipItem { Text = "первая строка\r\nвторая строка\r\nтретья строка", Source = "", At = now.AddMinutes(-12) });   // откуда — неизвестно
                clips.Add(new ClipItem { Text = many.ToString().Trim(), Source = "Word", At = now.AddDays(-1) });
                Calendar.TestSet(new List<CalEvent>
                {
                    // короткие темы и без места (пустую тему Calendar.Refresh сам заменяет на «Без названия»)
                    new CalEvent { Subject = "Обед", Location = "", Start = soon },
                    new CalEvent { Subject = "ДР", Location = "", Start = now.Date.AddDays(1), AllDay = true }
                });
            }
            if (DemoEmpty)
            {
                media = new MediaInfo();   // ничего не играет
                RenderMedia();
                Calendar.TestSet(new List<CalEvent>());
            }
            if (DemoEdge || DemoEmpty)
            {
                weather = DemoWeather();
                if (weatherGrid != null) RenderWeather();
                RenderClips();
                return;
            }
            clips.Add(new ClipItem { Text = "Встреча в 15:00, переговорная 2", Source = "Заметки", At = now.AddMinutes(-2) });
            clips.Add(new ClipItem { Text = "https://github.com/cxxperman/kapsula.txt", Source = "Браузер", At = now.AddMinutes(-9) });
            clips.Add(new ClipItem { Text = "#F08A4B", Source = "Пипетка", At = now.AddMinutes(-16) });
            clips.Add(new ClipItem { Text = "var pet = new PetView(2.6, Ink);", Source = "Редактор кода", At = now.AddMinutes(-34) });
            clips.Add(new ClipItem { Text = DemoText, Source = "Kapsula", At = now.AddHours(-2), Pinned = true });
            weather = DemoWeather();
            if (weatherGrid != null) RenderWeather();   // вкладка уже построена с пустой погодой
            RenderClips();   // список уже построен пустым — перерисовать
        }

        static string DemoDeg(double v) { return Math.Round(v).ToString("0").Replace("-", "−") + "°"; }

        // Прохладный осенний день: облачно, к вечеру небольшой дождь (в «неудобном» варианте — мороз, штиль и снег)
        static WeatherData DemoWeather()
        {
            var w = DemoEdge ? new WeatherData
            {
                // идёт снег — значит, и осадки за день не нулевые (100% и миллиметры воды)
                City = "Ош", Source = "демо", Temp = -21, Max = -18, Min = -27, Feels = -29, Wind = 0, WindDir = 0, Humidity = 100, Pressure = 790,
                Uv = 0, RainProb = 100, DewPoint = -23, PressureTrend = 0, UvTomorrow = 0, RainMm = 6.5, Sunrise = "08:59", Sunset = "15:51", IsDay = true, Code = 71
            } : new WeatherData
            {
                // длинное — настоящее название (поиск городов отдаёт одно название, без области): «Петропавловск-» / «Камчатский»
                City = DemoLong ? "Петропавловск-Камчатский" : "Нью-Йорк", Source = "демо", Temp = 8, Max = 10, Min = 3, Feels = 5, Wind = 4.2, WindDir = 225, Humidity = 72, Pressure = 758,
                Uv = 1.4, RainProb = 35, DewPoint = 3, PressureTrend = -0.6, UvTomorrow = 2, RainMm = 0.6, Sunrise = "06:41", Sunset = "16:37", IsDay = true, Code = 2
            };
            DateTime h0 = DateTime.Now.Date.AddHours(DateTime.Now.Hour);
            int[] codes = DemoEdge ? new int[] { 71, 71, 73, 73, 75, 71, 3, 3, 0, 0, 0, 71 } : new int[] { 2, 2, 3, 3, 61, 61, 3, 2, 1, 1, 2, 3 };
            double[] temps = DemoEdge ? new double[] { -21, -22, -22, -23, -24, -25, -25, -26, -27, -27, -26, -25 } : new double[] { 8, 9, 9, 8, 7, 7, 6, 5, 5, 4, 4, 3 };
            for (int i = 0; i < codes.Length; i++)
            {
                DateTime t = h0.AddHours(i);
                bool day = t.Hour >= 7 && t.Hour < 17;
                w.Hours.Add(new string[] { t.ToString("HH:mm"), Weather.Symbol(codes[i]), DemoDeg(temps[i]), codes[i].ToString(), day ? "1" : "0" });
            }
            var ru = new System.Globalization.CultureInfo("ru-RU");
            int[] dcodes = DemoEdge ? new int[] { 71, 73, 75, 3, 0, 0, 71 } : new int[] { 2, 61, 3, 1, 0, 3, 71 };
            double[] dmax = DemoEdge ? new double[] { -18, -20, -24, -19, -15, -12, -10 } : new double[] { 10, 9, 7, 8, 11, 6, 2 };
            double[] dmin = DemoEdge ? new double[] { -27, -29, -31, -28, -22, -20, -17 } : new double[] { 3, 4, 2, 1, 3, 0, -3 };
            for (int i = 0; i < dcodes.Length; i++)
            {
                string dn = ru.DateTimeFormat.GetAbbreviatedDayName(DateTime.Today.AddDays(i).DayOfWeek);
                w.Days.Add(new string[] { i == 0 ? "Сегодня" : char.ToUpper(dn[0]) + dn.Substring(1), Weather.Symbol(dcodes[i]), DemoDeg(dmax[i]), DemoDeg(dmin[i]), dcodes[i].ToString() });
            }
            return w;
        }

        // Сеть, Wi-Fi и Bluetooth на вкладке «Система»
        static ControlState DemoControl()
        {
            var c = new ControlState { NetUp = true, HasWifi = true, NetName = "Wi-Fi", WifiOn = true, BtOn = true };
            if (DemoEmpty) return c;   // Bluetooth включён, но связанных устройств нет
            if (DemoEdge)
            {
                c.Bt.Add(new BtDevice { Name = "🎧 Pixel Buds", Connected = true });
                c.Bt.Add(new BtDevice { Name = "JBL", Connected = false });
                c.BtBattery[c.Bt[0].Name] = 5;   // почти сели
                return c;
            }
            c.Bt.Add(new BtDevice { Name = DemoLong ? "Наушники Sony WH-1000XM5 (второй комплект, гостиная)" : "Наушники", Connected = true });
            c.Bt.Add(new BtDevice { Name = DemoLong ? "Портативная колонка JBL Charge 5 на кухне у окна" : "Колонка на кухне", Connected = false });
            c.BtBattery[c.Bt[0].Name] = 80;
            return c;
        }

        static List<WifiNet> DemoWifi()
        {
            if (DemoEmpty) return new List<WifiNet>();   // рядом ни одной сети
            if (DemoEdge)
                return new List<WifiNet>
                {
                    new WifiNet { Ssid = "📶 Дом", Signal = 100, Secure = true, Connected = true, Known = true },
                    new WifiNet { Ssid = "A", Signal = 3, Secure = false },
                    new WifiNet { Ssid = "TP-LINK_5G_3F2A", Signal = 50, Secure = true }   // скрытые сети (без имени) Net.cs не показывает
                };
            return new List<WifiNet>
            {
                new WifiNet { Ssid = DemoLong ? "Очень_длинное_имя_сети_Wi-Fi_5G_в_гостиной_на_втором_этаже" : "Kapsula Home", Signal = 92, Secure = true, Connected = true, Known = true },
                new WifiNet { Ssid = "Кофейня — гости", Signal = 64, Secure = false },
                new WifiNet { Ssid = "Office 5G", Signal = 41, Secure = true }
            };
        }
    }
}
