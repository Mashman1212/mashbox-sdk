$ErrorActionPreference = 'Stop'
$treeFiles=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'hashes.json') -Raw | ConvertFrom-Json).PSObject.Properties.Name
foreach($treeAssembly in @('MashBoxSDK','Assembly-CSharp-Editor')) {
 $treeRsp=Get-Content -LiteralPath ('D:/MappyX/Library/Bee/artifacts/500b0aE.dag/'+$treeAssembly+'.rsp') -Raw
 foreach($treeFile in $treeFiles){$treeRsp=$treeRsp.Replace('D:/mashbox-sdk/com.mg.mashbox.sdk/'+$treeFile,($PSScriptRoot.Replace('\','/')+'/staged/'+$treeFile))}
 $treeRsp=$treeRsp -replace '(?m)^-out:.*$',('-out:"'+$env:TEMP+'/'+$treeAssembly+'.TreeDistance.dll"')
 $treeRsp=$treeRsp -replace '(?m)^-refout:.*$',('-refout:"'+$env:TEMP+'/'+$treeAssembly+'.TreeDistance.ref.dll"')
 if($treeAssembly -ne 'MashBoxSDK'){$treeRsp=$treeRsp -replace '-r:"[^"\r\n]*/MashBoxSDK(?:\.ref)?\.dll"',('-r:"'+$env:TEMP+'/MashBoxSDK.TreeDistance.ref.dll"')}
 $treeResponse=Join-Path $env:TEMP ($treeAssembly+'.TreeDistance.rsp')
 Set-Content -LiteralPath $treeResponse -Value $treeRsp
 $treeLog=Join-Path $PSScriptRoot ($treeAssembly+'.log')
 & 'C:/Program Files/Unity/Hub/Editor/6000.4.12f1/Editor/Data/netcorerun/netcorerun.exe' 'C:/Program Files/Unity/Hub/Editor/6000.4.12f1/Editor/Data/DotNetSdkRoslyn/csc.dll' "@$treeResponse" *> $treeLog
 if($LASTEXITCODE -ne 0){Get-Content $treeLog;throw 'Compilation failed'}
 Write-Output ($treeAssembly+': compilation passed')
}
