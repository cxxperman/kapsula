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

# --- батарея ноутбука: сколько осталось и значок заряда
$iwT = $asm.GetType('Kapsula.IslandWindow')
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

# --- Зверёк — главное: своя вкладка сразу после «Главной», первый раздел настроек, лапка
$pp = $iwT.GetField('PPet', $sf).GetValue($null)
$tg = $iwT.GetField('TabGroups', $sf).GetValue($null)
$own = $false; foreach ($g in $tg) { if ($g.Length -eq 1 -and $g[0] -eq $pp) { $own = $true } }
T 'вкладки: у зверька своя группа' $own $true
T 'вкладки: зверёк сразу после «Главной»' ([int]$tg[1][0]) ([int]$pp)
$to = $iwT.GetField('TabOrder', $sf).GetValue($null)
T 'вкладки: каждая страница в порядке один раз' (@($to | Sort-Object -Unique).Count) 13
$swT = $asm.GetType('Kapsula.SettingsWindow')
$so = $swT.GetField('SectionOrder', $sf).GetValue($null)
T 'настройки: «Зверёк» — первый раздел' $so[0] 'Зверёк'
$sg = $swT.GetField('SectionGlyphs', $sf).GetValue($null); $sh = $swT.GetField('SectionHints', $sf).GetValue($null); $sc = $swT.GetField('SectionColors', $sf).GetValue($null)
T 'настройки: у каждого раздела значок, подсказка и цвет' (($so.Length -eq $sg.Length) -and ($so.Length -eq $sh.Length) -and ($so.Length -eq $sc.Length)) $true
$tm = $swT.GetMethod('Times', $sf)
T 'гладили: 1 раз' ($tm.Invoke($null, @(1))) 'раз'
T 'гладили: 3 раза' ($tm.Invoke($null, @(3))) 'раза'
T 'гладили: 12 раз' ($tm.Invoke($null, @(12))) 'раз'
T 'гладили: 22 раза' ($tm.Invoke($null, @(22))) 'раза'
$pg = $asm.GetType('Kapsula.Ui').GetProperty('PawGeometry', [Reflection.BindingFlags]'Public,Static').GetValue($null, $null)
T 'лапка: рисунок 9×8' ('{0}x{1}' -f $pg.Bounds.Width, $pg.Bounds.Height) '9x8'

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

# --- Ночной колпак: ночью — на свободной голове, не поверх короны
$pvT = $asm.GetType('Kapsula.PetView'); $oc = $pvT.GetMethod('OutfitCells', $sf)
$pvT.GetField('TestNight').SetValue($null, 1)
T 'колпак: ночью с бабочкой — колпак есть, повязка — без него' (($oc.Invoke($null, @('band'))).Length -eq 3 -and ($oc.Invoke($null, @('bow'))).Length -gt 6) $true
T 'колпак: корона остаётся короной' (($oc.Invoke($null, @('crown'))).Length) 5
T 'колпак: «без наряда» — без колпака' (($oc.Invoke($null, @('none'))).Length) 0
$pvT.GetField('TestNight').SetValue($null, 0)
T 'колпак: днём бабочка без колпака' (($oc.Invoke($null, @('bow'))).Length) 6
$pvT.GetField('TestNight').SetValue($null, -1)

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

"`nПройдено: $script:pass, не пройдено: $script:fail"
if ($script:fail -gt 0) { exit 1 }
