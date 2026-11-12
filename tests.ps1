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

"`nПройдено: $script:pass, не пройдено: $script:fail"
if ($script:fail -gt 0) { exit 1 }
