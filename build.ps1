# Собирает Kapsula.exe встроенным в Windows компилятором C#. Ничего не скачивает.
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
    "$fw\System.Runtime.WindowsRuntime.dll",
    (Facade 'System.Runtime'),
    "$meta\Windows.Foundation.winmd",
    "$meta\Windows.Media.winmd",
    "$meta\Windows.Storage.winmd",
    "$meta\Windows.Devices.winmd",
    "$meta\Windows.Graphics.winmd",
    "$meta\Windows.Globalization.winmd",
    "$meta\Windows.UI.winmd",
    "$meta\Windows.ApplicationModel.winmd"
)

$args2 = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', '/codepage:65001', "/out:$Out")
$args2 += $refs | ForEach-Object { "/r:$_" }
# Исходники лежат в папке src: по файлу на крупную часть приложения
$args2 += (Get-ChildItem "$here\src\*.cs" | ForEach-Object { $_.FullName })

$args2 += $verFile

& $csc $args2
$code = $LASTEXITCODE
Remove-Item $verFile -ErrorAction SilentlyContinue
if ($code -ne 0) { throw "Сборка не удалась" }
"OK: $Out (версия $Version)"
