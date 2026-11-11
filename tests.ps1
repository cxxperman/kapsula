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

"`nПройдено: $script:pass, не пройдено: $script:fail"
if ($script:fail -gt 0) { exit 1 }
