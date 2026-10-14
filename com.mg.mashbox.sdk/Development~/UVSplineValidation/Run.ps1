param(
    [Parameter(Mandatory)][string]$Project,
    [Parameter(Mandatory)][string]$PackageCache,
    [string]$Source = (Join-Path $PSScriptRoot '../../Runtime/Maps/Spline/UVSpline.cs'),
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.12f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$Project = [System.IO.Path]::GetFullPath($Project)
New-Item -ItemType Directory -Force -Path "$Project/Assets/Runtime", "$Project/Assets/Editor", "$Project/Packages", "$Project/ProjectSettings" | Out-Null
if (!(Test-Path -LiteralPath "$Project/ProjectSettings/ProjectVersion.txt")) {
    Set-Content -LiteralPath "$Project/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 6000.4.12f1'
}
$dependencies = [ordered]@{}
foreach ($package in @('com.unity.splines', 'com.unity.mathematics', 'com.unity.settings-manager')) {
    $directory = Get-ChildItem -LiteralPath $PackageCache -Directory | Where-Object { $_.Name.StartsWith($package + '@') } | Select-Object -First 1
    if (!$directory) { throw "Missing cached package $package" }
    $dependencies[$package] = 'file:' + $directory.FullName.Replace('\', '/')
}
$dependencies['com.unity.modules.imgui'] = '1.0.0'
$dependencies['com.unity.modules.physics'] = '1.0.0'
@{ dependencies = $dependencies } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$Project/Packages/manifest.json"
Copy-Item -LiteralPath $Source -Destination "$Project/Assets/Runtime/UVSpline.cs" -Force
Copy-Item -LiteralPath "$PSScriptRoot/MultiSplineLoft.cs" -Destination "$Project/Assets/Runtime/MultiSplineLoft.cs" -Force
Copy-Item -LiteralPath "$PSScriptRoot/UVSplineValidation.cs" -Destination "$Project/Assets/Editor/UVSplineValidation.cs" -Force
foreach ($phase in @('Run', 'VerifyFreshEditor')) {
    $report = if ($phase -eq 'Run') { 'regression-results.txt' } else { 'fresh-editor-results.txt' }
    if (Test-Path -LiteralPath "$Project/$report") { Remove-Item -LiteralPath "$Project/$report" }
    $arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $Project + '"'), '-executeMethod', ('UVSplineValidation.' + $phase), '-logFile', ('"' + "$Project/$phase.log" + '"'))
    $process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $Project -WindowStyle Hidden -PassThru
    while (!$process.HasExited) { Start-Sleep -Seconds 2; $process.Refresh() }
    if (Test-Path -LiteralPath "$Project/$report") { Get-Content -LiteralPath "$Project/$report" }
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath "$Project/$report")) { throw "Unity validation failed; see $Project/$phase.log" }
}
