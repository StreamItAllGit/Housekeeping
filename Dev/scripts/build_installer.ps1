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

    # 4. Copy scripts and manifests (version.txt inside app\ for Clean Release Root parity)
    Copy-Item -LiteralPath (Join-Path $devRoot "version.txt") -Destination (Join-Path $appDir "version.txt") -Force
    if (Test-Path -LiteralPath (Join-Path $devRoot "mother.manifest.json")) {
        Copy-Item -LiteralPath (Join-Path $devRoot "mother.manifest.json") -Destination (Join-Path $buildSource "mother.manifest.json") -Force
    }

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

    # Copy C# installer source + icon into %TEMP%\hk_inst_*\src so MSBuild never embeds D:\Projects\... paths
    Copy-Item -LiteralPath (Join-Path $devRoot "Housekeeping.Installer\Housekeeping.Installer.csproj") -Destination (Join-Path $projCopyDir "Housekeeping.Installer.csproj") -Force
    Copy-Item -LiteralPath (Join-Path $devRoot "Housekeeping.Installer\Program.cs") -Destination (Join-Path $projCopyDir "Program.cs") -Force
    $wizardForm = Join-Path $devRoot "Housekeeping.Installer\InstallerWizardForm.cs"
    if (Test-Path -LiteralPath $wizardForm) {
        Copy-Item -LiteralPath $wizardForm -Destination (Join-Path $projCopyDir "InstallerWizardForm.cs") -Force
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
        "publish",
        (Join-Path $projCopyDir "Housekeeping.Installer.csproj"),
        "-c", "Release",
        "-r", "win-x64",
        "-o", $publishDir,
        "/p:InstallerVersion=$Version",
        "/p:FileVersion=$fileVer",
        "/p:InstallerPayloadZip=$payloadZip",
        "/p:InstallerMetadataJson=$metaJsonPath",
        "/p:InstallerIconPath=$iconDst",
        "/nologo"
    )
    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    $compiledExe = Join-Path $publishDir "Housekeeping.Installer.exe"
    if (-not (Test-Path -LiteralPath $compiledExe)) {
        throw "Expected single-file output missing: $compiledExe"
    }

    # 4. Copy into Installers\ while retaining all historical versioned .exe installers side-by-side
    $outExeName = "Housekeeping-Setup-v$Version-win-x64.exe"
    $finalExePath = Join-Path $installersDir $outExeName
    Copy-Item -LiteralPath $compiledExe -Destination $finalExePath -Force

    # 5. Cryptographically sign with Authenticode SHA-256 (preferring StreamItAll Code Signing)
    $cert = $null
    if ($CertThumbprint) {
        $cert = Get-Item "Cert:\CurrentUser\My\$CertThumbprint" -ErrorAction SilentlyContinue
    }
    if (-not $cert) {
        $cert = @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } |
            Sort-Object @{Expression = { if ($_.Subject -match "StreamItAll") { 0 } else { 1 } }; Ascending = $true}, @{Expression = "NotAfter"; Descending = $true} |
            Select-Object -First 1)
        if ($cert -is [array]) { $cert = if ($cert.Count -gt 0) { $cert[0] } else { $null } }
    }
    if (-not $cert) {
        throw "No active Code Signing certificate with private key found in Cert:\CurrentUser\My."
    }

    $signtool = @(Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1)
    if ($signtool -and $signtool.Count -gt 0) {
        & $signtool[0].FullName sign /fd SHA256 /sha1 $cert.Thumbprint /q $finalExePath
        if ($LASTEXITCODE -ne 0) {
            throw "signtool.exe failed with exit code $LASTEXITCODE"
        }
    } else {
        Set-AuthenticodeSignature -LiteralPath $finalExePath -Certificate $cert -HashAlgorithm SHA256 | Out-Null
    }

    Write-Host "Built & signed: $finalExePath ($([Math]::Round((Get-Item $finalExePath).Length / 1MB, 2)) MB)"
    Write-Host "Signer: $($cert.Subject) ($($cert.Thumbprint))"

    # 5b. Mirror latest installer to central D:\Projects\Installers\ and remove superseded Housekeeping copies
    $centralInstallersDir = "D:\Projects\Installers"
    if (-not (Test-Path -LiteralPath $centralInstallersDir)) {
        New-Item -ItemType Directory -Path $centralInstallersDir -Force | Out-Null
    }
    Get-ChildItem -LiteralPath $centralInstallersDir -Filter "Housekeeping-Setup-*.exe" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne $outExeName } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
    $centralTarget = Join-Path $centralInstallersDir $outExeName
    Copy-Item -LiteralPath $finalExePath -Destination $centralTarget -Force
    Write-Host "Mirrored latest installer to central hub: $centralTarget"

    # 6. Run global 4-pillar audit_installer.ps1 gate
    $auditorScript = "%USERPROFILE%\.gemini\config\skills\release-packaging\scripts\audit_installer.ps1"
    if (Test-Path -LiteralPath $auditorScript) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $auditorScript -InstallerPath $installersDir -StagingDir $buildSource -ExpectedVersion $Version
        if ($LASTEXITCODE -ne 0) {
            throw "audit_installer.ps1 failed with exit code $LASTEXITCODE"
        }
    }

    # 7. Package self-contained 6-file Releases\Latest\ distribution bundle & 7z archives per Standard 03 §6
    $releasesDir = Join-Path $projectRoot "Releases"
    $latestDir = Join-Path $releasesDir "Latest"
    New-Item -ItemType Directory -Force -Path $latestDir | Out-Null

    # Clean existing installer in Latest
    Get-ChildItem -LiteralPath $latestDir -Filter "Housekeeping-Setup-*.exe" -File -ErrorAction SilentlyContinue | Remove-Item -Force

    # Copy new installer to Latest
    Copy-Item -LiteralPath $finalExePath -Destination (Join-Path $latestDir $outExeName) -Force

    # Copy Root Quartet docs (README, FEATURES, CHANGELOG)
    $rootReadme = Join-Path $projectRoot "README.md"
    $rootFeatures = Join-Path $projectRoot "FEATURES.md"
    $rootChangelog = Join-Path $projectRoot "CHANGELOG.md"
    if (Test-Path -LiteralPath $rootReadme) { Copy-Item -LiteralPath $rootReadme -Destination $latestDir -Force }
    if (Test-Path -LiteralPath $rootFeatures) { Copy-Item -LiteralPath $rootFeatures -Destination $latestDir -Force }
    if (Test-Path -LiteralPath $rootChangelog) { Copy-Item -LiteralPath $rootChangelog -Destination $latestDir -Force }

    # Generate or copy RELEASE_NOTES.md
    $relNotesPath = Join-Path $latestDir "RELEASE_NOTES.md"
    $notesContent = @"
# Housekeeping v$Version Release Notes

### Highlights
- Single-Surface WinForms GUI installer with clean release root (app\, scripts\, scripts\interactive\).
- Greenroom-style Amber (#f59e0b) left sidebar rail with 220px/56px collapse toggle and pinned drive space.
- Global Ctrl+K Omni-Search and ? keyboard shortcuts modal.
- System Doctor in Settings with one-click .7z backup and diagnostic bundle export.
- Standard 15 logging grammar and 10MB x 5 log rotation.
"@
    Set-Content -LiteralPath $relNotesPath -Value $notesContent -Encoding UTF8

    # Generate SHA256SUMS.txt for all 5 payload files
    $shaLines = @()
    Get-ChildItem -LiteralPath $latestDir -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" } | ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $shaLines += "$hash  $($_.Name)"
    }
    Set-Content -LiteralPath (Join-Path $latestDir "SHA256SUMS.txt") -Value ($shaLines -join "`r`n") -Encoding UTF8
    Write-Host "Packaged Releases\Latest bundle (6 files)."

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
