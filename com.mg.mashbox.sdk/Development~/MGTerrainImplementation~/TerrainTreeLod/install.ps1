$ErrorActionPreference = 'Stop'
$packageRoot = 'D:\mashbox-sdk\com.mg.mashbox.sdk'
$files = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'files.json') -Raw | ConvertFrom-Json
$installed = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installed-hashes.json') -Raw | ConvertFrom-Json
foreach ($relative in $files) {
    $destination = Join-Path $packageRoot $relative
    $original = Join-Path (Join-Path $PSScriptRoot 'original') $relative
    $staged = Join-Path (Join-Path $PSScriptRoot 'staged') $relative
    $currentHash = (Get-FileHash -LiteralPath $destination).Hash
    $previousHash = ($installed | Where-Object { $_.Path -eq $relative }).Hash
    if ($currentHash -ne (Get-FileHash -LiteralPath $original).Hash -and $currentHash -ne (Get-FileHash -LiteralPath $staged).Hash -and $currentHash -ne $previousHash) {
        throw "Source changed since preparation: $relative"
    }
}
foreach ($relative in $files) {
    Copy-Item -LiteralPath (Join-Path (Join-Path $PSScriptRoot 'staged') $relative) -Destination (Join-Path $packageRoot $relative)
}
Write-Output "Installed $($files.Count) reviewed tree LOD files."
$files | ForEach-Object { [pscustomobject]@{ Path = $_; Hash = (Get-FileHash -LiteralPath (Join-Path $packageRoot $_)).Hash } } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'installed-hashes.json')
