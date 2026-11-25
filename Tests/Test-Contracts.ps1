#Requires -Version 7.6
param(
    [Parameter(Mandatory)]
    [string]$SPTPath
)

$ErrorActionPreference = 'Stop'
$installDir = (Get-Item -LiteralPath $SPTPath).FullName

# Exercise the same profile model and JSON converters that the server uses for saves.
$null = [Reflection.Assembly]::LoadFrom((Join-Path $installDir 'SPT_Runtime/SPTarkov.Server.Core.dll'))
$registrators = [SPTarkov.Server.Core.Utils.Json.IJsonConverterRegistrator[]]@(
    [SPTarkov.Server.Core.Utils.Json.SptJsonConverterRegistrator]::new()
)
$serializer = [SPTarkov.Server.Core.Utils.JsonUtil]::new($registrators)
$profileType = [SPTarkov.Server.Core.Models.Eft.Profile.SptProfile]
$profileKey = 'net.liquidwarp.unlockgearprogression'

$baseline = '{"spt":{"fixture.otherMod":{"enabled":true}}}'
$profile = $serializer.Deserialize($baseline, $profileType)
$entry = [System.Text.Json.JsonDocument]::Parse(
    '{"schemaVersion":1,"unlockedTemplates":["000000000000000000000001"]}'
)
try {
    $profile.SptData.ExtensionData[$profileKey] = $entry.RootElement.Clone()
} finally {
    $entry.Dispose()
}

$saved = $serializer.Serialize($profile, $profileType, $false)
$restored = $serializer.Deserialize($saved, $profileType)
$catalogue = $restored.SptData.ExtensionData[$profileKey]
$templates = $catalogue.GetProperty('unlockedTemplates')
if ($catalogue.GetProperty('schemaVersion').GetInt32() -ne 1 -or
    $templates.GetArrayLength() -ne 1 -or $templates[0].GetString() -cne '000000000000000000000001') {
    throw 'The catalogue changed during an SPT profile save and reload.'
}
if (-not $restored.SptData.ExtensionData['fixture.otherMod'].GetProperty('enabled').GetBoolean()) {
    throw 'Saving the catalogue changed another profile extension.'
}
$secondProfile = $serializer.Deserialize($baseline, $profileType)
if ($secondProfile.SptData.ExtensionData.ContainsKey($profileKey)) {
    throw 'A separate profile inherited the first catalogue.'
}
Write-Output 'PASS  Profile JSON preserves the unlock, its schema version, and unrelated extension data.'
Write-Output 'PASS  An independent profile has its own catalogue state.'

# Read signatures as metadata so Unity and its native runtime are never initialized.
$null = [Reflection.Assembly]::LoadFrom((Join-Path $installDir 'BepInEx/core/Mono.Cecil.dll'))
$eftPath = Join-Path $installDir 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$selectorMethods = @(
    'System.Collections.Generic.IEnumerable`1<EFT.InventoryLogic.Item> GetItemCollections(EFT.InventoryLogic.Slot)'
    'System.Boolean Select(EFT.InventoryLogic.Item,EFT.InventoryLogic.Slot,System.Boolean,Diz.LanguageExtensions.Error&)'
)
$expectedMethods = [ordered]@{
    'EFT.UI.EquipItemWindow' = @(
        'System.Collections.Generic.IEnumerable`1<EFT.InventoryLogic.Item> GetAvailableItems()'
        'System.Void ItemSelectedHandler(EFT.InventoryLogic.Item)'
    )
    'EFT.InventoryLogic.WeaponModdingManipulation' = $selectorMethods
    'EFT.InventoryLogic.EditBuildManipulation' = $selectorMethods
    'EFT.UI.EditBuildScreen' = @(
        'System.Collections.Generic.List`1<EFT.InventoryLogic.Item> GetMissingBuildItems(System.Collections.Generic.List`1<EFT.InventoryLogic.Mod>)'
        'System.Void TryToAssembleBuild()'
    )
    'EFT.ClientBackendSession' = @(
        'System.Void SendOperationQueued(System.Object,Comfort.Common.Callback)'
        'System.Void ApplyProfileChanges(Comfort.Common.Result`1<EFT.ClientBackendSession/QueueData>)'
    )
}

$module = [Mono.Cecil.ModuleDefinition]::ReadModule($eftPath)
try {
    foreach ($pair in $expectedMethods.GetEnumerator()) {
        $typeName = $pair.Key
        $type = $module.GetType($typeName)
        if ($null -eq $type) {
            throw "Missing EFT type $typeName in $eftPath"
        }
        $signatures = foreach ($method in $type.Methods) {
            $method.FullName.Replace("${typeName}::", '')
        }
        foreach ($expected in $pair.Value) {
            if ($signatures -cnotcontains $expected) {
                throw "Missing method on $typeName in $eftPath — $expected"
            }
        }
        Write-Output "PASS  $typeName has the expected method signatures."
    }
} finally {
    $module.Dispose()
}
