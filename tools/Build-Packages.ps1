#requires -Version 5.1
<#
.SYNOPSIS
Build both OverlayHUD distributions with a verified current desktop bundle.
.DESCRIPTION
Builds Release with NativeOnly=false/true and isolated intermediate/output paths.
Writes -Full.zip and -Native.zip to the repository root. Never writes the legacy
unsuffixed archive. Existing output archives require -Force. Staging and build
outputs are retained under overlay-hud/dist/packages/<run-id> for inspection.
.PARAMETER BundlePath
Existing OverlayHUD_app.zip; defaults to overlay-hud/package-flat/OverlayHUD_app.zip.
Relative custom paths are resolved from the caller's working directory.
.PARAMETER RebuildDesktop
Run npm run package and create a fresh desktop ZIP in staging before building plugins.
Cannot be combined with BundlePath. Existing input bundles are not overwritten.
.PARAMETER Force
Allow replacement of the two suffixed output archives only.
.EXAMPLE
powershell -NoProfile -File tools/Build-Packages.ps1
.EXAMPLE
pwsh -NoProfile -File tools/Build-Packages.ps1 -Force
#>
[CmdletBinding()]
param(
    [string] $BundlePath,
    [switch] $RebuildDesktop,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Join-Path $repoRoot 'overlay-hud'
$project = Join-Path $projectRoot 'OverlayHUD.csproj'
$fullTemplate = Join-Path $projectRoot 'package-flat'
$nativeTemplate = Join-Path $projectRoot 'package-native'
if ($RebuildDesktop -and -not [string]::IsNullOrWhiteSpace($BundlePath)) {
    throw 'Use either -RebuildDesktop or -BundlePath, not both.'
}
if ([string]::IsNullOrWhiteSpace($BundlePath)) {
    $BundlePath = Join-Path $fullTemplate 'OverlayHUD_app.zip'
}

function Assert-File([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file not found: $Path"
    }
    if ((Get-Item -LiteralPath $Path).Length -eq 0) {
        throw "Required file is empty: $Path"
    }
}

function Assert-OutputAvailable([string] $Path) {
    if (Test-Path -LiteralPath $Path) {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
            throw "Output path is not a file: $Path"
        }
        if (-not $Force) {
            throw "Output already exists: $Path. Use -Force to replace the suffixed archives."
        }
    }
}

