param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [string]$ProjectDirectory = (Join-Path $env:TEMP ('MashBoxBorderProbe-' + [guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
# Use a fresh disposable project; the user's map never enters Play Mode.
if (Test-Path -LiteralPath $ProjectDirectory) { throw 'Choose a new, empty project directory.' }
$package = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
New-Item -ItemType Directory -Path "$ProjectDirectory/Assets/Editor","$ProjectDirectory/Packages","$ProjectDirectory/ProjectSettings" -Force | Out-Null
Copy-Item -LiteralPath "$package/Runtime/Maps/MBWorldBorderWall.cs" -Destination "$ProjectDirectory/Assets/MBWorldBorderWall.cs"
Copy-Item -LiteralPath "$PSScriptRoot/BorderRuntimeProbe.cs" -Destination "$ProjectDirectory/Assets/Editor/BorderRuntimeProbe.cs"
Copy-Item -LiteralPath "$PSScriptRoot/BorderRuntimeProbe.shader" -Destination "$ProjectDirectory/Assets/BorderRuntimeProbe.shader"
[IO.File]::WriteAllText("$ProjectDirectory/Packages/manifest.json", '{"dependencies":{"com.unity.modules.physics":"1.0.0"}}')
$arguments = @('-batchmode','-nographics','-projectPath',('"' + $ProjectDirectory + '"'),'-executeMethod','BorderRuntimeProbe.Begin','-logFile',('"' + "$ProjectDirectory/unity.log" + '"'))
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WorkingDirectory $ProjectDirectory -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath "$ProjectDirectory/runtime-probe.txt"
if ($process.ExitCode -ne 0) { throw "Unity validation failed; inspect $ProjectDirectory/unity.log" }
