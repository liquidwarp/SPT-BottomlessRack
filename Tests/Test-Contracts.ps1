#Requires -Version 7.6
param(
    [Parameter(Mandatory)]
    [string]$SPTPath,
    [string]$ClientAssembly
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
        'System.Void RefreshItemViews()'
        'System.Boolean EFT.InventoryLogic.SelectableItemContext.ISelectionContext.IsActive(EFT.InventoryLogic.ItemContext,System.String&)'
    )
    'EFT.UI.InventoryScreen' = @(
        'System.Void Show(EFT.UI.InventoryScreen/InventoryScreenController)'
    )
    'EFT.UI.WeaponModding.WeaponModdingScreen' = @(
        'System.Void Show(EFT.InventoryLogic.Item,EFT.InventoryLogic.InventoryController,EFT.InventoryLogic.CompoundItem[])'
    )
    'EFT.UI.WeaponModding.DropDownMenu' = @(
        'System.Void Show(EFT.InventoryLogic.ModdingSelectableItemContext,EFT.UI.WeaponModding.ModdingScreenSlotView,EFT.InventoryLogic.ItemController)'
        'System.Void CreateDropdownModdingItemView(EFT.InventoryLogic.SelectableItemContext,EFT.InventoryLogic.Item,EFT.InventoryLogic.ItemController,UnityEngine.RectTransform)'
    )
    'EFT.UI.WeaponModding.ModdingScreenSlotView' = @(
        'System.Void Show(EFT.InventoryLogic.ModdingSelectableItemContext,EFT.UI.IItemObserverScreen,EFT.InventoryLogic.Slot,EFT.InventoryLogic.CompoundItem,EFT.UI.WeaponModding.DropDownMenu,UnityEngine.Transform,EFT.InventoryLogic.InventoryController,EFT.UI.ItemUiContext)'
        'System.Void SetLockedStatus(System.Boolean)'
        'System.Void CreateModdingItemView(EFT.InventoryLogic.SelectableItemContext,EFT.InventoryLogic.Item,EFT.InventoryLogic.ItemController,UnityEngine.Transform)'
    )
    'EFT.UI.ItemUiContext' = @(
        'EFT.UI.DragAndDrop.ItemView CreateSlotItemView(EFT.InventoryLogic.Item,EFT.InventoryLogic.ItemContext,EFT.InventoryLogic.ItemController,EFT.InventoryLogic.IItemOwner,EFT.SkillManager)'
        'EFT.UI.DragAndDrop.ItemView CreateItemView(EFT.InventoryLogic.Item,EFT.InventoryLogic.ItemContext,ItemRotation,EFT.InventoryLogic.ItemController,EFT.InventoryLogic.IItemOwner,EFT.UI.FilterPanel,EFT.UI.DragAndDrop.IContainerView,System.Boolean,System.Boolean)'
    )
    'EFT.UI.DragAndDrop.ModdingSelectableItemView' = @(
        'EFT.UI.DragAndDrop.ModdingSelectableItemView Create(EFT.InventoryLogic.Item,EFT.InventoryLogic.ModdingSelectableItemContext,ItemRotation,EFT.InventoryLogic.ItemController,EFT.InventoryLogic.IItemOwner,EFT.UI.FilterPanel,EFT.UI.DragAndDrop.IContainerView,EFT.UI.ItemUiContext,EFT.UI.Insurance.InsuranceCompany,System.Boolean)'
    )
    'EFT.UI.ItemContextInteractionsSwitcher' = @(
        'Comfort.Common.IResult IsInteractive(EFT.InventoryLogic.EItemInfoButton)'
    )
    'EFT.BonusController' = @('System.Boolean HasBonus(EFT.EBonusType)')
    'EFT.UI.MenuTaskBar' = @(
        'System.Void InitBonusController(EFT.BonusController)'
        'System.Void BonusChangedHandler(System.Boolean,EFT.EBonusType)'
    )
    'EFT.InventoryLogic.WeaponModdingManipulation' = $selectorMethods
    'EFT.InventoryLogic.EditBuildManipulation' = $selectorMethods
    'EFT.UI.EditBuildScreen' = @(
        'System.Void Show(EFT.InventoryLogic.Item,EFT.InventoryLogic.Item,EFT.InventoryLogic.InventoryController,EFT.IEftSession)'
        'System.Void method_15()'
        'System.Threading.Tasks.Task PerformAssemble(System.Collections.Generic.IEnumerable`1<EFT.InventoryLogic.Mod>)'
        'System.Collections.Generic.List`1<EFT.InventoryLogic.Item> GetMissingBuildItems(System.Collections.Generic.List`1<EFT.InventoryLogic.Mod>)'
        'System.Void TryToAssembleBuild()'
        'System.Void SetItemReadyToAssembleActive(System.Boolean,System.Boolean)'
        'System.Void UpdateItemBody(EFT.InventoryLogic.Item)'
    )
    'EFT.UI.EquipmentBuildsScreen' = @(
        'System.Void EquipBuild()'
        'System.Boolean TryApplyBuild(System.Collections.Generic.List`1<EFT.InventoryLogic.Item>&,Diz.LanguageExtensions.Error&)'
        'System.Void ApplyBuild(EFT.UI.Builds.EquipmentBuild)'
        'System.Void FindPresetParts(System.Collections.Generic.IReadOnlyCollection`1<EFT.InventoryLogic.Item>)'
    )
    'EFT.ClientBackendSession' = @(
        'System.Void SendOperationRightNow(System.Object,Comfort.Common.Callback)'
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
    $fields = @{
        'EFT.UI.WeaponModding.ModdingScreenSlotView' = @('_locked')
        'EFT.UI.EquipItemWindow' = @('_slot', '_viewList')
        'EFT.UI.DragAndDrop.GridItemView' = @('TagName', '_tagColor', 'Caption')
        'EFT.UI.ItemObserveScreen`2' = @('Item', 'RefreshingWeapon')
        'EFT.UI.EditBuildScreen' = @('_itemBody', '_playerItems', '_acceptableWeaponBodies')
        'EFT.UI.EquipmentBuildsScreen' = @('_currentBuild', '_realInventoryController', '_isApplying')
        'EFT.UI.UIInputNode' = @('UI')
    }
    foreach ($entry in $fields.GetEnumerator()) {
        $names = $module.GetType($entry.Key).Fields.Name
        foreach ($name in $entry.Value) {
            if ($names -cnotcontains $name) { throw "Missing $($entry.Key).$name" }
        }
    }
    $slotFactory = $module.GetType('EFT.UI.ItemUiContext').Methods | Where-Object Name -CEQ 'CreateSlotItemView'
    $views = $slotFactory.Body.Instructions | Where-Object { $_.Operand.Name -ceq 'Create' } |
        ForEach-Object { $_.Operand.DeclaringType.FullName }
    foreach ($view in 'SelectableSlotItemView', 'TextOnlySelectableSlotItemView') {
        if ($views -cnotcontains "EFT.UI.DragAndDrop.$view") { throw "Missing equipment picker layout $view" }
    }
    Write-Output 'PASS  The equipment picker has distinct icon and text row factories and native tag bindings.'

    $modification = $module.GetType('EFT.EBonusType').Fields | Where-Object Name -CEQ 'UnlockWeaponModification'
    $contextMenu = $module.GetType('EFT.UI.ItemContextInteractionsSwitcher').Methods | Where-Object Name -CEQ 'IsInteractive'
    $taskBar = $module.GetType('EFT.UI.MenuTaskBar').Methods | Where-Object Name -CEQ 'InitBonusController'
    foreach ($gate in $contextMenu, $taskBar) {
        $usesWorkbench = $gate.Body.Instructions | Where-Object {
            $_.Operand.FullName -ceq 'System.Boolean EFT.BonusController::HasBonus(EFT.EBonusType)' -and
            $_.Previous.Operand -eq $modification.Constant
        }
        if (-not $usesWorkbench) { throw "Workbench eligibility check missing from $($gate.FullName)" }
    }
    Write-Output 'PASS  The item context menu and Edit Build taskbar query the weapon modification bonus.'

    $callback = $module.GetType('EFT.ClientBackendSession').Methods | Where-Object Name -CEQ 'SendCallback'
    $calls = @($callback.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } |
        ForEach-Object { $_.Operand.FullName })
    $apply = [Array]::FindIndex($calls, [Predicate[object]]{ param($m) $m -like '*::ApplyProfileChanges(*' })
    $notify = [Array]::FindIndex($calls, [Predicate[object]]{ param($m) $m -like '*Comfort.Common.Callback::Invoke(*' })
    if ($apply -lt 0 -or $notify -le $apply) { throw 'EFT no longer applies the inventory delta before the immediate callback.' }
    Write-Output 'PASS  Selector field bindings and the inventory acknowledgement order match the installed client.'
} finally {
    $module.Dispose()
}

