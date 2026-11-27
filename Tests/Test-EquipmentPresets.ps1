#Requires -Version 7.6
using namespace EFT.InventoryLogic
param([string]$SPTPath, [string]$ClientAssembly)

$ErrorActionPreference = 'Stop'
# Use SPT's CoreCLR build of Harmony to run the actual preset hooks against EFT's managed DLL.
$null = [Reflection.Assembly]::LoadFrom((Join-Path $SPTPath 'SPT_Runtime/0Harmony.dll'))
$client = [Reflection.Assembly]::LoadFrom((Get-Item -LiteralPath $ClientAssembly).FullName)
$managed = Join-Path $SPTPath 'EscapeFromTarkov_Data/Managed'
$null = [Reflection.Assembly]::LoadFrom((Join-Path $managed 'Assembly-CSharp.dll'))
$null = [Reflection.Assembly]::LoadFrom((Join-Path $managed 'Comfort.dll'))
$static = [Reflection.BindingFlags]'NonPublic, Static'
$fields = [Reflection.BindingFlags]'Public, NonPublic, Instance'
$filters = [ItemFilter[]]@()
$templates = [JsonType.ItemTemplates]::new()
function New-ItemId { [Guid]::NewGuid().ToString('N').Substring(8) }
function New-Template($type, $id, $parent) {
    $tpl = [Activator]::CreateInstance($type)
    # Armor templates expose case-sensitive member pairs that PowerShell cannot bind by name.
    [ItemTemplate].GetProperty('_id').SetValue($tpl, [EFT.MongoID]$id)
    foreach ($field in 'Width','Height','StackMaxSize','StackObjectsCount') { [ItemTemplate].GetField($field).SetValue($tpl, 1) }
    [ItemTemplate].GetField('ConflictingItems').SetValue($tpl, [string[]]@())
    [ItemTemplate].GetField('DiscardLimit').SetValue($tpl, -1)
    if ($parent) {
        [ItemTemplate].GetField('<ParentId>k__BackingField').SetValue($tpl, [EFT.MongoID]$parent)
        $templates.Add([EFT.MongoID]$id, $tpl)
    }
    return $tpl
}
function New-Slot($name, $merge = 'Merge') { [Slot]::new($name, $filters, $false, $merge) }
function Add-ToGrid($grid, $item) { $null = $grid.FindLocationForItem($item).Add($item, $false) }

$body = New-Template ([WeaponTemplate]) '5e870397991fd70db46995c8' '5422acb9af1c889c16000029'
$body.Slots = @((New-Slot 'mod_handguard'), (New-Slot 'mod_stock'))
$body.Chambers = [Slot[]]@()
$body.ammoCaliber = 'Caliber12g'
[WeaponTemplate].GetField('weapFireType').SetValue($body, [EFT.InventoryLogic.Weapon+EFireMode[]]@('single'))
$stock = [Weapon]::new((New-ItemId), $body)
$target = [Weapon]::new((New-ItemId), $body)
$factoryPlan = [Weapon]::new((New-ItemId), $body)
$original = @('5e87076ce2db31558c75a11d', '5e87116b81c4ed43e83cefdd')
$magpul = @('5eea21647547d6330471b3c9', '5eea217fc64c5d0dfc05712a')
$partTypes = @([HandguardTemplate], [StockTemplate])
$parents = @('55818a104bdc2db9688b4569', '55818a594bdc2db9688b456a')
$originalIds = foreach ($i in 0..1) {
    $part = [Mod]::new((New-ItemId), (New-Template $partTypes[$i] $original[$i] $parents[$i]))
    $stock.Slots[$i].Add($part)
    $factoryPlan.Slots[$i].Add([Mod]::new((New-ItemId), $part.Template))
    $target.Slots[$i].Add([Mod]::new((New-ItemId), (New-Template $partTypes[$i] $magpul[$i] $parents[$i])))
    $part.Id
}
$equipTpl = New-Template ([InventoryEquipmentTemplate]) '55d7217a4bdc2d86028b456d'
$equipTpl.Slots = @(foreach ($name in [Enum]::GetNames([EquipmentSlot])) { New-Slot $name 'DontMerge' })
$equipment = [InventoryEquipment]::new((New-ItemId), $equipTpl)
$plan = [InventoryEquipment]::new((New-ItemId), $equipTpl)
$away = [InventoryEquipment]::new((New-ItemId), $equipTpl)
$plan.GetSlot('FirstPrimaryWeapon').Add($target)
$away.GetSlot('FirstPrimaryWeapon').Add($factoryPlan)
$stashTpl = New-Template ([StashTemplate]) '566abbc34bdc2d92178b4576'
$stashTpl.Grids = @([Grid]::new('hideout', 10, 16, $false, $filters, $null))
$stash = [Stash]::new((New-ItemId), $stashTpl)
$tableTpl = New-Template ([SortingTableTemplate]) '602543c13fee350cd564d032'
$tableTpl.Slots = @((New-Slot 'BuildSlot' 'DontMerge'))
$tableTpl.Grids = $stashTpl.Grids
$table = [SortingTable]::new((New-ItemId), $tableTpl)
$inventory = [Inventory]::new($equipment, $stash, $null, $null, $table, $null, $null,
    [Collections.Generic.Dictionary[EBoundItem,EFT.MongoID]]::new(), $null, [Collections.Generic.List[EFT.MongoID]]::new(), $null, $false)
