// Kapsula — «остров» для Windows. Часть исходного кода: Banners.cs
// «Уведомления только на острове»: чтобы уведомление не показывалось дважды (окошком Windows и карточкой острова),
// Kapsula убирает уведомление Windows сразу, как только показала его сама. Для этого она чаще проверяет новые
// уведомления, пока режим включён. Уведомление остаётся в истории острова (до закрытия Kapsula).
// Переключатель «Показывать баннеры» в параметрах Windows для этого не годится: служба уведомлений
// подхватывает его только после перезапуска, а перезапускать её программе нельзя.
namespace Kapsula
{
    static class Banners
    {
        public static bool On { get { return Store.Get("bannersOff") == "1"; } }

        // Системные предупреждения (защита, обслуживание) не трогаем: пусть Windows показывает их как обычно
        public static bool Skip(string aumid)
        {
            string a = (aumid ?? "").ToLowerInvariant();
            return a.StartsWith("windows.systemtoast") || a.Contains("sechealth") || a.Contains("defender") || a.Contains("kapsula");
        }

        // Как часто проверять новые уведомления: в режиме «только на острове» — часто, чтобы окошко Windows едва успевало появиться
        public static int PollMs { get { return On ? 150 : 1200; } }

        public static void Set(bool on) { Store.Set("bannersOff", on ? "1" : "0"); }
    }
}
