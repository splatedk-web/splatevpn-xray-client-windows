param([switch]$SkipBuild, [switch]$ForceZip)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$version = '2.12.5'
$buildDir = Join-Path $projectRoot "release-v$version"
$outputDir = Join-Path $projectRoot 'installer_output'
$runtimeFolder = Join-Path $projectRoot 'runtime/WebView2'
$runtimeExecutable = Join-Path $runtimeFolder 'msedgewebview2.exe'
if (!(Test-Path -LiteralPath $runtimeExecutable)) {
    $runtimeCab = Join-Path $projectRoot 'dependencies/Microsoft.WebView2.FixedVersionRuntime.155.0.4283.45.x64.cab'
    $runtimeStage = Join-Path $projectRoot 'dependencies/fixed-runtime-155.0.4283.45'
    New-Item -ItemType Directory -Force -Path (Split-Path $runtimeCab),$runtimeStage,$runtimeFolder | Out-Null
    if (!(Test-Path -LiteralPath $runtimeCab)) {
        Invoke-WebRequest -Uri 'https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/01b0fbd9-6ea3-4e12-818a-a71f73f3855b/Microsoft.WebView2.FixedVersionRuntime.155.0.4283.45.x64.cab' -OutFile $runtimeCab -UseBasicParsing
    }
    & expand.exe $runtimeCab '-F:*' $runtimeStage | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Fixed WebView2 extraction failed' }
    Copy-Item -Path (Join-Path $runtimeStage 'Microsoft.WebView2.FixedVersionRuntime.155.0.4283.45.x64/*') -Destination $runtimeFolder -Recurse -Force
}
$runtimeSignature = Get-AuthenticodeSignature -LiteralPath $runtimeExecutable
if ($runtimeSignature.Status -ne 'Valid' -or $runtimeSignature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw 'Bundled WebView2 must have a valid Microsoft signature'
}
if (!$SkipBuild) {
    dotnet publish (Join-Path $projectRoot 'SplateClientDesktop.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $buildDir
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
}
foreach ($relative in @('runtime/WebView2/msedgewebview2.exe','runtime/WebView2/msedge.dll','runtime/WebView2/icudtl.dat','SplateClient.exe','core/xray.exe','core/tun2socks.exe','core/wintun.dll','core/geoip.dat','core/geosite.dat','wwwroot/index.html','wwwroot/js/app.js','wwwroot/js/updates.js','wwwroot/js/themes.js','wwwroot/js/nodes.js','wwwroot/js/features.js','wwwroot/js/palettes.js','wwwroot/js/palettes.json','wwwroot/assets/themes/pony.png','wwwroot/assets/themes/winx.png','wwwroot/assets/themes/winx-kiko.png','wwwroot/css/style.css','Assets/presets/preset_white_cidr.txt','Assets/presets/preset_harvester.txt','Assets/presets/preset_white_universal.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $buildDir $relative))) { throw "Package is incomplete: $relative" }
}
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
foreach ($relative in @('wwwroot/js/smart-tools.js','wwwroot/js/objects.js','wwwroot/js/objects-data.js','wwwroot/js/objects-data.json','Assets/filters/adguard-dns-source.bin','Assets/filters/additional.json','Assets/filters/ai-domains.json','Assets/licenses/AdGuard-DNS-filter-LICENSE','Assets/licenses/AdGuard-DNS-filter-NOTICE','wwwroot/assets/themes/object-sakura.png','wwwroot/assets/themes/object-ocean.png','wwwroot/assets/themes/object-cyberpunk.png','wwwroot/assets/themes/object-sunset.png','wwwroot/assets/themes/object-aurora.png','wwwroot/assets/themes/object-red.png')) {
    if (!(Test-Path -LiteralPath (Join-Path $buildDir $relative))) { throw "Package is incomplete: $relative" }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'),(Join-Path $projectRoot 'PORT_PARITY.md') -Destination $buildDir -Force
$zip = Join-Path $outputDir "SplateVPN_Portable_v$version.zip"
Compress-Archive -Path (Join-Path $buildDir '*') -DestinationPath $zip -Force
if (!$ForceZip) {
    $iscc = @('C:\Program Files (x86)\Inno Setup 6\ISCC.exe','C:\Program Files\Inno Setup 6\ISCC.exe') | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($iscc) { & $iscc (Join-Path $projectRoot 'installer.iss'); if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' } }
    else { Write-Host 'Inno Setup not installed; portable ZIP is ready.' }
}
Write-Host "Ready: $zip"
