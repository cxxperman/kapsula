# Собирает Kapsula.exe встроенным в Windows компилятором C#. Ничего не скачивает: библиотеки WebView2 лежат в lib\webview2.
# Номер версии берётся из файла version.txt; -Version и -Out нужны только для проверок.
param([string]$Version, [string]$Out)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Version) { $Version = (Get-Content "$here\version.txt" -Raw).Trim() }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Версия должна быть вида 0.1.0, а не '$Version'" }
if (-not $Out) { $Out = "$here\Kapsula.exe" }
$verFile = Join-Path ([IO.Path]::GetTempPath()) "KapsulaVersion_$PID.cs"
"[assembly: System.Reflection.AssemblyVersion(`"$Version.0`")]`r`n[assembly: System.Reflection.AssemblyFileVersion(`"$Version.0`")]`r`n[assembly: System.Reflection.AssemblyProduct(`"Kapsula`")]`r`n[assembly: System.Reflection.AssemblyTitle(`"Kapsula`")]" | Set-Content $verFile -Encoding UTF8
$fw   = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc  = "$fw\csc.exe"
$meta = "$env:WINDIR\System32\WinMetadata"

function Facade($name) {
    $f = Get-ChildItem "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\$name" -Recurse -Filter "$name.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $f) { throw "Не найдена сборка $name" }
    $f.FullName
}

$refs = @(
    "$fw\WPF\PresentationFramework.dll",
    "$fw\WPF\PresentationCore.dll",
    "$fw\WPF\WindowsBase.dll",
    "$fw\System.Xaml.dll",
    "$fw\System.dll",
    "$fw\System.Core.dll",
    "$fw\System.Drawing.dll",
    "$fw\System.Windows.Forms.dll",
    "$fw\System.Web.Extensions.dll",
    "$fw\System.Data.dll",
    "$fw\System.Security.dll",
    "$fw\System.Xml.dll",
    "$fw\System.Management.dll",
    "$fw\Microsoft.VisualBasic.dll",
    "$fw\WPF\UIAutomationClient.dll",
    "$fw\System.IO.Compression.dll",
    "$fw\System.IO.Compression.FileSystem.dll",
    "$fw\WPF\UIAutomationTypes.dll",
    "$fw\System.Runtime.WindowsRuntime.dll",
    (Facade 'System.Runtime'),
    (Facade 'System.Runtime.InteropServices.WindowsRuntime'),
    "$meta\Windows.Foundation.winmd",
    "$meta\Windows.Media.winmd",
    "$meta\Windows.Storage.winmd",
    "$meta\Windows.Devices.winmd",
    "$meta\Windows.Graphics.winmd",
    "$meta\Windows.Globalization.winmd",
    "$meta\Windows.UI.winmd",
    "$meta\Windows.ApplicationModel.winmd",
    "$meta\Windows.Networking.winmd",
    "$here\lib\webview2\Microsoft.Web.WebView2.Core.dll",
    "$here\lib\webview2\Microsoft.Web.WebView2.Wpf.dll"
)

$args2 = @('/nologo', '/target:winexe', '/platform:x64', '/unsafe', '/optimize+', '/utf8output', '/codepage:65001', "/out:$Out")
$args2 += $refs | ForEach-Object { "/r:$_" }
# Исходники лежат в папке src: по файлу на крупную часть приложения
$args2 += (Get-ChildItem "$here\src\*.cs" | ForEach-Object { $_.FullName })

$args2 += $verFile
# Плагин для Millennium лежит внутри exe: Kapsula сама кладёт его в папку Steam по кнопке «Подключить»
# Значок файла программы — зверёк (assets\kapsula.ico собран из того же рисунка, что и в интерфейсе)
if (Test-Path "$here\assets\kapsula.ico") { $args2 += "/win32icon:$here\assets\kapsula.ico" }
$args2 += "/resource:$here\integrations\millennium\kapsula\plugin.json,Kapsula.millennium.plugin.json"
$args2 += "/resource:$here\integrations\millennium\kapsula\.millennium\Dist\index.js,Kapsula.millennium.index.js"

# Встроенный браузер (Microsoft WebView2): библиотеки лежат внутри exe и достаются при первом открытии
foreach ($n in "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.Wpf.dll", "WebView2Loader.dll") { $args2 += "/resource:$here\lib\webview2\$n,Kapsula.webview2.$n" }

# Драйвер камеры «Kapsula Camera» — отдельная библиотека для любых программ (64 и 32 бита); лежит внутри exe
$camDir = Join-Path ([IO.Path]::GetTempPath()) "KapsulaCam_$PID"; New-Item -ItemType Directory -Force $camDir | Out-Null
$camOut = Join-Path $camDir "KapsulaCam.dll"   # имя файла = имя сборки, иначе Windows не загрузит драйвер
& $csc /nologo /target:library /platform:anycpu /unsafe /optimize+ /codepage:65001 /utf8output "/out:$camOut" "$here\cam\KapsulaCam.cs"
if ($LASTEXITCODE -ne 0) { throw "Драйвер камеры не собрался" }
$args2 += "/resource:$camOut,Kapsula.camera.KapsulaCam.dll"
# версия драйвера — по его исходнику: сборщик каждый раз выдаёт немного другой файл, а переустанавливать драйвер зря не нужно
$camVer = Join-Path $camDir "version.txt"
(Get-FileHash "$here\cam\KapsulaCam.cs" -Algorithm SHA256).Hash.Substring(0, 8).ToLower() | Set-Content $camVer -NoNewline -Encoding ASCII
$args2 += "/resource:$camVer,Kapsula.camera.version"

& $csc $args2
$code = $LASTEXITCODE
Remove-Item $camDir -Recurse -ErrorAction SilentlyContinue
Remove-Item $verFile -ErrorAction SilentlyContinue
if ($code -ne 0) { throw "Сборка не удалась" }
"OK: $Out (версия $Version)"
