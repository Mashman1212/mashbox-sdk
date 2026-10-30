param(
    [Parameter(Mandatory=$true)][string]$Msvc,
    [Parameter(Mandatory=$true)][string]$SdkInclude,
    [Parameter(Mandatory=$true)][string]$SdkLib,
    [string]$BuildDirectory = (Join-Path $env:TEMP 'MashBoxOpenSubdivBuild'),
    [string]$Output = (Join-Path $PSScriptRoot '../../Editor/Plugins/x86_64/StudioOpenSubdiv.dll')
)
$ErrorActionPreference='Stop'
# OpenSubdiv 3.6.1, f26d0dedeaf846ea321865a527bb4489691c2f04.
# Msvc is a VC/Tools/MSVC/<version> directory. SdkInclude contains ucrt,
# shared and um; SdkLib contains ucrt/x64 and um/x64. Official extracted
# Microsoft.Windows.SDK.CPP + .x64 packages are supported, as is an installed SDK.
$compiler=Join-Path $Msvc 'bin/Hostx64/x64/cl.exe'
foreach($required in @($compiler,(Join-Path $SdkInclude 'ucrt/math.h'),(Join-Path $SdkLib 'ucrt/x64/libucrt.lib'))){
    if(!(Test-Path -LiteralPath $required)){throw "Missing native build prerequisite: $required"}
}
New-Item -ItemType Directory -Force -Path $BuildDirectory | Out-Null
$build=(Resolve-Path -LiteralPath $BuildDirectory).Path
$arguments=@('/nologo','/std:c++17','/EHsc','/MT','/O2','/LD','/D_USE_MATH_DEFINES',
    ('/I"'+$Msvc+'/include"'),('/I"'+$SdkInclude+'/ucrt"'),('/I"'+$SdkInclude+'/shared"'),('/I"'+$SdkInclude+'/um"'),
    ('/I"'+$PSScriptRoot+'/OpenSubdiv"'),('/Fo"'+$build+'/"'),('"'+$PSScriptRoot+'/StudioOpenSubdiv.cpp"'))
foreach($part in @('far','sdc','vtr')){
    $arguments+=Get-ChildItem (Join-Path $PSScriptRoot "OpenSubdiv/opensubdiv/$part") -Filter '*.cpp' | Sort-Object Name | ForEach-Object {'"'+$_.FullName+'"'}
}
$arguments+=@('/link',('/LIBPATH:"'+$Msvc+'/lib/x64"'),('/LIBPATH:"'+$Msvc+'/lib/onecore/x64"'),
    ('/LIBPATH:"'+$SdkLib+'/ucrt/x64"'),('/LIBPATH:"'+$SdkLib+'/um/x64"'),
    ('/OUT:"'+$build+'/StudioOpenSubdiv.dll"'),('/IMPLIB:"'+$build+'/StudioOpenSubdiv.lib"'))
# CL's /link applies to the rest of its line, including response-file input.
$response=Join-Path $build 'native-build.rsp'
$arguments -join ' ' | Set-Content -LiteralPath $response -Encoding utf8
& $compiler "@$response"
if($LASTEXITCODE -ne 0){throw "OpenSubdiv compilation failed ($LASTEXITCODE)."}
New-Item -ItemType Directory -Force -Path (Split-Path $Output) | Out-Null
Copy-Item -LiteralPath (Join-Path $build 'StudioOpenSubdiv.dll') -Destination $Output
Get-FileHash -LiteralPath $Output -Algorithm SHA256
