# MatTrakr build & tag script
#
#   .\build.ps1                  -> local build   (tag: local-<builddate>)
#   .\build.ps1 -Target dev      -> dev build     (tag: nightly-<buildnumber>-<builddate>, increments .buildnumber)
#   .\build.ps1 -Target release  -> release build (tag: <major>.<minor>.<buildnumber>-<builddate>, increments .buildnumber)
#
# The matching compose stack is (re)deployed afterwards unless -NoDeploy is set.
param(
    [ValidateSet("local", "dev", "release")]
    [string]$Target = "local",
    [switch]$NoDeploy
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$buildDate = Get-Date -Format "yyyyMMdd"
$majorMinor = (Get-Content VERSION -TotalCount 1).Trim()

function Get-NextBuildNumber {
    $n = [int](Get-Content .buildnumber -TotalCount 1).Trim()
    $n++
    Set-Content .buildnumber "$n" -Encoding ascii
    return $n
}

switch ($Target) {
    "local" {
        $tag = "local-$buildDate"
        $composeFile = "docker-compose.dev.yml"
    }
    "dev" {
        $build = Get-NextBuildNumber
        $tag = "nightly-$build-$buildDate"
        $composeFile = "docker-compose.dev.yml"
    }
    "release" {
        $build = Get-NextBuildNumber
        $tag = "$majorMinor.$build-$buildDate"
        $composeFile = "docker-compose.release.yml"
    }
}

Write-Host "==> Building mattrakr:$tag ($Target)" -ForegroundColor Cyan
docker build -t "mattrakr:$tag" --build-arg "APP_VERSION=$tag" .
if ($LASTEXITCODE -ne 0) { throw "docker build failed" }

# Also tag what the compose stack expects
$composeTag = if ($Target -eq "release") { "mattrakr:latest" } else { "mattrakr:nightly-local" }
docker tag "mattrakr:$tag" $composeTag

if (-not $NoDeploy) {
    Write-Host "==> Redeploying stack ($composeFile)" -ForegroundColor Cyan
    docker compose -f $composeFile up -d
    if ($LASTEXITCODE -ne 0) { throw "docker compose up failed" }
}

Write-Host "==> Done: mattrakr:$tag" -ForegroundColor Green
