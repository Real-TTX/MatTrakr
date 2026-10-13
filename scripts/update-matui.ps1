#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Copies the newest MatUi toolkit (the UI of all Mat apps) into MatTrakr.
.DESCRIPTION
    MatTrakr carries its own copy of the toolkit in src/MatTrakr/wwwroot/matui (rule CODE-03: copy, do not link),
    so it works offline and as a PWA. Run this when MatDesign has a new release, then rebuild and look at the pages.
    The version is the first line of matui.css.

    Two sources:
      -Url     a running MatDesign (default http://localhost:8060); its archive /ui/matui.zip is downloaded.
      -Source  a checkout of the MatDesign repository; the CSS parts are bundled in the order MatDesign uses.
.EXAMPLE
    ./scripts/update-matui.ps1
.EXAMPLE
    ./scripts/update-matui.ps1 -Url https://design.example.com
.EXAMPLE
    ./scripts/update-matui.ps1 -Source C:/Users/Matthias/Desktop/Development/MatDesign
#>
param(
    [string]$Url = 'http://localhost:8060',
    [string]$Source
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'src/MatTrakr/wwwroot/matui'
New-Item -ItemType Directory -Path $target -Force | Out-Null

if ($Source) {
    $ui = Join-Path $Source 'src/MatUi/wwwroot'
    $parts = 'tokens', 'base', 'shell', 'controls', 'forms', 'pickers', 'lists', 'dialogs', 'layout', 'apps'
    $names = ($parts | ForEach-Object { "matui.$_.css" })
    $bundle = New-Object System.Text.StringBuilder
    [void]$bundle.AppendLine("/* MatUi - bundled for MatTrakr from: $($names -join ' ') */")
    foreach ($name in $names) {
        [void]$bundle.AppendLine('')
        [void]$bundle.AppendLine("/* ===== $name ===== */")
        [void]$bundle.AppendLine([System.IO.File]::ReadAllText((Join-Path $ui "css/$name")))
    }
    [System.IO.File]::WriteAllText((Join-Path $target 'matui.css'), $bundle.ToString(), (New-Object System.Text.UTF8Encoding($false)))
    Copy-Item (Join-Path $ui 'js/matui.js') (Join-Path $target 'matui.js') -Force
    Copy-Item (Join-Path $ui 'js/matui.boot.js') (Join-Path $target 'matui.boot.js') -Force
    Copy-Item (Join-Path $ui 'img/matui-icons.svg') (Join-Path $target 'matui-icons.svg') -Force
}
else {
    $work = Join-Path ([System.IO.Path]::GetTempPath()) ('matui-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        $zip = Join-Path $work 'matui.zip'
        Invoke-WebRequest -Uri ($Url.TrimEnd('/') + '/ui/matui.zip') -OutFile $zip -UseBasicParsing
        Expand-Archive -Path $zip -DestinationPath $work -Force
        $folder = Join-Path $work 'matui'
        foreach ($file in 'matui.css', 'matui.js', 'matui.boot.js', 'matui-icons.svg') {
            Copy-Item -Path (Join-Path $folder $file) -Destination (Join-Path $target $file) -Force
        }
    }
    finally {
        Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ('Toolkit updated: ' + (Get-Content (Join-Path $target 'matui.css') -TotalCount 1)) -ForegroundColor Green
Write-Host 'Now rebuild and look at every page in light and dark, and at 390 px.'
