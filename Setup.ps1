param(
    [switch]$CheckOnly
)

$scriptBaseName = 'Setup'
$ErrorLogPath = Join-Path $env:TEMP ($scriptBaseName + '-LastError.log')

function Wait-BeforeClosing {
    param([string]$Prompt = 'Press Enter to close')
    try { Read-Host ("`n" + $Prompt) | Out-Null }
    catch {
        Write-Host ("`n" + $Prompt)
        try { [void][Console]::ReadKey($true) } catch {}
    }
}

function Show-Failure {
    param($ErrorRecord, [string]$Title = 'SETUP FAILED - the script cannot continue')
    $location = ''
    $command = ''
    if ($ErrorRecord.InvocationInfo) {
        $location = "$($ErrorRecord.InvocationInfo.ScriptName):$($ErrorRecord.InvocationInfo.ScriptLineNumber)"
        if ($ErrorRecord.InvocationInfo.Line) { $command = $ErrorRecord.InvocationInfo.Line.Trim() }
    }
    Write-Host ''
    Write-Host '================================================================' -ForegroundColor Red
    Write-Host $Title -ForegroundColor Red
    Write-Host '================================================================' -ForegroundColor Red
    Write-Host "Error: $($ErrorRecord.Exception.Message)" -ForegroundColor Red
    if ($location) { Write-Host "Location: $location" -ForegroundColor Red }
    if ($command) { Write-Host "Command: $command" -ForegroundColor Red }
    Write-Host ($ErrorRecord | Out-String) -ForegroundColor Yellow
    if ($ErrorRecord.ScriptStackTrace) { Write-Host $ErrorRecord.ScriptStackTrace -ForegroundColor Yellow }
    $details = @(
        "$scriptBaseName failure at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        "Error: $($ErrorRecord.Exception.Message)",
        "Location: $location",
        "Command: $command",
        ($ErrorRecord | Out-String),
        "$($ErrorRecord.ScriptStackTrace)"
    ) -join [Environment]::NewLine
    try {
        [IO.File]::WriteAllText($ErrorLogPath, $details, (New-Object Text.UTF8Encoding($false)))
        Write-Host "Also saved to: $ErrorLogPath" -ForegroundColor Yellow
    } catch { Write-Host "Could not save the error log: $($_.Exception.Message)" -ForegroundColor Yellow }
}

function Test-NodeExecutable {
    param([string]$NodePath)
    if (-not $NodePath -or -not (Test-Path -LiteralPath $NodePath -PathType Leaf)) { return $false }
    try {
        $information = & $NodePath -p "JSON.stringify({major:Number(process.versions.node.split('.')[0]),architecture:process.arch})"
        if ($LASTEXITCODE -ne 0) { return $false }
        $parsed = $information | ConvertFrom-Json
        return ($parsed.major -ge 24 -and $parsed.architecture -eq 'x64')
    } catch { return $false }
}

function Get-NodeRelease {
    param($Releases)
    $release = $Releases | Where-Object {
        $_.version -match '^v24\.\d+\.\d+$' -and $_.lts -and $_.files -contains 'win-x64-zip'
    } | Select-Object -First 1
    if (-not $release) { throw 'Could not find an official Node.js 24 LTS Windows x64 release.' }
    return $release
}

function Get-ExpectedArchiveHash {
    param([string]$ChecksumText, [string]$ArchiveName)
    $pattern = '(?m)^([a-fA-F0-9]{64})\s+' + [regex]::Escape($ArchiveName) + '\s*$'
    $match = [regex]::Match($ChecksumText, $pattern)
    if (-not $match.Success) { throw 'The Node.js archive is missing from the official SHA256 list.' }
    return $match.Groups[1].Value
}

function Find-OrInstallNode {
    param([string]$RuntimeRoot, [switch]$ReadOnly)
    $existing = Get-Command node.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($existing -and (Test-NodeExecutable $existing.Source)) { return $existing.Source }
    $recordPath = Join-Path $RuntimeRoot 'Node_Runtime_Private.json'
    if (Test-Path -LiteralPath $recordPath) {
        try {
            $record = [IO.File]::ReadAllText($recordPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
            if ($record.directoryName -match '^node-v24\.\d+\.\d+-win-x64$') {
                $savedNode = Join-Path (Join-Path $RuntimeRoot $record.directoryName) 'node.exe'
                if (Test-NodeExecutable $savedNode) { return $savedNode }
            }
        } catch {}
    }
    if ($ReadOnly) { throw 'Node.js 24 or newer is missing. Run Setup.cmd without -CheckOnly to install the portable runtime.' }
    Write-Host 'Downloading the official Node.js 24 LTS portable runtime...' -ForegroundColor Cyan
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $release = Get-NodeRelease (Invoke-RestMethod -Uri 'https://nodejs.org/dist/index.json')
    $archiveName = 'node-' + $release.version + '-win-x64.zip'
    $releaseUrl = 'https://nodejs.org/dist/' + $release.version + '/'
    $checksumText = (Invoke-WebRequest -UseBasicParsing -Uri ($releaseUrl + 'SHASUMS256.txt')).Content
    $expectedHash = Get-ExpectedArchiveHash $checksumText $archiveName
    New-Item -ItemType Directory -Force -Path $RuntimeRoot | Out-Null
    $archivePath = Join-Path $RuntimeRoot $archiveName
    Invoke-WebRequest -UseBasicParsing -Uri ($releaseUrl + $archiveName) -OutFile $archivePath
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) { throw 'The downloaded Node.js archive failed SHA256 verification.' }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $RuntimeRoot -Force
    $directoryName = 'node-' + $release.version + '-win-x64'
    $nodePath = Join-Path (Join-Path $RuntimeRoot $directoryName) 'node.exe'
    if (-not (Test-NodeExecutable $nodePath)) { throw 'The downloaded Node.js runtime could not start.' }
    $record = @{ directoryName = $directoryName } | ConvertTo-Json
    [IO.File]::WriteAllText($recordPath, $record, (New-Object Text.UTF8Encoding($false)))
    return $nodePath
}

$ErrorActionPreference = 'Stop'
try {
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'This widget requires 64-bit Windows.' }
    $processorArchitecture = $env:PROCESSOR_ARCHITECTURE
    if ($env:PROCESSOR_ARCHITEW6432) { $processorArchitecture = $env:PROCESSOR_ARCHITEW6432 }
    if ($processorArchitecture -ne 'AMD64') { throw 'This version of the native widget supports Windows x64.' }
    $projectRoot = $PSScriptRoot
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'package-lock.json'))) {
        throw 'Copy the complete project folder, not only Setup.cmd and Setup.ps1.'
    }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler is missing. Enable .NET Framework 4.x, then run setup again.' }
    $runtimeRoot = Join-Path $env:LOCALAPPDATA 'CodexUsageWidget\Runtime\node'
    $nodePath = Find-OrInstallNode -RuntimeRoot $runtimeRoot -ReadOnly:$CheckOnly
    $env:PATH = [IO.Path]::GetDirectoryName($nodePath) + [IO.Path]::PathSeparator + $env:PATH
    [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
    $arguments = @((Join-Path $projectRoot 'scripts\setup.mjs'))
    if ($CheckOnly) { $arguments += '--check' }
    & $nodePath @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Usage Rings setup failed. See the details above and Setup_Report_Private.json in the project folder.' }
} catch {
    Show-Failure $_
}
Wait-BeforeClosing