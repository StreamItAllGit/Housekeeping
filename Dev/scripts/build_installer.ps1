# @module: Housekeeping.Dev.BuildInstaller
# @purpose: Stages Clean Release Root (Builds\v<Version>\), guards PII/paths, builds Single-EXE Setup & Portable ZIP, and packages 7-file Releases\ with hardlinks & 7z snapshots.
# @depends-on: PowerShell 5.1+, dotnet SDK 8.0+, python 3, System.IO.Compression.FileSystem
# @exports: build_installer.ps1

[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$CertThumbprint = "",
    [switch]$TestAutomationBuild,
    [switch]$SkipSigning,
    [string]$OutputExePath = "",
    [string]$AuditInstallerScript = "",
    [string]$BuildReleaseToolPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$devRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectRoot = (Resolve-Path (Join-Path $devRoot "..")).Path
$groupContainer = Split-Path -Parent $projectRoot
$projectsRoot = Split-Path -Parent $groupContainer
$expectedProjectsLeaf = 'Proj' + 'ects'

if ([string]::IsNullOrWhiteSpace($Version)) {
    $verFile = Join-Path $devRoot "version.txt"
    if (-not (Test-Path -LiteralPath $verFile)) { throw "Unable to locate Dev\version.txt." }
    $Version = (Get-Content -LiteralPath $verFile -Raw).Trim()
}
$Version = $Version.Trim().TrimStart('v', 'V')

$buildFlavor = if ($TestAutomationBuild) { "v$Version-automation" } else { "v$Version" }
$buildSource = Join-Path (Join-Path $projectRoot "Builds") $buildFlavor
$appDir = Join-Path $buildSource "app"
$releasesRoot = Join-Path $projectRoot "Releases"
$verReleasesDir = Join-Path $releasesRoot "v$Version"
$latestDir = Join-Path $releasesRoot "Latest"
if ($TestAutomationBuild -and [string]::IsNullOrWhiteSpace($OutputExePath)) {
    $OutputExePath = Join-Path ([System.IO.Path]::GetTempPath()) "Housekeeping-Setup-v$Version-win-x64-automation.exe"
}

function Remove-StagingJunkAndCheckGuards {
    param([string]$RootPath, [switch]$DisallowAutomation)
    Get-ChildItem -LiteralPath $RootPath -Recurse -Force -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -match '^(desktop\.ini.*|Thumbs\.db.*|ehthumbs\.db|\.DS_Store.*|\._.*)$' -or
            $_.Extension -eq '.pdb' -or $_.Name -like 'Foundation.*.xml'
        } | Remove-Item -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath (Join-Path $RootPath "AGENTS.md")) { throw "Containment failure: AGENTS.md must never be staged in '$RootPath'." }
    if (Test-Path -LiteralPath (Join-Path $RootPath "LICENSE")) { throw "Layout failure: loose root LICENSE must not exist in '$RootPath' (keep app\LICENSE only)." }
    if (Test-Path -LiteralPath (Join-Path $RootPath "mother.manifest.json")) { throw "Layout failure: loose root mother.manifest.json must not exist in '$RootPath'." }
    if ($DisallowAutomation) {
        $leaks = @(Get-ChildItem -LiteralPath $RootPath -Recurse -Force -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "Foundation.Automation.*" })
        if ($leaks.Count -gt 0) { throw "Containment failure: Foundation.Automation.* leaked into Release staging '$RootPath'." }
    }
}