if ($ClientAssembly) {
    $client = [Reflection.Assembly]::LoadFrom((Get-Item -LiteralPath $ClientAssembly).FullName)
    $null = [Reflection.Assembly]::LoadFrom($eftPath)
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $installDir 'EscapeFromTarkov_Data/Managed/Comfort.dll'))
    $flags = [Reflection.BindingFlags]'NonPublic, Static'
    $namespace = 'Liquidwarp.UnlockGearProgression.Client'
    $plugin = [Mono.Cecil.ModuleDefinition]::ReadModule($client.Location)
    try {
        if ($null -ne $plugin.GetType("$namespace.CataloguePanel")) { throw 'The client still includes the standalone stash menu.' }
        foreach ($name in 'StashPatch', 'ModdingScreenPatch', 'BuildScreenPatch') {
            $entry = $plugin.GetType("$namespace.$name").Methods | Where-Object Name -CEQ 'Postfix'
            $calls = @($entry.Body.Instructions.Operand | Where-Object { $_ -is [Mono.Cecil.MethodReference] })
            if ($calls.FullName -cnotcontains "System.Void $namespace.GearClient::Enter(EFT.InventoryLogic.InventoryController)") {
                throw "$name no longer loads unlocks when its native screen opens."
            }
        }
    } finally {
        $plugin.Dispose()
    }
    Write-Output 'PASS  The native inventory, Modding, and Edit Build screens load unlocks without the standalone stash menu.'

    $selectorHooks = @{
        EquipPreviewTagPatch = @([EFT.UI.EquipItemWindow], 'RefreshItemViews')
        ModdingPreviewLabelPatch = @([EFT.UI.WeaponModding.DropDownMenu], 'CreateDropdownModdingItemView')
        BuildSlotLabelPatch = @([EFT.UI.WeaponModding.ModdingScreenSlotView], 'CreateModdingItemView')
        EquippedModdingSlotPatch = @([EFT.UI.WeaponModding.ModdingScreenSlotView], 'Show')
        EquipmentPresets = @([EFT.UI.EquipmentBuildsScreen], 'EquipBuild')
        BuildAssembly = @([EFT.UI.EditBuildScreen], 'TryToAssembleBuild')
        UnlockedWeaponBodyPatch = @([EFT.UI.EditBuildScreen], 'CheckForVitalParts')
    }
    foreach ($binding in $selectorHooks.GetEnumerator()) {
        $patch = $client.GetType("$namespace.$($binding.Key)", $true)
        $targets = @($patch.GetCustomAttributesData() | Where-Object { $_.AttributeType.FullName -ceq 'HarmonyLib.HarmonyPatch' })
        if ($targets.Count -ne 1 -or $targets[0].ConstructorArguments[0].Value -ne $binding.Value[0] -or
            $targets[0].ConstructorArguments[1].Value -cne $binding.Value[1]) { throw "Unexpected selector target on $patch" }
    }
    $tag = $client.GetType("$namespace.EquipPreviewTagPatch").GetMethod('Postfix', $flags)
    $priority = $tag.GetCustomAttributesData() | Where-Object { $_.AttributeType.FullName -ceq 'HarmonyLib.HarmonyPriority' }
    if ($null -eq $priority -or $priority.ConstructorArguments[0].Value -ne [HarmonyLib.Priority]::Last) {
        throw 'The Unlocked tag must run after normal equipment location tags.'
    }
    Write-Output 'PASS  Equipment, dropdown labels, and equipped-slot hover target their native views.'

    # Edit Build can show two distinct EFT items for the picker entry and the part fitted to the plan.
    $template = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.ItemTemplate])
    $template._id = [EFT.MongoID]'000000000000000000000011'
    $pickerItem = [EFT.InventoryLogic.Item]::new('000000000000000000000012', $template)
    $fittedItem = [EFT.InventoryLogic.Item]::new('000000000000000000000013', $template)
    $gear = $client.GetType("$namespace.GearClient")
    $unlocks = $gear.GetField('Unlocked', $flags).GetValue($null)
    $revision = $gear.GetField('_revision', $flags)
    $originalRevision = $revision.GetValue($null)
    $buildLabel = $client.GetType("$namespace.ModdingPreviewLabelPatch").GetMethod('IsUnlockedBuildPart', $flags)
    try {
        $revision.SetValue($null, 1)
        foreach ($screenType in [EFT.UI.EditBuildScreen], [EFT.UI.WeaponModding.WeaponModdingScreen]) {
            $screen = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($screenType)
            $context = [EFT.InventoryLogic.ModdingSelectableItemContext]::new($screen)
            foreach ($earned in $false, $true) {
                if ($earned) { $null = $unlocks.Add($pickerItem.StringTemplateId) }
                else { $null = $unlocks.Remove($pickerItem.StringTemplateId) }
                foreach ($part in $pickerItem, $fittedItem) {
                    $actual = $buildLabel.Invoke($null, @($context, $part))
                    if ($actual -ne ($earned -and $screenType -eq [EFT.UI.EditBuildScreen])) {
                        throw "Wrong unlock label on $($screenType.Name) for $($part.Id) (earned=$earned)"
                    }
                }
            }
        }
    } finally {
        $null = $unlocks.Remove($pickerItem.StringTemplateId)
        $revision.SetValue($null, $originalRevision)
    }
    Write-Output 'PASS  An earned template labels both Edit Build item instances and leaves ordinary Modding rows alone.'

    $hooks = @(
        $client.GetType("$namespace.WeaponModificationBonusPatch", $true).GetMethod('Postfix', $flags)
        $client.GetType("$namespace.EditBuildTaskBarPatch", $true).GetMethod('Prefix', $flags)
    )
    try {
        # EFT identifies a raid by game type. These managed stand-ins skip all Unity constructors.
        foreach ($scene in @($null, [EFT.HideoutGame], [EFT.LocalGame])) {
            if ($null -ne $scene) {
                [Comfort.Common.Singleton[EFT.AbstractGame]]::Instance = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($scene)
            }
            foreach ($bonus in [EFT.EBonusType]::UnlockWeaponModification, [EFT.EBonusType]::UnlockItemCraft) {
                foreach ($active in $false, $true) {
                    $expected = $active -or ($bonus -eq [EFT.EBonusType]::UnlockWeaponModification -and $scene -ne [EFT.LocalGame])
                    foreach ($hook in $hooks) {
                        $values = [object[]]@($bonus, $active)
                        $null = $hook.Invoke($null, $values)
                        if ($values[1] -ne $expected) { throw "$($hook.DeclaringType.Name) returned $($values[1]) for $bonus (active=$active, game=$scene)" }
                    }
                }
            }
        }
    } finally {
        [Comfort.Common.Singleton[EFT.AbstractGame]]::Instance = $null
    }
    Write-Output 'PASS  Both edit menus allow a level-zero Workbench in the stash and Hideout, and respect raid and crafting bonus state.'
}
