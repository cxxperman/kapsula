# Проверки логики Kapsula без запуска интерфейса: калькулятор, единицы, таймер, напоминания,
# преобразования текста, чтение QR-кода и распознавание текста.
# Запуск:  powershell -ExecutionPolicy Bypass -File tests.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -AssemblyName System.Drawing
$asm = [Reflection.Assembly]::LoadFile((Join-Path $here 'Kapsula.exe'))
$script:pass = 0; $script:fail = 0

function T($name, $got, $want) {
    if ("$got" -eq "$want") { $script:pass++ }
    else { $script:fail++; "FAIL  $name`n      получено: [$got]`n      ожидалось: [$want]" }
}

# Вызов статического метода с out-параметрами: возвращает массив аргументов после вызова и результат
function Call($type, $method, [object[]]$a) {
    $m = $asm.GetType("Kapsula.$type").GetMethod($method)
    $arr = New-Object object[] $a.Length
    for ($i = 0; $i -lt $a.Length; $i++) { $arr[$i] = if ($a[$i] -is [psobject] -and $a[$i].PSObject.BaseObject) { $a[$i].PSObject.BaseObject } else { $a[$i] } }
    $r = $m.Invoke($null, $arr)
    return @{ Result = $r; Args = $arr }
}

# --- калькулятор
foreach ($c in @(@('2400 * 15%', '360'), @('5/2', '2,5'), @('(2+3)*4', '20'), @('10 - 3 - 2', '5'), @('1,5 + 1,5', '3'))) {
    $r = Call 'Calc' 'TryCalc' @($c[0], $null)
    T "calc $($c[0])" $r.Args[1] $c[1]
}
foreach ($bad in 'привет', '42', 'notepad', '2 +') {
    $r = Call 'Calc' 'TryCalc' @($bad, $null)
    T "calc отклоняет '$bad'" $r.Result $false
}

# --- единицы
$r = Call 'Calc' 'TryConvert' @('5 миль в км', $null);  T 'мили в км' $r.Args[1] '5 миль = 8,047 км'
$r = Call 'Calc' 'TryConvert' @('2 кг в фунт', $null);  T 'кг в фунты' $r.Args[1] '2 кг = 4,409 фунт'
$r = Call 'Calc' 'TryConvert' @('5 кг в км', $null);    T 'разные величины отклоняются' $r.Result $false

# --- таймер
$r = Call 'Calc' 'TryTimer' @('таймер 10 мин', [TimeSpan]::Zero); T 'таймер 10 мин' $r.Args[1].TotalMinutes 10
$r = Call 'Calc' 'TryTimer' @('90 сек', [TimeSpan]::Zero);       T '90 сек' $r.Args[1].TotalSeconds 90
$r = Call 'Calc' 'TryTimer' @('2 часа', [TimeSpan]::Zero);       T '2 часа' $r.Args[1].TotalHours 2
$r = Call 'Calc' 'TryTimer' @('десять минут', [TimeSpan]::Zero); T 'слова без цифр отклоняются' $r.Result $false

# --- напоминания
$now = Get-Date '2026-11-11 17:50:00'
$cases = @(
    @('через 10 мин чай', '11.11 18:00', 'Чай', $false),
    @('в 18:30 созвон', '11.11 18:30', 'Созвон', $false),
    @('завтра в 9 позвонить маме', '12.11 09:00', 'Позвонить маме', $false),
    @('каждый день в 23:00 таблетки', '11.11 23:00', 'Таблетки', $true),
    @('напомни через 2 часа выключить духовку', '11.11 19:50', 'Выключить духовку', $false),
    @('в 9 зарядка', '12.11 09:00', 'Зарядка', $false)
)
foreach ($c in $cases) {
    $r = Call 'Calc' 'TryReminder' @($c[0], $now, [datetime]::MinValue, '', $false)
    T "напоминание '$($c[0])' время" $r.Args[2].ToString('dd.MM HH:mm') $c[1]
    T "напоминание '$($c[0])' текст" $r.Args[3] $c[2]
    T "напоминание '$($c[0])' повтор" $r.Args[4] $c[3]
}
foreach ($bad in 'просто текст без времени', 'в 25:00 ошибка') {
    $r = Call 'Calc' 'TryReminder' @($bad, $now, [datetime]::MinValue, '', $false)
    T "напоминание отклоняет '$bad'" $r.Result $false
}

# --- преобразования текста
T 'раскладка en->ru' (Call 'TextTools' 'FixLayout' @('ghbdtn')).Result 'привет'
T 'раскладка ru->en' (Call 'TextTools' 'FixLayout' @('руддщ')).Result 'hello'
T 'транслит' (Call 'TextTools' 'Translit' @('Привет, мир')).Result 'Privet, mir'
T 'заголовок' (Call 'TextTools' 'Title' @('привет большой мир')).Result 'Привет Большой Мир'
T 'base64 туда-обратно' (Call 'TextTools' 'B64Dec' @((Call 'TextTools' 'B64Enc' @('Kapsula — тест')).Result)).Result 'Kapsula — тест'

# --- QR-код: сетка тестового кода лежит рядом в tests-qr.txt
$hex = (Get-Content (Join-Path $here 'tests-qr.txt') -Raw).Trim()
$n = 160
function QrBitmap($scale, $bg, $angle) {
    $bmp = New-Object System.Drawing.Bitmap 1600, 1000, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear($bg)
    $qr = New-Object System.Drawing.Bitmap ($n * $scale), ($n * $scale)
    $gq = [System.Drawing.Graphics]::FromImage($qr); $gq.Clear([System.Drawing.Color]::White)
    for ($y = 0; $y -lt $n; $y++) { for ($x = 0; $x -lt $n; $x++) {
        $v = [Convert]::ToInt32($hex.Substring($y * 40 + [int][math]::Floor($x / 4), 1), 16)
        if ((($v -shr (3 - ($x % 4))) -band 1) -eq 1) { $gq.FillRectangle([System.Drawing.Brushes]::Black, $x * $scale, $y * $scale, $scale, $scale) }
    } }
    $gq.Dispose()
    if ($angle -ne 0) { $g.TranslateTransform(800, 500); $g.RotateTransform($angle); $g.DrawImage($qr, [single](-$qr.Width / 2), [single](-$qr.Height / 2)); $g.ResetTransform() }
    else { $g.DrawImage($qr, 500, 200) }
    $g.Dispose(); $qr.Dispose()
    return ,$bmp
}
$light = [System.Drawing.Color]::FromArgb(245, 245, 245); $dark = [System.Drawing.Color]::FromArgb(32, 33, 36)
foreach ($c in @(@(3, $light, 0, 'крупный'), @(1, $light, 0, 'мелкий'), @(2, $dark, 0, 'тёмный фон'), @(3, $light, 90, 'поворот 90'), @(3, $light, 7, 'наклон 7'))) {
    $bmp = QrBitmap $c[0] $c[1] $c[2]
    T "QR $($c[3])" (Call 'Qr' 'Decode' @($bmp)).Result 'https://example.com/kapsula-qr-test'
    $bmp.Dispose()
}
$empty = New-Object System.Drawing.Bitmap 800, 600, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
T 'QR на пустой картинке не находится' ([string](Call 'Qr' 'Decode' @($empty)).Result) ''

# --- распознавание текста (встроенный движок Windows)
$img = New-Object System.Drawing.Bitmap 900, 220, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($img); $g.Clear([System.Drawing.Color]::White)
$g.TextRenderingHint = 'AntiAlias'
$f = New-Object System.Drawing.Font 'Segoe UI', 26
$g.DrawString('Счёт на оплату № 48213', $f, [System.Drawing.Brushes]::Black, 30, 30)
$g.DrawString('Invoice total: 184500', $f, [System.Drawing.Brushes]::Black, 30, 110)
$g.Dispose()
$ms = New-Object IO.MemoryStream; $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$ocrArgs = New-Object object[] 1
$ocrArgs[0] = [byte[]]$ms.ToArray()
$text = $asm.GetType('Kapsula.Ocr').GetMethod('Recognize').Invoke($null, $ocrArgs)
$flat = ($text -replace '\s+', ' ')
T 'OCR: русская строка' ($flat -match '48213') $true
T 'OCR: слово «оплату»' ($flat -match 'оплату') $true
T 'OCR: английская строка' ($flat -match 'Invoice total') $true

# --- автообновление: разбор версий и выбор релиза из ответа GitHub
foreach ($c in @(@('v0.2.1', '0.2.1.0'), @('0.3', '0.3.0.0'), @('v1.4.0-alpha', '1.4.0.0'))) {
    $r = Call 'Updater' 'TryParseVersion' @($c[0], $null)
    T "версия $($c[0])" $r.Args[1] $c[1]
}
foreach ($bad in 'latest', 'v1', '1.2.3.4', 'v1.x') {
    $r = Call 'Updater' 'TryParseVersion' @($bad, $null)
    T "версия отклоняет '$bad'" $r.Result $false
}
$h = 'a' * 64
$json = '[{"tag_name":"v0.2.0","draft":false,"assets":[{"name":"Kapsula.exe","browser_download_url":"https://x/020","digest":"sha256:' + $h + '"}]},' +
        '{"tag_name":"v0.9.0","draft":true,"assets":[{"name":"Kapsula.exe","browser_download_url":"https://x/090","digest":"sha256:' + $h + '"}]},' +
        '{"tag_name":"v0.5.0","draft":false,"assets":[{"name":"notes.txt","browser_download_url":"https://x/notes"}]},' +
        '{"tag_name":"v0.3.1-alpha","draft":false,"prerelease":true,"assets":[{"name":"Kapsula.exe.sha256","browser_download_url":"https://x/031sha"},{"name":"Kapsula.exe","browser_download_url":"https://x/031"}]}]'
$r = Call 'Updater' 'PickRelease' @($json, $null, $null, $null, $null)
T 'релиз: берётся самый новый с файлом, черновики пропускаются' $r.Args[1] '0.3.1.0'
T 'релиз: адрес файла' $r.Args[2] 'https://x/031'
T 'релиз: адрес контрольной суммы' $r.Args[4] 'https://x/031sha'
$one = '{"tag_name":"v0.2.0","assets":[{"name":"Kapsula.exe","browser_download_url":"https://x/020","digest":"sha256:' + $h + '"}]}'
$r = Call 'Updater' 'PickRelease' @($one, $null, $null, $null, $null)
T 'релиз: контрольная сумма из digest' $r.Args[3] $h
$r = Call 'Updater' 'PickRelease' @('[]', $null, $null, $null, $null)
T 'релиз: пустой список' $r.Result $false
# почему проверка не удалась — по-русски: английский текст исключений .NET остаётся только в error.log
$ft = $asm.GetType('Kapsula.Updater').GetMethod('FailText', [Reflection.BindingFlags]'NonPublic,Static')
function UpdFail($e) { $a = New-Object object[] 1; $a[0] = $e.PSObject.BaseObject; return $ft.Invoke($null, $a) }
T 'обновление: нет сети — по-русски' (UpdFail (New-Object System.Net.WebException("The remote name could not be resolved: 'api.github.com'"))) 'нет связи с GitHub'
T 'обновление: файл программы занят — по-русски' (UpdFail (New-Object System.IO.IOException('The process cannot access the file'))) 'не получилось заменить файл программы'
T 'обновление: свой отказ проверки — как есть' (UpdFail (New-Object System.Exception('контрольная сумма не совпала, обновление отброшено'))) 'контрольная сумма не совпала, обновление отброшено'
T 'обновление: непонятный ответ — без английского' (UpdFail (New-Object System.ArgumentException('Invalid JSON primitive: x'))) 'непонятный ответ GitHub'

# --- Discord: разбор уведомления от плагина XSOverlay (Vencord)
$notif = '{"type":1,"timeout":3,"title":"Аня (#общий, Сервер)","content":"привет <color=#7289da>@Макс</color>, идём?","useBase64Icon":false,"icon":"default","sourceApp":"Vencord"}'
$envl = '{"sender":"Vencord","target":"xsoverlay","command":"SendNotification","jsonData":' + (ConvertTo-Json $notif) + ',"rawData":null}'
$r = Call 'DiscordBridge' 'Parse' @($envl, $null, $null, $null)
T 'Discord: уведомление разобрано' $r.Result $true
T 'Discord: заголовок' $r.Args[1] 'Аня (#общий, Сервер)'
T 'Discord: теги цвета убраны' $r.Args[2] 'привет @Макс, идём?'
$r = Call 'DiscordBridge' 'Parse' @('{"command":"Other","jsonData":"{}"}', $null, $null, $null)
T 'Discord: чужая команда отклонена' $r.Result $false
$r = Call 'DiscordBridge' 'Parse' @('не json', $null, $null, $null)
T 'Discord: мусор отклонён' $r.Result $false
T 'Discord: плагин XSOverlay включён' (Call 'DiscordBridge' 'XsOverlayOn' @('{"plugins":{"Other":{"enabled":false},"XSOverlay":{"enabled":true,"webSocketPort":42070}}}')).Result $true
T 'Discord: плагин XSOverlay выключен' (Call 'DiscordBridge' 'XsOverlayOn' @('{"plugins":{"XSOverlay":{"enabled":false},"Other":{"enabled":true}}}')).Result $false
T 'Discord: источник discord.com разрешён' (Call 'DiscordBridge' 'OriginAllowed' @('https://discord.com')).Result $true
T 'Discord: источник canary разрешён' (Call 'DiscordBridge' 'OriginAllowed' @('https://canary.discord.com')).Result $true
T 'Discord: посторонний сайт запрещён' (Call 'DiscordBridge' 'OriginAllowed' @('https://evil.example')).Result $false
T 'Discord: подделка под discord.com запрещена' (Call 'DiscordBridge' 'OriginAllowed' @('https://discord.com.evil.example')).Result $false

# --- погода: какая часть светового дня прошла
$noon = Get-Date '2026-06-01 12:00:00'
T 'Погода: полдень между 06:00 и 18:00 — половина дня' (Call 'IslandWindow' 'DayProgress' @('06:00', '18:00', $noon)).Result 0.5
T 'Погода: ночью солнца нет' (Call 'IslandWindow' 'DayProgress' @('06:00', '18:00', (Get-Date '2026-06-01 23:00:00'))).Result -1
T 'Погода: непонятное время не ломает расчёт' (Call 'IslandWindow' 'DayProgress' @('', 'abc', $noon)).Result -1

# --- поиск файлов и названия плееров
T 'Поиск файлов: опасные символы убраны' (Call 'FileSearch' 'Sanitize' @("a' OR 1=1 --")).Result 'a OR 1=1 --'
T 'Поиск файлов: шаблоны убраны' (Call 'FileSearch' 'Sanitize' @('100%_[x]')).Result '100x'
T 'Поиск файлов: слишком короткий запрос не ищется' (Call 'FileSearch' 'Find' @('ab', 6)).Result.Count 0
T 'Плеер: имя из exe' (Call 'MediaWatcher' 'PrettyName' @('Spotify.exe')).Result 'Spotify'
T 'Плеер: имя из пути' (Call 'MediaWatcher' 'PrettyName' @('C:\Program Files\AIMP\AIMP.exe')).Result 'AIMP'
T 'Плеер: имя программы из магазина' (Call 'MediaWatcher' 'PrettyName' @('SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify')).Result 'SpotifyMusic'
T 'Плеер: Firefox по хешу папки установки' (Call 'MediaWatcher' 'PrettyName' @('308046B0AF4A39CB')).Result 'Firefox'
T 'Плеер: 16 обычных букв — не Firefox' (Call 'MediaWatcher' 'PrettyName' @('ABCDEFGHIJKLMNOP')).Result 'ABCDEFGHIJKLMNOP'

T 'Громкость плеера: процесс подходит к названию' (Call 'AppVolume' 'Matches' @('Spotify', 'SpotifyMusic')).Result $true
T 'Громкость плеера: чужой процесс не подходит' (Call 'AppVolume' 'Matches' @('chrome', 'Spotify')).Result $false
T 'Громкость плеера: слишком короткое имя не подходит' (Call 'AppVolume' 'Matches' @('ab', 'abc')).Result $false

# --- Steam через Millennium: откуда разрешено качать значок и кто может подключаться
T 'Steam: значок с сервера Steam разрешён' (Call 'SteamBridge' 'IconAllowed' @('https://avatars.steamstatic.com/abc_medium.jpg')).Result $true
T 'Steam: значок с чужого сайта запрещён' (Call 'SteamBridge' 'IconAllowed' @('https://example.com/a.jpg')).Result $false
T 'Steam: значок по http запрещён' (Call 'SteamBridge' 'IconAllowed' @('http://avatars.steamstatic.com/a.jpg')).Result $false
T 'Steam: подключение из клиента Steam разрешено' (Call 'SteamBridge' 'OriginAllowed' @('https://steamloopback.host')).Result $true
T 'Steam: подключение с сайта запрещено' (Call 'SteamBridge' 'OriginAllowed' @('https://evil.example')).Result $false
T 'Steam: вид уведомления по-русски' (Call 'SteamBridge' 'KindName' @('Friend Online')).Result 'Друг в сети'

T 'Steam: ссылка входа в лобби разрешена' (Call 'SteamBridge' 'InviteUrlAllowed' @('steam://joinlobby/570/109775241000000000/76561197960265735')).Result $true
T 'Steam: запуск с подключением разрешён' (Call 'SteamBridge' 'InviteUrlAllowed' @('steam://rungame/730/76561197960265735/+connect%201.2.3.4')).Result $true
T 'Steam: обычная ссылка — не приглашение' (Call 'SteamBridge' 'InviteUrlAllowed' @('steam://open/friends')).Result $false
T 'Steam: чужой адрес запрещён' (Call 'SteamBridge' 'SteamUrlAllowed' @('https://example.com/x')).Result $false
T 'Steam: адрес с пробелами и кавычками запрещён' (Call 'SteamBridge' 'SteamUrlAllowed' @('steam://run/1" & calc.exe')).Result $false
T 'Steam: плагин лежит внутри exe' ($asm.GetType('Kapsula.SteamBridge').GetProperty('BundledVersion').GetValue($null, $null) -match '^\d+\.\d+\.\d+$') $true
T 'Steam: версия из plugin.json' (Call 'SteamBridge' 'VersionOf' @('{ "name": "kapsula", "version": "1.2.3" }')).Result '1.2.3'

