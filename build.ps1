param(
    [string]$SPTPath = 'D:\SPT\4.1 Fika',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$InstallMod,

    [switch]$InstallClientMod,

    [switch]$InstallServerMod,

    [switch]$Test,
    [switch]$PackageRelease
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path "$SPTPath\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll")) {
    throw "SPT not found at '$SPTPath'"
}

$buildArgs = @(
    "$PSScriptRoot\UnlockGearProgression.slnx"
    "-c", $Configuration
    "-p:SPTPath=$SPTPath"
    "-p:InstallClientMod=$($InstallMod -or $InstallClientMod)"
    "-p:InstallServerMod=$($InstallMod -or $InstallServerMod)"
    "-p:PackageRelease=$($PackageRelease.IsPresent)"
    "-m:1"
    "-p:NuGetAudit=false"
    "-tl:off"
)
dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

if ($Test) {
    $env:UGP_TEST_SPT_PATH = (Resolve-Path -LiteralPath $SPTPath).Path
    dotnet test "$PSScriptRoot\Tests\UnlockGearProgression.Tests.csproj" -c $Configuration -m:1 "-p:SPTPath=$SPTPath" "-p:NuGetAudit=false" -tl:off
    if ($LASTEXITCODE -ne 0) { throw "Mod tests failed ($LASTEXITCODE)" }
    $clientAssembly = Join-Path $PSScriptRoot "Client/bin/$Configuration/netstandard2.1/Liquidwarp-UnlockGearProgression.Client.dll"
    pwsh -NoProfile -File "$PSScriptRoot\Tests\Test-Contracts.ps1" -SPTPath $SPTPath -ClientAssembly $clientAssembly
    if ($LASTEXITCODE -ne 0) { throw "DLL contract checks failed ($LASTEXITCODE)" }
}
