$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$dotnet = "dotnet"
try {
    & $dotnet --info | Out-Null
} catch {
    $dotnetExe = "C:\Program Files\dotnet\dotnet.exe"
    if (Test-Path $dotnetExe) {
        $dotnet = $dotnetExe
    } else {
        Write-Host "dotnet SDK was not found."
        Write-Host "Install .NET SDK 8 first:"
        Write-Host "winget install -e --id Microsoft.DotNet.SDK.8"
        Read-Host "Press Enter to exit"
        exit 1
    }
}

Remove-Item -Recurse -Force "$PSScriptRoot\_FINAL" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$PSScriptRoot\bin" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$PSScriptRoot\obj" -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$PSScriptRoot\_FINAL" | Out-Null

& $dotnet publish "$PSScriptRoot\HarRecorder.csproj" -c Release -r win-x64 --self-contained true --nologo `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:EnableCompressionInSingleFile=true `
  /p:DebugType=none `
  /p:DebugSymbols=false

$exe = "$PSScriptRoot\bin\Release\net8.0-windows\win-x64\publish\HarRecorder.exe"
if (!(Test-Path $exe)) {
    throw "Final EXE was not found: $exe"
}

Copy-Item $exe "$PSScriptRoot\_FINAL\HarRecorder.exe" -Force

Write-Host ""
Write-Host "DONE"
Write-Host "Final standalone GUI EXE:"
Write-Host "$PSScriptRoot\_FINAL\HarRecorder.exe"
Write-Host ""
Write-Host "Use ONLY this EXE. Do NOT use HarRecorder.dll."
Read-Host "Press Enter to exit"