function New-FlatArchive([string] $Stage, [string] $Destination, [string[]] $Names) {
    # Explicit entries avoid directory records, wildcard leakage, and a leading ./.
    $archive = [System.IO.Compression.ZipFile]::Open(
        $Destination, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $Names) {
            $source = Join-Path $Stage $name
            Assert-File $source
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $source, $name, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally {
        $archive.Dispose()
    }
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Destination)
    try {
        $actual = @($archive.Entries | ForEach-Object { $_.FullName })
        $difference = @(Compare-Object -ReferenceObject $Names -DifferenceObject $actual -CaseSensitive)
        if ($actual.Count -ne $Names.Count -or $difference.Count -ne 0) {
            throw "Unexpected ZIP entries in $Destination : $($actual -join ', ')"
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-BundleSource([System.IO.Compression.ZipArchive] $Archive, [string] $EntryName, [string] $SourcePath) {
    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "Desktop bundle is missing $EntryName. Run with -RebuildDesktop."
    }
    $stream = $entry.Open()
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bundledHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '')
    }
    finally {
        $stream.Dispose()
        $sha.Dispose()
    }
    $sourceHash = (Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash
    if ($bundledHash -cne $sourceHash) {
        throw "Desktop bundle contains stale $EntryName. Run with -RebuildDesktop."
    }
}

Assert-File $project
Assert-File (Join-Path $fullTemplate 'manifest.json')
Assert-File (Join-Path $nativeTemplate 'manifest.json')
$fullManifest = Get-Content -LiteralPath (Join-Path $fullTemplate 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$nativeManifest = Get-Content -LiteralPath (Join-Path $nativeTemplate 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$version = [string] $fullManifest.version_number
if ($version -notmatch '^\d+\.\d+\.\d+$' -or $fullManifest.name -cne 'OverlayHUD' -or $fullManifest.author -cne 'EgorSalad') {
    throw 'Unexpected Full manifest identity or version.'
}
foreach ($field in @('name', 'author', 'version_number', 'website_url')) {
    if ($fullManifest.$field -cne $nativeManifest.$field) {
        throw "Full and Native manifest $field must match."
    }
}
if (@(Compare-Object -ReferenceObject @($fullManifest.dependencies) -DifferenceObject @($nativeManifest.dependencies) -CaseSensitive).Count -ne 0) {
    throw 'Full and Native manifest dependencies must match.'
}
[xml] $projectXml = Get-Content -LiteralPath $project -Raw
$projectVersion = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($projectVersion -cne $version) {
    throw "Project version $projectVersion differs from manifest version $version."
}

$fullOutput = Join-Path $repoRoot "EgorSalad-OverlayHUD-$version-Full.zip"
$nativeOutput = Join-Path $repoRoot "EgorSalad-OverlayHUD-$version-Native.zip"
Assert-OutputAvailable $fullOutput
Assert-OutputAvailable $nativeOutput

foreach ($template in @($fullTemplate, $nativeTemplate)) {
    foreach ($name in @('README.md', 'manifest.json', 'THIRD_PARTY_NOTICES.md')) {
        Assert-File (Join-Path $template $name)
    }
}
Assert-File (Join-Path $fullTemplate 'icon.png')
Assert-File (Join-Path $repoRoot 'LICENSE')
$runId = [Guid]::NewGuid().ToString('N')
$workRoot = Join-Path $projectRoot "dist/packages/$runId"
[void][System.IO.Directory]::CreateDirectory($workRoot)
Write-Host "Build and staging directory: $workRoot"
if ($RebuildDesktop) {
    Push-Location (Join-Path $repoRoot 'game-overlay')
    try {
        & npm.cmd run package | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Electron packaging failed.' }
    }
    finally {
        Pop-Location
    }
    $desktopDirectory = Join-Path $repoRoot 'game-overlay/dist/OverlayHUD-win32-x64'
    Assert-File (Join-Path $desktopDirectory 'OverlayHUD.exe')
    $BundlePath = Join-Path $workRoot 'OverlayHUD_app.zip'
    # .NET Framework CreateFromDirectory writes backslashes on Windows; ZIP paths must use '/'.
    $desktopNames = @(Get-ChildItem -LiteralPath $desktopDirectory -Recurse -File | ForEach-Object {
        $_.FullName.Substring($desktopDirectory.Length + 1).Replace('\', '/')
    } | Sort-Object)
    New-FlatArchive -Stage $desktopDirectory -Destination $BundlePath -Names $desktopNames
}
Assert-File $BundlePath
$BundlePath = (Resolve-Path -LiteralPath $BundlePath).ProviderPath
foreach ($name in @('README.md', 'manifest.json', 'THIRD_PARTY_NOTICES.md')) {
    $text = Get-Content -LiteralPath (Join-Path $nativeTemplate $name) -Raw -Encoding UTF8
    if ($text -match '(?i)Electron|Node\.js|Chromium|OverlayHUD_app|OverlayHUD\.exe|desktop') {
        throw "Native template contains desktop-only content: $name"
    }
}
$bundle = [System.IO.Compression.ZipFile]::OpenRead($BundlePath)
try {
    if ($null -eq $bundle.GetEntry('OverlayHUD.exe')) {
        throw 'The app bundle must contain OverlayHUD.exe at its root.'
    }
    $packageEntry = $bundle.GetEntry('resources/app/package.json')
    if ($null -eq $packageEntry) { throw 'Desktop bundle has no package.json. Run with -RebuildDesktop.' }
    $reader = [IO.StreamReader]::new($packageEntry.Open())
    try { $desktopPackage = $reader.ReadToEnd() | ConvertFrom-Json }
    finally { $reader.Dispose() }
    if ($desktopPackage.version -cne $version) {
        throw "Desktop bundle version $($desktopPackage.version) differs from release $version. Run with -RebuildDesktop."
    }
    foreach ($name in @('server.js', 'overlay.html', 'overlay-state.js', 'overlay.css')) {
        Assert-BundleSource $bundle "resources/$name" (Join-Path $repoRoot $name)
    }
    foreach ($name in @('main.js', 'preload.js', 'game-lifetime.js', 'overlay-config.json')) {
        Assert-BundleSource $bundle "resources/app/$name" (Join-Path $repoRoot "game-overlay/$name")
    }
    $assetsRoot = Join-Path $repoRoot 'assets'
    foreach ($asset in Get-ChildItem -LiteralPath $assetsRoot -Recurse -File) {
        $relative = $asset.FullName.Substring($assetsRoot.Length + 1).Replace('\', '/')
        Assert-BundleSource $bundle "resources/assets/$relative" $asset.FullName
    }
    Write-Host 'Desktop bundle version and source hashes verified (including localization assets).'
}
finally {
    $bundle.Dispose()
}
[void](Get-Command dotnet -ErrorAction Stop)

$commonNames = @('icon.png', 'manifest.json', 'OverlayHUD.dll', 'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')
$variants = @(
    [pscustomobject]@{
        Name = 'Full'; NativeOnly = 'false'; Template = $fullTemplate
        Entries = @('icon.png', 'manifest.json', 'OverlayHUD.dll', 'OverlayHUD_app.zip', 'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')
        Output = $fullOutput
    },
    [pscustomobject]@{
        Name = 'NativeOnly'; NativeOnly = 'true'; Template = $nativeTemplate
        Entries = $commonNames; Output = $nativeOutput
    }
)

foreach ($variant in $variants) {
    $outputDir = Join-Path $workRoot "build/$($variant.Name)"
    $stage = Join-Path $workRoot "stage/$($variant.Name)"
    [void][System.IO.Directory]::CreateDirectory($stage)
    # Resolve all paths from the repository, not the caller's working directory.
    # The project must exclude all obj/bin/dist trees from default Compile globs.
    $intermediate = (Join-Path $projectRoot "obj/$($variant.Name)/$runId/").Replace('\', '/')
    $outputProperty = $outputDir.Replace('\', '/') + '/'
    $buildArguments = @(
        'build', $project, '-c', 'Release', '--nologo',
        "-p:NativeOnly=$($variant.NativeOnly)",
        "-p:BaseIntermediateOutputPath=$intermediate",
        "-p:MSBuildProjectExtensionsPath=$intermediate",
        "-p:OutputPath=$outputProperty",
        '-p:AppendTargetFrameworkToOutputPath=false',
        '-p:AppendRuntimeIdentifierToOutputPath=false'
    )
    Write-Host "Building $($variant.Name) (NativeOnly=$($variant.NativeOnly))..."
    & dotnet @buildArguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for $($variant.Name) with exit code $LASTEXITCODE. Staging retained: $workRoot"
    }
    $dll = Join-Path $outputDir 'OverlayHUD.dll'
    Assert-File $dll
    Copy-Item -LiteralPath $dll -Destination (Join-Path $stage 'OverlayHUD.dll')
    Copy-Item -LiteralPath (Join-Path $fullTemplate 'icon.png') -Destination (Join-Path $stage 'icon.png')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $stage 'LICENSE')
    foreach ($name in @('README.md', 'manifest.json', 'THIRD_PARTY_NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $variant.Template $name) -Destination (Join-Path $stage $name)
    }
    if ($variant.NativeOnly -eq 'false') {
        Copy-Item -LiteralPath $BundlePath -Destination (Join-Path $stage 'OverlayHUD_app.zip')
    }
    $stagedArchive = Join-Path $workRoot ([System.IO.Path]::GetFileName($variant.Output))
    New-FlatArchive -Stage $stage -Destination $stagedArchive -Names $variant.Entries
    Write-Host "$($variant.Name) DLL SHA256: $((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash)"
}

# Publish only after both builds and both ZIP entry checks have succeeded.
Assert-OutputAvailable $fullOutput
Assert-OutputAvailable $nativeOutput
foreach ($variant in $variants) {
    $stagedArchive = Join-Path $workRoot ([System.IO.Path]::GetFileName($variant.Output))
    # File.Copy also refuses a destination created after preflight unless -Force was given.
    [System.IO.File]::Copy($stagedArchive, $variant.Output, $Force.IsPresent)
    Write-Host "Created: $($variant.Output)"
    Write-Host "ZIP SHA256: $((Get-FileHash -LiteralPath $variant.Output -Algorithm SHA256).Hash)"
}
