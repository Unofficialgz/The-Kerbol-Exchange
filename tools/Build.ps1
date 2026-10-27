param([Parameter(Mandatory=$true)][string]$KspRoot)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild=& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if(!$msbuild){throw 'MSBuild not found'}
foreach($project in @('OreMarket.sln','tests\OreMarket.Tests.csproj')){
 & $msbuild (Join-Path $root $project) /t:Build /p:Configuration=Release /p:UseSharedCompilation=false "/p:KspRoot=$KspRoot" /nologo /verbosity:minimal
 if($LASTEXITCODE -ne 0){throw 'Build failed'}
}
& (Join-Path $root 'tests\bin\Release\OreMarket.Tests.exe')
if($LASTEXITCODE -ne 0){throw 'Regression tests failed'}
