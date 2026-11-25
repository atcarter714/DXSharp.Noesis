[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release', [switch]$Locked, [switch]$Smoke)
$ErrorActionPreference = 'Stop'
function Invoke-DotNet { & dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $args ($LASTEXITCODE)" } }
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $solution = 'DXSharp.Noesis.slnx'
    if ($Locked) { Invoke-DotNet restore $solution --locked-mode } else { Invoke-DotNet restore $solution }
    Invoke-DotNet build $solution -c $Configuration --no-restore
    Invoke-DotNet run --project tests/Verification -c $Configuration --no-build
    Invoke-DotNet pack src/DXSharp.Noesis -c $Configuration --no-build -o artifacts/packages
    if ($Smoke) {
        Invoke-DotNet run --project samples/OrbitalCommand -c $Configuration --no-build -- --allocator-smoke --warp --debug
        Invoke-DotNet run --project samples/OrbitalCommand -c $Configuration --no-build -- --smoke
        Invoke-DotNet run --project samples/Starfighter -c $Configuration --no-build -- --smoke
    }
} finally { Pop-Location }