# --- Steam: разбор файлов библиотеки
$vdf = "`"libraryfolders`"`n{`n`t`"0`"`n`t{`n`t`t`"path`"`t`t`"C:\\Program Files (x86)\\Steam`"`n`t}`n`t`"1`"`n`t{`n`t`t`"path`"`t`t`"D:\\SteamLibrary`"`n`t}`n}"
$libs = (Call 'Steam' 'LibraryPaths' @($vdf)).Result
T 'Steam: две библиотеки' $libs.Count 2
T 'Steam: путь второй библиотеки' $libs[1] 'D:\SteamLibrary'
$acf = "`"AppState`"`n{`n`t`"appid`"`t`t`"1145360`"`n`t`"name`"`t`t`"Hades`"`n`t`"StateFlags`"`t`t`"4`"`n`t`"BytesToDownload`"`t`t`"100`"`n`t`"BytesDownloaded`"`t`t`"100`"`n}"
$g = (Call 'Steam' 'ParseManifest' @($acf)).Result
T 'Steam: название игры' $g.Name 'Hades'
T 'Steam: игра готова' $g.Ready $true
$g2 = (Call 'Steam' 'ParseManifest' @($acf.Replace('"4"', '"1026"'))).Result
T 'Steam: игра в загрузке не готова' $g2.Ready $false
$svc = (Call 'Steam' 'ParseManifest' @($acf.Replace('Hades', 'Steamworks Common Redistributables'))).Result
T 'Steam: служебный пакет пропущен' ($svc -eq $null) $true

# --- спектр звука: чистый тон должен дать пик ровно в своей частотной ячейке
$fre = New-Object double[] 1024; $fim = New-Object double[] 1024
for ($i = 0; $i -lt 1024; $i++) { $fre[$i] = [Math]::Sin(2 * [Math]::PI * 37 * $i / 1024) }
$fft = $asm.GetType('Kapsula.Spectrum').GetMethod('Fft')
$fa = New-Object object[] 2; $fa[0] = [double[]]$fre; $fa[1] = [double[]]$fim
[void]$fft.Invoke($null, $fa)
$best = 0; $bestV = 0
for ($i = 1; $i -lt 512; $i++) { $v = [Math]::Sqrt($fre[$i] * $fre[$i] + $fim[$i] * $fim[$i]); if ($v -gt $bestV) { $bestV = $v; $best = $i } }
T 'спектр: пик тона' $best 37
T 'спектр: высота пика' ([Math]::Round($bestV)) 512

# --- быстрая заметка из строки ввода
$iw = $asm.GetType('Kapsula.IslandWindow')
$nc = $iw.GetMethod('NoteCommandText')
T 'заметка: текст после слова' $nc.Invoke($null, @('заметка купить молоко')) 'купить молоко'
T 'заметка: «запиши»' $nc.Invoke($null, @('Запиши позвонить в 5')) 'позвонить в 5'
T 'заметка: обычный запрос' $nc.Invoke($null, @('notepad')) ''
T 'заметка: одно слово без текста' $nc.Invoke($null, @('заметка ')) ''

# --- Gemini: тело запроса и разбор потока ответа
Add-Type -AssemblyName System.Web.Extensions
$gc = $asm.GetType('Kapsula.GeminiChat'); $cm = $asm.GetType('Kapsula.ChatMsg')
function Msg($role, $text) { $m = [Activator]::CreateInstance($cm); $cm.GetField('Role').SetValue($m, $role); $cm.GetField('Text').SetValue($m, $text); return $m }
$hist = [Activator]::CreateInstance(([System.Collections.Generic.List`1]).MakeGenericType($cm)); $hist.Add((Msg 'user' 'привет')); $hist.Add((Msg 'assistant' 'здравствуйте')); $hist.Add((Msg 'user' 'как дела?'))
$ga = New-Object object[] 3; $ga[0] = 'gemini-3.8-flash'; $ga[1] = $hist; $ga[2] = $null
$gbody = $gc.GetMethod('Body').Invoke($null, $ga)
$gjson = (New-Object System.Web.Script.Serialization.JavaScriptSerializer).Serialize($gbody) | ConvertFrom-Json
T 'gemini: модель' $gjson.model 'gemini-3.8-flash'
T 'gemini: разговор не сохраняется' $gjson.store $false
T 'gemini: шаги по ролям' (($gjson.input | ForEach-Object { $_.type }) -join ',') 'user_input,model_output,user_input'
T 'gemini: текст шага' $gjson.input[2].content[0].text 'как дела?'
$pa = New-Object object[] 2; $pa[0] = 'data: {"delta":{"type":"text","text":"Привет"},"event_type":"step.delta","index":0}'; $pa[1] = $null
T 'gemini: кусок текста из потока' $gc.GetMethod('Piece').Invoke($null, $pa) 'Привет'
$pa[0] = 'data: {"event_type":"step.start","index":0,"step":{"type":"model_output"}}'; $pa[1] = $null
T 'gemini: служебное событие без текста' ($null -eq $gc.GetMethod('Piece').Invoke($null, $pa)) $true
$pa[0] = 'data: {"error":{"code":"not_found","message":"нет такой модели"},"event_type":"error"}'; $pa[1] = $null
[void]$gc.GetMethod('Piece').Invoke($null, $pa)
T 'gemini: ошибка из потока' $pa[1] 'нет такой модели'
$pa[0] = '{"error":{"message":"gemini-3.8-flash is currently experiencing high demand","code":"service_unavailable"}}'; $pa[1] = $null
[void]$gc.GetMethod('Piece').Invoke($null, $pa)
T 'gemini: ошибка перегрузки отдельной строкой' $pa[1] 'gemini-3.8-flash is currently experiencing high demand'

# --- наушники: разбор сигнала о заряде
$ebT = $asm.GetType('Kapsula.Earbuds'); $parse = $ebT.GetMethod('Parse')
function Buds([string]$hex) { $b = [byte[]]($hex.Split(' ') | ForEach-Object { [Convert]::ToByte($_, 16) }); $a = New-Object object[] 2; $a[0] = $b; $a[1] = -50; return $parse.Invoke($null, $a) }
function BF($o, $n) { $o.GetType().GetField($n).GetValue($o) }
$pro = Buds '07 19 01 0E 20 2B 97 8F 01 00 00'
T 'наушники: модель' (BF $pro 'Model') 'AirPods Pro'
T 'наушники: левый' (BF $pro 'Left') 70
T 'наушники: правый' (BF $pro 'Right') 90
T 'наушники: футляр неизвестен' (BF $pro 'Case') -1
T 'наушники: самый низкий заряд' $pro.GetType().GetProperty('Lowest').GetValue($pro) 70
$max = Buds '07 19 01 0A 20 02 F8 80 00 00 00'
T 'наушники: Max — модель' (BF $max 'Model') 'AirPods Max'
T 'наушники: Max — одна батарея' (BF $max 'Single') 80
T 'наушники: Max — подпись' $max.GetType().GetProperty('Text').GetValue($max) '80%'
$usbc = Buds '07 19 01 1F 20 2B 00 80 05 12 C5 0D'
T 'наушники: Max USB-C — точный процент из соседнего байта' (BF $usbc 'Single') 5
$usbc2 = Buds '07 19 01 1F 20 2B 07 80 4A 12 C5 0D'
T 'наушники: Max USB-C — 74%' (BF $usbc2 'Single') 74
$usbc3 = Buds '07 19 01 1F 20 2B 07 80 05 12 C5 0D'
T 'наушники: Max USB-C — байт не сходится с десятками, берём десятки' (BF $usbc3 'Single') 70
T 'наушники: чужой сигнал Apple' ($null -eq (Buds '10 05 01 18 44 00 00 00 00')) $true
T 'наушники: неизвестная модель' ($null -eq (Buds '07 19 01 FF FF 00 99 00 00')) $true
# Beats: у Studio Buds два наушника и футляр, у накладных Solo 3 и Studio 3 — одна батарея
$sb = Buds '07 19 01 11 20 2B 97 86 01 00 00'
T 'наушники: Studio Buds — не одна батарея' (BF $sb 'Single') -1
T 'наушники: Studio Buds — левый' (BF $sb 'Left') 70
T 'наушники: Studio Buds — правый' (BF $sb 'Right') 90
T 'наушники: Studio Buds — футляр' (BF $sb 'Case') 60
T 'наушники: Solo 3 — одна батарея' (BF (Buds '07 19 01 06 20 02 F8 80 00 00 00') 'Single') 80
T 'наушники: Studio 3 — одна батарея' (BF (Buds '07 19 01 09 20 02 F8 80 00 00 00') 'Single') 80

# --- календарь: подписи времени
$cal = $asm.GetType('Kapsula.Calendar'); $ce = $asm.GetType('Kapsula.CalEvent')
$nowT = [DateTime]::new(2026, 3, 10, 14, 0, 0)
function Ev($start, $allDay) { $e = [Activator]::CreateInstance($ce); $ce.GetField('Start').SetValue($e, $start); $ce.GetField('AllDay').SetValue($e, $allDay); $ce.GetField('Subject').SetValue($e, 'x'); return $e }
T 'календарь: сегодня' $cal.GetMethod('When').Invoke($null, @((Ev $nowT.AddHours(1) $false), $nowT)) 'сегодня в 15:00'
T 'календарь: завтра' $cal.GetMethod('When').Invoke($null, @((Ev $nowT.AddDays(1).AddMinutes(-270) $false), $nowT)) 'завтра в 9:30'
T 'календарь: весь день' $cal.GetMethod('When').Invoke($null, @((Ev $nowT.Date $true), $nowT)) 'сегодня, весь день'
T 'календарь: через минуты' $cal.GetMethod('In').Invoke($null, @($nowT.AddMinutes(10), $nowT)) 'через 10 мин'
T 'календарь: через часы' $cal.GetMethod('In').Invoke($null, @($nowT.AddMinutes(65), $nowT)) 'через 1 ч 5 мин'
T 'календарь: уже началось' $cal.GetMethod('In').Invoke($null, @($nowT.AddMinutes(-1), $nowT)) 'сейчас'
# событие без места (день рождения в проверке KAPSULA_DEMOLONG) — место пустое, а не null: неделя и день не падают
T 'календарь: место по умолчанию — пустое' ($null -ne $ce.GetField('Location').GetValue([Activator]::CreateInstance($ce))) $true

# --- календарь: дата события по-русски, даже если в Windows английский формат
$ceT = $asm.GetType('Kapsula.CalEvent'); $ce = [Activator]::CreateInstance($ceT); $ceT.GetField('Subject').SetValue($ce, 'Врач'); $ceT.GetField('Location').SetValue($ce, ''); $ceT.GetField('Start').SetValue($ce, [datetime]'2026-12-15 09:30')
$was = [Threading.Thread]::CurrentThread.CurrentCulture; [Threading.Thread]::CurrentThread.CurrentCulture = 'en-US'; $cw = $asm.GetType('Kapsula.Calendar').GetMethod('When').Invoke($null, @($ce, [datetime]'2026-11-14 12:00')); [Threading.Thread]::CurrentThread.CurrentCulture = $was
T 'календарь: дата события по-русски и в английской Windows' $cw '15 декабря в 9:30'

# --- расход Claude Code: из строки журнала берутся только числа токенов; один ответ считается один раз, вчерашние не считаются
$cu = $asm.GetType('Kapsula.ClaudeUsage')
$cu.GetMethod('Reset').Invoke($null, @())
$today = [DateTime]::Now.Date
$stamp = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$old = [DateTime]::UtcNow.AddDays(-3).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
function UsageLine($id, $ts) { '{"parentUuid":"x","message":{"model":"m","id":"' + $id + '","type":"message","role":"assistant","content":[],"usage":{"input_tokens":2,"cache_creation_input_tokens":300,"cache_read_input_tokens":4000,"output_tokens":150,"iterations":[{"input_tokens":2,"output_tokens":150}]}},"type":"assistant","uuid":"u","timestamp":"' + $ts + '"}' }
$cnt = $cu.GetMethod('Count')
T 'расход: новый ответ' $cnt.Invoke($null, @((UsageLine 'msg_a1' $stamp), $today)) $true
T 'расход: та же запись второй строкой' $cnt.Invoke($null, @((UsageLine 'msg_a1' $stamp), $today)) $false
T 'расход: вчерашний ответ' $cnt.Invoke($null, @((UsageLine 'msg_b2' $old), $today)) $false
$userLine = '{"type":"user","message":{"role":"user","content":"hi"},"timestamp":"' + $stamp + '"}'
T 'расход: строка пользователя' $cnt.Invoke($null, @($userLine, $today)) $false
[void]$cnt.Invoke($null, @((UsageLine 'msg_c3' $stamp), $today))
T 'расход: выход' $cu.GetField('Out').GetValue($null) 300
T 'расход: ввод' $cu.GetField('In').GetValue($null) 4
T 'расход: кэш' ("$($cu.GetField('CacheRead').GetValue($null))/$($cu.GetField('CacheWrite').GetValue($null))") '8000/600'
T 'расход: ответов' $cu.GetField('Replies').GetValue($null) 2
T 'расход: сокращение' $cu.GetMethod('Short').Invoke($null, @([long]1234567)) '1,2 млн'
T 'расход: за неделю вместе с прошлыми днями' $cu.GetProperty('Week').GetValue($null) 450
$cu.GetMethod('Reset').Invoke($null, @())

# --- сведения для сообщения об ошибке: имя пользователя Windows в них не попадает
$scrub = $iw.GetMethod('Scrub')
$sample = "в файле $env:USERPROFILE\AppData\Roaming\Kapsula\x.txt"
T 'ошибка: путь к профилю скрыт' $scrub.Invoke($null, @($sample)) 'в файле %USERPROFILE%\AppData\Roaming\Kapsula\x.txt'
if ($env:USERNAME.Length -ge 3) { T 'ошибка: имя пользователя скрыто' ($scrub.Invoke($null, @("C:\Users\$env:USERNAME\file")) -notmatch [regex]::Escape($env:USERNAME)) $true }

# --- Claude Code: наши хуки добавляются и убираются, чужие настройки и чужие хуки остаются как были
$cb = $asm.GetType('Kapsula.ClaudeBridge')
$tmpSet = Join-Path ([IO.Path]::GetTempPath()) "kapsula-test-settings-$PID.json"
$orig = '{"autoUpdatesChannel":"latest","hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo hi"}]}]}}'
[IO.File]::WriteAllText($tmpSet, $orig)
$cb.GetField('SettingsOverride').SetValue($null, $tmpSet)
T 'claude: до подключения' $cb.GetProperty('Installed').GetValue($null) $false
T 'claude: подключение' $cb.GetMethod('Install').Invoke($null, @()) $null
$after = Get-Content $tmpSet -Raw | ConvertFrom-Json
T 'claude: подключено' $cb.GetProperty('Installed').GetValue($null) $true
T 'claude: чужая настройка цела' $after.autoUpdatesChannel 'latest'
T 'claude: чужой хук цел' $after.hooks.Stop[0].hooks[0].command 'echo hi'
T 'claude: наш хук разрешений' $after.hooks.PermissionRequest[0].hooks[0].url 'http://127.0.0.1:42072/claude/permission'
T 'claude: вид хука' $after.hooks.PermissionRequest[0].hooks[0].type 'http'
T 'claude: копия прежнего файла' ([IO.File]::ReadAllText("$tmpSet.kapsula-bak")) $orig
[void]$cb.GetMethod('Install').Invoke($null, @())
$twice = Get-Content $tmpSet -Raw | ConvertFrom-Json
T 'claude: повторное подключение не дублирует' @($twice.hooks.PermissionRequest).Count 1
T 'claude: отключение' $cb.GetMethod('Remove').Invoke($null, @()) $null
$gone = Get-Content $tmpSet -Raw | ConvertFrom-Json
T 'claude: после отключения' $cb.GetProperty('Installed').GetValue($null) $false
T 'claude: чужой хук остался' $gone.hooks.Stop[0].hooks[0].command 'echo hi'
T 'claude: наших событий нет' ($gone.hooks.PSObject.Properties.Name -join ',') 'Stop'
[IO.File]::WriteAllText($tmpSet, '{ это не json')
T 'claude: битый файл не трогаем' ([string]$cb.GetMethod('Install').Invoke($null, @()) -ne '') $true
T 'claude: битый файл цел' ([IO.File]::ReadAllText($tmpSet)) '{ это не json'
Remove-Item $tmpSet, "$tmpSet.kapsula-bak" -ErrorAction SilentlyContinue
$cb.GetField('SettingsOverride').SetValue($null, $null)

