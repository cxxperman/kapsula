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
        const string DemoText = "Kapsula — остров для Windows: музыка, заметки, буфер и зверёк в одной капсуле.";

        // Из блока --demo при запуске: история буфера и погода
        void DemoSeed()
        {
            clips.Clear();
            DateTime now = DateTime.Now;
            clips.Add(new ClipItem { Text = "Встреча в 15:00, переговорная 2", Source = "Заметки", At = now.AddMinutes(-2) });
            clips.Add(new ClipItem { Text = "https://github.com/cxxperman/kapsula", Source = "Браузер", At = now.AddMinutes(-9) });
            clips.Add(new ClipItem { Text = "#F08A4B", Source = "Пипетка", At = now.AddMinutes(-16) });
            clips.Add(new ClipItem { Text = "var pet = new PetView(2.6, Ink);", Source = "Редактор кода", At = now.AddMinutes(-34) });
            clips.Add(new ClipItem { Text = DemoText, Source = "Kapsula", At = now.AddHours(-2), Pinned = true });
            weather = DemoWeather();
            if (weatherGrid != null) RenderWeather();   // вкладка уже построена с пустой погодой
            RenderClips();   // список уже построен пустым — перерисовать
        }

        static string DemoDeg(double v) { return Math.Round(v).ToString("0").Replace("-", "−") + "°"; }

        // Прохладный осенний день: облачно, к вечеру небольшой дождь
        static WeatherData DemoWeather()
        {
            var w = new WeatherData
            {
                City = "Нью-Йорк", Source = "демо", Temp = 8, Max = 10, Min = 3, Feels = 5, Wind = 4.2, WindDir = 225, Humidity = 72, Pressure = 758,
                Uv = 1.4, RainProb = 35, DewPoint = 3, PressureTrend = -0.6, UvTomorrow = 2, RainMm = 0.6, Sunrise = "06:41", Sunset = "16:37", IsDay = true, Code = 2
            };
            DateTime h0 = DateTime.Now.Date.AddHours(DateTime.Now.Hour);
            int[] codes = { 2, 2, 3, 3, 61, 61, 3, 2, 1, 1, 2, 3 };
            double[] temps = { 8, 9, 9, 8, 7, 7, 6, 5, 5, 4, 4, 3 };
            for (int i = 0; i < codes.Length; i++)
            {
                DateTime t = h0.AddHours(i);
                bool day = t.Hour >= 7 && t.Hour < 17;
                w.Hours.Add(new string[] { t.ToString("HH:mm"), Weather.Symbol(codes[i]), DemoDeg(temps[i]), codes[i].ToString(), day ? "1" : "0" });
            }
            var ru = new System.Globalization.CultureInfo("ru-RU");
            int[] dcodes = { 2, 61, 3, 1, 0, 3, 71 };
            double[] dmax = { 10, 9, 7, 8, 11, 6, 2 }, dmin = { 3, 4, 2, 1, 3, 0, -3 };
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
            c.Bt.Add(new BtDevice { Name = "Наушники", Connected = true });
            c.Bt.Add(new BtDevice { Name = "Колонка на кухне", Connected = false });
            c.BtBattery["Наушники"] = 80;
            return c;
        }

        static List<WifiNet> DemoWifi()
        {
            return new List<WifiNet>
            {
                new WifiNet { Ssid = "Kapsula Home", Signal = 92, Secure = true, Connected = true, Known = true },
                new WifiNet { Ssid = "Кофейня — гости", Signal = 64, Secure = false },
                new WifiNet { Ssid = "Office 5G", Signal = 41, Secure = true }
            };
        }
    }
}
