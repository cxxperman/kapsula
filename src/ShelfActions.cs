// Kapsula — «остров» для Windows. Часть исходного кода: ShelfActions.cs
// Действия с файлами на полке. Правый щелчок по файлу: открыть, показать в папке, скопировать путь, сжать в ZIP,
// а для картинок — сохранить как JPG или PNG, уменьшить вдвое, распознать текст (встроенным в Windows
// распознаванием, без интернета). Кнопка «В архив» складывает всю полку в один ZIP в папку «Загрузки».
// Новые файлы появляются рядом с исходными и сразу ложатся на полку. Исходные файлы не меняются.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace Kapsula
{
    partial class IslandWindow
    {
        static readonly string[] ShelfImageExt = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".jxr" };
        static bool ShelfIsImage(string p) { return Array.IndexOf(ShelfImageExt, (Path.GetExtension(p) ?? "").ToLowerInvariant()) >= 0; }
        TextBlock shelfHint;
        DispatcherTimer shelfHintTimer;
        const string ShelfHintDefault = "Правый щелчок — действия";

        // Свободное имя в папке: «отчёт.zip», «отчёт (2).zip», …
        static string FreeName(string dir, string name, string ext)
        {
            string p = Path.Combine(dir, name + ext);
            for (int i = 2; File.Exists(p) || Directory.Exists(p); i++) p = Path.Combine(dir, name + " (" + i + ")" + ext);
            return p;
        }

        // Куда класть новый файл: рядом с исходным, а если туда писать нельзя — в «Загрузки»
        static string OutDir(string near)
        {
            string dir = Path.GetDirectoryName(near);
            try
            {
                string probe = Path.Combine(dir, ".kapsula-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return dir;
            }
            catch { return DownloadsDir(); }
        }

        void ShelfMenu(string path)
        {
            var menu = new WF.ContextMenuStrip();
            bool isDir = Directory.Exists(path), img = !isDir && ShelfIsImage(path);
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            menu.Items.Add("Открыть", null, delegate { try { System.Diagnostics.Process.Start(path); } catch { } });
            menu.Items.Add("Показать в папке", null, delegate { try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { } });
            menu.Items.Add("Скопировать путь", null, delegate { try { Clipboard.SetText(path); ShelfSay("Путь скопирован", true); } catch { } });
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Сжать в ZIP", null, delegate { ShelfJob("Сжато", delegate { return ZipOne(path); }); });
            if (img)
            {
                if (ext != ".jpg" && ext != ".jpeg") menu.Items.Add("Сохранить как JPG", null, delegate { ShelfJob("Готово", delegate { return ConvertImage(path, ".jpg", 1); }); });
                if (ext != ".png") menu.Items.Add("Сохранить как PNG", null, delegate { ShelfJob("Готово", delegate { return ConvertImage(path, ".png", 1); }); });
                menu.Items.Add("Уменьшить вдвое", null, delegate { ShelfJob("Уменьшено", delegate { return ConvertImage(path, null, 0.5); }); });
                menu.Items.Add("Распознать текст", null, delegate { ShelfOcr(path); });
            }
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Убрать с полки", null, delegate { shelf.Remove(path); SaveShelf(); RenderShelf(); });
            DarkMenuRenderer.Apply(menu, 230, false);
            // пока открыто меню, панель не сворачивается, хотя курсор и ушёл с острова
            holdOpenUntil = Environment.TickCount + 60000;
            menu.Closed += delegate { holdOpenUntil = Environment.TickCount + 1500; };
            menu.Show(WF.Cursor.Position);
        }

        // Короткая подпись в шапке полки (если она на экране) или надпись на капсуле
        void ShelfSay(string text, bool good)
        {
            if (state == 2 && page == PShelf && shelfHint != null)
            {
                shelfHint.Text = text;
                shelfHint.Foreground = good ? (Brush)accent : Dim;
                if (shelfHintTimer == null)
                {
                    shelfHintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3200) };
                    shelfHintTimer.Tick += delegate { shelfHintTimer.Stop(); shelfHint.Text = ShelfHintDefault; shelfHint.Foreground = Faint; };
                }
                shelfHintTimer.Stop(); shelfHintTimer.Start();
            }
            else Toast(((char)0xE8F1).ToString(), text, null, -1, good ? (Brush)accent : null);
            if (good && rider != null && rOut && !rFly) rider.Play("hop", 700);   // зверёк радуется готовому файлу
        }

        // Работа в фоне (в отдельном потоке: картинкам WPF нужен такой). Результат — новый файл, он ложится на полку.
        void ShelfJob(string doneText, Func<string> work)
        {
            ShelfSay("Работаю…", false);
            var t = new Thread(delegate()
            {
                string made = null, err = null;
                try { made = work(); }
                catch (Exception ex) { err = ex is NotSupportedException || ex is FileFormatException ? "Этот формат Windows открыть не может" : "Не получилось: " + ex.Message; Store.LogError("полка", ex); }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (made != null)
                    {
                        if (!shelf.Contains(made)) shelf.Add(made);
                        SaveShelf(); RenderShelf();
                        ShelfSay(doneText + ": " + Path.GetFileName(made), true);
                    }
                    else ShelfSay(err ?? "Не получилось", false);
                }));
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        static string ZipOne(string path)
        {
            bool dir = Directory.Exists(path);
            string clean = path.TrimEnd('\\', '/');
            string name = dir ? Path.GetFileName(clean) : Path.GetFileNameWithoutExtension(clean);
            string zip = FreeName(OutDir(clean), name, ".zip");
            if (dir) ZipFile.CreateFromDirectory(clean, zip, CompressionLevel.Optimal, true);
            else using (var a = ZipFile.Open(zip, ZipArchiveMode.Create)) a.CreateEntryFromFile(clean, Path.GetFileName(clean), CompressionLevel.Optimal);
            return zip;
        }

        // Вся полка — одним архивом в «Загрузки»
        static string ZipAll(string[] paths)
        {
            string zip = FreeName(DownloadsDir(), "Полка " + DateTime.Now.ToString("yyyy-MM-dd HH-mm"), ".zip");
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Func<string, string> unique = delegate(string n)
            {
                string b = n; int i = 2;
                while (!used.Add(n)) n = Path.GetFileNameWithoutExtension(b) + " (" + (i++) + ")" + Path.GetExtension(b);
                return n;
            };
            using (var a = ZipFile.Open(zip, ZipArchiveMode.Create))
                foreach (string p in paths)
                {
                    string clean = p.TrimEnd('\\', '/');
                    if (File.Exists(clean)) a.CreateEntryFromFile(clean, unique(Path.GetFileName(clean)), CompressionLevel.Optimal);
                    else if (Directory.Exists(clean))
                    {
                        string root = unique(Path.GetFileName(clean));
                        foreach (string f in Directory.GetFiles(clean, "*", SearchOption.AllDirectories))
                            a.CreateEntryFromFile(f, root + "/" + f.Substring(clean.Length + 1).Replace('\\', '/'), CompressionLevel.Optimal);
                    }
                }
            return zip;
        }

        static BitmapSource LoadImage(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource f = dec.Frames[0];
                f.Freeze();
                return f;
            }
        }

        // toExt: ".jpg", ".png" или null — тот же формат; scale: 1 или 0.5
        static string ConvertImage(string path, string toExt, double scale)
        {
            BitmapSource src = LoadImage(path);
            if (scale != 1) src = new TransformedBitmap(src, new ScaleTransform(scale, scale));
            string ext = toExt ?? (Path.GetExtension(path) ?? ".png").ToLowerInvariant();
            BitmapEncoder enc;
            if (ext == ".jpg" || ext == ".jpeg") { src = OnWhite(src); enc = new JpegBitmapEncoder { QualityLevel = 90 }; }   // у JPG нет прозрачности — белый фон
            else if (ext == ".bmp") enc = new BmpBitmapEncoder();
            else if (ext == ".gif") enc = new GifBitmapEncoder();
            else if (ext == ".tif" || ext == ".tiff") enc = new TiffBitmapEncoder();
            else { enc = new PngBitmapEncoder(); ext = ".png"; }   // WEBP, HEIC и прочее Windows сохранять не умеет — в PNG
            enc.Frames.Add(BitmapFrame.Create(src));
            string name = Path.GetFileNameWithoutExtension(path) + (scale != 1 ? " (меньше)" : "");
            string outp = FreeName(OutDir(path), name, ext);
            using (var o = File.Create(outp)) enc.Save(o);
            return outp;
        }

        static BitmapSource OnWhite(BitmapSource src)
        {
            int w = src.PixelWidth, h = src.PixelHeight;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                dc.DrawImage(src, new Rect(0, 0, w, h));
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            return new FormatConvertedBitmap(rtb, PixelFormats.Bgr24, null, 0);
        }

        // Текст с картинки — в буфер обмена. Большие картинки сначала уменьшаются: распознаванию хватает 2400 точек.
        void ShelfOcr(string path)
        {
            ShelfSay("Читаю текст…", false);
            var t = new Thread(delegate()
            {
                string text = null, err = null;
                try
                {
                    BitmapSource src = LoadImage(path);
                    double k = Math.Min(1, 2400.0 / Math.Max(src.PixelWidth, src.PixelHeight));
                    if (k < 1) src = new TransformedBitmap(src, new ScaleTransform(k, k));
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(src));
                    using (var ms = new MemoryStream()) { enc.Save(ms); text = Ocr.Recognize(ms.ToArray()); }
                }
                catch (Exception ex) { err = ex is NotSupportedException || ex is FileFormatException ? "Этот формат Windows открыть не может" : "Не получилось прочитать"; Store.LogError("полка, текст", ex); }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!string.IsNullOrEmpty(text))
                    {
                        try { Clipboard.SetText(text); } catch { }
                        int lines = text.Split('\n').Length;
                        ShelfSay("Текст скопирован · строк: " + lines, true);
                    }
                    else ShelfSay(err ?? "Текста на картинке не нашлось", false);
                }));
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }
    }
}
