$ErrorActionPreference = 'Stop'
$treePackage = 'D:/mashbox-sdk/com.mg.mashbox.sdk'
$treeHashes = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'hashes.json') -Raw | ConvertFrom-Json
foreach ($entry in $treeHashes.PSObject.Properties) {
    $target = Join-Path $treePackage $entry.Name
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.Value) { throw "Source changed: $($entry.Name)" }
}
foreach ($entry in $treeHashes.PSObject.Properties) {
    Copy-Item -LiteralPath (Join-Path (Join-Path $PSScriptRoot 'staged') $entry.Name) -Destination (Join-Path $treePackage $entry.Name)
}
Write-Output 'Installed verified tree distance control files.'
