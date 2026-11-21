// Kapsula — «остров» для Windows. Часть исходного кода: DiscordBuild.cs
// Подключена ли в Discord наша сборка Vencord (официальный Vencord плюс плагин Kapsula). Ставит и снимает её core.txt —
// на карточке Maska; Kapsula только смотрит, что получилось, — для строки «Звонки Discord на острове» в настройках.
// Ничего не пишет и не меняет. Отметка — %APPDATA%\Vencord\dist\kapsula.txt: прежде её писала сама Kapsula, теперь
// core.txt, тем же путём и с тем же именем.
using System;
using System.IO;

namespace Kapsula
{
    static class DiscordBuild
    {
        static string Dist { get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vencord"), "dist"); } }

        // Подключено: отметка на месте и в Vencord действительно наша сборка (официальный установщик Vencord,
        // «Repair», мог вернуть свою — тогда отметка осталась, а плагина нет)
        // Настройки спрашивают раз в две секунды, а renderer.js — несколько мегабайт: читаем его заново, только когда он изменился
        public static bool Connected
        {
            get
            {
                string dist = Dist;
                if (!File.Exists(Path.Combine(dist, "kapsula.txt"))) return false;
                DateTime at;
                try { at = File.GetLastWriteTimeUtc(Path.Combine(dist, "renderer.js")); } catch { return false; }
                if (at != seenAt) { seen = ConnectedIn(dist); seenAt = at; }
                return seen;
            }
        }
        static DateTime seenAt;
        static bool seen;

        // То же для любой папки dist — для проверок (tests.ps1) на поддельной папке
        public static bool ConnectedIn(string dist)
        {
            try
            {
                if (!File.Exists(Path.Combine(dist, "kapsula.txt"))) return false;
                string f = Path.Combine(dist, "renderer.js");
                if (!File.Exists(f)) return false;
                string js = File.ReadAllText(f);
                return js.Contains("\"Maska\"") || js.Contains("\"Kapsula\"");
            }
            catch { return false; }
        }
    }
}