function Assert-StagingPiiClean {
    param([string]$RootPath)
    $forbiddenExts = @(".pdb", ".db", ".sqlite", ".sqlite3", ".log")
    $userLead = [string]([char]67) + ':[\\/]+' + 'Us' + 'ers[\\/]+'
    $dLead = [string]([char]68) + ':[\\/]+'
    $patterns = @(
        @{ Name = "Local User Profile Path"; Regex = "(?i)$userLead(?!Public|Default)[A-Za-z0-9._-]+" },
        @{ Name = "Local Workspace Path";    Regex = "(?i)$dLead" + "Proj" + "ects[\\/]+" },
        @{ Name = "Local Production Path";   Regex = "(?i)$dLead" + "Prog" + "rams[\\/]+" },
        @{ Name = "Google API Key";          Regex = 'AIza[0-9A-Za-z\-_]{35}' },
        @{ Name = "OpenAI/API Secret Key";   Regex = 'sk-[A-Za-z0-9]{20,}' },
        @{ Name = "GitHub Token";            Regex = '(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{22,})' },
        @{ Name = "OAuth Client Secret";     Regex = '(?i)(client_secret|refresh_token|access_token)\s*[:=]\s*["''][A-Za-z0-9\-_/.]{12,}["'']' },
        @{ Name = "Private Key Block";       Regex = '-----BEGIN [A-Z ]*PRIVATE KEY-----' }
    )
    foreach ($f in (Get-ChildItem -LiteralPath $RootPath -Recurse -File -Force)) {
        if (($f.Name -ieq ".env") -or ($f.Name -ilike "oauth_creds*.json") -or ($f.Name -ilike "*secret*.json") -or ($f.Extension -in $forbiddenExts)) {
            throw "Forbidden artifact in staging directory: '$($f.FullName)'."
        }
        if ($f.Extension -in @(".png", ".ico", ".jpg", ".jpeg", ".webp", ".woff", ".woff2")) { continue }
        $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
        if ($bytes.Length -eq 0) { continue }
        $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
        $utf16 = [System.Text.Encoding]::Unicode.GetString($bytes, 0, ($bytes.Length - ($bytes.Length % 2)))
        foreach ($p in $patterns) {
            $hit = [regex]::Match($ascii, $p.Regex)
            if (-not $hit.Success) { $hit = [regex]::Match($utf16, $p.Regex) }
            if ($hit.Success) { throw "PII/Secret Gate Failed in '$($f.FullName)' ($($p.Name)): '$($hit.Value)'" }
        }
    }
}

function Convert-ToSanitizedReleaseMarkdown {
    param([string]$RawText, [string]$Kind, [string]$Ver)
    if ([string]::IsNullOrWhiteSpace($RawText)) { return "" }
    $sep = '[\\/]+'
    $dLead = [string]([char]68) + ':' + $sep
    $cLead = [string]([char]67) + ':' + $sep
    $filePrefix = 'fi' + 'le:///'
    $txt = ($RawText -replace "`r`n", "`n")
    $txt = [regex]::Replace($txt, "\[([^\]]+)\]\($filePrefix[^)]+\)", '$1')
    $txt = [regex]::Replace($txt, "$filePrefix[^\s\)]+", '')
    $txt = [regex]::Replace($txt, "(?i)$dLead" + "Proj" + "ects$sep" + "Personal$sep" + "Housekeeping[\\/]*", '')
    $txt = [regex]::Replace($txt, "(?i)$dLead" + "Proj" + "ects$sep" + "In" + "stallers[\\/]*", 'the Housekeeping Releases bundle ')
    $txt = [regex]::Replace($txt, "(?i)$dLead" + "Proj" + "ects$sep" + "SharedLibs$sep", 'Foundation SDK ')
    $txt = [regex]::Replace($txt, "(?i)$dLead" + "Proj" + "ects$sep", '')
    $txt = [regex]::Replace($txt, "(?i)$dLead" + "Prog" + "rams$sep(?:Housekeeping[\\/]*)?", '%LOCALAPPDATA%\Programs\StreamItAll\Mother\Housekeeping\')
    $txt = [regex]::Replace($txt, "(?i)$cLead" + "Us" + "ers$sep[^\\/\s)]+[\\/]*", '%USERPROFILE%\')
    $txt = [regex]::Replace($txt, '(?i)\bDev[\\/](docs|Housekeeping\.[A-Za-z]+|scripts)[\\/]?[^\s`)]*', 'app/')
    if ($Kind -eq "README" -and $txt -notmatch '(?m)^##\s+License\s+&\s+Legal') {
        $txt = $txt.TrimEnd() + "`n`n---`n`n## License & Legal`n`nHousekeeping is licensed under the **Mozilla Public License Version 2.0 (`MPL-2.0`)**. See `app\LICENSE` in portable/installed builds for the complete license text.`n"
    } elseif ($Kind -eq "FEATURES") {
        $txt = (($txt -split "`n" | Where-Object { $_ -notmatch '(?i)\|\s*\*\*`?(In-Dev|Planned)' }) -join "`n").TrimEnd() + "`n"
    }
    return $txt
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)
    [System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($Path), $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function Invoke-SignBinary {
    param([string]$TargetPath, [string]$PreferredThumbprint)
    $cert = if ($PreferredThumbprint) { Get-Item "Cert:\CurrentUser\My\$PreferredThumbprint" -ErrorAction SilentlyContinue } else { $null }
    if (-not $cert) {
        $cert = @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } |
            Sort-Object @{Expression = { if ($_.Subject -match "StreamItAll") { 0 } else { 1 } }; Ascending = $true}, @{Expression = "NotAfter"; Descending = $true} |
            Select-Object -First 1)
        if ($cert -is [array]) { $cert = if ($cert.Count -gt 0) { $cert[0] } else { $null } }
    }
    if (-not $cert) { throw "No active Code Signing certificate with private key found in Cert:\CurrentUser\My." }
    $signtool = @(Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } | Sort-Object FullName -Descending | Select-Object -First 1)
    if ($signtool.Count -gt 0) {
        & $signtool[0].FullName sign /fd SHA256 /sha1 $cert.Thumbprint /q $TargetPath
        if ($LASTEXITCODE -ne 0) { throw "signtool.exe failed ($LASTEXITCODE)." }
    } else {
        $sig = Set-AuthenticodeSignature -LiteralPath $TargetPath -Certificate $cert -HashAlgorithm SHA256
        if ($sig.Status -ne "Valid") { Write-Warning "Authenticode status for '$TargetPath': $($sig.Status)" }
    }
}