# --- «claude …» в Windows Terminal: обратная косая черта в конце не съедает закрывающую кавычку
$wq = $iw.GetMethod('WtQuoted', [Reflection.BindingFlags]'NonPublic,Static')
T 'claude: папка в кавычках' ($wq.Invoke($null, @('C:\proj'))) '"C:\proj"'
T 'claude: корень диска — косая черта удвоена' ($wq.Invoke($null, @('D:\'))) '"D:\\"'

# --- QR-код для телефона: наш кодировщик → наш же декодер, для ссылок разной длины (версии 1–6)
$qm = $asm.GetType('Kapsula.QrMake'); $qd = $asm.GetType('Kapsula.Qr')
foreach ($s in @('hi', 'http://192.168.1.23:42075/?k=0123456789abcdef01234567', 'https://example.com/' + ('x' * 90))) {
    $m = $qm.GetMethod('Encode').Invoke($null, @($s))
    $n = $m.GetLength(0); $sc = 6
    $bmp = [System.Drawing.Bitmap]::new(($n + 8) * $sc, ($n + 8) * $sc)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::White)
    for ($y = 0; $y -lt $n; $y++) { for ($x = 0; $x -lt $n; $x++) { if ($m[$y, $x]) { $g.FillRectangle([System.Drawing.Brushes]::Black, ($x + 4) * $sc, ($y + 4) * $sc, $sc, $sc) } } }
    $g.Dispose()
    T "qr: туда и обратно ($($s.Length) симв.)" ($qd.GetMethod('Decode').Invoke($null, [object[]]@($bmp.PSObject.BaseObject))) $s
    $bmp.Dispose()
}

# --- пульт с телефона: длинный текст в адресе читается целиком (больше одного пакета)
$rh = $asm.GetType('Kapsula.Phone').GetMethod('ReadHead', [Reflection.BindingFlags]'NonPublic,Static')
$longReq = "GET /do?k=x&a=type&v=" + [Uri]::EscapeDataString('привет ' * 285) + " HTTP/1.1`r`nHost: a`r`n`r`n"
$longMs = New-Object IO.MemoryStream (,[Text.Encoding]::ASCII.GetBytes($longReq))
T 'пульт: длинный текст с телефона не обрезается' ($rh.Invoke($null, @($longMs.PSObject.BaseObject)) -eq $longReq) $true

# --- задачи на сегодня естественным языком
$tp = $asm.GetType('Kapsula.Tasks').GetMethod('Parse')
$today = [DateTime]::new(2026, 11, 12)
$r = $tp.Invoke($null, @('купить хлеб, потом позвонить маме в 18:30', $today))
T 'задачи: две из одной фразы' $r.Count 2
T 'задачи: время у второй' ($r[1].Time) '18:30:00'
$r = $tp.Invoke($null, @('завтра отправить отчёт', $today))
T 'задачи: завтра' ($r[0].Day.ToString('yyyy-MM-dd')) '2026-11-13'

# --- умный буфер: тип записи
$ck = $asm.GetType('Kapsula.IslandWindow').GetMethod('ClipKind', [Reflection.BindingFlags]'NonPublic,Static')
$ci = $asm.GetType('Kapsula.ClipItem')
foreach ($c in @(@('https://kapsula.app/x', 2), @('#ff9f0a', 3), @('const x = () => { return 1; }', 4), @('просто текст', 1))) {
    $it = [Activator]::CreateInstance($ci); $ci.GetField('Text').SetValue($it, $c[0])
    T "буфер: тип «$($c[0])»" ($ck.Invoke($null, @($it))) $c[1]
}

# --- полка: фото «боком» поворачиваются по пометке EXIF
Add-Type -AssemblyName PresentationCore, WindowsBase
$ot = $asm.GetType('Kapsula.IslandWindow').GetMethod('OrientationTransform', [Reflection.BindingFlags]'NonPublic,Static')
T 'полка: EXIF 1 — без поворота' ($null -eq $ot.Invoke($null, @(1))) $true
T 'полка: EXIF 6 — на 90° по часовой' $ot.Invoke($null, @(6)).Angle 90
T 'полка: EXIF 8 — на 270°' $ot.Invoke($null, @(8)).Angle 270
$exifJpg = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'kapsula-exif-' + [Guid]::NewGuid().ToString('N') + '.jpg')
$exifSrc = [System.Windows.Media.Imaging.BitmapSource]::Create(40, 20, 96, 96, [System.Windows.Media.PixelFormats]::Bgr32, $null, (New-Object byte[] (40 * 20 * 4)), 160)
$exifMd = New-Object System.Windows.Media.Imaging.BitmapMetadata('jpg'); $exifMd.SetQuery('/app1/ifd/{ushort=274}', [uint16]6)
$exifEnc = New-Object System.Windows.Media.Imaging.JpegBitmapEncoder; $exifEnc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($exifSrc, $null, $exifMd, $null))
$exifFs = [IO.File]::Create($exifJpg); $exifEnc.Save($exifFs); $exifFs.Close()
$exifImg = $asm.GetType('Kapsula.IslandWindow').GetMethod('LoadImage', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, @($exifJpg))
T 'полка: фото с пометкой EXIF 6 открывается повёрнутым' ("$($exifImg.PixelWidth)x$($exifImg.PixelHeight)") '20x40'
Remove-Item $exifJpg -ErrorAction SilentlyContinue

# --- батарея ноутбука: сколько осталось и значок заряда
$iwT = $asm.GetType('Kapsula.IslandWindow')
T 'уведомления: вчерашнее — с днём' ($iwT.GetMethod('NotifAgo', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, @((Get-Date).Date.AddDays(-1).AddHours(14).AddMinutes(5)))) 'вчера, 14:05'
# раньше — месяц словом и в родительном падеже, как в календаре: не «12 май» и не «12 ноя»
T 'уведомления: давнее — месяц словом' ($iwT.GetMethod('NotifAgo', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, @([datetime]'2025-05-12 14:05'))) '12 мая, 14:05'
T 'задачи: день недели со строчной' ($iwT.GetMethod('DayShort', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, @([datetime]'2026-11-16', [Globalization.CultureInfo]::GetCultureInfo('ru-RU')))) 'пн, 16 ноября'
# сокращение посередине годится и для заголовков окон: «|», «<», «"» не роняют его (Path.GetExtension больше не нужен)
T 'сокращение: заголовок окна с «|» не падает' ($asm.GetType('Kapsula.Ui').GetMethod('MiddleEllipsis').Invoke($null, @('Kapsula | GitHub — Google Chrome и ещё длинный хвост', 24))) 'Kapsula | GitHub…хвост'
$lt = $iwT.GetMethod('LeftText', [Reflection.BindingFlags]'NonPublic,Static')
T 'батарея: осталось минут' ($lt.Invoke($null, @(45))) '45 мин'
T 'батарея: осталось часов' ($lt.Invoke($null, @(92))) '1 ч 32 мин'
T 'батарея: с нулём в минутах' ($lt.Invoke($null, @(65))) '1 ч 05 мин'
T 'батарея: ровно часы' ($lt.Invoke($null, @(120))) '2 ч'
$bg = $iwT.GetMethod('BatGlyph', [Reflection.BindingFlags]'NonPublic,Static')
T 'батарея: значок 23%' ([int][char]"$($bg.Invoke($null, @(23, $false)))") 0xE852
T 'батарея: значок полной' ([int][char]"$($bg.Invoke($null, @(100, $false)))") 0xE83F
T 'батарея: значок пустой' ([int][char]"$($bg.Invoke($null, @(2, $false)))") 0xE850
T 'батарея: значок зарядки 95%' ([int][char]"$($bg.Invoke($null, @(95, $true)))") 0xEA93
T 'батарея: значок зарядки 41%' ([int][char]"$($bg.Invoke($null, @(41, $true)))") 0xE85E

# --- блок «Сеть» на «Главной»: скорость по-человечески
$rt = $iwT.GetMethod('Rate', [Reflection.BindingFlags]'NonPublic,Static')
T 'сеть: почти тишина' ($rt.Invoke($null, @([double]100))) '0 КБ/с'
T 'сеть: килобайты' ($rt.Invoke($null, @([double]51200))) '50 КБ/с'
T 'сеть: мегабайты' ($rt.Invoke($null, @([double]2516582.4))) '2,4 МБ/с'
T 'сеть: 999 КБ/с — ещё килобайты' ($rt.Invoke($null, @([double](999 * 1024)))) '999 КБ/с'
T 'сеть: с 1000 КБ/с — мегабайты' ($rt.Invoke($null, @([double](1000 * 1024)))) '1,0 МБ/с'

