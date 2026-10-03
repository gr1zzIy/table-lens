param(
    [ValidateSet('win-x64', 'osx-arm64', 'osx-x64', 'linux-x64', 'linux-arm64')]
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $root "artifacts/publish/$Runtime"
$packages = Join-Path $root 'artifacts/packages'
if (Test-Path $destination) { Remove-Item $destination -Recurse -Force }
New-Item $packages -ItemType Directory -Force | Out-Null
& dotnet publish (Join-Path $root 'src/TableLens.Desktop/TableLens.Desktop.csproj') -c Release -r $Runtime --self-contained true -o $destination -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Copy-Item (Join-Path $root 'LICENSE') $destination
Copy-Item (Join-Path $root 'README.md') $destination
Copy-Item (Join-Path $root 'THIRD_PARTY_NOTICES.md') $destination
Copy-Item (Join-Path $root 'licenses') $destination -Recurse
Copy-Item (Join-Path $root 'docs') $destination -Recurse

if ($Runtime.StartsWith('osx-')) {
    $bundle = Join-Path $root "artifacts/publish/TableLens-$Runtime/TableLens.app"
    if (Test-Path (Split-Path $bundle -Parent)) { Remove-Item (Split-Path $bundle -Parent) -Recurse -Force }
    New-Item (Join-Path $bundle 'Contents/MacOS') -ItemType Directory -Force | Out-Null
    New-Item (Join-Path $bundle 'Contents/Resources') -ItemType Directory -Force | Out-Null
    Copy-Item "$destination/*" (Join-Path $bundle 'Contents/MacOS') -Recurse
    Copy-Item (Join-Path $PSScriptRoot 'Info.plist') (Join-Path $bundle 'Contents/Info.plist')
    Copy-Item (Join-Path $root 'src/TableLens.Desktop/Assets/tablelens.icns') (Join-Path $bundle 'Contents/Resources/tablelens.icns')
    if ($IsMacOS) {
        & chmod +x (Join-Path $bundle 'Contents/MacOS/TableLens.Desktop')
        # Ad-hoc signing makes the Apple Silicon bundle internally consistent.
        # Distribution outside development still requires Developer ID signing.
        & codesign --force --deep --sign - $bundle
        if ($LASTEXITCODE -ne 0) { throw 'Ad-hoc signing failed' }
    }
    $destination = Split-Path $bundle -Parent
}

if ($Runtime.StartsWith('win-')) {
    $archive = Join-Path $packages "TableLens-$Runtime.zip"
    Compress-Archive -Path "$destination/*" -DestinationPath $archive -Force
} else {
    $archive = Join-Path $packages "TableLens-$Runtime.tar.gz"
    & tar -czf $archive -C $destination .
    if ($LASTEXITCODE -ne 0) { throw 'Archive creation failed' }
}
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$archive.sha256", "$hash  $([IO.Path]::GetFileName($archive))`n")
Write-Output $archive
