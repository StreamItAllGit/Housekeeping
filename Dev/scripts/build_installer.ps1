[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "",

    [Parameter(Mandatory = $false)]
    [string]$CertThumbprint = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$devRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectRoot = (Resolve-Path (Join-Path $devRoot "..")).Path
$buildsRoot = Join-Path $projectRoot "Builds"
$installersDir = Join-Path $projectRoot "Installers"

if (-not $Version) {
    $verFile = Join-Path $devRoot "version.txt"
    if (-not (Test-Path -LiteralPath $verFile)) {
        throw "Unable to locate Dev\version.txt to resolve active version."
    }
    $Version = (Get-Content -LiteralPath $verFile -Raw).Trim()
}

$Version = $Version.Trim().TrimStart('v', 'V')
$buildSource = Join-Path $buildsRoot "v$Version"

# Automatically stage Builds\v$Version
if (Test-Path -LiteralPath $buildSource) {
    Remove-Item -LiteralPath $buildSource -Recurse -Force
}
Write-Host "Staging build output into $buildSource..."
New-Item -ItemType Directory -Force -Path $buildSource | Out-Null
$appDir = Join-Path $buildSource "app"
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

    # 0. Build Housekeeping.Web SPA into Housekeeping.Server\wwwroot\ before publishing
    $webRoot = Join-Path $devRoot "Housekeeping.Web"
    $webNodeModules = Join-Path $webRoot "node_modules"
    if (-not (Test-Path -LiteralPath $webNodeModules)) {
        Write-Host "Restoring Housekeeping.Web node_modules..."
        & npm.cmd --prefix $webRoot ci
        if ($LASTEXITCODE -ne 0) {
            & npm.cmd --prefix $webRoot install
            if ($LASTEXITCODE -ne 0) { throw "npm install for Housekeeping.Web failed" }
        }
    }
    Write-Host "Building Housekeeping.Web SPA..."
    & npm.cmd --prefix $webRoot run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build for Housekeeping.Web failed" }
    $builtIndexHtml = Join-Path $devRoot "Housekeeping.Server\wwwroot\index.html"
    if (-not (Test-Path -LiteralPath $builtIndexHtml)) {
        throw "Expected compiled SPA entry missing after npm run build: $builtIndexHtml"
    }

    # 1. Publish DesktopHost into app\
    & dotnet publish (Join-Path $devRoot "Housekeeping.DesktopHost\Housekeeping.DesktopHost.csproj") -c Release -r win-x64 --self-contained false -o $appDir /p:Version=$Version /nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish DesktopHost failed" }

    # 2. Publish Server into app\
    & dotnet publish (Join-Path $devRoot "Housekeeping.Server\Housekeeping.Server.csproj") -c Release -r win-x64 --self-contained false -o $appDir /p:Version=$Version /nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish Server failed" }

    # 3. Publish Launcher into root Builds\v$Version\Housekeeping.exe
    $launcherTemp = Join-Path ([System.IO.Path]::GetTempPath()) "hk_launcher_$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    & dotnet publish (Join-Path $devRoot "Housekeeping.Launcher\Housekeeping.Launcher.csproj") -c Release -r win-x64 --self-contained false -o $launcherTemp /p:Version=$Version /nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish Launcher failed" }
    Copy-Item -LiteralPath (Join-Path $launcherTemp "Housekeeping.exe") -Destination (Join-Path $buildSource "Housekeeping.exe") -Force
    Remove-Item -LiteralPath $launcherTemp -Recurse -Force -ErrorAction SilentlyContinue

    # 4. Copy scripts, manifests, and portable runtime AGENTS.md (zero local dev/prod paths)
    Copy-Item -LiteralPath (Join-Path $devRoot "version.txt") -Destination (Join-Path $appDir "version.txt") -Force
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $motherManifestSrc = Join-Path $devRoot "mother.manifest.json"
    if (Test-Path -LiteralPath $motherManifestSrc) {
        $manifestRaw = Get-Content -LiteralPath $motherManifestSrc -Raw -Encoding UTF8
        $manifestStamped = [regex]::Replace($manifestRaw, '"updatedAtUtc"\s*:\s*"[^"]*"', "`"updatedAtUtc`": `"$([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'))`"")
        [System.IO.File]::WriteAllText((Join-Path $buildSource "mother.manifest.json"), $manifestStamped, $utf8NoBom)
        [System.IO.File]::WriteAllText((Join-Path $appDir "suite.manifest.json"), $manifestStamped, $utf8NoBom)
    }

    $portableAgentsTemplate = @'
# Runtime Context: Housekeeping (`v{VER}`)

- **Role:** Automated Storage Hygiene, Media Transcoding & Deduplication Companion Daemon (`v{VER}`, Port `5178`).
- **Clean Release Root Layout:**
  - `./Housekeeping.exe` (Root launcher shim) & `./mother.manifest.json`
  - `./app/` (`Housekeeping.exe`, `Housekeeping.Server.exe`, `Housekeeping-Uninstall.exe`, `suite.manifest.json`, `version.txt`)
  - `./data/` (`settings.json`, `history.json`, `compliance_reports.json`, `pending_swaps.json`, `dats/`, `tools/`)
  - `./backups/` (`config_snapshots/` & state archives)
  - `./logs/` (`Housekeeping_Live.log`, `telemetry_heartbeat.json`, `runs/`, `crashes/`)
  - `./scripts/` (`start-daemon.bat`, `stop-daemon.bat`, `status-check.bat`, `interactive/repair-config.bat`)
- **Safety Invariants:** Never overwrite `./data/` user state during upgrades. Use `--test-mode --dry-run` with isolated `--data-dir` and `--log-dir` for verification.
'@
    $portableAgentsContent = $portableAgentsTemplate.Replace('{VER}', $Version)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText((Join-Path $buildSource "AGENTS.md"), $portableAgentsContent.Trim() + "`r`n", $utf8NoBom)

    $scriptsDst = Join-Path $buildSource "scripts"
    New-Item -ItemType Directory -Force -Path $scriptsDst | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $devRoot "scripts") -File -Filter "*.bat" | Copy-Item -Destination $scriptsDst -Force

    $interactiveDst = Join-Path $scriptsDst "interactive"
    New-Item -ItemType Directory -Force -Path $interactiveDst | Out-Null
    if (Test-Path -LiteralPath (Join-Path $devRoot "scripts\interactive")) {
        Get-ChildItem -LiteralPath (Join-Path $devRoot "scripts\interactive") -File -Filter "*.bat" | Copy-Item -Destination $interactiveDst -Force
    }

    # 5. Clean up any loose PDBs in $buildSource to ensure zero forbidden debug artifacts
    Get-ChildItem -LiteralPath $buildSource -Recurse -Filter "*.pdb" | Remove-Item -Force