$controller = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([BackEndInventoryController])
$owner = [ItemController]::new($plan, 'test', 'preset', $false, 'HideoutProduction')
[InventoryController].GetField('<Inventory>k__BackingField').SetValue($controller, $inventory)
$controller._rootItem = $equipment
$controller._activeEvents = $owner._activeEvents
$controller._searchController = $owner.SearchController
$controller._examined = $true
$controller._restrictionsInLobby = [Collections.Generic.Dictionary[string,int]]::new()
$controller._idGenerator = [BackendIdGenerator]::new($null)
foreach ($i in 1..16) { $controller._idGenerator._idCache.Add([EFT.MongoID](New-ItemId)) }
foreach ($root in $stash, $equipment, $table) { $root.CurrentAddress = $controller.CreateItemAddress() }
Add-ToGrid $stash.Grid $stock
$screenType = [EFT.UI.EquipmentBuildsScreen]
$screen = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($screenType)
$screenType.GetField('_realInventoryController', $fields).SetValue($screen, $controller)
$screenType.GetField('_applyToEquipment', $fields).SetValue($screen, $equipment)
$matcher = $client.GetType('Liquidwarp.BottomlessRack.Client.PresetItems', $true)
$assignments = $matcher.GetField('NewBodies', $static)
$clientGear = $client.GetType('Liquidwarp.BottomlessRack.Client.GearClient', $true)
$unlocked = $clientGear.GetField('Unlocked', $static).GetValue($null)
$clientGear.GetProperty('Inventory', $static).SetValue($null, $controller)
$clientGear.GetField('_revision', $static).SetValue($null, 1)
foreach ($id in @($body._id) + $magpul) { $null = $unlocked.Add([string]$id) }

function Set-Loadout($loadout) {
    $build = [EFT.UI.Builds.EquipmentBuild]::new([EFT.MongoID](New-ItemId), 'Preset', [EFT.Builds.EEquipmentBuildType]::Standard)
    $build._equipment = $loadout
    $screenType.GetField('_currentBuild', $fields).SetValue($screen, $build)
}
function Get-Shortages($loadout) {
    Set-Loadout $loadout
    $missing = $null
    $errorReason = $null
    if (!$screen.TryApplyBuild([ref]$missing, [ref]$errorReason)) { throw "Preset simulation failed. $errorReason" }
    return ,$missing
}
function Apply-Loadout($loadout) {
    Set-Loadout $loadout
    [EquipCalls]::Simulations = [EquipCalls]::Applications = 0
    $screen.EquipBuild()
    if ([EquipCalls]::Simulations -ne 1 -or [EquipCalls]::Applications -ne 1) {
        throw "Expected one availability check and one ApplyBuild call. Counts were $([EquipCalls]::Simulations) and $([EquipCalls]::Applications)."
    }
    # Commit the same operations in memory that ApplyBuild would send on the inventory queue.
    $transfer = [ItemManipulator]::TransferContent($loadout, $equipment, [CompoundItem[]]@($table), $stash,
        $controller, $null, [string[]]@('Dogtag'), $false, $false)
    if ($transfer.Failed) { throw "Equipment transfer failed. $($transfer.Error)" }
    $stow = [ItemManipulator]::EmptyContainer($table, [CompoundItem[]]@($stash), $controller)
    if ($stow.Failed) { throw "Could not stow displaced equipment. $($stow.Error)" }
}