function Resolve-BuildReleaseTool {
    param([string]$ExplicitPath)
    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) { $candidates.Add($ExplicitPath) }
    if (-not [string]::IsNullOrWhiteSpace($env:FOUNDATION_TOOLS_DIR)) { $candidates.Add((Join-Path $env:FOUNDATION_TOOLS_DIR "build_release_universal.py")) }
    if ((Split-Path -Leaf $projectsRoot) -ieq $expectedProjectsLeaf) {
        $candidates.Add((Join-Path $projectsRoot "Personal\Foundation\Dev\tools\build_release_universal.py"))
        $candidates.Add((Join-Path $projectsRoot "SharedLibs\Foundation\v0.5.3\tools\build_release_universal.py"))
    }
    $gitCommon = (& git -C $projectRoot rev-parse --git-common-dir 2>$null | Out-String).Trim()
    if (-not [string]::IsNullOrWhiteSpace($gitCommon)) {
        $commonFull = if ([System.IO.Path]::IsPathRooted($gitCommon)) { $gitCommon } else { [System.IO.Path]::GetFullPath((Join-Path $projectRoot $gitCommon)) }
        $repoParent = Split-Path -Parent (Split-Path -Parent $commonFull)
        $repoProjects = Split-Path -Parent $repoParent
        if ((Split-Path -Leaf $repoProjects) -ieq $expectedProjectsLeaf) {
            $candidates.Add((Join-Path $repoProjects "Personal\Foundation\Dev\tools\build_release_universal.py"))
        }
    }
    return ($candidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) } | Select-Object -First 1)
}

function Get-PeFileVersion {
    param([string]$SemVer)
    $m = [regex]::Match($SemVer, '^(\d+)\.(\d+)\.(\d+)(?:-[A-Za-z]+\.(\d+))?')
    if (-not $m.Success) { return "0.2.0.0" }
    $rev = 0
    if ($m.Groups[4].Success) {
        $raw = $m.Groups[4].Value
        $parsed = 0L
        if ($raw.Length -eq 8) { $rev = [int]$raw.Substring(4, 4) }
        elseif ([long]::TryParse($raw, [ref]$parsed)) { $rev = [int][Math]::Min([Math]::Max($parsed, 0L), 65534L) }
    }
    return "$([Math]::Min([int]$m.Groups[1].Value,65534)).$([Math]::Min([int]$m.Groups[2].Value,65534)).$([Math]::Min([int]$m.Groups[3].Value,65534)).$rev"
}

