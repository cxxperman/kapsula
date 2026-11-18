// kit.txt. CoreApp.cs — дорога к core.txt из любого приложения dev.txt™: стоит ли он, как его открыть и где взять.
// Каждое приложение ненавязчиво показывает её в одном месте (настройки, меню или «О программе»):
// если core.txt есть — «Открыть core.txt», если нет — «Скачать core.txt». Файл копируется в приложение как есть.
using System;
using System.Diagnostics;
using System.IO;

namespace DevTxt
{
    static class CoreApp
    {
        // Последний выпуск: браузер сразу скачивает Core.exe — он же и установщик
        public const string DownloadUrl = "https://github.com/cxxperman/core.txt/releases/latest/download/Core.exe";

        // Одной фразой — зачем он нужен. Показывается рядом с кнопкой.
        public const string Pitch = "Все приложения dev.txt в одном окне: установка, обновления и общие настройки";

        // Куда core.txt ставит себя сам
        public static string Path
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "dev.txt", "Core.exe"); }
        }

        public static bool Installed
        {
            get { try { return File.Exists(Path); } catch { return false; } }
        }

        // online — приложение сейчас на связи с хабом, то есть core.txt запущен (откуда бы он ни был запущен)
        public static string Label(bool online) { return online || Installed ? "Открыть core.txt" : "Скачать core.txt"; }

        // Открыть core.txt: запущенный — просьбой через хаб (show), установленный — запуском, а если его нет — скачать
        public static void Go(bool online, Action show)
        {
            try
            {
                if (online && show != null) { show(); return; }
                Process.Start(Installed ? Path : DownloadUrl);
            }
            catch { }
        }
    }
}