New-Item -ItemType Directory -Force -Path $installersDir | Out-Null

# 1. Pre-audit staged payload files for forbidden artifacts & zero PII / local developer paths (ASCII + UTF-16LE)
$forbiddenExts = @(".pdb", ".db", ".sqlite", ".sqlite3", ".log")
$sensitivePatterns = @(
    @{ Name = "Local User Profile Path"; Regex = '(?i)C:[\\/]+Users[\\/]+(?!Public|Default)[A-Za-z0-9._-]+' },
    @{ Name = "Local Workspace Path";    Regex = '(?i)D:[\\/]+Projects[\\/]+' },
    @{ Name = "Local Production Path";   Regex = '(?i)D:[\\/]+Programs[\\/]+' },
    @{ Name = "Google API Key";          Regex = 'AIza[0-9A-Za-z\-_]{35}' },
    @{ Name = "OpenAI/API Secret Key";   Regex = 'sk-[A-Za-z0-9]{20,}' },
    @{ Name = "GitHub Token";            Regex = 'gh[pousr]_[A-Za-z0-9]{30,}' },
    @{ Name = "Private Key Block";       Regex = '-----BEGIN [A-Z ]*PRIVATE KEY-----' }
)

foreach ($f in (Get-ChildItem -LiteralPath $buildSource -Recurse -File -Force)) {
    if (($f.Name -ieq ".env") -or ($f.Name -ilike "*secret*.json") -or ($f.Extension -in $forbiddenExts)) {
        throw "Forbidden sensitive/debug artifact in staging directory '$($f.FullName)'."
    }
    if ($f.Extension -in @(".png", ".ico", ".jpg", ".jpeg", ".webp", ".woff", ".woff2")) {
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    if ($bytes.Length -eq 0) { continue }
    $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
    $utf16 = [System.Text.Encoding]::Unicode.GetString($bytes, 0, ($bytes.Length - ($bytes.Length % 2)))
    foreach ($p in $sensitivePatterns) {
        $hit = [regex]::Match($ascii, $p.Regex)
        if (-not $hit.Success) {
            $hit = [regex]::Match($utf16, $p.Regex)
        }
        if ($hit.Success) {
            throw "PII/Path Sanitization Gate Failed in '$($f.FullName)' ($($p.Name)): '$($hit.Value)'"
        }
    }
}

# Verify Installers\ contains zero loose sidecar folders or non-.exe files (retaining historical .exe installers side-by-side)
foreach ($existing in @(Get-ChildItem -LiteralPath $installersDir -Force)) {
    if ($existing.PSIsContainer -or ($existing.Extension -ine ".exe")) {
        throw "Single-EXE violation in Installers\: unexpected sidecar '$($existing.FullName)'. Only versioned .exe installers may reside in Installers\."
    }
}

# 2. Build deterministic payload.zip + metadata.json in ephemeral %TEMP% staging (outside D:\Projects\...)
$tempStage = Join-Path ([System.IO.Path]::GetTempPath()) "hk_inst_$([guid]::NewGuid().ToString('N').Substring(0, 10))"
$projCopyDir = Join-Path $tempStage "src"
$publishDir = Join-Path $tempStage "pub"
New-Item -ItemType Directory -Force -Path $projCopyDir, $publishDir | Out-Null

try {
    $payloadZip = Join-Path $tempStage "payload.zip"
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $files = @(Get-ChildItem -LiteralPath $buildSource -Recurse -File | Sort-Object FullName)
    $fs = [System.IO.File]::Create($payloadZip)
    try {
        $za = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            $fixedTime = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            foreach ($file in $files) {
                $rel = $file.FullName.Substring($buildSource.Length).TrimStart('\', '/').Replace('\', '/')
                $entry = $za.CreateEntry($rel, [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTime
                $inStream = [System.IO.File]::OpenRead($file.FullName)
                $outStream = $entry.Open()
                try {
                    $inStream.CopyTo($outStream)
                } finally {
                    $outStream.Dispose()
                    $inStream.Dispose()
                }
            }
        } finally {
            $za.Dispose()
        }
    } finally {
        $fs.Dispose()
    }

    $payloadSha = (Get-FileHash -LiteralPath $payloadZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $metaJsonPath = Join-Path $tempStage "metadata.json"
    $metaObj = [ordered]@{
        AppName       = "Housekeeping"
        Version       = $Version
        PayloadSha256 = $payloadSha
        FileCount     = $files.Count
    }
    Set-Content -LiteralPath $metaJsonPath -Encoding UTF8 -Value ($metaObj | ConvertTo-Json)

    # Copy C# installer source (all *.cs partial files) + icon into %TEMP%\hk_inst_*\src so MSBuild never embeds D:\Projects\... paths
    $installerProjDir = Join-Path $devRoot "Housekeeping.Installer"
    Copy-Item -LiteralPath (Join-Path $installerProjDir "Housekeeping.Installer.csproj") -Destination (Join-Path $projCopyDir "Housekeeping.Installer.csproj") -Force
    Get-ChildItem -LiteralPath $installerProjDir -File -Filter "*.cs" | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $projCopyDir $_.Name) -Force
    }
    $iconSrc = Join-Path $buildSource "Assets\app_icon.ico"
    if (-not (Test-Path -LiteralPath $iconSrc)) {
        $iconSrc = Join-Path $devRoot "Housekeeping.DesktopHost\Assets\app_icon.ico"
    }
    $iconDst = Join-Path $projCopyDir "app_icon.ico"
    if (Test-Path -LiteralPath $iconSrc) {
        Copy-Item -LiteralPath $iconSrc -Destination $iconDst -Force
    }

    # Derive numeric Windows PE FileVersion (X.Y.Z.R, each component uint16 <= 65534) from SemVer + optional pre-release number
    $verMatch = [regex]::Match($Version, '^(\d+)\.(\d+)\.(\d+)(?:-[A-Za-z]+\.(\d+))?')
    $fileVer = if ($verMatch.Success) {
        $revNum = 0
        if ($verMatch.Groups[4].Success) {
            $rawRev = $verMatch.Groups[4].Value
            if ($rawRev.Length -eq 8) {
                # Map 8-digit YYYYMMDD nightly date stamp to 4-digit MMDD (101..1231 <= 65534)
                $revNum = [int]$rawRev.Substring(4, 4)
            } else {
                $parsedRev = 0L
                if ([long]::TryParse($rawRev, [ref]$parsedRev)) {
                    $revNum = [int][Math]::Min([Math]::Max($parsedRev, 0L), 65534L)
                }
            }
        }
        $maj = [Math]::Min([int]$verMatch.Groups[1].Value, 65534)
        $min = [Math]::Min([int]$verMatch.Groups[2].Value, 65534)
        $pat = [Math]::Min([int]$verMatch.Groups[3].Value, 65534)
        "$maj.$min.$pat.$revNum"
    } else {
        "1.0.0.0"
    }

    # 3. Publish single-file self-contained win-x64 executable with zero PDBs and deterministic PathMap
    $dotnetArgs = @(
        "publish", (Join-Path $projCopyDir "Housekeeping.Installer.csproj"), "-c", "Release", "-r", "win-x64", "-o", $publishDir,
        "/p:InstallerVersion=$Version", "/p:FileVersion=$fileVer", "/p:InstallerPayloadZip=$payloadZip",
        "/p:InstallerMetadataJson=$metaJsonPath", "/p:InstallerIconPath=$iconDst", "/nologo"
    )
    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    $compiledExe = Join-Path $publishDir "Housekeeping.Installer.exe"
    if (-not (Test-Path -LiteralPath $compiledExe)) { throw "Expected single-file output missing: $compiledExe" }

    # 4. Copy into Installers\ while retaining all historical versioned .exe installers side-by-side
    $outExeName = "Housekeeping-Setup-v$Version-win-x64.exe"
    $finalExePath = Join-Path $installersDir $outExeName
    Copy-Item -LiteralPath $compiledExe -Destination $finalExePath -Force

    # 5. Cryptographically sign with Authenticode SHA-256 (preferring StreamItAll Code Signing)
    $cert = if ($CertThumbprint) { Get-Item "Cert:\CurrentUser\My\$CertThumbprint" -ErrorAction SilentlyContinue } else { $null }
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
    if ($signtool -and $signtool.Count -gt 0) {
        & $signtool[0].FullName sign /fd SHA256 /sha1 $cert.Thumbprint /q $finalExePath
        if ($LASTEXITCODE -ne 0) { throw "signtool.exe failed with exit code $LASTEXITCODE" }
    } else {
        Set-AuthenticodeSignature -LiteralPath $finalExePath -Certificate $cert -HashAlgorithm SHA256 | Out-Null
    }
    Write-Host "Built & signed: $finalExePath ($([Math]::Round((Get-Item $finalExePath).Length / 1MB, 2)) MB) | Signer: $($cert.Subject)"

    # 5b. Mirror latest installer to central D:\Projects\Installers\ and remove superseded Housekeeping copies
    $centralInstallersDir = "D:\Projects\Installers"
    New-Item -ItemType Directory -Path $centralInstallersDir -Force | Out-Null
    Get-ChildItem -LiteralPath $centralInstallersDir -Filter "Housekeeping-Setup-*.exe" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne $outExeName } | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
    $centralTarget = Join-Path $centralInstallersDir $outExeName
    Copy-Item -LiteralPath $finalExePath -Destination $centralTarget -Force
    Write-Host "Mirrored latest installer to central hub: $centralTarget"

    # 6. Run global 4-pillar audit_installer.ps1 gate
    $auditorScript = "%USERPROFILE%\.gemini\config\skills\release-packaging\scripts\audit_installer.ps1"
    if (Test-Path -LiteralPath $auditorScript) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $auditorScript -InstallerPath $installersDir -StagingDir $buildSource -ExpectedVersion $Version
        if ($LASTEXITCODE -ne 0) { throw "audit_installer.ps1 failed with exit code $LASTEXITCODE" }
    }

    # 7. Package self-contained 6-file Releases\Latest\ (and Releases\v$Version\ for full SemVer) bundle & 7z archives per Standard 03 §1.8 & §6
    $releasesDir = Join-Path $projectRoot "Releases"
    $latestDir = Join-Path $releasesDir "Latest"
    if (Test-Path -LiteralPath $latestDir) {
        Remove-Item -LiteralPath $latestDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $latestDir | Out-Null

    function ConvertTo-SanitizedReleaseMarkdown([string]$rawText) {
        if ([string]::IsNullOrEmpty($rawText)) { return "" }
        $clean = [regex]::Replace($rawText, '\[([^\]]+)\]\(file:///[^)]+\)', '$1')
        $clean = [regex]::Replace($clean, 'file:///[^\s\)]+', '')
        $clean = [regex]::Replace($clean, '(?i)[CD]:[\\/]+Projects[\\/]+[^\s`"''\)]*', '<Workspace>')
        return $clean
    }

    # 1/6: Copy signed installer to Latest
    Copy-Item -LiteralPath $finalExePath -Destination (Join-Path $latestDir $outExeName) -Force

    # 2/6: Generate end-user README.md from Dev\docs\USER_GUIDE.md (fallback to root README.md)
    $userGuidePath = Join-Path $devRoot "docs\USER_GUIDE.md"
    $rootReadme = Join-Path $projectRoot "README.md"
    $rawGuide = if (Test-Path -LiteralPath $userGuidePath) {
        Get-Content -LiteralPath $userGuidePath -Raw -Encoding UTF8
    } elseif (Test-Path -LiteralPath $rootReadme) {
        Get-Content -LiteralPath $rootReadme -Raw -Encoding UTF8
    } else {
        "# Housekeeping v$Version`r`n"
    }
    $rawGuide = $rawGuide -replace 'v1\.0\.0-rc\.11-hf\.2', "v$Version"
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText((Join-Path $latestDir "README.md"), (ConvertTo-SanitizedReleaseMarkdown $rawGuide), $utf8NoBom)

    # 3/6 & 4/6: Copy sanitized FEATURES.md and CHANGELOG.md
    $rootFeatures = Join-Path $projectRoot "FEATURES.md"
    $rootChangelog = Join-Path $projectRoot "CHANGELOG.md"
    if (Test-Path -LiteralPath $rootFeatures) {
        $featRaw = Get-Content -LiteralPath $rootFeatures -Raw -Encoding UTF8
        [System.IO.File]::WriteAllText((Join-Path $latestDir "FEATURES.md"), (ConvertTo-SanitizedReleaseMarkdown $featRaw), $utf8NoBom)
    }
    $changelogRaw = ""
    if (Test-Path -LiteralPath $rootChangelog) {
        $changelogRaw = Get-Content -LiteralPath $rootChangelog -Raw -Encoding UTF8
        [System.IO.File]::WriteAllText((Join-Path $latestDir "CHANGELOG.md"), (ConvertTo-SanitizedReleaseMarkdown $changelogRaw), $utf8NoBom)
    }

    # 5/6: Dynamically extract RELEASE_NOTES.md from CHANGELOG.md section ## [$Version]
    $relNotesPath = Join-Path $latestDir "RELEASE_NOTES.md"
    $escapedVer = [regex]::Escape($Version)
    $secMatch = [regex]::Match($changelogRaw, "(?ms)^##\s+\[$escapedVer\][^\r\n]*\r?\n(.*?)(?=^##\s+\[|\z)")
    $notesBody = if ($secMatch.Success -and -not [string]::IsNullOrWhiteSpace($secMatch.Groups[1].Value)) {
        $secMatch.Groups[1].Value.Trim().TrimEnd('-').Trim()
    } else {
        "Automated release bundle for Housekeeping v$Version. See CHANGELOG.md for full details."
    }
    $notesContent = "# Housekeeping v$Version Release Notes`r`n`r`n" + (ConvertTo-SanitizedReleaseMarkdown $notesBody) + "`r`n"
    [System.IO.File]::WriteAllText($relNotesPath, $notesContent, $utf8NoBom)

    # 6/6: Generate SHA256SUMS.txt for all 5 payload files
    $shaLines = @()
    Get-ChildItem -LiteralPath $latestDir -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" } | Sort-Object Name | ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $shaLines += "$hash  $($_.Name)"
    }
    [System.IO.File]::WriteAllText((Join-Path $latestDir "SHA256SUMS.txt"), ($shaLines -join "`r`n") + "`r`n", $utf8NoBom)
    Write-Host "Packaged Releases\Latest bundle (6 files)."

    # Standard 03 §1.8: Retain unpacked Releases\v$Version\ mirror for full MAJOR.MINOR.PATCH releases
    if ($Version -match '^\d+\.\d+\.\d+$') {
        $unpackedVersionDir = Join-Path $releasesDir "v$Version"
        if (Test-Path -LiteralPath $unpackedVersionDir) {
            Remove-Item -LiteralPath $unpackedVersionDir -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $unpackedVersionDir | Out-Null
        Get-ChildItem -LiteralPath $latestDir -File | Copy-Item -Destination $unpackedVersionDir -Force
        Write-Host "Mirrored unpacked full-release bundle to Releases\v$Version\ (6 files)."
    }

    # Create Latest.7z and versioned .7z archives using 7z
    $sevenZipExe = "C:\Program Files\7-Zip\7z.exe"
    if (Test-Path -LiteralPath $sevenZipExe) {
        $latestArchive = Join-Path $releasesDir "Latest.7z"
        $versionArchive = Join-Path $releasesDir "v$Version.7z"
        if (Test-Path -LiteralPath $latestArchive) { Remove-Item -LiteralPath $latestArchive -Force }
        if (Test-Path -LiteralPath $versionArchive) { Remove-Item -LiteralPath $versionArchive -Force }

        & $sevenZipExe a -t7z -mx=9 $latestArchive "$latestDir\*" | Out-Null
        Copy-Item -LiteralPath $latestArchive -Destination $versionArchive -Force
        Write-Host "Created Releases\Latest.7z and Releases\v$Version.7z"
    }
} finally {
    if (Test-Path -LiteralPath $tempStage) {
        Remove-Item -LiteralPath $tempStage -Recurse -Force -ErrorAction SilentlyContinue
    }
}