if (Test-Path -LiteralPath $buildSource) { Remove-Item -LiteralPath $buildSource -Recurse -Force }
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

$webRoot = Join-Path $devRoot "Housekeeping.Web"
if (-not (Test-Path -LiteralPath (Join-Path $webRoot "node_modules"))) {
    & npm.cmd --prefix $webRoot ci
    if ($LASTEXITCODE -ne 0) { & npm.cmd --prefix $webRoot install; if ($LASTEXITCODE -ne 0) { throw "npm install failed." } }
}
& npm.cmd --prefix $webRoot run build
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $devRoot "Housekeeping.Server\wwwroot\index.html"))) {
    throw "Housekeeping.Web SPA build failed."
}

$extraAutoProp = if ($TestAutomationBuild) { @("-p:HousekeepingTestAutomation=true") } else { @() }
foreach ($projRel in @("Housekeeping.DesktopHost\Housekeeping.DesktopHost.csproj", "Housekeeping.Server\Housekeeping.Server.csproj")) {
    $pubArgs = @("publish", (Join-Path $devRoot $projRel), "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-t:Rebuild,Publish",
        "-p:TreatWarningsAsErrors=true", "-p:DebugType=none", "-p:DebugSymbols=false", "-p:Version=$Version", "-o", $appDir) + $extraAutoProp
    & dotnet @pubArgs
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $projRel ($LASTEXITCODE)." }
}

$tempStage = Join-Path ([System.IO.Path]::GetTempPath()) ("hk_pkg_" + [Guid]::NewGuid().ToString("N"))
$launcherTemp = Join-Path $tempStage "launcher"
$uninstTemp = Join-Path $tempStage "uninst"
$publishDir = Join-Path $tempStage "setup"
$portableStageDir = Join-Path $tempStage "portable"
New-Item -ItemType Directory -Force -Path $launcherTemp, $uninstTemp, $publishDir, $portableStageDir | Out-Null

