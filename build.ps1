#requires -Version 5.1
<#
.SYNOPSIS
    Build ProxyTool.exe

.DESCRIPTION
    Compiles the project with the C# compiler bundled in the .NET Framework
    (no .NET SDK or Visual Studio required).

    The program icon is optional: if app.ico exists in the project root it is
    embedded into the executable; otherwise a plain icon-less build is produced.

.PARAMETER Clean
    Remove build artifacts and exit.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Clean
#>
param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }

$src = Join-Path $root 'src'
$ico = Join-Path $root 'app.ico'
$out = Join-Path $root 'ProxyTool.exe'

function Get-CscPath {
    $candidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }
    throw 'csc.exe not found. .NET Framework 4.x is required.'
}

if ($Clean) {
    Remove-Item $out -Force -ErrorAction SilentlyContinue
    Write-Output 'Build artifacts removed.'
    return
}

Write-Output "Root     : $root"

$csc = Get-CscPath
Write-Output "Compiler : $csc"
Write-Output ''

$args = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    "/out:$out",
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    "$src\ProxyTool.cs"
)

if (Test-Path $ico) {
    Write-Output '[1/2] Icon     : app.ico (embedded)'
    $args = @("/win32icon:$ico") + $args
}
else {
    Write-Output '[1/2] Icon     : app.ico not found, building without icon'
}

Write-Output '[2/2] Compiling ProxyTool.exe'
& $csc $args
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$exe = Get-Item $out
Write-Output ''
Write-Output ("Done: {0} ({1:N1} KB)" -f $exe.Name, ($exe.Length / 1KB))