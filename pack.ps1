# Packages the mod in Thunderstore format, so r2modman can import it as a "local mod".
#
# Without this the DLL still loads (BepInEx scans the plugins folder), but r2modman
# neither lists it nor lets you enable/disable it -- it only knows what's in mods.yml.
# The same zip also works to hand to someone else.
#
# Usage: .\pack.ps1

$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent $MyInvocation.MyCommand.Path
$dll   = Join-Path $root "bin\Release\ValheimTweaks.dll"

# The version comes from the csproj, so it can't drift from what the plugin reports in the log.
$version = ([xml](Get-Content (Join-Path $root "ValheimTweaks.csproj"))).Project.PropertyGroup.Version |
           Where-Object { $_ } | Select-Object -First 1

# Builds Release right here. Previously the script only READ bin\Release and warned
# you to build first; since the everyday command is `dotnet build` (which produces
# Debug), Release sat stale for days and several zips shipped an old DLL with a new
# manifest. The symptom was misleading: r2modman showed the new version (it reads the
# manifest) while the Configuration Manager showed the old one (it reads the DLL),
# which looks like a duplicate mod.
Write-Host "building Release..."
& dotnet build -c Release -v minimal | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Release build failed; zip not generated" }
if (-not (Test-Path $dll)) { throw "bin\Release\ValheimTweaks.dll did not appear" }

# Safety belt: checks that the packaged DLL IS the csproj version. A zip with the
# wrong version costs the other person a round of testing.
#
# Read from the PE version resource, not with [Reflection.AssemblyName]::GetAssemblyName:
# that one LOADS the file, and Windows App Control refuses to load an unsigned DLL
# ("a policy blocked this file", HRESULT 0x800711C7), which killed the packaging on a
# machine where the build itself was fine. VersionInfo only reads the header. MSBuild
# derives FileVersion from <Version>, so the check is the same one.
$dllVersion = ((Get-Item $dll).VersionInfo.FileVersion -split '\.')[0..2] -join '.'
if ($dllVersion -ne $version) {
    throw "DLL in bin\Release is $dllVersion but the csproj says $version. Zip aborted."
}

# Second belt: BepInPlugin needs a compile-time literal, so Plugin.VERSION is typed by
# hand and can drift from the csproj. When it does, r2modman and the log disagree about
# which version is running and a bug report points at the wrong code. Caught exactly
# that on 0.34.0.
$declared = (Select-String -Path (Join-Path $root "src\Plugin.cs") `
                           -Pattern 'VERSION\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value
if ($declared -ne $version) {
    throw "src\Plugin.cs declares $declared but the csproj says $version. Zip aborted."
}
Write-Host "  DLL verified: $dllVersion"

$outDir = Join-Path $root "dist"
$stage = Join-Path $outDir "stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

# --- manifest.json (Thunderstore format) ---
# description is capped at 250 characters.
$manifest = [ordered]@{
    name           = "ValheimTweaks"
    version_number = $version
    website_url    = ""
    description    = "Video and network tweaks the Valheim menu does not offer: anisotropic filtering, quality anti-aliasing, adjustable fog, exclusive fullscreen, network timeout and simulation distance."
    dependencies   = @(
        "denikson-BepInExPack_Valheim-5.4.2350",
        # The options menu (F1) comes from here; without it you can only edit the .cfg by hand.
        "shudnal-ConfigurationManager-1.1.18"
    )
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $stage "manifest.json") -Encoding UTF8

# --- icon.png: Thunderstore requires exactly 256x256 ---
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::FromArgb(255, 26, 32, 40))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 214, 158, 74)), 10
# Simple mark: a diamond (shield) with a bar, readable as a thumbnail.
$g.DrawPolygon($pen, @(
    (New-Object System.Drawing.Point(128, 34)),
    (New-Object System.Drawing.Point(214, 128)),
    (New-Object System.Drawing.Point(128, 222)),
    (New-Object System.Drawing.Point(42, 128))
))
$g.DrawLine($pen, 128, 78, 128, 178)
$g.Dispose()
$bmp.Save((Join-Path $stage "icon.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# FINDINGS.md stays in the repository only: it's investigation notes, not user-facing docs.
Copy-Item (Join-Path $root "README.md") (Join-Path $stage "README.md") -Force
Copy-Item $dll (Join-Path $stage "ValheimTweaks.dll") -Force

$zip = Join-Path $outDir "ValheimTweaks-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
Remove-Item $stage -Recurse -Force

"package: $zip"
Get-ChildItem $zip | Select-Object Name, @{n='KB';e={[math]::Round($_.Length/1KB,1)}} | Format-Table -AutoSize