# --- «Подключить Discord»: на поддельных папках — Discord без Vencord и Discord с Vencord; ничего настоящего не трогаем
$di = $asm.GetType('Kapsula.DiscordInstall')
$tmp = Join-Path $env:TEMP ('kapsula-di-' + [Guid]::NewGuid().ToString('N'))
$roam = Join-Path $tmp 'Roaming'; $loc = Join-Path $tmp 'Local'; $src = Join-Path $tmp 'build'
$res = Join-Path $loc 'Discord\app-1.0.9268\resources'
New-Item -ItemType Directory -Force $res, $src, (Join-Path $loc 'Discord\app-1.0.9100\resources') | Out-Null
foreach ($f in 'patcher.js', 'preload.js', 'renderer.js', 'renderer.css') { Set-Content (Join-Path $src $f) ('// kapsula build ' + $f + (' ' * 120)) }
Set-Content (Join-Path $res 'app.asar') 'discord'
$env:KAPSULA_TEST_APPDATA = $roam; $env:KAPSULA_TEST_LOCALAPPDATA = $loc; $env:KAPSULA_TEST_VENCORD_DIR = $src
try {
    # 1) Discord без Vencord: Kapsula встраивает сама — как установщик Vencord: _app.asar и заглушка app.asar
    T 'discord: подключение без Vencord' ($di.GetMethod('Install').Invoke($null, @())) $null
    $dist = Join-Path $roam 'Vencord\dist'
    T 'discord: сборка на месте' (Get-Content (Join-Path $dist 'renderer.js') -Raw).Contains('kapsula build') $true
    T 'discord: исходный app.asar сохранён' ((Get-Content (Join-Path $res '_app.asar') -Raw).Trim()) 'discord'
    T 'discord: папки app нет (с ней Discord не запускался)' (Test-Path (Join-Path $res 'app')) $false
    $stub = [IO.File]::ReadAllBytes((Join-Path $res 'app.asar'))
    $hs = [int][BitConverter]::ToUInt32($stub, 4); $jl = [int][BitConverter]::ToUInt32($stub, 12)
    $hdr = [Text.Encoding]::UTF8.GetString($stub, 16, $jl) | ConvertFrom-Json
    $ix = $hdr.files.'index.js'; $pk = $hdr.files.'package.json'; $base = 8 + $hs
    $idxText = [Text.Encoding]::UTF8.GetString($stub, $base + [int]$ix.offset, [int]$ix.size)
    $pkgText = [Text.Encoding]::UTF8.GetString($stub, $base + [int]$pk.offset, [int]$pk.size)
    T 'discord: заглушка app.asar — архив asar' (([BitConverter]::ToUInt32($stub, 0) -eq 4) -and ($stub.Length -eq $base + [int]$ix.size + [int]$pk.size)) $true
    T 'discord: заглушка загружает patcher.js Vencord' ($idxText.StartsWith('require(') -and $idxText.Contains('Vencord\\dist\\patcher.js')) $true
    T 'discord: заглушка запускает index.js' (($pkgText | ConvertFrom-Json).main) 'index.js'
    T 'discord: старая версия Discord не тронута' ((Test-Path (Join-Path $loc 'Discord\app-1.0.9100\resources\_app.asar')) -or (Test-Path (Join-Path $loc 'Discord\app-1.0.9100\resources\app'))) $false
    # Discord обновился, и Vencord сам перенёс заглушку в новую версию — «Отключить» вернёт и её
    $res2 = Join-Path $loc 'Discord\app-1.0.9300\resources'
    New-Item -ItemType Directory -Force $res2 | Out-Null
    Set-Content (Join-Path $res2 '_app.asar') 'discord new'; Copy-Item (Join-Path $res 'app.asar') (Join-Path $res2 'app.asar')
    T 'discord: отключение' ($di.GetMethod('Uninstall').Invoke($null, @())) $null
    T 'discord: app.asar вернулся' (((Get-Content (Join-Path $res 'app.asar') -Raw).Trim() -eq 'discord') -and -not (Test-Path (Join-Path $res '_app.asar')) -and -not (Test-Path (Join-Path $res 'app'))) $true
    T 'discord: и в новой версии Discord' (((Get-Content (Join-Path $res2 'app.asar') -Raw).Trim() -eq 'discord new') -and -not (Test-Path (Join-Path $res2 '_app.asar'))) $true
    T 'discord: нашей сборки не осталось' (Test-Path (Join-Path $dist 'renderer.js')) $false
    Remove-Item (Join-Path $loc 'Discord\app-1.0.9300') -Recurse -Force
    # 2) Discord, сломанный подключением из 0.6.1–0.6.2 (папка app рядом с _app.asar, а app.asar нет)
    $broken = {
        New-Item -ItemType Directory -Force $dist, (Join-Path $res 'app') | Out-Null
        foreach ($f in 'patcher.js', 'preload.js', 'renderer.js', 'renderer.css') { Set-Content (Join-Path $dist $f) ('// kapsula build ' + $f) }
        Move-Item (Join-Path $res 'app.asar') (Join-Path $res '_app.asar')
        Set-Content (Join-Path $res 'app\index.js') 'require("C:\\Users\\u\\AppData\\Roaming\\Vencord\\dist\\patcher.js");'
        Set-Content (Join-Path $res 'app\package.json') '{"name":"discord","main":"index.js"}'
        Set-Content (Join-Path $dist 'kapsula.txt') ("Kapsula`r`nvencord=kapsula`r`ninjected=" + $res)
    }
    & $broken
    T 'discord: починка при запуске' ($di.GetMethod('RepairBroken').Invoke($null, @())) $true
    T 'discord: после починки — заглушка, рядом исходный Discord' ((-not (Test-Path (Join-Path $res 'app'))) -and ((Get-Content (Join-Path $res '_app.asar') -Raw).Trim() -eq 'discord') -and ([IO.File]::ReadAllBytes((Join-Path $res 'app.asar'))[0] -eq 4)) $true
    T 'discord: повторная починка ничего не трогает' ($di.GetMethod('RepairBroken').Invoke($null, @())) $false
    T 'discord: отключение после починки' ($di.GetMethod('Uninstall').Invoke($null, @())) $null
    T 'discord: после починки и отключения Discord как был' (((Get-Content (Join-Path $res 'app.asar') -Raw).Trim() -eq 'discord') -and -not (Test-Path (Join-Path $res '_app.asar'))) $true
    & $broken
    T 'discord: «Отключить» на сломанном' ($di.GetMethod('Uninstall').Invoke($null, @())) $null
    T 'discord: сломанный вернулся как был' (((Get-Content (Join-Path $res 'app.asar') -Raw).Trim() -eq 'discord') -and -not (Test-Path (Join-Path $res '_app.asar')) -and -not (Test-Path (Join-Path $res 'app'))) $true
    # 3) Discord с Vencord: меняются только файлы Vencord, прежние возвращаются
    Move-Item (Join-Path $res 'app.asar') (Join-Path $res '_app.asar'); Set-Content (Join-Path $res 'app.asar') 'vencord stub'
    New-Item -ItemType Directory -Force $dist | Out-Null
    foreach ($f in 'patcher.js', 'preload.js', 'renderer.js', 'renderer.css') { Set-Content (Join-Path $dist $f) 'official vencord' }
    T 'discord: подключение к Vencord' ($di.GetMethod('Install').Invoke($null, @())) $null
    T 'discord: Discord не тронут' ((Get-Content (Join-Path $res 'app.asar') -Raw).Trim()) 'vencord stub'
    T 'discord: прежний Vencord в резерве' ((Get-Content (Join-Path $roam 'Vencord\dist-before-kapsula\renderer.js') -Raw).Trim()) 'official vencord'
    T 'discord: повторное подключение не портит резерв' ($di.GetMethod('Install').Invoke($null, @())) $null
    T 'discord: резерв прежний' ((Get-Content (Join-Path $roam 'Vencord\dist-before-kapsula\renderer.js') -Raw).Trim()) 'official vencord'
    T 'discord: отключение от Vencord' ($di.GetMethod('Uninstall').Invoke($null, @())) $null
    T 'discord: официальный Vencord вернулся' ((Get-Content (Join-Path $dist 'renderer.js') -Raw).Trim()) 'official vencord'
    T 'discord: метка снята' ($di.GetProperty('Installed').GetValue($null)) $false
    # 4) Discord запущен (app.asar занят): «Подключить» не вышло — после него ничего не осталось; вторая попытка — как первая, «Отключить» убирает всё
    Remove-Item (Join-Path $roam 'Vencord') -Recurse -Force
    Remove-Item (Join-Path $res 'app.asar') -Force; Move-Item (Join-Path $res '_app.asar') (Join-Path $res 'app.asar')
    $lock = [IO.File]::Open((Join-Path $res 'app.asar'), 'Open', 'Read', 'Read')
    try { T 'discord: Discord занят — просим закрыть' ($di.GetMethod('Install').Invoke($null, @())) 'закройте Discord (и в трее у часов) и нажмите ещё раз' } finally { $lock.Close() }
    T 'discord: после неудачи — ни сборки, ни резерва, ни метки' ((Test-Path (Join-Path $dist 'renderer.js')) -or (Test-Path (Join-Path $roam 'Vencord\dist-before-kapsula')) -or ($di.GetProperty('Installed').GetValue($null))) $false
    T 'discord: после неудачи папки Vencord нет' (Test-Path (Join-Path $roam 'Vencord')) $false
    T 'discord: после неудачи Discord как был' (((Get-Content (Join-Path $res 'app.asar') -Raw).Trim() -eq 'discord') -and -not (Test-Path (Join-Path $res '_app.asar'))) $true
    T 'discord: вторая попытка' ($di.GetMethod('Install').Invoke($null, @())) $null
    T 'discord: вторая попытка — Vencord наш' ((Get-Content (Join-Path $dist 'kapsula.txt') -Raw).Contains('vencord=kapsula')) $true
    $res2 = Join-Path $loc 'Discord\app-1.0.9300\resources'
    New-Item -ItemType Directory -Force $res2 | Out-Null
    Set-Content (Join-Path $res2 '_app.asar') 'discord new'; Copy-Item (Join-Path $res 'app.asar') (Join-Path $res2 'app.asar')
    T 'discord: отключение после неудачи' ($di.GetMethod('Uninstall').Invoke($null, @())) $null
    T 'discord: после отключения — оба Discord как были' (((Get-Content (Join-Path $res 'app.asar') -Raw).Trim() -eq 'discord') -and -not (Test-Path (Join-Path $res '_app.asar')) -and ((Get-Content (Join-Path $res2 'app.asar') -Raw).Trim() -eq 'discord new') -and -not (Test-Path (Join-Path $res2 '_app.asar'))) $true
    T 'discord: после отключения — ни сборки, ни резерва' ((Test-Path (Join-Path $dist 'renderer.js')) -or (Test-Path (Join-Path $roam 'Vencord\dist-before-kapsula'))) $false
    Remove-Item (Join-Path $loc 'Discord\app-1.0.9300') -Recurse -Force
    # 5) Сломанное прежней версией: в резерве наша же сборка, метка «vencord=before» — «Отключить» всё равно убирает всё
    $bak = Join-Path $roam 'Vencord\dist-before-kapsula'
    New-Item -ItemType Directory -Force $dist, $bak | Out-Null
    foreach ($f in 'patcher.js', 'preload.js', 'renderer.js', 'renderer.css') { foreach ($d in $dist, $bak) { Set-Content (Join-Path $d $f) ('// kapsula build "Kapsula" ' + $f + (' ' * 120)) } }
    Move-Item (Join-Path $res 'app.asar') (Join-Path $res '_app.asar'); [IO.File]::WriteAllBytes((Join-Path $res 'app.asar'), $stub)
    Set-Content (Join-Path $dist 'kapsula.txt') "Kapsula`r`nvencord=before"
    T 'discord: в резерве наша сборка — отключение' ($di.GetMethod('Uninstall').Invoke($null, @())) $null
    T 'discord: в резерве наша сборка — Discord как был' (((Get-Content (Join-Path $res 'app.asar') -Raw).Trim() -eq 'discord') -and -not (Test-Path (Join-Path $res '_app.asar'))) $true
    T 'discord: в резерве наша сборка — ни сборки, ни резерва' ((Test-Path (Join-Path $dist 'renderer.js')) -or (Test-Path $bak)) $false
    # 6) Остатки нашей сборки без метки (неудачная попытка прежней версии) — не «прежний Vencord»: в резерв не уходят
    foreach ($f in 'patcher.js', 'preload.js', 'renderer.js', 'renderer.css') { Set-Content (Join-Path $dist $f) ('// kapsula build "Kapsula" ' + $f + (' ' * 120)) }
    T 'discord: подключение поверх своих остатков' ($di.GetMethod('Install').Invoke($null, @())) $null
    T 'discord: свои остатки не ушли в резерв' (Test-Path (Join-Path $roam 'Vencord\dist-before-kapsula')) $false
    T 'discord: свои остатки — Vencord наш' ((Get-Content (Join-Path $dist 'kapsula.txt') -Raw).Contains('vencord=kapsula')) $true
    T 'discord: отключение после своих остатков' ($di.GetMethod('Uninstall').Invoke($null, @())) $null
    T 'discord: после своих остатков Discord как был' (((Get-Content (Join-Path $res 'app.asar') -Raw).Trim() -eq 'discord') -and -not (Test-Path (Join-Path $res '_app.asar')) -and -not (Test-Path (Join-Path $dist 'renderer.js'))) $true
} finally {
    Remove-Item Env:KAPSULA_TEST_APPDATA, Env:KAPSULA_TEST_LOCALAPPDATA, Env:KAPSULA_TEST_VENCORD_DIR -ErrorAction SilentlyContinue
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# --- Задачи с датами на «Сегодня»: дата словами и цифрами (сегодня — среда, 11 ноября 2026)
$tk = $asm.GetType('Kapsula.Tasks'); $wed = [datetime]'2026-11-11'
function TP([string]$s) {
    $r = $tk.GetMethod('Parse').Invoke($null, @($s, $wed))
    ($r | ForEach-Object { $_.Day.ToString('yyyy-MM-dd') + ' ' + $(if ($null -ne $_.Time) { ([TimeSpan]$_.Time).ToString('hh\:mm') } else { '-' }) + ' ' + $_.Text + $(if ($_.Repeat) { ' [' + $_.Repeat + ']' } else { '' }) }) -join ' | '
}
# повторяющиеся задачи
T 'повтор: каждый понедельник в 10' (TP 'каждый понедельник в 10 планёрка') '2026-11-16 10:00 Планёрка [w:0]'
T 'повтор: ежедневно в 9' (TP 'ежедневно в 9 зарядка') '2026-11-11 09:00 Зарядка [d]'
T 'повтор: по будням' (TP 'по будням в 8 будильник') '2026-11-11 08:00 Будильник [wd]'
T 'повтор: по выходным' (TP 'по выходным уборка') '2026-11-14 - Уборка [we]'
T 'повтор: по понедельникам и средам' (TP 'по понедельникам и средам бассейн') '2026-11-11 - Бассейн [w:0,2]'
T 'повтор: каждую пятницу' (TP 'каждую пятницу отчёт') '2026-11-13 - Отчёт [w:4]'
T 'повтор: каждое 15-е число' (TP 'каждое 15-е число квартплата') '2026-11-15 - Квартплата [m:15]'
T 'повтор: каждый месяц 1-го' (TP 'каждый месяц 1-го счета') '2026-12-01 - Счета [m:1]'
T 'повтор: каждую неделю' (TP 'каждую неделю созвон с мамой') '2026-11-11 - Созвон с мамой [w:2]'
$nd = $tk.GetMethod('NextDay')
T 'повтор: 31-е в ноябре — последний день' ($nd.Invoke($null, @('m:31', [datetime]'2026-11-20')).ToString('yyyy-MM-dd')) '2026-11-30'
T 'повтор: после воскресенья — понедельник' ($nd.Invoke($null, @('w:0', [datetime]'2026-11-15')).ToString('yyyy-MM-dd')) '2026-11-16'
T 'повтор: подпись' ($tk.GetMethod('RepeatLabel').Invoke($null, @('w:0,2'))) 'по пн, ср'
# «Когда-нибудь» — задачи без дня
T 'когда-нибудь: без дня' (TP 'когда-нибудь выучить испанский') '9999-12-31 - Выучить испанский'
T 'когда-нибудь: без даты' (TP 'купить новый стол без даты') '9999-12-31 - Купить новый стол'
# шаги в заметке к задаче
$sc = $tk.GetMethod('StepCount'); $scArgs = @("Паспорт и билеты`n- билеты`n✓ зарядка`n[ ] вода`n-5 градусов — не шаг", 0)
$total = $sc.Invoke($null, $scArgs)
T 'шаги: сколько и сколько сделано' ("$($scArgs[1])/$total") '1/3'
T 'шаги: отметить' ($tk.GetMethod('StepToggle').Invoke($null, @('  - билеты'))) '  ✓ билеты'
T 'шаги: снять отметку' ($tk.GetMethod('StepToggle').Invoke($null, @('[x] вода'))) '- вода'
T 'шаги: обычная строка не меняется' ($tk.GetMethod('StepToggle').Invoke($null, @('Паспорт и билеты'))) 'Паспорт и билеты'
T 'задачи: в пятницу' (TP 'в пятницу отчёт') '2026-11-13 - Отчёт'
T 'задачи: во вторник в 10' (TP 'во вторник в 10 врач') '2026-11-17 10:00 Врач'
T 'задачи: «в среду» в среду — следующая' (TP 'в среду созвон') '2026-11-18 - Созвон'
T 'задачи: в следующую пятницу' (TP 'в следующую пятницу дедлайн') '2026-11-20 - Дедлайн'
T 'задачи: в следующий понедельник' (TP 'в следующий понедельник планёрка') '2026-11-16 - Планёрка'
T 'задачи: к пятнице' (TP 'к пятнице презентация') '2026-11-13 - Презентация'
T 'задачи: пт в 18' (TP 'пт в 18 кино') '2026-11-13 18:00 Кино'
T 'задачи: в 10 утра в пятницу' (TP 'в 10 утра в пятницу зал') '2026-11-13 10:00 Зал'
T 'задачи: «среда» как слово — не день' (TP 'настроить среду разработки') '2026-11-11 - Настроить среду разработки'
T 'задачи: через 3 дня' (TP 'через 3 дня ремонт') '2026-11-14 - Ремонт'
T 'задачи: через неделю' (TP 'через неделю отпуск') '2026-11-18 - Отпуск'
T 'задачи: через пару дней' (TP 'через пару дней позвонить маме') '2026-11-13 - Позвонить маме'
T 'задачи: 15 ноября' (TP '15 ноября день рождения') '2026-11-15 - День рождения'
T 'задачи: 1 января — следующий год' (TP 'праздник 1 января') '2027-01-01 - Праздник'
T 'задачи: в 10 мая — прошло, значит следующий год' (TP 'в 10 мая отпуск') '2027-05-10 - Отпуск'
T 'задачи: на 20.11' (TP 'билеты на 20.11') '2026-11-20 - Билеты'
T 'задачи: 25.12.2026' (TP 'ёлка 25.12.2026') '2026-12-25 - Ёлка'
T 'задачи: на выходных' (TP 'уборка на выходных') '2026-11-14 - Уборка'
T 'задачи: 14.30 — время, не дата' (TP 'в 14.30 созвон') '2026-11-11 14:30 Созвон'
T 'задачи: две задачи через «потом»' (TP 'отчёт, потом в 14:00 созвон') '2026-11-11 - Отчёт | 2026-11-11 14:00 Созвон'
T 'задачи: завтра в 9' (TP 'завтра в 9 спортзал') '2026-11-12 09:00 Спортзал'
T 'задачи: запятая перед временем и днём' (TP 'в 14:00 созвон, в пятницу отчёт') '2026-11-11 14:00 Созвон | 2026-11-13 - Отчёт'
T 'задачи: запятая перед днём недели' (TP 'в чт в 19 кино, в сб спортзал') '2026-11-12 19:00 Кино | 2026-11-14 - Спортзал'
T 'задачи: обычная запятая — одна задача' (TP 'купить молоко, хлеб') '2026-11-11 - Купить молоко, хлеб'
T 'задачи: день до запятой — для дела после неё' (TP 'каждый понедельник, в 10 планёрка') '2026-11-16 10:00 Планёрка [w:0]'
T 'задачи: только время — задачи нет' (TP 'завтра в 10') ''
T 'задачи: день и время после запятой — к делу перед ней' (TP 'позвонить маме, завтра в 10') '2026-11-12 10:00 Позвонить маме'
T 'задачи: время после запятой' (TP 'сходить в магазин, в 5 вечера') '2026-11-11 17:00 Сходить в магазин'
T 'задачи: день недели после запятой' (TP 'стрижка, в пятницу') '2026-11-13 - Стрижка'
T 'задачи: «через 2 дня» после запятой' (TP 'отчёт, через 2 дня;') '2026-11-13 - Отчёт'
# время с минутами и «вечера», «ночи», «дня»; день отсчёта «когда-нибудь» — от сегодняшнего дня
T 'задачи: в 7:30 вечера' (TP 'в 7:30 вечера кино') '2026-11-11 19:30 Кино'
T 'задачи: в 7.30 вечера' (TP 'в 7.30 вечера кино') '2026-11-11 19:30 Кино'
T 'задачи: в 12:00 ночи' (TP 'в 12:00 ночи звонок') '2026-11-11 00:00 Звонок'
T 'задачи: в 2:15 дня' (TP 'обед в 2:15 дня') '2026-11-11 14:15 Обед'
$sd = $tk.GetField('Someday').GetValue($null); $r = $tk.GetMethod('Parse').Invoke($null, @('завтра спорт', $sd))
T 'задачи: «завтра» от «когда-нибудь» — от сегодняшнего дня' ($r[0].Day.ToString('yyyy-MM-dd')) ((Get-Date).Date.AddDays(1).ToString('yyyy-MM-dd'))
$r = $tk.GetMethod('Parse').Invoke($null, @('в пятницу отчёт', $sd))
T 'задачи: день недели от «когда-нибудь» — без ошибки' ($r[0].Day -lt $sd -and $r[0].Day -gt (Get-Date).Date) $true

# --- Вид «Неделя»: понедельник недели, подпись диапазона, будущие разы повторяющихся задач
$sf = [Reflection.BindingFlags]'NonPublic,Static'
$wo = $iwT.GetMethod('WeekOf', $sf)
T 'неделя: суббота — с понедельника' ($wo.Invoke($null, @([datetime]'2026-11-14 18:30')).ToString('yyyy-MM-dd')) '2026-11-09'
T 'неделя: воскресенье — та же неделя' ($wo.Invoke($null, @([datetime]'2026-11-15')).ToString('yyyy-MM-dd')) '2026-11-09'
T 'неделя: понедельник — сам' ($wo.Invoke($null, @([datetime]'2026-11-16')).ToString('yyyy-MM-dd')) '2026-11-16'
$wr = $iwT.GetMethod('WeekRange', $sf); $ru = [Globalization.CultureInfo]::GetCultureInfo('ru-RU'); $yr = (Get-Date).Year
T 'неделя: подпись в одном месяце' ($wr.Invoke($null, @([datetime]"$yr-03-02", $ru))) '2–8 марта'
T 'неделя: подпись через месяц' ($wr.Invoke($null, @([datetime]"$yr-03-28", $ru))) '28 марта – 3 апреля'
T 'неделя: подпись через год' ($wr.Invoke($null, @([datetime]'2030-12-28', $ru))) '28 декабря 2030 – 3 января 2031'
$ro = $iwT.GetMethod('RepeatsOn', $sf)
function RT([string]$repeat, [string]$day, [string]$on, [string]$today) {
    $t = [Activator]::CreateInstance($asm.GetType('Kapsula.DayTask'))
    $t.GetType().GetField('Repeat').SetValue($t, $repeat); $t.GetType().GetField('Day').SetValue($t, [datetime]$day)
    $ro.Invoke($null, @($t, [datetime]$on, [datetime]$today))
}
T 'неделя: «пн и ср» — бледно в среду' (RT 'w:0,2' '2026-11-16' '2026-11-18' '2026-11-14') $true
T 'неделя: «пн и ср» — не в четверг' (RT 'w:0,2' '2026-11-16' '2026-11-19' '2026-11-14') $false
T 'неделя: ближайший раз — обычной задачей, не бледной' (RT 'd' '2026-11-14' '2026-11-14' '2026-11-14') $false
T 'неделя: каждый день — завтра бледно' (RT 'd' '2026-11-14' '2026-11-15' '2026-11-14') $true
T 'неделя: просроченная повторяющаяся — не в прошедшие дни' (RT 'd' '2026-11-11' '2026-11-13' '2026-11-14') $false
T 'неделя: обычная задача не повторяется' (RT '' '2026-11-14' '2026-11-15' '2026-11-14') $false

# --- «Всё на сегодня сделано»: сегодняшние разы повторяющихся задач
$dc = $iwT.GetMethod('DayCount', $sf)
T 'праздник: счётчик за сегодня' ($dc.Invoke($null, @('20261114:3', '20261114'))) 3
T 'праздник: вчерашний счётчик не в счёт' ($dc.Invoke($null, @('20261113:5', '20261114'))) 0
T 'праздник: счётчика ещё нет' ($dc.Invoke($null, @('', '20261114'))) 0
T 'праздник: испорченный счётчик — ноль' ($dc.Invoke($null, @('20261114:abc', '20261114'))) 0

# --- Погладить зверька: курсор ходит над ним туда-сюда (каждые 33 мс, как опрос острова)
$stT = $asm.GetType('Kapsula.Stroke')
function StrokeRun([scriptblock]$path, [int]$need) {
    $s = [Activator]::CreateInstance($stT); $mv = $stT.GetMethod('Move')
    for ($i = 0; $i -lt 60; $i++) {
        $t = $i * 33; $p = & $path $t
        if ($mv.Invoke($s, @([double]$p[0], [double]$p[1], [double]8, $need, (100000 + $t)))) { return $true }
    }
    $false
}
T 'гладить: туда-сюда — гладят' (StrokeRun { param($t) @((100 + 30 * [math]::Sin($t / 1000 * [math]::PI * 2 * 2)), 50) } 2) $true
T 'гладить: вверх-вниз — тоже' (StrokeRun { param($t) @(100, (50 + 25 * [math]::Sin($t / 1000 * [math]::PI * 2 * 2))) } 2) $true
T 'гладить: провели мимо — нет' (StrokeRun { param($t) @((100 + $t * 0.3), 50) } 2) $false
T 'гладить: дрожь руки — нет' (StrokeRun { param($t) @((100 + 2 * [math]::Sin($t / 1000 * [math]::PI * 2 * 4)), 50) } 2) $false
T 'гладить: один разворот (подвели и ушли) — нет' (StrokeRun { param($t) @((100 + 30 * [math]::Sin([math]::Min($t, 700) / 700 * [math]::PI)), 50) } 2) $false

# --- колёсико над карточкой: глушится только хвост жеста — и тогда, когда счётчик Windows ушёл в минус
$wm = $iwT.GetMethod('WheelMuted', $sf)
T 'колёсико: сразу после жеста над карточкой глушится' ($wm.Invoke($null, @([int]1450, [int]1000))) $true
T 'колёсико: через полсекунды после жеста снова работает' ($wm.Invoke($null, @([int]1450, [int]1500))) $false
T 'колёсико: без жеста не глушится и после 25 дней работы Windows' ($wm.Invoke($null, @([int]0, [int]-1000000000))) $false
T 'колёсико: давний жест не глушит' ($wm.Invoke($null, @([int]-2000000000, [int]2000000000))) $false
T 'колёсико: жест на переходе счётчика через край — глушится' ($wm.Invoke($null, @([int]-2147483448, [int]2147483447))) $true

# --- Время звонка: после часа — с часами, как на iPhone
$cc = $iwT.GetMethod('CallClock', $sf)
T 'звонок: секунды' ($cc.Invoke($null, @([TimeSpan]::FromSeconds(7)))) '0:07'
T 'звонок: минуты' ($cc.Invoke($null, @([TimeSpan]::FromSeconds(754)))) '12:34'
T 'звонок: больше часа — с часами' ($cc.Invoke($null, @([TimeSpan]::FromSeconds(3923)))) '1:05:23'
T 'звонок: сутки' ($cc.Invoke($null, @([TimeSpan]::FromHours(25.5)))) '25:30:00'

# --- «Не засыпать»: команда в строке острова и на сколько
$ap = $iwT.GetMethod('AwakeParse', $sf)
function AP([string]$q) { $a = [object[]]@($q, [TimeSpan]::Zero); $ok = $ap.Invoke($null, $a); if (-not $ok) { return 'нет' }; if ($a[1] -eq [TimeSpan]::Zero) { return 'пока не выключат' }; return ([string]$a[1].TotalMinutes) + ' мин' }
T 'не засыпать: просто' (AP 'не засыпать') 'пока не выключат'
T 'не засыпать: на 2 часа' (AP 'не засыпать 2 ч') '120 мин'
T 'не засыпать: «на 30 минут»' (AP 'Не засыпай на 30 минут') '30 мин'
T 'не засыпать: кофе 45 мин' (AP 'кофе 45 мин') '45 мин'
T 'не засыпать: caffeinate 2h' (AP 'caffeinate 2h') '120 мин'
T 'не засыпать: «кофейня» — не команда' (AP 'кофейня') 'нет'
T 'не засыпать: «кофе с молоком» — не команда' (AP 'кофе с молоком') 'нет'

# --- «Запустить таймер на …», «Не засыпать на …»: словами и в падеже после «на»; число не отрывается от слова
$nb = [string][char]0xA0
$spw = $iwT.GetMethod('SpanWords', $sf)
T 'на сколько: 1 минуту' ($spw.Invoke($null, @([TimeSpan]::FromMinutes(1)))) ('1' + $nb + 'минуту')
T 'на сколько: 21 минуту' ($spw.Invoke($null, @([TimeSpan]::FromMinutes(21)))) ('21' + $nb + 'минуту')
T 'на сколько: 2 минуты' ($spw.Invoke($null, @([TimeSpan]::FromMinutes(2)))) ('2' + $nb + 'минуты')
T 'на сколько: 11 минут' ($spw.Invoke($null, @([TimeSpan]::FromMinutes(11)))) ('11' + $nb + 'минут')
T 'на сколько: 1 час 30 минут' ($spw.Invoke($null, @([TimeSpan]::FromMinutes(90)))) ('1' + $nb + 'час 30' + $nb + 'минут')
T 'на сколько: 45 секунд' ($spw.Invoke($null, @([TimeSpan]::FromSeconds(45)))) ('45' + $nb + 'секунд')

# --- число со словом: 1 файл, 2 файла, 5 файлов, 11 файлов, 21 файл, 22 файла, 111 файлов; тысячи — узким неразрывным пробелом
$pl = $iwT.GetMethod('Plural', $sf)
foreach ($c in @(@(1, 'файл'), @(2, 'файла'), @(5, 'файлов'), @(11, 'файлов'), @(21, 'файл'), @(22, 'файла'), @(111, 'файлов'))) {
    T "число: $($c[0]) $($c[1])" ($pl.Invoke($null, @([int]$c[0], 'файл', 'файла', 'файлов'))) ([string]$c[0] + $nb + $c[1])
}
T 'число: 1 284 раза' ($pl.Invoke($null, @([int]1284, 'раз', 'раза', 'раз'))) ('1' + [char]0x202F + '284' + $nb + 'раза')
T 'тысячи: узкий неразрывный пробел' ((Call 'TextTools' 'Thousands' @([long]1284)).Result) ('1' + [char]0x202F + '284')

# --- звонок: название программы в карточке («WhatsApp», «Яндекс»); T сравнивает без учёта регистра, поэтому -ceq
$pr = $iwT.GetMethod('Pretty', $sf)
T 'звонок: WhatsApp — с большой A' (($pr.Invoke($null, @('whatsapp'))) -ceq 'WhatsApp') $true
T 'звонок: Яндекс — по-русски' ($pr.Invoke($null, @('yandex'))) 'Яндекс'
T 'звонок: Edge, а не Msedge' ($pr.Invoke($null, @('msedge'))) 'Edge'
T 'звонок: Zoom — с большой буквы' (($pr.Invoke($null, @('zoom'))) -ceq 'Zoom') $true

# --- сокращение текста не режет эмодзи пополам: ни «…» посередине, ни подсказка на 600 знаков
$emo = [char]::ConvertFromUtf32(0x1F600)
$me = $asm.GetType('Kapsula.Ui').GetMethod('MiddleEllipsis').Invoke($null, @(($emo * 12), 7))
T 'сокращение: эмодзи целые' ($me) ($emo + $emo + '…')
$tt = $iwT.GetMethod('TipText', $sf).Invoke($null, @(('a' * 598) + $emo + ('b' * 10)))
T 'подсказка: эмодзи на границе 600 знаков не режется' ($tt.Length.ToString() + ' ' + [char]::IsHighSurrogate($tt[$tt.Length - 2])) '599 False'

# --- строка команд: команда — само слово или его начало, а не любое слово с тем же началом («блокнот» не блокирует компьютер)
$hk = $iwT.GetMethod('HasKey', [Reflection.BindingFlags]'NonPublic,Static')
$lockKeys = [string[]]@('заблокировать', 'заблокируй', 'блокировка', 'блокировать', 'lock')
T 'команды: «блокнот» — не блокировка компьютера' ($hk.Invoke($null, @('блокнот', $lockKeys))) $false
T 'команды: начало слова «забл» — блокировка' ($hk.Invoke($null, @('забл', $lockKeys))) $true
T 'команды: «lock screen» — блокировка' ($hk.Invoke($null, @('lock screen', $lockKeys))) $true
T 'команды: «светлана» — не светлая тема' ($hk.Invoke($null, @('светлана', [string[]]@('светлая тема', 'светлый режим', 'light')))) $false
T 'команды: «lightroom» — не светлая тема' ($hk.Invoke($null, @('lightroom', [string[]]@('светлая тема', 'светлый режим', 'light')))) $false
T 'команды: «записки» — не запись экрана' ($hk.Invoke($null, @('записки', [string[]]@('запись экрана', 'записать экран', 'видеозапись', 'record')))) $false
T 'команды: «запись» — запись экрана' ($hk.Invoke($null, @('запись', [string[]]@('запись экрана', 'записать экран', 'видеозапись', 'record')))) $true
T 'команды: «голосование» — не диктовка' ($hk.Invoke($null, @('голосование', [string[]]@('диктовка', 'диктовать', 'голосовой ввод', 'голос', 'voice')))) $false
T 'команды: «ии привет» — чат' ($hk.Invoke($null, @('ии привет', [string[]]@('чат', 'claude', 'клод', 'ии')))) $true

# --- ярлыки: из перетащенного текста закрепляется только первая ссылка, без знаков препинания в конце
$dl = $iwT.GetMethod('DroppedLink', [Reflection.BindingFlags]'NonPublic,Static')
T 'ярлыки: ссылка и слова после неё' ($dl.Invoke($null, @('https://example.com is down'))) 'https://example.com'
T 'ярлыки: запятая после ссылки' ($dl.Invoke($null, @('https://example.com,'))) 'https://example.com'
T 'ярлыки: две ссылки строками — первая' ($dl.Invoke($null, @("https://a.com`r`nhttps://b.com"))) 'https://a.com'
T 'ярлыки: скобка и точка из текста вокруг' ($dl.Invoke($null, @('https://example.com/page).'))) 'https://example.com/page'
T 'ярлыки: своя скобка в ссылке остаётся' ($dl.Invoke($null, @('https://ru.wikipedia.org/wiki/Python_(язык)'))) 'https://ru.wikipedia.org/wiki/Python_(язык)'
T 'ярлыки: «https://» без адреса — не ссылка' ($null -eq $dl.Invoke($null, @('https://'))) $true
T 'ярлыки: обычный текст — не ссылка' ($null -eq $dl.Invoke($null, @('просто текст'))) $true

# --- клавиши: подпись сочетания в настройках
T 'клавиши: подпись сочетания Ctrl+Alt+G' ($asm.GetType('Kapsula.IslandWindow').GetMethod('HotText').Invoke($null, @([uint32]3, [uint32]0x47))) 'Ctrl+Alt+G'
T 'клавиши: выключенное сочетание' ($asm.GetType('Kapsula.IslandWindow').GetMethod('HotText').Invoke($null, @([uint32]3, [uint32]0))) 'Отключено'

# --- Зверёк — главное: своя вкладка сразу после «Главной», первый раздел настроек, лапка
$pp = $iwT.GetField('PPet', $sf).GetValue($null)
$tg = $iwT.GetField('TabGroups', $sf).GetValue($null)
$own = $false; foreach ($g in $tg) { if ($g.Length -eq 1 -and $g[0] -eq $pp) { $own = $true } }
T 'вкладки: у зверька своя группа' $own $true
T 'вкладки: зверёк сразу после «Главной»' ([int]$tg[1][0]) ([int]$pp)
$to = $iwT.GetField('TabOrder', $sf).GetValue($null)
T 'вкладки: каждая страница в порядке один раз' (@($to | Sort-Object -Unique).Count) 12
T 'вкладки: номера страниц подряд, без пустых мест' (($to | Measure-Object -Maximum).Maximum) ($to.Length - 1)
$swT = $asm.GetType('Kapsula.SettingsWindow')
$so = $swT.GetField('SectionOrder', $sf).GetValue($null)
T 'настройки: «Зверёк» — первый раздел' $so[0] 'Зверёк'
$sg = $swT.GetField('SectionGlyphs', $sf).GetValue($null); $sh = $swT.GetField('SectionHints', $sf).GetValue($null); $sc = $swT.GetField('SectionColors', $sf).GetValue($null)
T 'настройки: у каждого раздела значок, подсказка и цвет' (($so.Length -eq $sg.Length) -and ($so.Length -eq $sh.Length) -and ($so.Length -eq $sc.Length)) $true
T 'настройки: пустого раздела «Главная» нет' ($so -contains 'Главная') $false
T 'настройки: подсказки разделов не сдвинулись' ($sh[[array]::IndexOf($so, 'Музыка')]) 'Плеер, край, тексты песен и оценки'
T 'настройки: подсказка «Приватности» — на своём месте' ($sh[[array]::IndexOf($so, 'Приватность')]) 'Камера и микрофон, пароли и чем делиться'
$tm = $swT.GetMethod('Times', $sf)
T 'гладили: 1 раз' ($tm.Invoke($null, @(1))) 'раз'
T 'гладили: 3 раза' ($tm.Invoke($null, @(3))) 'раза'
T 'гладили: 12 раз' ($tm.Invoke($null, @(12))) 'раз'
T 'гладили: 22 раза' ($tm.Invoke($null, @(22))) 'раза'
$pg = $asm.GetType('Kapsula.Ui').GetProperty('PawGeometry', [Reflection.BindingFlags]'Public,Static').GetValue($null, $null)
T 'лапка: рисунок 9×8' ('{0}x{1}' -f $pg.Bounds.Width, $pg.Bounds.Height) '9x8'

# --- превью в настройках: мини-плеер только у строк про текст песни
$pk = $asm.GetType('Kapsula.SettingsWindow').GetMethod('PreviewKindFor', [Reflection.BindingFlags]'NonPublic,Static')
T 'превью: «Скрывать текст уведомлений» — не мини-плеер' ($pk.Invoke($null, @('Скрывать текст уведомлений'))) -1
T 'превью: «Сдвиг текста песни» — мини-плеер' ($pk.Invoke($null, @('Сдвиг текста песни'))) 2
T 'превью: «Тексты песен» — мини-плеер' ($pk.Invoke($null, @('Тексты песен'))) 2

# --- Демо-режим: на снимках только выдуманное
$dw = $iwT.GetMethod('DemoWeather', $sf).Invoke($null, @())
T 'демо: погода — 12 часов и 7 дней' ('{0}/{1}' -f $dw.Hours.Count, $dw.Days.Count) '12/7'
T 'демо: прогноз начинается с «Сегодня»' $dw.Days[0][0] 'Сегодня'
$dn = $iwT.GetMethod('DemoWifi', $sf).Invoke($null, @())
T 'демо: из сетей подключена одна' (@($dn | Where-Object { $_.Connected }).Count) 1
$dc = $iwT.GetMethod('DemoControl', $sf).Invoke($null, @())
T 'демо: Bluetooth — придуманные устройства' ($dc.Bt.Count) 2

# --- Прогулка по панели задач: только внизу экрана, видимая и во всю ширину
$tu = $iwT.GetMethod('TaskbarUsable', $sf); $rt = $asm.GetType('Kapsula.Native+RECT')
function RC([int]$l, [int]$t, [int]$r, [int]$b) { $o = [Activator]::CreateInstance($rt); $rt.GetField('L').SetValue($o, $l); $rt.GetField('T').SetValue($o, $t); $rt.GetField('R').SetValue($o, $r); $rt.GetField('B').SetValue($o, $b); ,$o }
$mon = RC 0 0 1920 1080
T 'панель задач: внизу — гуляет' ($tu.Invoke($null, @((RC 0 1032 1920 1080), $mon))) $true
T 'панель задач: спрятана — нет' ($tu.Invoke($null, @((RC 0 1078 1920 1126), $mon))) $false
T 'панель задач: сверху — нет' ($tu.Invoke($null, @((RC 0 0 1920 48), $mon))) $false
T 'панель задач: сбоку — нет' ($tu.Invoke($null, @((RC 0 0 62 1080), $mon))) $false

# --- зверёк несёт к карточке то, что на ней (и попутчик, и тот, что прыгает с окна)
$cm = $iwT.GetMethod('CarryMood', $sf)
T 'карточка: обычная — с письмом' ($cm.Invoke($null, @(''))) 'post'
T 'карточка: напоминание — с колокольчиком' ($cm.Invoke($null, @('bell'))) 'bell'
T 'карточка: звонок — с трубкой' ($cm.Invoke($null, @('phone'))) 'phone'
T 'карточка: достижение — с кубком' ($cm.Invoke($null, @('cup'))) 'cup'

# --- Ночной колпак: ночью — на свободной голове, не поверх короны
$pvT = $asm.GetType('Kapsula.PetView'); $oc = $pvT.GetMethod('OutfitCells', $sf)
$pvT.GetField('TestNight').SetValue($null, 1)
T 'колпак: ночью с бабочкой — колпак есть, повязка — без него' (($oc.Invoke($null, @('band'))).Length -eq 3 -and ($oc.Invoke($null, @('bow'))).Length -gt 6) $true
T 'колпак: корона остаётся короной' (($oc.Invoke($null, @('crown'))).Length) 5
T 'колпак: «без наряда» — без колпака' (($oc.Invoke($null, @('none'))).Length) 0
$pvT.GetField('TestNight').SetValue($null, 0)
T 'колпак: днём бабочка без колпака' (($oc.Invoke($null, @('bow'))).Length) 6
$pvT.GetField('TestNight').SetValue($null, -1)

# --- стикер: кадры GIF собираются по правилам самого GIF (холст во весь GIF, стирание и «верни как было»), битый файл — ошибка, а не пустой стикер
$ds = $iwT.GetMethod('DecodeSticker', $sf)
function GifLzw([int[]]$pix) { $codes = @(); foreach ($p in $pix) { $codes += 4, $p }; $codes += 5; $b = New-Object System.Collections.Generic.List[byte]; $acc = 0; $n = 0; foreach ($c in $codes) { $acc = $acc -bor ($c -shl $n); $n += 3; while ($n -ge 8) { $b.Add([byte]($acc -band 0xFF)); $acc = $acc -shr 8; $n -= 8 } }; if ($n -gt 0) { $b.Add([byte]($acc -band 0xFF)) }; ,([byte[]](@(2, $b.Count) + $b.ToArray() + @(0))) }
function GifFrame([int]$disp, [int]$left, [int]$idx) { ,([byte[]](@(0x21, 0xF9, 4, (($disp -shl 2) -bor 1), 10, 0, 0, 0, 0x2C, $left, 0, 0, 0, 2, 0, 2, 0, 0) + (GifLzw @($idx, $idx, $idx, $idx)))) }
function GifOf([object[]]$frames) { ,([byte[]]([Text.Encoding]::ASCII.GetBytes('GIF89a') + @(4, 0, 2, 0, 0x91, 0, 0, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255) + ($frames | ForEach-Object { $_ }) + @(0x3B))) }
function GifPx($bmp, [int]$x) { $p = New-Object byte[] 4; $bmp.CopyPixels((New-Object System.Windows.Int32Rect $x, 0, 1, 1), $p, 4, 0); '{0:X2}{1:X2}{2:X2}{3:X2}' -f $p[3], $p[2], $p[1], $p[0] }
$ga = [object[]]@((GifOf @((GifFrame 2 0 1), (GifFrame 1 2 3))), $null, $null); [void]$ds.Invoke($null, $ga)
T 'стикер: холст GIF — во весь его экран, а не по первому кадру' ('{0}x{1}' -f $ga[1][0].PixelWidth, $ga[1][0].PixelHeight) '4x2'
T 'стикер: место прошлого кадра стёрто (2) — без следов' ((GifPx $ga[1][1] 0) + ' ' + (GifPx $ga[1][1] 3)) '00000000 FF0000FF'
$ga = [object[]]@((GifOf @((GifFrame 1 0 1), (GifFrame 3 0 3), (GifFrame 1 2 2))), $null, $null); [void]$ds.Invoke($null, $ga)
T 'стикер: «верни как было» (3)' ((GifPx $ga[1][2] 0) + ' ' + (GifPx $ga[1][2] 3)) 'FFFF0000 FF00FF00'
$gb = [object[]]@([byte[]](1, 2, 3, 4), $null, $null)
T 'стикер: битый файл не открывается' $(try { [void]$ds.Invoke($null, $gb); 'открылся' } catch { 'ошибка' }) 'ошибка'

# --- Порты Kapsula на этом компьютере не пересекаются (в 0.6.1–0.6.3 пульт и звонки Discord делили 42073)
$ports = @('DiscordBridge', 'SteamBridge', 'ClaudeBridge', 'DiscordVoiceServer', 'PhoneCam', 'Phone') | ForEach-Object { $asm.GetType('Kapsula.' + $_).GetField('Port').GetValue($null) }
T 'порты: у каждой связи свой' (($ports | Sort-Object -Unique).Count) 6
T 'порты: звонки Discord — 42073, как в плагине' ($asm.GetType('Kapsula.DiscordVoiceServer').GetField('Port').GetValue($null)) 42073

# --- Команды острова плагину в Discord: несколько сразу, кавычки в имени экрана не ломают JSON
$dvs = $asm.GetType('Kapsula.DiscordVoiceServer')
$dvs.GetMethod('Command').Invoke($null, @('mute')); $dvs.GetMethod('Command').Invoke($null, @('stream:window:1"2\3'))
$cmdJson = $dvs.GetMethod('TakeCommands', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, @())
T 'discord: команды острова — правильный JSON' ((($cmdJson | ConvertFrom-Json).commands) -join '|') 'mute|stream:window:1"2\3'
T 'discord: после отправки очередь пуста' ($dvs.GetMethod('TakeCommands', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, @())) '{"commands":[]}'

# --- наряды за привычки: у каждого наряда есть название
T 'наряды: названий столько же, сколько нарядов' ($iwT.GetField('OutfitNames').GetValue($null).Length) ($iwT.GetField('OutfitIds').GetValue($null).Length)

# --- полученные наряды остаются насовсем: список через запятую
$oh = $iwT.GetMethod('OwnedHas', $sf)
T 'наряды: полученный — в списке' ($oh.Invoke($null, @('band,crown', 'crown'))) $true
T 'наряды: не полученный — нет' ($oh.Invoke($null, @('band,crown', 'bow'))) $false
T 'наряды: список пуст' ($oh.Invoke($null, @('', 'band'))) $false

# --- строка команд: ярлыки удаления программ — не программы; одна-две буквы ищутся с начала слова
$na = $iwT.GetMethod('NotApp')
T 'программы: «Деинсталлировать …» — не программа' ($na.Invoke($null, @(,'Деинсталлировать Foo'))) $true
T 'программы: «Uninstall Foo» — не программа' ($na.Invoke($null, @(,'Uninstall Foo'))) $true
T 'программы: «Удалить Foo» — не программа' ($na.Invoke($null, @(,'Удалить Foo'))) $true
T 'программы: «Удалённый помощник» — программа' ($na.Invoke($null, @(,'Удалённый помощник Windows'))) $false
T 'программы: обычная программа' ($na.Invoke($null, @(,'Блокнот'))) $false
$ws = $iwT.GetMethod('WordStart')
T 'поиск: «а» — начало второго слова' ($ws.Invoke($null, @('microsoft аудио', 'а'))) $true
T 'поиск: «а» посреди слова — мимо' ($ws.Invoke($null, @('деинсталлировать', 'а'))) $false
T 'поиск: после дефиса — начало слова' ($ws.Invoke($null, @('kapsula-демо', 'де'))) $true
T 'поиск: начало названия' ($ws.Invoke($null, @('блокнот', 'бл'))) $true

# --- «напомни …» в строке команд: когда сработает — словами
$rw = $iwT.GetMethod('RemindWhen')
T 'напомни: сегодня' ($rw.Invoke($null, @([DateTime]::Today.AddHours(15).AddMinutes(40), $false))) 'в 15:40'
T 'напомни: завтра' ($rw.Invoke($null, @([DateTime]::Today.AddDays(1).AddHours(9), $false))) 'завтра в 9:00'
T 'напомни: каждый день' ($rw.Invoke($null, @([DateTime]::Today.AddHours(23), $true))) 'каждый день в 23:00'
$far = [DateTime]::Today.AddDays(3).AddHours(9)
T 'напомни: дальше — с датой' ($rw.Invoke($null, @($far, $false))) ($far.ToString('d MMMM', [Globalization.CultureInfo]::GetCultureInfo('ru-RU')) + ' в 9:00')

# --- Хаб dev.txt: что Kapsula рассказывает другим приложениям (CoreHub.cs) — музыка, погода, календарь, непрочитанное, нагрузка
Add-Type -AssemblyName System.Web.Extensions
$chT = $asm.GetType('Kapsula.CoreHub')
$hubJs = New-Object System.Web.Script.Serialization.JavaScriptSerializer
function HubJson($o) { if ($null -eq $o) { 'null' } else { $hubJs.Serialize($o) } }

# значок погоды: все коды, которые понимает сам остров (Weather.Describe), и незнакомые
$wi = $chT.GetMethod('WeatherIcon')
foreach ($c in @(@(0, 'clear'), @(1, 'clear'), @(2, 'cloudy'), @(3, 'cloudy'), @(45, 'fog'), @(48, 'fog'), @(51, 'rain'), @(57, 'rain'), @(61, 'rain'), @(67, 'rain'),
                 @(71, 'snow'), @(77, 'snow'), @(80, 'rain'), @(82, 'rain'), @(85, 'snow'), @(86, 'snow'), @(95, 'storm'), @(99, 'storm'), @(-1, 'cloudy'), @(42, 'cloudy'))) {
    T "хаб: значок погоды для кода $($c[0])" ($wi.Invoke($null, @([int]$c[0]))) $c[1]
}
$wdT = $asm.GetType('Kapsula.WeatherData')
$wd = [Activator]::CreateInstance($wdT)
$wdT.GetField('City').SetValue($wd, 'Киев'); $wdT.GetField('Temp').SetValue($wd, [double]-3.6); $wdT.GetField('Code').SetValue($wd, [int]71)
$ws2 = $chT.GetMethod('WeatherState')
T 'хаб: погода — градусы целые, подпись и значок' (HubJson $ws2.Invoke($null, @($true, $wd))) '{"temp":-4,"text":"Снег","icon":"snow","city":"Киев"}'
T 'хаб: погода — показывать запретили' (HubJson $ws2.Invoke($null, @($false, $wd))) 'null'
T 'хаб: погода — не загружена' (HubJson $ws2.Invoke($null, @($true, $null))) 'null'
$wdT.GetField('Error').SetValue($wd, 'нет связи')
T 'хаб: погода — вместо прогноза ошибка' (HubJson $ws2.Invoke($null, @($true, $wd))) 'null'

# музыка
$ms = $chT.GetMethod('MediaState')
T 'хаб: музыка — трек' (HubJson $ms.Invoke($null, @($true, $true, $false, 'Ночной маршрут', 'Kapsula Demo', 'Spotify'))) '{"playing":false,"title":"Ночной маршрут","artist":"Kapsula Demo","app":"Spotify"}'
T 'хаб: музыка — трека нет' (HubJson $ms.Invoke($null, @($true, $false, $false, '', '', ''))) 'null'
T 'хаб: музыка — показывать запретили' (HubJson $ms.Invoke($null, @($false, $true, $true, 'А', 'Б', 'В'))) 'null'

# календарь: сегодняшние события, которые ещё не кончились, по порядку, не больше пяти
$ceT = $asm.GetType('Kapsula.CalEvent')
$evList = [Activator]::CreateInstance(([System.Collections.Generic.List`1]).MakeGenericType($ceT))
function HubEv($title, $start, $end, $allDay) {
    $e = [Activator]::CreateInstance($ceT)
    $ceT.GetField('Subject').SetValue($e, $title); $ceT.GetField('Location').SetValue($e, 'тайное место'); $ceT.GetField('Start').SetValue($e, [datetime]$start)
    if ($end) { $ceT.GetField('End').SetValue($e, [datetime]$end) }
    $ceT.GetField('AllDay').SetValue($e, [bool]$allDay)
    $evList.Add($e)
}
HubEv 'Завтра' '2026-11-19 09:00' '2026-11-19 10:00' $false
HubEv 'Поздний' '2026-11-18 23:00' '2026-11-19 01:00' $false
HubEv 'В' '2026-11-18 17:00' '2026-11-18 18:00' $false
HubEv 'Б' '2026-11-18 16:00' '2026-11-18 17:00' $false
HubEv 'А' '2026-11-18 15:00' '2026-11-18 16:00' $false
HubEv 'Сейчас' '2026-11-18 14:00' $null $false
HubEv 'Минуту назад' '2026-11-18 13:59' $null $false
HubEv 'Идёт' '2026-11-18 13:30' '2026-11-18 14:30' $false
HubEv 'Кончилось' '2026-11-18 09:00' '2026-11-18 10:00' $false
HubEv 'Весь день' '2026-11-18 00:00' '2026-11-19 00:00' $true
HubEv 'Ночная смена' '2026-11-17 22:00' '2026-11-18 15:00' $false
HubEv 'Вчера' '2026-11-17 10:00' '2026-11-17 11:00' $false
HubEv 'Вчера весь день' '2026-11-17 00:00' '2026-11-18 00:00' $true
$te = $chT.GetMethod('TodayEvents')
$hubNow = [datetime]'2026-11-18 14:00'
$got = $te.Invoke($null, [object[]]@($evList, $hubNow))
T 'хаб: события — не больше пяти' $got.Length 5
T 'хаб: события — сегодняшние и не кончившиеся, по порядку' (($got | ForEach-Object { $_['title'] + ' ' + $_['start'] + '-' + $_['end'] }) -join '|') 'Ночная смена 00:00-15:00|Весь день 00:00-23:59|Идёт 13:30-14:30|Сейчас 14:00-14:00|А 15:00-16:00'
T 'хаб: события — только название и время' (($got[2].Keys | Sort-Object) -join ',') 'end,start,title'
T 'хаб: события — «весь день» помечен' (($got[1].Keys | Sort-Object) -join ',') 'allDay,end,start,title'
$got = $te.Invoke($null, [object[]]@($evList, [datetime]'2026-11-18 23:30'))
T 'хаб: события — поздним вечером остаётся то, что идёт' (($got | ForEach-Object { $_['title'] + ' ' + $_['start'] + '-' + $_['end'] }) -join '|') 'Весь день 00:00-23:59|Поздний 23:00-01:00'
$cs = $chT.GetMethod('CalendarState')
T 'хаб: календарь — событий нет, но доступ есть' (HubJson $cs.Invoke($null, [object[]]@($true, $true, $evList, [datetime]'2026-11-20 12:00'))) '{"events":[]}'
T 'хаб: календарь — доступа нет' (HubJson $cs.Invoke($null, [object[]]@($true, $false, $evList, $hubNow))) 'null'
T 'хаб: календарь — показывать запретили' (HubJson $cs.Invoke($null, [object[]]@($false, $true, $evList, $hubNow))) 'null'

# непрочитанное
$us = $chT.GetMethod('UnreadState')
T 'хаб: непрочитанное — сумма и по программам' (HubJson $us.Invoke($null, @($true, $true, 2, 3))) '{"total":5,"apps":{"discord":2,"steam":3}}'
T 'хаб: непрочитанное — ничего нет' (HubJson $us.Invoke($null, @($true, $true, 0, 0))) '{"total":0,"apps":{"discord":0,"steam":0}}'
T 'хаб: непрочитанное — показывать запретили' (HubJson $us.Invoke($null, @($false, $true, 2, 3))) 'null'
T 'хаб: непрочитанное — счётчик выключен в Kapsula' (HubJson $us.Invoke($null, @($true, $false, 2, 3))) 'null'

# нагрузка
$ss = $chT.GetMethod('SystemState')
T 'хаб: нагрузка — всё измерено' (HubJson $ss.Invoke($null, @([double]0.354, 48, 66, [double]1536.4, [double]200))) '{"cpu":35,"ram":48,"gpu":66,"down":1536,"up":200}'
T 'хаб: нагрузка — без видеокарты и пока без сети' (HubJson $ss.Invoke($null, @([double]1.2, 48, -1, [double]-1, [double]-1))) '{"cpu":100,"ram":48,"gpu":null}'
$ll = $chT.GetMethod('LoadLine')
T 'нагрузка в игре: строка' ($ll.Invoke($null, @([double]0.35, 66, 48))) 'ЦП 35% · ГП 66% · ОЗУ 48%'
T 'нагрузка в игре: без видеокарты' ($ll.Invoke($null, @([double]0.05, -1, 100))) 'ЦП 5% · ОЗУ 100%'

# переключатели «Показывать…» (общий и два прежних) и строка «Нагрузка в игре» лежат в своих разделах окна настроек
$lay = $asm.GetType('Kapsula.SettingsWindow').GetField('Layout', $sf).GetValue($null)
$secOf = @{}; foreach ($l in $lay) { foreach ($t in $l) { $secOf[$t] = $l[0] } }
T 'настройки: «Делиться с приложениями dev.txt» — в «Приватности»' $secOf['Делиться с приложениями dev.txt'] 'Приватность'
T 'настройки: «Показывать музыку…» — в «Приватности»' $secOf['Показывать музыку, погоду и события другим приложениям dev.txt'] 'Приватность'
T 'настройки: «Показывать счётчик непрочитанного» — в «Приватности»' $secOf['Показывать счётчик непрочитанного'] 'Приватность'
T 'настройки: «Нагрузка в капсуле во время игры» — в «Играх»' $secOf['Нагрузка в капсуле во время игры'] 'Игры'

# --- Семья dev.txt (Family.cs, CoreHub.cs): «Не беспокоить», свой значок в трее, быстрые действия, клавиши, уведомления
# «Не беспокоить»: своё или общее из core.txt; время общего вышло, а выключение ещё не пришло, — уже не тихо
$qo = $chT.GetMethod('QuietOn')
$qn = [datetime]'2026-11-19 12:00'; $qms = [long]1763550000000
T 'семья: тихо — своё ещё идёт' ($qo.Invoke($null, @($qn.AddMinutes(5), $qn, $false, [long]0, $qms))) $true
T 'семья: тихо — своё кончилось, общего нет' ($qo.Invoke($null, @($qn.AddMinutes(-1), $qn, $false, [long]0, $qms))) $false
T 'семья: тихо — общее из core.txt, пока не выключат' ($qo.Invoke($null, @([datetime]::MinValue, $qn, $true, [long]0, $qms))) $true
T 'семья: тихо — общее до времени в будущем' ($qo.Invoke($null, @([datetime]::MinValue, $qn, $true, [long]($qms + 60000), $qms))) $true
T 'семья: тихо — время общего вышло' ($qo.Invoke($null, @([datetime]::MinValue, $qn, $true, [long]($qms - 1), $qms))) $false
T 'семья: тихо — общее выключено, своё идёт' ($qo.Invoke($null, @($qn.AddMinutes(30), $qn, $false, [long]($qms + 60000), $qms))) $true
$ql = $iwT.GetMethod('QuietLeft', $sf)
T 'семья: «Не беспокоить» — сколько осталось своего' ($ql.Invoke($null, [object[]]@($qn.AddMinutes(179), $false, $null, $qn))) 'осталось 2 ч 59 мин'
T 'семья: «Не беспокоить» — общее до времени' ($ql.Invoke($null, [object[]]@([datetime]::MinValue, $true, $qn.AddMinutes(20), $qn))) 'осталось 20 мин'
T 'семья: «Не беспокоить» — общее без конца' ($ql.Invoke($null, [object[]]@([datetime]::MinValue, $true, $null, $qn))) 'пока не выключите'
T 'семья: «Не беспокоить» — своё важнее общего' ($ql.Invoke($null, [object[]]@($qn.AddMinutes(45), $true, $null, $qn))) 'осталось 45 мин'

# свой значок в трее: нет, пока общий у core.txt, пока при запуске ждём его ответа и когда Kapsula закрывается
$ots = $chT.GetMethod('OwnTrayShown')
T 'семья: свой значок — core.txt нет' ($ots.Invoke($null, @($false, $false, 0))) $true
T 'семья: свой значок — значок у core.txt' ($ots.Invoke($null, @($false, $true, 0))) $false
T 'семья: свой значок — при запуске ждём core.txt' ($ots.Invoke($null, @($false, $false, 2500))) $false
T 'семья: свой значок — ждать больше нечего' ($ots.Invoke($null, @($false, $false, -40))) $true
T 'семья: свой значок — Kapsula закрывается' ($ots.Invoke($null, @($true, $false, 0))) $false

# быстрые действия в общем меню core.txt: не больше четырёх, первым — окно настроек, без «Выхода» и автозапуска
$trs = $chT.GetMethod('TrayState')
T 'семья: быстрые действия для трея core.txt' (HubJson $trs.Invoke($null, @($false, $true))) '{"items":[{"key":"show","label":"Настройки"},{"key":"awake","label":"Не засыпать","check":false},{"key":"lyrics","label":"Тексты песен","check":true},{"key":"shots","label":"Папка со снимками"}]}'
T 'семья: «Не засыпать» включено — с галочкой' ((HubJson $trs.Invoke($null, @($true, $false))) -match '"key":"awake","label":"Не засыпать","check":true') $true
T 'семья: в общем меню не больше четырёх пунктов' ($trs.Invoke($null, @($false, $false))['items'].Length -le 4) $true

# сочетания клавиш для страницы «Клавиши» core.txt: как стоят сейчас; выключенное — пустое и не спорит,
# занятое другой программой (full) остаётся включённым — так в core.txt видно, с чем оно совпало
$hotT = $asm.GetType('Kapsula.Hot')
$hots = [Activator]::CreateInstance(([System.Collections.Generic.List`1]).MakeGenericType($hotT))
function HotOf($key, $name, $mods, $vk, $ok) {
    $h = [Activator]::CreateInstance($hotT)
    $hotT.GetField('Key').SetValue($h, $key); $hotT.GetField('Name').SetValue($h, $name)
    $hotT.GetField('Mods').SetValue($h, [uint32]$mods); $hotT.GetField('Vk').SetValue($h, [uint32]$vk); $hotT.GetField('Ok').SetValue($h, [bool]$ok)
    $hots.Add($h)
}
HotOf 'cmd' 'Строка команд' 0x1 0x20 $true
HotOf 'area' 'Снимок области с разметкой' 0x3 0x53 $true
HotOf 'full' 'Снимок всего экрана' 0x7 0x53 $false
HotOf 'ocr' 'Текст с экрана' 0 0 $true
$ksa = New-Object object[] 1; $ksa[0] = $hots
T 'семья: сочетания для core.txt' (HubJson $chT.GetMethod('KeysState').Invoke($null, $ksa)) '{"keys":[{"id":"cmd","label":"Строка команд","keys":"Alt+Space","on":true},{"id":"area","label":"Снимок области с разметкой","keys":"Ctrl+Alt+S","on":true},{"id":"full","label":"Снимок всего экрана","keys":"Ctrl+Alt+Shift+S","on":true},{"id":"ocr","label":"Текст с экрана","keys":"","on":false}]}'

# уведомление от другого приложения: заголовок до 60 знаков, текст до 200, в одну строку; подпись — имя продукта
$nt = $chT.GetMethod('NotifyText')
$cut60 = $nt.Invoke($null, @(('а' * 61), 60))
T 'семья: заголовок уведомления — до 60 знаков' $cut60.Length 60
T 'семья: обрезанный заголовок кончается «…»' $cut60.EndsWith('…') $true
T 'семья: ровно 60 знаков не режутся' ($nt.Invoke($null, @(('б' * 60), 60))) ('б' * 60)
T 'семья: текст уведомления — до 200 знаков' ($nt.Invoke($null, @(('в' * 500), 200))).Length 200
T 'семья: переводы строк и лишние пробелы — одним пробелом' ($nt.Invoke($null, @("  Можно`r`nосвободить`t 12 ГБ  ", 200))) 'Можно освободить 12 ГБ'
T 'семья: не строка — пусто' ($nt.Invoke($null, @(42, 60))) ''
T 'семья: текста нет — пусто' ($nt.Invoke($null, @($null, 60))) ''
T 'семья: эмодзи на границе пополам не режется' ($nt.Invoke($null, @((('г' * 58) + [char]::ConvertFromUtf32(0x1F9F9) + 'дд'), 60))) (('г' * 58) + '…')
$an = $chT.GetMethod('AppName')
T 'семья: уведомление подписано именем приложения' ($an.Invoke($null, @('metla'))) 'metla.txt'
T 'семья: странное имя отправителя — без подписи' ($an.Invoke($null, @('Metla!'))) ''
# карточка — как у уведомлений Windows: сверху приложение, ниже заголовок и текст; заголовок-название второй раз не пишется
$nc = $chT.GetMethod('NotifyCard')
function Card($app, $title, $text) { $a = [object[]]@([string]$app, [string]$title, [string]$text, $null, $null); [void]$nc.Invoke($null, $a); return $a[3] + ' | ' + $a[4] }
T 'семья: карточка — заголовок совпал с приложением' (Card 'metla.txt' 'metla.txt' 'Можно освободить 12 ГБ') 'metla.txt | Можно освободить 12 ГБ'
T 'семья: карточка — свой заголовок уходит в текст' (Card 'metla.txt' 'Очистка готова' 'Освобождено 12 ГБ') 'metla.txt | Очистка готова — Освобождено 12 ГБ'
T 'семья: карточка — только заголовок' (Card 'aura.txt' 'Обработка голоса включена' '') 'aura.txt | Обработка голоса включена'
T 'семья: карточка — без подписи отправителя' (Card '' '' 'Текст без заголовка') 'Уведомление | Текст без заголовка'
T 'семья: карточка — пустое уведомление не показывается' (Card 'metla.txt' '' '') ' | '
# общий цвет подсветки из hub.theme: «#RRGGBB» — его, null и непонятное — свой
$ta = $chT.GetMethod('ThemeAccent')
# hub.theme разбирается тем же JavaScriptSerializer, что и в HubLink (строки PowerShell в словаре C# не видит)
function ThemeOf($json) { $a = New-Object object[] 1; $a[0] = $hubJs.DeserializeObject($json); return $ta.Invoke($null, $a) }
T 'семья: общий цвет из core.txt' (ThemeOf '{"mode":"dark","accent":"#f08a4b","reducedMotion":false}') '#F08A4B'
T 'семья: общего цвета нет — свой' ("" + (ThemeOf '{"mode":"dark","accent":null,"reducedMotion":false}')) ''
T 'семья: непонятный цвет — свой' ("" + (ThemeOf '{"mode":"dark","accent":"orange","reducedMotion":false}')) ''
T 'семья: тема без цвета — свой' ("" + (ThemeOf '{"mode":"light"}')) ''

# где core.txt показывает настройки kapsula.txt (поле section): ключи те же, переезжает только место
$sof = $chT.GetMethod('SectionOf')
foreach ($c in @(@('autostart', 'startup'), @('autoUpdate', 'startup'), @('share', 'family'), @('hubShare', 'family'), @('hubUnread', 'family'), @('windowsNotify', ''), @('miniPlayer', ''), @('musicEdge', ''), @('showInGames', 'games'), @('fsTrack', 'games'), @('gameLoad', 'games'), @('autoHide', ''), @('walk', ''), @('lyrics', ''))) {
    T "семья: настройка $($c[0]) — раздел core.txt" ("" + $sof.Invoke($null, @($c[0]))) $c[1]
}
$chT.GetMethod('Describe', $sf).Invoke($null, @()) | Out-Null
$pub = @{}; foreach ($g in $chT.GetMethod('Settings').Invoke($null, @())['groups']) { foreach ($i in $g['items']) { $pub[$i['key']] = $i } }
T 'семья: «Показывать остров в играх» — на странице «Игры» core.txt' ($pub['showInGames']['section'] + ' · ' + $pub['showInGames']['label']) 'games · Показывать остров в играх'
T 'семья: «Новый трек поверх игр» по умолчанию включён' $pub['fsTrack']['value'] $true
T 'семья: «Нагрузка в капсуле» по умолчанию выключена' $pub['gameLoad']['value'] $false
T 'семья: автозапуск — в «Запуск и обновления»' $pub['autostart']['section'] 'startup'
T 'семья: автообновление — в «Запуск и обновления»' $pub['autoUpdate']['section'] 'startup'
T 'семья: «Делиться с приложениями dev.txt» — в «Связь между приложениями»' $pub['share']['section'] 'family'
T 'семья: при одинаковых значениях прежние «Показывать…» не публикуются' ($pub.ContainsKey('hubShare') -or $pub.ContainsKey('hubUnread')) $false
T 'семья: «Сезонные наряды» остаются на странице kapsula.txt' $pub['petSeason'].ContainsKey('section') $false

# --- Окно настроек без лишнего (Merged.cs): сводные выборы вместо пар переключателей, строки «пока не трогали — не видно»,
# строки, которые появляются, только когда на что-то влияют, и то же самое в описании для core.txt
$mgT = $asm.GetType('Kapsula.Merged')
$mOn = $mgT.GetMethod('On'); $mChoice = $mgT.GetMethod('Choice'); $mApply = $mgT.GetMethod('Apply'); $mValue = $mgT.GetMethod('Value'); $mIndex = $mgT.GetMethod('Index')
$pairs = $mgT.GetField('All').GetValue($null)
T 'сводные выборы: их шесть' $pairs.Count 6

# значения по умолчанию — те же, что у кода, который эти ключи читает (Store.Get(…) != "0" или == "1")
foreach ($c in @(@('notifications', $false), @('bannersOff', $false), @('steamHideToasts', $true), @('liquid', $true), @('liquidSpectrum', $true), @('hoverMini', $true), @('miniLyricsOnly', $false), @('micDot', $false), @('camDot', $false), @('calendar', $true), @('soonPill', $true), @('hubShare', $true), @('hubUnread', $true), @('ciderRate', $true), @('earbuds', $true), @('twitchOverlay', $false), @('showInGames', $false))) {
    T "сводные выборы: $($c[0]) по умолчанию" ($mOn.Invoke($null, @($c[0], ''))) $c[1]
    T "сводные выборы: $($c[0])='0' — выключено" ($mOn.Invoke($null, @($c[0], '0'))) $false
    T "сводные выборы: $($c[0])='1' — включено" ($mOn.Invoke($null, @($c[0], '1'))) $true
}

# точные сочетания: прежние значения A/B (1 — включено) → вариант. У уточнения (B без A ни на что не влияет) «0/0» и «0/1» —
# один и тот же вариант «выключено»; у двух независимых все четыре сочетания — разные варианты
$want = @{
    'windowsNotify' = @{ '0/0' = 'windows'; '0/1' = 'windows'; '1/0' = 'both'; '1/1' = 'island' }
    'steamNotify'   = @{ '0/0' = 'steam'; '0/1' = 'steam'; '1/0' = 'both'; '1/1' = 'island' }
    'musicEdge'     = @{ '0/0' = 'flat'; '0/1' = 'flat'; '1/0' = 'volume'; '1/1' = 'spectrum' }
    'miniPlayer'    = @{ '0/0' = 'off'; '0/1' = 'off'; '1/0' = 'player'; '1/1' = 'lyrics' }
    'dots'          = @{ '0/0' = 'off'; '1/0' = 'mic'; '0/1' = 'cam'; '1/1' = 'both' }
    'remind'        = @{ '0/0' = 'off'; '1/0' = 'card'; '0/1' = 'countdown'; '1/1' = 'both' }
}
$keysOf = @{ 'windowsNotify' = 'notifications/bannersOff'; 'steamNotify' = 'steamPlugin/steamHideToasts'; 'musicEdge' = 'liquid/liquidSpectrum'; 'miniPlayer' = 'hoverMini/miniLyricsOnly'; 'dots' = 'micDot/camDot'; 'remind' = 'calendar/soonPill' }
$raws = @('', '0', '1', 'x')
foreach ($p in $pairs) {
    $id = $p.Id; $n = $p.Values.Length
    T "сводный выбор $($id): прежние ключи" ($p.A + '/' + $p.B) $keysOf[$id]
    T "сводный выбор $($id): подписей столько же, сколько вариантов" $p.Labels.Length $n
    T "сводный выбор $($id): вариантов три у уточнения и четыре у двух независимых" $n $(if ($p.Tier) { 3 } else { 4 })
    $reached = @{}
    foreach ($ra in $raws) {
        foreach ($rb in $raws) {
            $a = [bool]$mOn.Invoke($null, @($p.A, $ra)); $b = [bool]$mOn.Invoke($null, @($p.B, $rb))
            $c = [int]$mChoice.Invoke($null, @($p, $a, $b)); $reached[$c] = 1
            $ab = "$([int]$a)/$([int]$b)"
            T "сводный выбор $($id): $($p.A)='$ra', $($p.B)='$rb' — вариант" $p.Values[$c] $want[$id][$ab]
            # прежние значения → вариант → те же значения: выбрать то, что уже выбрано, ничего не меняет
            $w = [object[]]@($p, $c, $a, $b); [void]$mApply.Invoke($null, $w)
            T "сводный выбор $($id): $ab → $($p.Values[$c]) → как было" "$([int]$w[2])/$([int]$w[3])" $ab
            # значение для core.txt — туда и обратно
            T "сводный выбор $($id): $ab — значение для core.txt туда и обратно" ($mIndex.Invoke($null, @($p, $mValue.Invoke($null, @($p, $a, $b))))) $c
            # любой вариант из этого состояния: выбран именно он, а «выключено» у уточнения не трогает уточнение
            for ($k = 0; $k -lt $n; $k++) {
                $w = [object[]]@($p, $k, $a, $b); $ok = $mApply.Invoke($null, $w)
                T "сводный выбор $($id): из $ab — вариант $($p.Values[$k])" "$ok $($mChoice.Invoke($null, @($p, $w[2], $w[3])))" "True $k"
                if ($p.Tier -and $k -eq 0) { T "сводный выбор $($id): из $ab «$($p.Labels[0])» не трогает $($p.B)" $w[3] $b }
                if ($p.Tier -and $k -gt 0) { T "сводный выбор $($id): из $ab «$($p.Labels[$k])» ставит $($p.B) явно" $w[3] ($k -eq 2) }
            }
        }
    }
    T "сводный выбор $($id): каждый вариант достижим из прежних значений" $reached.Count $n
    $w = [object[]]@($p, $n, $true, $true)
    T "сводный выбор $($id): несуществующий вариант ничего не меняет" "$($mApply.Invoke($null, $w)) $($w[2]) $($w[3])" 'False True True'
    T "сводный выбор $($id): незнакомое значение от core.txt" ($mIndex.Invoke($null, @($p, 'нет такого'))) -1
    T "сводный выбор $($id): не строка от core.txt" ($mIndex.Invoke($null, @($p, $true))) -1
}

# «Делиться с приложениями dev.txt»: один переключатель, пока hubShare и hubUnread совпадают; разошлись — два прежних
$mShare = $mgT.GetMethod('ShareShown')
foreach ($c in @(@($true, $true, 'share'), @($false, $false, 'share'), @($true, $false, 'hubShare,hubUnread'), @($false, $true, 'hubShare,hubUnread'))) {
    $shown = @(); foreach ($key in @('share', 'hubShare', 'hubUnread')) { if ($mShare.Invoke($null, @($key, $c[0], $c[1]))) { $shown += $key } }
    T "делиться: hubShare=$($c[0]), hubUnread=$($c[1]) — видно" ($shown -join ',') $c[2]
}
T 'делиться: другие ключи не задевает' ($mShare.Invoke($null, @('lyrics', $true, $false))) $true

# «Пока не трогали — не видно»: строка видна, только если значение уже не такое, как по умолчанию
$mUsed = $mgT.GetMethod('Used')
T 'пока не трогали — не видно: семь строк' ($mgT.GetField('Quiet').GetValue($null) -join ',') 'uiaRate,vscodeButton,callMode,font,motion,cascade,followWin'
foreach ($c in @(
    @('uiaRate', '', $true, $false), @('uiaRate', '0', $true, $false), @('uiaRate', '1', $true, $true),
    @('vscodeButton', '', $true, $false), @('vscodeButton', '0', $true, $false), @('vscodeButton', '1', $true, $true),
    @('callMode', '', $true, $false), @('callMode', '0', $true, $false), @('callMode', '1', $true, $true),
    @('font', '', $true, $false), @('font', '   ', $true, $false), @('font', 'Georgia', $true, $true), @('font', 'Cascadia Mono, Consolas', $true, $true),
    @('motion', '', $true, $false), @('motion', '', $false, $false), @('motion', '0', $true, $false), @('motion', '5', $false, $false),
    @('motion', '5', $true, $true), @('motion', '0', $false, $true), @('motion', '3', $true, $true), @('motion', '6', $true, $false), @('motion', '-1', $true, $false), @('motion', 'x', $true, $false),
    @('cascade', '', $true, $false), @('cascade', '1', $true, $false), @('cascade', '0', $true, $true),
    @('followWin', '1', $true, $false), @('followWin', '0', $true, $true), @('followWin', '', $true, $true),
    @('seconds', '', $true, $true), @('seconds', '1', $true, $true))) {
    $sys = if ($c[2]) { '' } else { ', анимации Windows выключены' }
    T "пока не трогали — не видно: $($c[0])='$($c[1])'$sys" ($mUsed.Invoke($null, @($c[0], $c[1], $c[2]))) $c[3]
}
# «Тема как в Windows» видна и по умолчанию, когда перекрывает выбранный цвет: светлая Windows или светлый цвет
$mTheme = $mgT.GetMethod('ThemeRowShown')
foreach ($c in @(@('1', $true, $false, $false), @('1', $false, $false, $true), @('1', $true, $true, $true), @('1', $false, $true, $true), @('0', $true, $false, $true), @('', $true, $false, $true))) {
    T "«Тема как в Windows»: followWin='$($c[0])', Windows тёмная=$($c[1]), выбран светлый=$($c[2])" ($mTheme.Invoke($null, @($c[0], $c[1], $c[2]))) $c[3]
}

# строки, которые появляются, только когда на что-то влияют, — и каждая стоит в своём разделе
$needs = @{}; foreach ($nd in $mgT.GetField('Needs').GetValue($null)) { $needs[$nd[0]] = $nd[1] }
foreach ($c in @(@('Сдвиг текста песни', 'lyrics'), @('Вернуть остров наверх', 'detached'), @('Ключ Cider', 'ciderRate'), @('Канал Twitch', 'twitchOverlay'), @('Чат Twitch и вне игр', 'twitchOverlay'), @('Нагрузка в капсуле во время игры', 'showInGames'), @('Пауза, когда сняли наушники', 'earbuds'), @('Зверёк в переходах', 'eyes'), @('Зверёк гуляет по окнам', 'eyes'), @('Качество камеры', 'camera'))) {
    T "зависимая строка: «$($c[0])» — только при $($c[1])" $needs[$c[0]] $c[1]
}
foreach ($k in $needs.Keys) { T "зависимая строка: «$k» стоит в разделе" ($secOf.ContainsKey($k)) $true }

# пункты с section при установленном core.txt показывает он, а не окно kapsula.txt
$mVia = $mgT.GetMethod('ViaCore')
foreach ($c in @(@($true, 'autostart', $true), @($true, 'autoUpdate', $true), @($true, 'showInGames', $true), @($true, 'fsTrack', $true), @($true, 'gameLoad', $true), @($true, 'share', $true), @($true, 'hubShare', $true), @($true, 'hubUnread', $true), @($true, 'autoHide', $false), @($true, 'walk', $false), @($true, 'windowsNotify', $false), @($false, 'autostart', $false), @($false, 'share', $false), @($false, 'gameLoad', $false))) {
    T "core.txt установлен=$($c[0]): «$($c[1])» показывает core.txt" ($mVia.Invoke($null, @($c[0], $c[1]))) $c[2]
}

# описание для core.txt: сводные выборы — одним пунктом choice; прежние ключи в описании не видны, но settings.set их принимает
$pubKeys = $chT.GetMethod('Keys').Invoke($null, @($true)); $allKeys = $chT.GetMethod('Keys').Invoke($null, @($false))
foreach ($k in @('autoHide', 'walk', 'windowsNotify', 'miniPlayer', 'musicEdge', 'share', 'lyrics', 'petRide', 'eyes', 'seconds', 'rim', 'shadow', 'petSeason', 'showInGames', 'fsTrack', 'gameLoad', 'autostart', 'autoUpdate')) {
    T "core.txt: «$k» в описании" ($pubKeys -contains $k) $true
}
foreach ($k in @('notifications', 'hoverMini', 'liquid', 'hubShare', 'hubUnread')) {
    T "core.txt: прежний «$k» в описании не виден" ($pubKeys -contains $k) $false
    T "core.txt: прежний «$k» по-прежнему принимается" ($allKeys -contains $k) $true
}
# сценарии core.txt читают текущее значение autoHide и walk из описания — без него сценарий не срабатывает
T 'core.txt: «autoHide» и «walk» для сценариев остаются в описании' (($pubKeys -contains 'autoHide') -and ($pubKeys -contains 'walk')) $true
T 'core.txt: «Уведомления Windows» — выбор из трёх' ($pub['windowsNotify']['type'] + ' ' + (@($pub['windowsNotify']['options'] | ForEach-Object { $_['value'] }) -join ',')) 'choice windows,both,island'
T 'core.txt: подписи вариантов — те же, что в окне' (@($pub['windowsNotify']['options'] | ForEach-Object { $_['label'] }) -join ',') 'Только Windows,Остров и Windows,Только остров'
T 'core.txt: подсказка «Уведомлений Windows» называет вариант так же, как он подписан' ($pub['windowsNotify']['hint'].Contains('«Только остров»')) $true
T 'core.txt: подсказка «Уведомлений Windows» не спорит сама с собой' ($pub['windowsNotify']['hint'].Contains('нигде не сохраняются')) $false
T 'core.txt: «Уведомления Windows» по умолчанию — только в Windows' $pub['windowsNotify']['value'] 'windows'
T 'core.txt: «Мини-плеер при наведении» по умолчанию — обычный' $pub['miniPlayer']['value'] 'player'
T 'core.txt: «Край во время музыки» без настроек — эквалайзер' $pub['musicEdge']['value'] 'spectrum'
T 'core.txt: «Делиться с приложениями dev.txt» — один переключатель на странице «Общие»' ($pub['share']['type'] + ' ' + $pub['share']['section'] + ' ' + $pub['share']['value']) 'switch family True'
T 'core.txt: подпись общего переключателя' $pub['share']['label'] 'Делиться с приложениями dev.txt'

# разметка окна: один раздел «Общее», каждая строка — один раз, каждый раздел — в списке разделов, строки — где их ищут
T 'настройки: раздел «Общее» один' (@($lay | Where-Object { $_[0] -eq 'Общее' }).Count) 1
$rowsAll = @(); foreach ($l in $lay) { for ($i = 1; $i -lt $l.Length; $i++) { if (-not $l[$i].StartsWith('#')) { $rowsAll += $l[$i] } } }
T 'настройки: в разметке каждая строка один раз' (@($rowsAll | Sort-Object -Unique).Count) $rowsAll.Count
foreach ($l in $lay) { T "настройки: раздел «$($l[0])» есть в списке разделов" ($so -contains $l[0]) $true }
foreach ($c in @(@('Скрывать текст уведомлений', 'Уведомления'), @('Секунды в часах', 'Внешний вид'), @('Монитор', 'Поведение'), @('core.txt — лаунчер dev.txt', 'Общее'), @('core.txt: запуск и обновления', 'Общее'), @('core.txt: игры', 'Игры'), @('core.txt: чем делиться', 'Приватность'), @('Выйти из kapsula.txt', 'Общее'), @('Показывать остров в играх', 'Игры'), @('Уведомления Windows', 'Уведомления'), @('Напоминания о событиях', 'Уведомления'), @('Индикаторы камеры и микрофона', 'Приватность'), @('Край во время музыки', 'Музыка'), @('Мини-плеер при наведении', 'Музыка'), @('Уведомления Steam', 'Steam и Discord'), @('Режим звонка', 'Дополнительно'))) {
    T "настройки: «$($c[0])» — в разделе «$($c[1])»" $secOf[$c[0]] $c[1]
}
$sn = $swT.GetMethod('SectionName', $sf)
T 'настройки: прежний раздел «Безопасность» открывается как «Приватность»' ($sn.Invoke($null, @('Безопасность'))) 'Приватность'
T 'настройки: прежний раздел «Вкладки острова» открывается как «Вкладки»' ($sn.Invoke($null, @('Вкладки острова'))) 'Вкладки'
T 'настройки: «Музыка» остаётся «Музыкой»' ($sn.Invoke($null, @('Музыка'))) 'Музыка'
$df = $swT.GetMethod('DemoFor', $sf)
foreach ($c in @(@('Уведомления Windows', 'notif'), @('Скрывать текст уведомлений', 'notif'), @('Напоминания о событиях', 'remind'), @('Индикаторы камеры и микрофона', 'privacy'), @('Зверёк гуляет по окнам', 'walk'))) {
    T "настройки: пример на острове у «$($c[0])»" ($df.Invoke($null, @($c[0]))) $c[1]
}
T 'превью: «Край во время музыки» — сам остров' ($pk.Invoke($null, @('Край во время музыки'))) 0
T 'превью: «Мини-плеер при наведении» — мини-плеер' ($pk.Invoke($null, @('Мини-плеер при наведении'))) 2

# --- Доводка после проверки: «Не беспокоить» переходит к core.txt, только когда он подтвердил; ничего не теряется
$qh = $chT.GetMethod('QuietHandOver')
function HandOver($localMin, $focus, $untilMin, $askedSame) {
    $now = [long]1763550000000
    $end = if ($localMin -gt 0) { $now + [long]($localMin * 60000) } else { [long]0 }
    $until = if ($untilMin -eq 0) { [long]0 } else { $now + [long]($untilMin * 60000) }
    $asked = if ($askedSame) { $end } else { [long]0 }
    $a = [object[]]@($end, $now, [bool]$focus, $until, $asked, 0)
    $r = $qh.Invoke($null, $a)
    return "$r/$($a[5])"
}
T 'не беспокоить: своего нет — ничего' (HandOver 0 $false 0 $false) '0/0'
T 'не беспокоить: своё на 60 мин, у core.txt тихо нет — попросить его на 60' (HandOver 60 $false 0 $false) '2/60'
T 'не беспокоить: своё на 59,5 мин — просим с запасом вверх, 60' (HandOver 59.5 $false 0 $false) '2/60'
T 'не беспокоить: попросили, а core.txt ещё не ответил — своё остаётся' (HandOver 60 $false 0 $true) '0/0'
T 'не беспокоить: core.txt подтвердил на то же время — своё гасится' (HandOver 60 $true 60 $true) '1/0'
T 'не беспокоить: у core.txt «пока не выключат» — своё гасится' (HandOver 60 $true 0 $false) '1/0'
T 'не беспокоить: у core.txt тихо дольше своего — своё гасится' (HandOver 60 $true 180 $false) '1/0'
T 'не беспокоить: у core.txt на полминуты короче — разница не в счёт' (HandOver 60 $true 59.5 $false) '1/0'
T 'не беспокоить: у core.txt короче своего — продлить, а не потерять' (HandOver 60 $true 20 $false) '2/60'
T 'не беспокоить: продлить просили, core.txt решил по-своему — его время главнее' (HandOver 60 $true 20 $true) '1/0'
T 'не беспокоить: у core.txt время уже вышло — попросить заново' (HandOver 60 $true (-1) $false) '2/60'
$qa = $chT.GetMethod('QuietAdopt'); $qnow = [long]1763550000000
T 'не беспокоить: связь пропала, у core.txt было тихо ещё 30 мин — остаётся своим' ($qa.Invoke($null, @([long]($qnow + 1800000), $qnow, [long]0))) ($qnow + 1800000)
T 'не беспокоить: связь пропала, у core.txt было не тихо — своё не меняется' ($qa.Invoke($null, @([long]0, $qnow, [long]0))) 0
T 'не беспокоить: связь пропала, время core.txt уже вышло — своё не меняется' ($qa.Invoke($null, @([long]($qnow - 1), $qnow, [long]0))) 0
T 'не беспокоить: своё и так дольше — не укорачивается' ($qa.Invoke($null, @([long]($qnow + 60000), $qnow, [long]($qnow + 120000)))) 0
$um = $chT.GetMethod('UtcMs'); $fm = $chT.GetMethod('FromUtcMs')
$t0 = [datetime]'2026-11-20 13:45:10'
T 'не беспокоить: время туда и обратно (мс с 1970 года, UTC)' ($fm.Invoke($null, @($um.Invoke($null, @($t0)))).ToString('yyyy-MM-dd HH:mm:ss')) '2026-11-20 13:45:10'
T 'не беспокоить: 1970 год по UTC — ноль' ($um.Invoke($null, @([datetime]::SpecifyKind([datetime]'1970-01-01', 'Utc')))) 0

# свой значок: сколько ещё ждать core.txt при запуске — и через переход счётчика Windows через ноль
$twl = $chT.GetMethod('TrayWaitLeft')
T 'значок: не ждём' ($twl.Invoke($null, @(0, 12345))) 0
T 'значок: ждём ещё 600 мс' ($twl.Invoke($null, @(1000, 400))) 600
T 'значок: ждать больше нечего' (($twl.Invoke($null, @(1000, 1400))) -le 0) $true
T 'значок: счётчик перешёл через ноль — ждём столько же' ($twl.Invoke($null, @(([int]::MinValue + 100), ([int]::MaxValue - 100)))) 201
# уведомления приложений dev.txt: «Не беспокоить» — только история; остров спрятан от игры — карточка ждёт; не теряются
$nr = $chT.GetMethod('NotifyRoute')
foreach ($c in @(@($true, $false, $false, 'history'), @($true, $true, $false, 'history'), @($false, $true, $false, 'hold'), @($false, $true, $true, 'hold'), @($false, $false, $true, 'history'), @($false, $false, $false, 'card'))) {
    T "уведомление: тихо=$($c[0]), остров спрятан от игры=$($c[1]), открыт вопрос Claude=$($c[2])" ($nr.Invoke($null, @($c[0], $c[1], $c[2]))) $c[3]
}
$nm = $chT.GetMethod('NotifyMore')
T 'уведомление: одно — текст как есть' ($nm.Invoke($null, @('Можно освободить 12 ГБ', 0))) 'Можно освободить 12 ГБ'
T 'уведомление: за игру пришло три — последнее и сколько ещё' ($nm.Invoke($null, @('Можно освободить 12 ГБ', 2))) 'Можно освободить 12 ГБ · ещё 2 в истории'
T 'уведомление: без текста — только сколько ещё' ($nm.Invoke($null, @('', 1))) 'ещё 1 в истории'
T 'семья: карточка — заголовок совпал с приложением, текста нет: одна строка, без пустой' (Card 'metla.txt' 'metla.txt' '') 'metla.txt | '

# settings.set от core.txt: что именно сделает каждый ключ (сводный выбор, своё действие пункта или SetFlag)
$plan = $chT.GetMethod('Plan')
foreach ($c in @(
    @('windowsNotify', 'island', 'pair windowsNotify 2'), @('windowsNotify', 'windows', 'pair windowsNotify 0'), @('miniPlayer', 'lyrics', 'pair miniPlayer 2'),
    @('musicEdge', 'flat', 'pair musicEdge 0'), @('musicEdge', 'spectrum', 'pair musicEdge 2'), @('windowsNotify', 'nope', 'такого варианта нет'),
    @('windowsNotify', $true, 'такого варианта нет'), @('notifications', $true, 'flag notifications True'), @('hoverMini', $false, 'flag hoverMini False'),
    @('liquid', $true, 'flag liquid True'), @('hubShare', $false, 'flag hubShare False'), @('hubUnread', $true, 'flag hubUnread True'),
    @('share', $false, 'own share False'), @('autoHide', $true, 'flag autoHide True'), @('walk', $false, 'flag walk False'),
    @('autostart', $true, 'own autostart True'), @('autoUpdate', $false, 'own autoUpdate False'), @('showInGames', $true, 'flag showInGames True'),
    @('fsTrack', $false, 'flag fsTrack False'), @('gameLoad', $true, 'flag gameLoad True'), @('autoHide', 'да', 'нужно «да» или «нет»'), @('nope', $true, 'такой настройки нет'))) {
    T "settings.set $($c[0])=$($c[1])" ($plan.Invoke($null, @($c[0], $c[1]))) $c[2]
}

# сводный выбор пишет только то, что меняется, уточнение — раньше главного, и ровно так, как его писала прежняя строка
$mWrites = $mgT.GetMethod('Writes'); $mRoute = $mgT.GetMethod('Route')
foreach ($p in $pairs) {
    for ($k = -1; $k -le $p.Values.Length; $k++) {
        foreach ($a0 in @($false, $true)) {
            foreach ($b0 in @($false, $true)) {
                $w = [object[]]@($p, $k, $a0, $b0); $ok = $mApply.Invoke($null, $w)
                $want = @(); if ($ok -and $w[3] -ne $b0) { $want += "$($p.B)=$($w[3])" }; if ($ok -and $w[2] -ne $a0) { $want += "$($p.A)=$($w[2])" }
                $got = @($mWrites.Invoke($null, @($p, $k, $a0, $b0)) | ForEach-Object { "$($_.Key)=$($_.Value)" })
                T "сводный выбор $($p.Id): из $([int]$a0)/$([int]$b0) вариант $k — что пишется" ($got -join ';') ($want -join ';')
            }
        }
    }
}
foreach ($c in @(@('bannersOff', 'banners'), @('miniLyricsOnly', 'miniLyrics'), @('steamHideToasts', 'steam'), @('steamPlugin', 'plugin'), @('liquidSpectrum', 'store'), @('micDot', 'store'), @('camDot', 'store'), @('calendar', 'store'), @('soonPill', 'store'), @('notifications', 'flag'), @('liquid', 'flag'), @('hoverMini', 'flag'), @('hubShare', 'flag'), @('hubUnread', 'flag'))) {
    T "сводный выбор: «$($c[0])» ставится как прежней строкой — $($c[1])" ($mRoute.Invoke($null, @($c[0]))) $c[1]
}
# зависимые строки: поиск находит и спрятанную — и говорит, когда она появится; «Выйти» — пока значок у core.txt
$mNote = $mgT.GetMethod('NeedNote')
foreach ($nd in $mgT.GetField('Needs').GetValue($null)) {
    T "поиск: у спрятанной «$($nd[0])» сказано, когда она появится" (($mNote.Invoke($null, @($nd[1]))) -ne 'Появится, когда понадобится.') $true
}
T 'выход: строка «Выйти из kapsula.txt» — только пока значок у core.txt' $needs['Выйти из kapsula.txt'] 'coreTray'
T 'выход: без core.txt поиск ведёт к «Выходу» в меню значка' (($mNote.Invoke($null, @('coreTray'))).Contains('«Выход»')) $true
$qw = $iwT.GetMethod('QuitWord')
foreach ($c in @(@('выход', $true), @('Выйти', $true), @('выйти из kapsula', $true), @('закрыть kapsula.txt', $true), @('выход из капсулы', $true), @('quit', $true), @('exit', $true),
    @('выходные', $false), @('закрыть вкладку', $false), @('выход на работу', $false), @('выйти из дома', $false), @('', $false))) {
    T "выход: команда «$($c[0])»" ($qw.Invoke($null, @($c[0]))) $c[1]
}

# переключатели, которые меняют и не в окне (core.txt, меню у часов, клавиши), раз в секунду встают как есть;
# у каждой настройки, которую видит core.txt, в окне то же название — одна настройка, одно имя
$synced = $swT.GetField('Synced').GetValue($null)
foreach ($t in $synced) { T "настройки: «$t» сверяется и стоит в разметке" ($secOf.ContainsKey($t)) $true }
foreach ($f in $chT.GetField('flags', $sf).GetValue($null)) {
    $ft = $f.GetType(); $label = $ft.GetField('Label').GetValue($f)
    if ($null -eq $label -or $null -ne $ft.GetField('Choice').GetValue($f)) { continue }
    T "core.txt: «$label» — то же название в окне, и переключатель там сверяется" ($synced -contains $label) $true
}
# у каждого сверяемого переключателя есть откуда взять значение (окно не создаётся: метод только собирает делегаты)
$swBare = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($swT)
$ss = $swT.GetMethod('SwitchSource', [Reflection.BindingFlags]'NonPublic,Instance')
foreach ($t in $synced) { T "настройки: «$t» знает, откуда брать значение" ($null -ne $ss.Invoke($swBare, @($t))) $true }
T 'настройки: «Смена раскладки» меняют только здесь — не сверяется' ($null -eq $ss.Invoke($swBare, @('Смена раскладки'))) $true
$ch = $swT.GetMethod('ColorHint')
T 'цвет острова: без общего цвета — обычная подсказка' ($ch.Invoke($null, @($false))) 'Наведите на цвет — остров сверху сразу покажет его. Клик сохраняет.'
T 'цвет острова: цвет подсветки из core.txt — сказано' (($ch.Invoke($null, @($true))).EndsWith(' Цвет подсветки задаёт core.txt.')) $true
$ca = $chT.GetMethod('CoreArgs')
foreach ($c in @(@('general', '--page general'), @('games', '--page games'), @($null, ''), @('two words', ''), @('app:metla', ''))) {
    T "core.txt: запуск на странице «$($c[0])»" ($ca.Invoke($null, @($c[0]))) $c[1]
}
T 'core.txt: строки «Настраивается в core.txt» — у каждой группы своя' (@($swT.GetField('CoreStartup').GetValue($null), $swT.GetField('CoreGames').GetValue($null), $swT.GetField('CoreFamily').GetValue($null)) | Sort-Object -Unique).Count 3
"`nПройдено: $script:pass, не пройдено: $script:fail"
if ($script:fail -gt 0) { exit 1 }
