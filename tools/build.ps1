[CmdletBinding()]
param([string]$Version='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $cfg=Get-Content release-config.json -Raw | ConvertFrom-Json
    python tools/build_common_glamour.py --check
    if ($LASTEXITCODE) { throw 'Common glamour evidence or generated list failed validation' }
    python tools/check_non_equipment_sources.py
    if ($LASTEXITCODE) { throw 'Non-equipment acquisition evidence failed validation' }
    $buildArgs=@('build',$cfg.project,'-c','Release','-p:UseSharedCompilation=false','--nologo')
    if ($Version) { $buildArgs+="-p:Version=$Version" }
    & dotnet @buildArgs
    if ($LASTEXITCODE) { throw 'Plugin build failed' }
    if ($Version) {
        $binary=Join-Path $root "$($cfg.build)/$($cfg.internal_name).dll"
        if ([Reflection.AssemblyName]::GetAssemblyName($binary).Version -ne [version]$Version) { throw 'Actual DLL version differs from planned release' }
    }
    dotnet run --project tests/Steward.Tests.csproj -c Release
    if ($LASTEXITCODE) { throw 'Rule tests failed' }
    dotnet run --project native-tests/Binding.Tests.csproj -c Release
    if ($LASTEXITCODE) { throw 'Native binding regression checks failed' }
    python -m unittest discover -s tests -p 'test_*.py' -v
    if ($LASTEXITCODE) { throw 'Integration regression checks failed' }
} finally { Pop-Location }