$harmony = [HarmonyLib.Harmony]::new('bottomlessrack.tests.equipmentpresets')
try {
    Add-Type @'
public static class EquipCalls
{
    public static int Simulations;
    public static int Applications;
    public static void CountSimulation() { Simulations++; }
    public static bool CaptureApply() { Applications++; return false; }
}
'@
    $null = $harmony.CreateClassProcessor($client.GetType('Liquidwarp.BottomlessRack.Client.EquipmentPresets')).Patch()
    $null = $harmony.Patch($screenType.GetMethod('TryApplyBuild'), [HarmonyLib.HarmonyMethod]::new([EquipCalls].GetMethod('CountSimulation')))
    $null = $harmony.Patch($screenType.GetMethod('ApplyBuild'), [HarmonyLib.HarmonyMethod]::new([EquipCalls].GetMethod('CaptureApply')))
    foreach ($patch in $matcher.GetNestedTypes([Reflection.BindingFlags]'NonPublic')) {
        if ($patch.GetCustomAttributes([HarmonyLib.HarmonyPatch], $false).Length) {
            $null = $harmony.CreateClassProcessor($patch).Patch()
        }
    }
    [Comfort.Common.Singleton[EFT.ItemFactory]]::Instance = [EFT.ItemFactory]::new($templates)
    $missing = Get-Shortages $plan
    if ($missing.Count -ne 3 -or (Compare-Object $missing.StringTemplateId (@([string]$body._id) + $magpul))) {
        throw 'Shotgunner must request a separate receiver and just the two missing Magpul parts.'
    }
    $copy = [Weapon]::new((New-ItemId), $body)
    $roots = [Collections.Generic.List[Item]]::new()
    $roots.Add($copy)
    foreach ($id in $magpul) { $roots.Add([Mod]::new((New-ItemId), $templates[[EFT.MongoID]$id])) }
    foreach ($item in $roots) { Add-ToGrid $stash.Grid $item }
    $bound = $matcher.GetMethod('AssignBodies', $static).Invoke($null, [object[]]@($missing, $roots))
    $assignments.SetValue($null, $bound)
    Apply-Loadout $plan
    $assignments.SetValue($null, $null)
    foreach ($round in 1..3) {
        Apply-Loadout $away
        if ($copy.Parent.Container.ParentItem -ne $stash -or $equipment.GetSlot('FirstPrimaryWeapon').ContainedItem -ne $stock) {
            throw 'The factory loadout did not reuse the original Mossberg and stow the customized one.'
        }
        Apply-Loadout $plan
        if ($equipment.GetSlot('FirstPrimaryWeapon').ContainedItem -ne $copy -or $stock.Parent.Container.ParentItem -ne $stash) {
            throw 'The return trip selected the wrong receiver.'
        }
        foreach ($i in 0..1) {
            if ($stock.Slots[$i].ContainedItem.Id -ne $originalIds[$i] -or $copy.Slots[$i].ContainedItem -ne $roots[$i+1]) {
                throw 'A preset switch stripped or replaced a fitted attachment.'
            }
        }
    }
    Write-Output 'PASS  Shotgunner preserves both Mossbergs across three round trips. Each complete Equip runs one availability check and calls ApplyBuild once.'

    $rigTpl = New-Template ([VestTemplate]) (New-ItemId) '5448e5284bdc2dcb718b4567'
    $rigGrids = [Grid[]]@([Grid]::new('main', 3, 3, $false, $filters, $null))
    [CompoundItemTemplate].GetField('Slots').SetValue($rigTpl, [Slot[]]@((New-Slot 'Front_plate'), (New-Slot 'Back_plate')))
    [CompoundItemTemplate].GetField('Grids').SetValue($rigTpl, $rigGrids)
    $plates = @(foreach ($i in 0..1) { New-Template ([ArmorPlateTemplate]) (New-ItemId) '644120aa86ffbe10ee032b6f' })
    $magTpl = New-Template ([MagazineTemplate]) (New-ItemId) '5448bc234bdc2d3c308b4569'
    $magTpl.Cartridges = @([StackSlot]::new('cartridges', 10, $filters))
    $ammo = @(foreach ($i in 0..1) { New-Template ([AmmoTemplate]) (New-ItemId) '5485a8684bdc2da71d8b4567' })
    foreach ($tpl in $ammo) {
        [ItemTemplate].GetField('StackMaxSize').SetValue($tpl, 50)
        [AmmoTemplate].GetField('casingSounds').SetValue($tpl, 'rifle')
    }
    function New-PackedRig($runs) {
        $rig = [Vest]::new((New-ItemId), $rigTpl)
        foreach ($i in 0..1) { $rig.Slots[$i].Add([ArmorPlate]::new((New-ItemId), $plates[$i])) }
        $mag = [Magazine]::new((New-ItemId), $magTpl)
        foreach ($run in $runs) {
            $rounds = [Ammo]::new((New-ItemId), $ammo[$run[0]])
            $rounds.StackObjectsCount = $run[1]
            $add = $mag.Cartridges.Add($rounds, $false)
            if ($add.Failed) { throw "Could not load the magazine. $($add.Error)" }
        }
        Add-ToGrid $rig.Grids[0] $mag
        return $rig
    }
    $wanted = New-PackedRig @(@(0, 2), @(1, 1))
    $owned = New-PackedRig @(@(0, 1), @(0, 1), @(1, 1))
    $different = New-PackedRig @(@(1, 1), @(0, 2))
    $rigPlan = [InventoryEquipment]::new((New-ItemId), $equipTpl)
    $rigPlan.GetSlot('TacticalVest').Add($wanted)
    foreach ($rig in $different, $owned) { Add-ToGrid $stash.Grid $rig }
    $same = $matcher.GetMethod('SameBuild', $static)
    if ($same.Invoke($null, @($wanted, $different)) -or !$same.Invoke($null, @($wanted, $owned))) {
        throw 'Loaded rounds must match by type, quantity, and firing order across stack boundaries.'
    }
    Apply-Loadout $rigPlan
    if ($equipment.GetSlot('TacticalVest').ContainedItem -ne $owned) { throw 'Equip selected the wrong packed rig.' }
    Apply-Loadout $away
    $front = $owned.Slots[0].ContainedItem
    $back = $owned.Slots[1].ContainedItem
    foreach ($slot in $owned.Slots) { $null = $slot.RemoveItemWithoutRestrictions() }
    $owned.Slots[0].Add($back)
    $owned.Slots[1].Add($front)
    if ($same.Invoke($null, @($wanted, $owned))) { throw 'Reversed front and back plates matched.' }

    $packed = @($owned.Grids[0].Items)[0]
    $bagTpl = New-Template ([BackpackTemplate]) (New-ItemId) '5448e53e4bdc2d60728b4567'
    [CompoundItemTemplate].GetField('Grids').SetValue($bagTpl, $rigGrids)
    $left = [Backpack]::new((New-ItemId), $bagTpl)
    $right = [Backpack]::new((New-ItemId), $bagTpl)
    Add-ToGrid $left.Grids[0] ([Mod]::new((New-ItemId), $templates[[EFT.MongoID]$magpul[0]]))
    $cargo = [Mod]::new((New-ItemId), $templates[[EFT.MongoID]$magpul[0]])
    foreach ($location in @([LocationInGrid]::new(1, 0, 'Horizontal'), [LocationInGrid]::new(0, 0, 'Vertical'))) {
        $null = $right.Grids[0].AddItemWithoutRestrictions($cargo, $location)
        if ($same.Invoke($null, @($left, $right))) { throw 'A different packed position or rotation matched.' }
        $null = $right.Grids[0].Remove($cargo, $false)
    }
    $missing = Get-Shortages $rigPlan
    foreach ($tpl in @($rigTpl, $magTpl) + $plates) { $null = $unlocked.Add([string][ItemTemplate].GetProperty('_id').GetValue($tpl)) }
    if ($missing.Count -ne 6 -or $matcher.GetMethod('Supplies', $static).Invoke($null, [object[]]@($rigPlan, $missing)).Count) {
        throw 'An incomplete loadout could strip packed items or issue a rig with unavailable ammunition.'
    }
    if ($front.Parent.Container.ParentItem -ne $owned -or $packed.Parent.Container.ParentItem -ne $owned) {
        throw 'Previewing a different loadout disturbed the stored rig.'
    }
    foreach ($tpl in $ammo) {
        $rounds = [Ammo]::new((New-ItemId), $tpl)
        $rounds.StackObjectsCount = 5
        Add-ToGrid $stash.Grid $rounds
    }
    $missing = Get-Shortages $rigPlan
    if ($missing.Count -ne 4) { throw 'Loose ammunition should supply the new rig without opening another one.' }
    $roots = [Collections.Generic.List[Item]]::new()
    foreach ($item in $missing) {
        $fresh = [Comfort.Common.Singleton[EFT.ItemFactory]]::Instance.CreateItem((New-ItemId), $item.StringTemplateId, $null)
        $roots.Add($fresh)
        Add-ToGrid $stash.Grid $fresh
    }
    $assignments.SetValue($null, $matcher.GetMethod('AssignBodies', $static).Invoke($null, [object[]]@($missing, $roots)))
    Apply-Loadout $rigPlan
    $assignments.SetValue($null, $null)
    $issuedRig = $equipment.GetSlot('TacticalVest').ContainedItem
    Apply-Loadout $away
    Apply-Loadout $rigPlan
    if ($issuedRig -eq $owned -or $issuedRig -eq $different -or !$same.Invoke($null, @($wanted, $issuedRig)) -or
        $equipment.GetSlot('TacticalVest').ContainedItem -ne $issuedRig -or $packed.Parent.Container.ParentItem -ne $owned) {
        throw 'The available loadout must assemble and reuse the new packed rig without stripping another one.'
    }
    Write-Output 'PASS  Presets preserve packed rigs, distinguish plate slots, grid layouts and ammunition, and skip incomplete builds.'

    $mountTpl = New-Template ([MountTemplate]) (New-ItemId) '55818b224bdc2dde698b456f'
    $mountTpl.Slots = @((New-Slot 'mod_scope'))
    $opticTpl = New-Template ([ModTemplate]) (New-ItemId) '5448fe124bdc2da5018b4567'
    $looseMount = [Mod]::new((New-ItemId), $mountTpl)
    $looseMount.Slots[0].Add([Mod]::new((New-ItemId), $opticTpl))
    Add-ToGrid $stash.Grid $looseMount
    $editBody = [Weapon]::new((New-ItemId), $body)
    Add-ToGrid $stash.Grid $editBody
    $editPlan = [Weapon]::new((New-ItemId), $body)
    $planMount = [Mod]::new((New-ItemId), $mountTpl)
    $planMount.Slots[0].Add([Mod]::new((New-ItemId), $opticTpl))
    $editPlan.Slots[0].Add($planMount)
    $inventory.HideoutAreaStashes = [Collections.Generic.Dictionary[EFT.EAreaType,CompoundItem]]::new()
    $editType = [EFT.UI.EditBuildScreen]
    $edit = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($editType)
    $edit.InventoryController = $controller
    $editType.BaseType.GetField('Item', $fields).SetValue($edit, $editPlan)
    $editType.GetField('_itemBody', $fields).SetValue($edit, $editBody)
    $manipulation = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EditBuildManipulation])
    $editType.GetField('_useAllManipulation', $fields).SetValue($edit, $manipulation)
    foreach ($tpl in $mountTpl, $opticTpl) { $null = $unlocked.Add([string]$tpl._id) }
    $null = $harmony.CreateClassProcessor($client.GetType('Liquidwarp.BottomlessRack.Client.MissingBuildItemsPatch')).Patch()
    $planner = $client.GetType('Liquidwarp.BottomlessRack.Client.BuildAssembly').GetMethod('Missing', $static)
    function Assert-EditBuildOptics([int]$expected) {
        $manipulation._playerItems = @($inventory.GetPlayerItems([EPlayerItems]::Stash) | Where-Object { $edit.IsAcceptableItem($_) })
        $missing = $edit.GetMissingBuildItems([WeaponAssembler]::GetInstalledMods($editBody))
        if ($missing.Count -ne $expected -or @($missing | Where-Object StringTemplateId -ne ([string]$opticTpl._id)).Count) {
            throw "Edit Build expected $expected missing optics. Preview showed [$($missing.StringTemplateId -join ', ')]."
        }
        $requested = 0
        foreach ($line in $planner.Invoke($null, @($edit, $editBody))) {
            if ($line.TemplateId -ne $opticTpl._id) { throw "Unexpected Edit Build request for $($line.TemplateId)." }
            $requested += $line.Quantity
        }
        if ($requested -ne $expected) { throw "Edit Build requested $requested optics. Expected $expected." }
    }
    Assert-EditBuildOptics 0
    $editPlan.Slots[1].Add([Mod]::new((New-ItemId), $opticTpl))
    Assert-EditBuildOptics 1
    $null = $editPlan.Slots[1].RemoveItemWithoutRestrictions()
    $bareMount = [Mod]::new((New-ItemId), $mountTpl)
    $editBody.Slots[0].Add($bareMount)
    Assert-EditBuildOptics 1
    $null = $editBody.Slots[0].RemoveItemWithoutRestrictions()
    Add-ToGrid $stash.Grid $bareMount
    $mounts = @($inventory.GetPlayerItems([EPlayerItems]::Stash) | Where-Object TemplateId -eq $mountTpl._id)
    $optic = $looseMount.Slots[0].ContainedItem
    $null = $looseMount.Slots[0].RemoveItemWithoutRestrictions()
    $mounts[1].Slots[0].Add($optic)
    Assert-EditBuildOptics 1
    Write-Output 'PASS  Edit Build reuses the optic on the selected loose mount, counts it once, and ignores optics on unused spare mounts.'
} finally {
    $assignments.SetValue($null, $null)
    $harmony.UnpatchSelf()
}