try {
    $fileVer = Get-PeFileVersion -SemVer $Version
    & dotnet publish (Join-Path $devRoot "Housekeeping.Launcher\Housekeeping.Launcher.csproj") -c Release -r win-x64 --self-contained false `
        -t:Rebuild,Publish -p:PublishSingleFile=true -p:TreatWarningsAsErrors=true -p:DebugType=none -p:DebugSymbols=false -p:Version=$Version -o $launcherTemp
    if ($LASTEXITCODE -ne 0) { throw "Publish Launcher failed ($LASTEXITCODE)." }
    Copy-Item -LiteralPath (Join-Path $launcherTemp "Housekeeping.exe") -Destination (Join-Path $buildSource "Housekeeping.exe") -Force

    $installerProj = Join-Path $devRoot "Housekeeping.Installer\Housekeeping.Installer.csproj"
    $uninstArgs = @("publish", $installerProj, "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-t:Rebuild,Publish",
        "-p:PublishSingleFile=true", "-p:EnableCompressionInSingleFile=false", "-p:TreatWarningsAsErrors=true", "-p:DebugType=none", "-p:DebugSymbols=false",
        "-p:InstallerVersion=$Version", "-p:FileVersion=$fileVer", "-o", $uninstTemp) + $extraAutoProp
    & dotnet @uninstArgs
    if ($LASTEXITCODE -ne 0) { throw "Publish Housekeeping-Uninstall.exe failed ($LASTEXITCODE)." }
    Copy-Item -LiteralPath (Join-Path $uninstTemp "Housekeeping.Installer.exe") -Destination (Join-Path $appDir "Housekeeping-Uninstall.exe") -Force

    Copy-Item -LiteralPath (Join-Path $devRoot "version.txt") -Destination (Join-Path $appDir "version.txt") -Force
    $licenseSrc = Join-Path $projectRoot "LICENSE"
    if (Test-Path -LiteralPath $licenseSrc) { Copy-Item -LiteralPath $licenseSrc -Destination (Join-Path $appDir "LICENSE") -Force }
    $suiteManifestSrc = Join-Path $devRoot "suite.manifest.json"
    if (Test-Path -LiteralPath $suiteManifestSrc) {
        $stamp = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        $suiteRaw = [regex]::Replace((Get-Content -LiteralPath $suiteManifestSrc -Raw -Encoding UTF8), '"updatedAtUtc"\s*:\s*"[^"]*"', "`"updatedAtUtc`": `"$stamp`"")
        Write-Utf8NoBom -Path (Join-Path $appDir "suite.manifest.json") -Content $suiteRaw
    }
    $settingsExampleSrc = Join-Path $devRoot "config\settings.example.json"
    if (Test-Path -LiteralPath $settingsExampleSrc) {
        $cfgDir = Join-Path $appDir "config"
        New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
        Copy-Item -LiteralPath $settingsExampleSrc -Destination (Join-Path $cfgDir "settings.example.json") -Force
    }
    $scriptsDst = Join-Path $buildSource "scripts"
    $interactiveDst = Join-Path $scriptsDst "interactive"
    New-Item -ItemType Directory -Force -Path $interactiveDst | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $devRoot "scripts") -File -Filter "*.bat" | Copy-Item -Destination $scriptsDst -Force
    if (Test-Path -LiteralPath (Join-Path $devRoot "scripts\interactive")) {
        Get-ChildItem -LiteralPath (Join-Path $devRoot "scripts\interactive") -File -Filter "*.bat" | Copy-Item -Destination $interactiveDst -Force
    }

    Remove-StagingJunkAndCheckGuards -RootPath $buildSource -DisallowAutomation:(-not $TestAutomationBuild)
    Assert-StagingPiiClean -RootPath $buildSource

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $payloadZip = Join-Path $tempStage "payload.zip"
    [System.IO.Compression.ZipFile]::CreateFromDirectory($buildSource, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $setupArgs = @("publish", $installerProj, "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-t:Rebuild,Publish",
        "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true",
        "-p:TreatWarningsAsErrors=true", "-p:DebugType=none", "-p:DebugSymbols=false",
        "-p:InstallerVersion=$Version", "-p:FileVersion=$fileVer", "-p:InstallerPayloadZip=$payloadZip", "-o", $publishDir) + $extraAutoProp
    & dotnet @setupArgs
    if ($LASTEXITCODE -ne 0) { throw "Setup EXE publish failed ($LASTEXITCODE)." }
    $builtExe = Join-Path $publishDir "Housekeeping.Installer.exe"
    if (-not (Test-Path -LiteralPath $builtExe)) { throw "Expected installer missing: $builtExe" }

    if ($TestAutomationBuild) {
        $autoDir = Split-Path -Parent $OutputExePath
        if (-not [string]::IsNullOrWhiteSpace($autoDir)) { New-Item -ItemType Directory -Force -Path $autoDir | Out-Null }
        Copy-Item -LiteralPath $builtExe -Destination $OutputExePath -Force
        if (-not $SkipSigning) { Invoke-SignBinary -TargetPath $OutputExePath -PreferredThumbprint $CertThumbprint }
        $verAuto = (& $OutputExePath --version 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($verAuto)) { throw "Automation --version check failed: $verAuto" }
        Write-Host "Automation setup ready: $OutputExePath (v$verAuto)" -ForegroundColor Green
        return
    }

    if (Test-Path -LiteralPath $verReleasesDir) { Remove-Item -LiteralPath $verReleasesDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $verReleasesDir | Out-Null
    $targetExePath = Join-Path $verReleasesDir "Housekeeping-Setup-v$Version-win-x64.exe"
    $portableZipPath = Join-Path $verReleasesDir "Housekeeping-v$Version-win-x64-Portable.zip"
    Copy-Item -LiteralPath $builtExe -Destination $targetExePath -Force
    if (-not $SkipSigning) { Invoke-SignBinary -TargetPath $targetExePath -PreferredThumbprint $CertThumbprint }
    $verOut = (& $targetExePath --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($verOut)) { throw "Setup --version check failed: $verOut" }

    $userGuidePath = Join-Path $devRoot "docs\USER_GUIDE.md"
    $rawGuide = if (Test-Path -LiteralPath $userGuidePath) { Get-Content -LiteralPath $userGuidePath -Raw -Encoding UTF8 }
        elseif (Test-Path -LiteralPath (Join-Path $projectRoot "README.md")) { Get-Content -LiteralPath (Join-Path $projectRoot "README.md") -Raw -Encoding UTF8 }
        else { "# Housekeeping v$Version`n" }
    Write-Utf8NoBom -Path (Join-Path $verReleasesDir "README.md") -Content (Convert-ToSanitizedReleaseMarkdown -RawText $rawGuide -Kind "README" -Ver $Version)

    $changelogRaw = ""
    foreach ($docName in @("FEATURES.md", "CHANGELOG.md")) {
        $docSrc = Join-Path $projectRoot $docName
        if (Test-Path -LiteralPath $docSrc) {
            $rawDoc = Get-Content -LiteralPath $docSrc -Raw -Encoding UTF8
            if ($docName -eq "CHANGELOG.md") { $changelogRaw = $rawDoc }
            Write-Utf8NoBom -Path (Join-Path $verReleasesDir $docName) -Content (Convert-ToSanitizedReleaseMarkdown -RawText $rawDoc -Kind $docName.Replace(".md", "") -Ver $Version)
        }
    }
    $escVer = [regex]::Escape($Version)
    $sec = [regex]::Match($changelogRaw, "(?ms)^##\s+\[$escVer\][^\r\n]*\r?\n(.*?)(?=^##\s+\[|\z)")
    $notesBody = if ($sec.Success -and -not [string]::IsNullOrWhiteSpace($sec.Groups[1].Value)) { $sec.Groups[1].Value.Trim().TrimEnd('-').Trim() }
        else { "Release bundle for Housekeeping v$Version. See CHANGELOG.md for full details." }
    Write-Utf8NoBom -Path (Join-Path $verReleasesDir "RELEASE_NOTES.md") -Content ("# Housekeeping v$Version Release Notes`n`n" + (Convert-ToSanitizedReleaseMarkdown -RawText $notesBody -Kind "NOTES" -Ver $Version) + "`n")

    Copy-Item (Join-Path $buildSource "*") $portableStageDir -Recurse -Force
    foreach ($relDoc in @("README.md", "FEATURES.md", "CHANGELOG.md", "RELEASE_NOTES.md")) {
        $docFull = Join-Path $verReleasesDir $relDoc
        if (Test-Path -LiteralPath $docFull) { Copy-Item -LiteralPath $docFull -Destination (Join-Path $portableStageDir $relDoc) -Force }
    }
    Remove-StagingJunkAndCheckGuards -RootPath $portableStageDir -DisallowAutomation
    [System.IO.Compression.ZipFile]::CreateFromDirectory($portableStageDir, $portableZipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $releaseTool = Resolve-BuildReleaseTool -ExplicitPath $BuildReleaseToolPath
    if (-not $releaseTool) { throw "build_release_universal.py not found (pass -BuildReleaseToolPath or set FOUNDATION_TOOLS_DIR)." }
    & python $releaseTool $verReleasesDir --regen-sums --allow-portable-zip
    if ($LASTEXITCODE -ne 0) { throw "build_release_universal.py failed for '$verReleasesDir' ($LASTEXITCODE)." }

    if (Test-Path -LiteralPath $latestDir) { Remove-Item -LiteralPath $latestDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $latestDir | Out-Null
    foreach ($fileItem in (Get-ChildItem -LiteralPath $verReleasesDir -File)) {
        $dst = Join-Path $latestDir $fileItem.Name
        try { New-Item -ItemType HardLink -Path $dst -Value $fileItem.FullName | Out-Null }
        catch { Copy-Item -LiteralPath $fileItem.FullName -Destination $dst -Force }
    }
    & python $releaseTool $latestDir --allow-portable-zip
    if ($LASTEXITCODE -ne 0) { throw "build_release_universal.py failed for '$latestDir' ($LASTEXITCODE)." }

    if ((Split-Path -Leaf $projectsRoot) -ieq $expectedProjectsLeaf) {
        $centralGroup = Join-Path (Join-Path $projectsRoot "Releases") (Split-Path -Leaf $groupContainer)
        $centralJunction = Join-Path $centralGroup (Split-Path -Leaf $projectRoot)
        New-Item -ItemType Directory -Force -Path $centralGroup | Out-Null
        if (-not (Test-Path -LiteralPath $centralJunction)) {
            $mk = Start-Process -FilePath "cmd.exe" -ArgumentList @("/c", "mklink", "/J", $centralJunction, $releasesRoot) -NoNewWindow -Wait -PassThru
            if ($mk.ExitCode -ne 0) { throw "Failed creating Releases junction '$centralJunction'." }
        }
        $jItem = Get-Item -LiteralPath $centralJunction -Force
        if (($jItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) { throw "Releases link '$centralJunction' is not a junction." }
    }

    $auditor = if (-not [string]::IsNullOrWhiteSpace($AuditInstallerScript)) { $AuditInstallerScript }
        else { Join-Path $env:USERPROFILE ".gemini\config\skills\dev\scripts\audit_installer.ps1" }
    $canAuditLayout = ((Split-Path -Leaf $projectsRoot) -ieq $expectedProjectsLeaf) -or (-not [string]::IsNullOrWhiteSpace($AuditInstallerScript))
    if ((Test-Path -LiteralPath $auditor) -and -not $SkipSigning -and $canAuditLayout) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $auditor -InstallerPath $verReleasesDir -StagingDir $buildSource -ExpectedVersion $Version -ReleaseLayoutMode SingleSetup7 -VerifyChecksums
        if ($LASTEXITCODE -ne 0) { throw "audit_installer.ps1 failed for '$verReleasesDir' ($LASTEXITCODE)." }
    } elseif ($SkipSigning) {
        Write-Warning "Skipping audit_installer.ps1 Authenticode verification because -SkipSigning was specified."
    }

    $sevenZip = if ($cmd = Get-Command 7z -ErrorAction SilentlyContinue) { $cmd.Source }
        elseif (Test-Path -LiteralPath "$env:ProgramFiles\7-Zip\7z.exe") { "$env:ProgramFiles\7-Zip\7z.exe" } else { $null }
    if ($sevenZip) {
        foreach ($pair in @(@($verReleasesDir, (Join-Path $releasesRoot "v$Version.7z")), @($latestDir, (Join-Path $releasesRoot "Latest.7z")))) {
            if (Test-Path -LiteralPath $pair[1]) { Remove-Item -LiteralPath $pair[1] -Force }
            & $sevenZip a -t7z -mx=9 $pair[1] "$($pair[0])\*" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "7z failed for $($pair[1])." }
        }
    }

    $minorGroups = @(Get-ChildItem -LiteralPath $releasesRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^v(\d+)\.(\d+)\.\d+' } |
        Group-Object { $m = [regex]::Match($_.Name, '^v(\d+)\.(\d+)'); "{0:D6}.{1:D6}" -f [int]$m.Groups[1].Value, [int]$m.Groups[2].Value } |
        Sort-Object Name -Descending)
    foreach ($oldGrp in ($minorGroups | Select-Object -Skip 3)) {
        foreach ($oldDir in $oldGrp.Group) {
            if (Test-Path -LiteralPath (Join-Path $releasesRoot "$($oldDir.Name).7z")) {
                Remove-Item -LiteralPath $oldDir.FullName -Recurse -Force
            }
        }
    }
    Write-Host "Packaged 7-file release bundle ready: $verReleasesDir -> $latestDir" -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $tempStage) { Remove-Item -LiteralPath $tempStage -Recurse -Force -ErrorAction SilentlyContinue }
}
