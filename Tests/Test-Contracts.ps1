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
$profileKey = 'net.liquidwarp.bottomlessrack'

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
    'EFT.UI.TraderDealScreen' = @(
        'System.Void Show(EFT.Trading.Trader,EFT.Profile,EFT.InventoryLogic.InventoryController,EFT.UI.ETradeMode,EFT.UI.ItemUiContext,EFT.Quests.QuestController,System.Collections.Generic.IEnumerable`1<EFT.Trading.Trader>)'
        'System.Boolean IsValidDeal(System.Boolean&,AutoExchange/AutoExchangeData&)'
        'System.Threading.Tasks.Task OnDealButtonClickAsync()'
    )
    'EFT.UI.DragAndDrop.TradingItemView' = @(
        'System.Void SetPrice(System.Nullable`1<EFT.Trading.Trader/ItemPrice>)'
        'System.Void ShowTooltip()'
    )
    'EFT.Trading.Assortment' = @('System.Void SelectItem(EFT.InventoryLogic.Item)')
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
    'EFT.InventoryLogic.DropdownManipulation' = @(
        'System.Boolean Select(EFT.InventoryLogic.Item,EFT.InventoryLogic.Slot,Diz.LanguageExtensions.Error&)'
        'System.Boolean SelectCheck(EFT.InventoryLogic.Item,EFT.InventoryLogic.Slot,Diz.LanguageExtensions.Error&)'
    )
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
        'EFT.UI.EquipItemWindow' = @('_slot', '_viewList', '_placeHolder')
        'EFT.UI.DragAndDrop.GridItemView' = @('TagName', '_tagColor', 'Caption')
        'EFT.UI.DragAndDrop.TradingItemView' = @('_traderAssortment', '_price', '_currency', '_schemeIcon')
        'EFT.UI.TraderDealScreen' = @('_autoExchange', '_traderGridView')
        'EFT.UI.ItemObserveScreen`2' = @('Item', 'RefreshingWeapon', '_manipulation')
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

    $panelMethods = $module.GetType('EFT.UI.ItemSpecificationPanel').Methods
    $draw = $panelMethods | Where-Object Name -ceq 'method_1'
    if ($draw.Body.Instructions.Operand.Name -cnotcontains 'SetDescriptionText') { throw 'The inspect panel description hook moved.' }
    foreach ($method in $panelMethods | Where-Object Name -cin 'Show', 'OnItemExaminedEvent') {
        if ($method.Body.Instructions.Operand.FullName -cnotcontains $draw.FullName) {
            throw "Inspect descriptions no longer refresh through $($method.Name)."
        }
    }

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

    $deal = $module.GetType('EFT.UI.TraderDealScreen/CG_OnDealButtonClickAsync').Methods | Where-Object Name -ceq 'MoveNext'
    $steps = $deal.Body.Instructions.Operand | Where-Object {
        $_ -is [Mono.Cecil.MethodReference] -and $_.Name -cin 'IsValidDeal','ApproveAutoExchange','ExchangeCurrency','Purchase'
    }
    if (($steps.Name -join ',') -cne 'IsValidDeal,ApproveAutoExchange,ExchangeCurrency,Purchase') {
        throw 'The trader Deal action changed its validation or payment order.'
    }
} finally {
    $module.Dispose()
}

if ($ClientAssembly) {
    $client = [Reflection.Assembly]::LoadFrom((Get-Item -LiteralPath $ClientAssembly).FullName)
    $null = [Reflection.Assembly]::LoadFrom($eftPath)
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $installDir 'EscapeFromTarkov_Data/Managed/Comfort.dll'))
    $flags = [Reflection.BindingFlags]'NonPublic, Static'
    $namespace = 'Liquidwarp.BottomlessRack.Client'
    $plugin = [Mono.Cecil.ModuleDefinition]::ReadModule($client.Location)
    try {
        if ($null -ne $plugin.GetType("$namespace.CataloguePanel")) { throw 'The client still includes the standalone stash menu.' }
        foreach ($name in 'MenuCataloguePatch', 'StashPatch', 'ModdingScreenPatch', 'BuildScreenPatch', 'TraderPurchases') {
            $entry = $plugin.GetType("$namespace.$name").Methods | Where-Object Name -CEQ 'Postfix'
            $calls = @($entry.Body.Instructions.Operand | Where-Object { $_ -is [Mono.Cecil.MethodReference] })
            if ($calls.FullName -cnotcontains "System.Void $namespace.GearClient::Enter(EFT.InventoryLogic.InventoryController)") {
                throw "$name no longer loads unlocks when its native screen opens."
            }
        }
        foreach ($type in $plugin.GetTypes()) {
            $menuCalls = $type.Methods.Body.Instructions.Operand | Where-Object {
                $_ -is [Mono.Cecil.MethodReference] -and $_.DeclaringType.Name -ceq 'DropDownMenu' -and $_.Name -in 'Show', 'Close'
            }
            if ($menuCalls) { throw "$($type.FullName) controls the attachment dropdown." }
        }
        $builder = $plugin.GetType("$namespace.PreviewRows").Methods | Where-Object Name -CEQ 'BuildWeapon'
        $construction = $builder.Body.Instructions.Operand | Where-Object { $_ -is [Mono.Cecil.MethodReference] }
        if ($construction.FullName -cnotcontains 'EFT.InventoryLogic.Item EFT.ItemFactory::CreateItem(System.String,System.String,UnparsedData)' -or
            $construction.FullName -cnotcontains 'System.Void EFT.InventoryLogic.Slot::Add(EFT.InventoryLogic.Item)') {
            throw 'Weapon previews must bind the server recipe to detached EFT items and slots.'
        }
        $exchange = $plugin.GetType("$namespace.TraderPurchases").NestedTypes | Where-Object Name -like '<PurchaseWithExchange>d__*'
        $steps = ($exchange.Methods | Where-Object Name -ceq 'MoveNext').Body.Instructions.Operand | Where-Object {
            $_ -is [Mono.Cecil.MethodReference] -and $_.Name -cin 'ApproveAutoExchange','StillSelected','ExchangeCurrency','Purchase'
        }
        if (($steps.Name -join ',') -cne 'ApproveAutoExchange,StillSelected,ExchangeCurrency,StillSelected,Purchase') {
            throw 'A trader exchange must recheck the quoted selection before converting funds and before buying.'
        }
        $hover = $plugin.GetType("$namespace.TraderPurchases/Hover")
        $afterHover = $hover.Methods | Where-Object Name -ceq 'Postfix'
        if (!$afterHover -or $hover.Methods.Name -ccontains 'Prefix' -or $afterHover.Body.Instructions.Operand.Name -ccontains 'Show') {
            throw 'The unlocked price must preserve the trader tooltip that EFT and item-source mods just displayed.'
        }
    } finally {
        $plugin.Dispose()
    }
    Write-Output 'PASS  The native inventory, Modding, and Edit Build screens load unlocks without the standalone stash menu.'

    $selectorHooks = @{
        EquipPreviewTagPatch = @([EFT.UI.EquipItemWindow], 'RefreshItemViews')
        ModdingPreviewLabelPatch = @([EFT.UI.WeaponModding.DropDownMenu], 'CreateDropdownModdingItemView')
        'ModdingPreviewLabelPatch+FittedPart' = @([EFT.UI.WeaponModding.ModdingScreenSlotView], 'CreateModdingItemView')
        InstallCheckPatch = @([EFT.InventoryLogic.DropdownManipulation], 'SelectCheck')
        InstallPatch = @([EFT.InventoryLogic.DropdownManipulation], 'Select')
        EquippedModdingSlotPatch = @([EFT.UI.WeaponModding.ModdingScreenSlotView], 'Show')
        EquipmentPresets = @([EFT.UI.EquipmentBuildsScreen], 'EquipBuild')
        BuildAssembly = @([EFT.UI.EditBuildScreen], 'TryToAssembleBuild')
        UnlockedWeaponBodyPatch = @([EFT.UI.EditBuildScreen], 'CheckForVitalParts')
        TraderPurchases = @([EFT.UI.TraderDealScreen], 'Show')
        'TraderPurchases+Price' = @([EFT.UI.DragAndDrop.TradingItemView], 'SetPrice')
        'TraderPurchases+Hover' = @([EFT.UI.DragAndDrop.TradingItemView], 'ShowTooltip')
        'TraderPurchases+Select' = @([EFT.Trading.Assortment], 'SelectItem')
        'TraderPurchases+Validate' = @([EFT.UI.TraderDealScreen], 'IsValidDeal')
        'TraderPurchases+ExchangeDeal' = @([EFT.UI.TraderDealScreen], 'OnDealButtonClickAsync')
        GearSales = @([EFT.UI.TraderDealScreen], 'Close')
        'GearSales+SellQuote' = @([EFT.Trading.Trader], 'GetUserItemPrice')
        'GearSales+Sell' = @([EFT.Trading.Assortment], 'Sell')
        'GearSales+SelectOffer' = @([EFT.UI.Ragfair.RagFair], 'CanBeSelectedAtRagfair')
        'GearSales+ConfirmOffer' = @([EFT.UI.Ragfair.AddOfferWindow], 'CheckRestrictions')
    }
    foreach ($binding in $selectorHooks.GetEnumerator()) {
        $patch = $client.GetType("$namespace.$($binding.Key)", $true)
        $targets = @($patch.GetCustomAttributesData() | Where-Object { $_.AttributeType.FullName -ceq 'HarmonyLib.HarmonyPatch' })
        if ($targets.Count -ne 1 -or $targets[0].ConstructorArguments[0].Value -ne $binding.Value[0] -or
            $targets[0].ConstructorArguments[1].Value -cne $binding.Value[1]) { throw "Unexpected selector target on $patch" }
    }
    $installHook = $client.GetType("$namespace.InstallPatch").GetCustomAttributes([HarmonyLib.HarmonyPatch], $false)[0].info
    $wrapper = $installHook.declaringType.GetMethod($installHook.methodName, $installHook.argumentTypes)
    if ($wrapper.GetParameters().Count -ne 3 -or !$wrapper.GetParameters()[2].IsOut) {
        throw 'The live preview hook must intercept the three-argument click before the four-argument swap.'
    }
    foreach ($name in 'EquipPreviewTagPatch', 'ModdingPreviewLabelPatch', 'ModdingPreviewLabelPatch+FittedPart') {
        $tag = $client.GetType("$namespace.$name").GetMethod('Postfix', $flags)
        $priority = $tag.GetCustomAttributesData() | Where-Object { $_.AttributeType.FullName -ceq 'HarmonyLib.HarmonyPriority' }
        if ($null -eq $priority -or $priority.ConstructorArguments[0].Value -ne [HarmonyLib.Priority]::Last) {
            throw "$name must label the item after its location or price has been drawn."
        }
    }
    Write-Output 'PASS  Equipment, dropdown labels, and equipped-slot hover target their native views.'

    # Edit Build can show two distinct EFT items for the picker entry and the part fitted to the plan.
    $template = [EFT.InventoryLogic.ModTemplate]::new()
    $template._id = [EFT.MongoID]'000000000000000000000011'
    $template.Width = $template.Height = $template.StackObjectsCount = $template.StackMaxSize = 1
    $template.ConflictingItems = [string[]]@()
    $pickerItem = [EFT.InventoryLogic.Mod]::new('000000000000000000000012', $template)
    $fittedItem = [EFT.InventoryLogic.Mod]::new('000000000000000000000013', $template)
    $gear = $client.GetType("$namespace.GearClient")
    $unlocks = $gear.GetField('Unlocked', $flags).GetValue($null)
    $revision = $gear.GetField('_revision', $flags)
    $originalRevision = $revision.GetValue($null)
    $buildLabel = $client.GetType("$namespace.ModdingPreviewLabelPatch").GetMethod('IsUnlockedBuildPart', $flags)
    $previewRows = $client.GetType("$namespace.PreviewRows")
    $previews = $previewRows.GetField('Previews', $flags).GetValue($null)
    $cache = $previewRows.GetField('ByTemplate', $flags).GetValue($null)
    try {
        $revision.SetValue($null, 1)
        $null = $previews.Add($pickerItem)
        $check = $client.GetType("$namespace.InstallCheckPatch").GetMethod('Prefix', $flags)
        $click = $client.GetType("$namespace.InstallPatch").GetMethod('Prefix', $flags)
        # Use a detached preview and a real stash copy so EFT can simulate both a move and a swap.
        $previewOwner = [EFT.InventoryLogic.ItemController]::new($pickerItem, 'preview', 'Unlocked gear', $false, 'HideoutProduction')
        $filters = [EFT.InventoryLogic.ItemFilter[]]@()
        $bodyTemplate = [EFT.InventoryLogic.CompoundItemTemplate]::new()
        $bodyTemplate._id = [EFT.MongoID]'000000000000000000000014'
        $bodyTemplate.Width = $bodyTemplate.Height = 1
        $bodyTemplate.ConflictingItems = [string[]]@()
        $bodyTemplate.Slots = @([EFT.InventoryLogic.Slot]::new('mod_sight_rear', $filters, $false, 'Merge'),
                                [EFT.InventoryLogic.Slot]::new('mod_handguard', $filters, $false, 'Merge'))
        $body = [EFT.InventoryLogic.CompoundItem]::new('000000000000000000000015', $bodyTemplate)
        $slot, $nearbySlot = $body.Slots
        $stashTemplate = [EFT.InventoryLogic.StashTemplate]::new()
        $stashTemplate.Grids = @([EFT.InventoryLogic.Grid]::new('main', 6, 3, $false, $filters, $null))
        $stash = [EFT.InventoryLogic.Stash]::new('000000000000000000000016', $stashTemplate)
        $equipmentTemplate = [EFT.InventoryLogic.InventoryEquipmentTemplate]::new()
        $equipmentTemplate.Slots = @(foreach ($name in [Enum]::GetNames([EFT.InventoryLogic.EquipmentSlot])) {
            [EFT.InventoryLogic.Slot]::new($name, $filters, $false, 'DontMerge')
        })
        $equipment = [EFT.InventoryLogic.InventoryEquipment]::new('000000000000000000000017', $equipmentTemplate)
        $bindings = [Collections.Generic.Dictionary[EFT.InventoryLogic.EBoundItem,EFT.MongoID]]::new()
        $inventory = [EFT.InventoryLogic.Inventory]::new($equipment, $stash, $null, $null, $null, $null,
            $null, $bindings, $null, [Collections.Generic.List[EFT.MongoID]]::new(), $null, $false)
        $controller = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.BackEndInventoryController])
        [EFT.InventoryLogic.InventoryController].GetField('<Inventory>k__BackingField').SetValue($controller, $inventory)
        $controller._rootItem = $equipment
        $controller._searchController = $previewOwner.SearchController
        $controller._activeEvents = $previewOwner._activeEvents
        $controller._examined = $true
        $controller._restrictionsInLobby = [Collections.Generic.Dictionary[string,int]]::new()
        $stash.CurrentAddress = $equipment.CurrentAddress = $controller.CreateItemAddress()
        foreach ($item in $body, $fittedItem) { $null = $stash.Grid.FindLocationForItem($item).Add($item, $false) }
        $live = [EFT.InventoryLogic.WeaponModdingManipulation]::new($controller, [EFT.InventoryLogic.CompoundItem[]]@($stash))
        $edit = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.EditBuildManipulation])
        foreach ($operation in $check, $click) {
            # A live click with an active Unity screen goes through the asynchronous stash-use tests.
            $available = $operation -eq $check
            $revision.SetValue($null, $(if ($available) { 1 } else { -1 }))
            foreach ($selector in $edit, $live) {
                foreach ($part in $pickerItem, $fittedItem) {
                    $argsForHook = [object[]]@($selector, $part, $slot, $null, $false)
                    $passToEft = $operation.Invoke($null, $argsForHook)
                    $ourPreview = $selector -eq $live -and $part -eq $pickerItem
                    if ($passToEft -eq $ourPreview -or ($ourPreview -and $argsForHook[4] -ne $available) -or
                        ($ourPreview -and !$available -and $null -eq $argsForHook[3])) {
                        throw "Wrong selection route for $($selector.GetType().Name) and $($part.Id)."
                    }
                }
            }
        }
        $revision.SetValue($null, 1)
        $blockingTemplate = [EFT.InventoryLogic.ItemTemplate]::new()
        $blockingTemplate._id = [EFT.MongoID]'000000000000000000000018'
        $blockingTemplate.Width = $blockingTemplate.Height = $blockingTemplate.StackObjectsCount = $blockingTemplate.StackMaxSize = 1
        $blockingTemplate.ConflictingItems = @($pickerItem.StringTemplateId)
        $blocker = [EFT.InventoryLogic.Item]::new('000000000000000000000019', $blockingTemplate)
        $oldSight = [EFT.InventoryLogic.Item]::new('000000000000000000000020', $blockingTemplate)
        foreach ($occupied in $false, $true) {
            if ($occupied) { $slot.Add($oldSight) }
            $previous = $slot.ContainedItem
            $nearbySlot.Add($blocker)
            $reason = $null
            if ($live.SelectCheck($fittedItem, $slot, [ref]$reason) -or
                $reason -isnot [EFT.InventoryLogic.Slot+ConflictingItemError] -or $reason.ConflictingItem -ne $blocker) {
                throw 'The stash copy must report the conflicting attachment still fitted to the build.'
            }
            foreach ($operation in $check, $click) {
                $values = [object[]]@($live, $pickerItem, $slot, $null, $true)
                if ($operation.Invoke($null, $values) -or $values[4] -or $values[3].GetType() -ne $reason.GetType() -or
                    $values[3].ConflictingItem -ne $blocker -or $slot.ContainedItem -ne $previous -or $pickerItem.Owner -ne $previewOwner) {
                    throw 'A blocked preview must reject hover and click with the same conflict as the stash copy and preserve both items.'
                }
            }
            $null = $nearbySlot.RemoveItemWithoutRestrictions()
            $values = [object[]]@($live, $pickerItem, $slot, $null, $false)
            if (!$live.SelectCheck($fittedItem, $slot, [ref]$reason) -or $check.Invoke($null, $values) -or !$values[4] -or
                $null -ne $values[3] -or $slot.ContainedItem -ne $previous -or $pickerItem.Owner -ne $previewOwner) {
                throw 'Removing the blocker must enable the preview, including when the part being replaced conflicts with it.'
            }
        }
        $null = $slot.RemoveItemWithoutRestrictions()
        $id = $pickerItem.StringTemplateId
        $null = $unlocks.Add($id)
        $cache.Add($id, $pickerItem)
        $include = $previewRows.GetMethod('Include', $flags)
        foreach ($editBuild in $false, $true) {
            foreach ($hasOwned in $false, $true) {
                $owned = [EFT.InventoryLogic.Item[]]@()
                if ($hasOwned) { $owned = [EFT.InventoryLogic.Item[]]@($fittedItem) }
                $rows = @($include.Invoke($null, [object[]]@($owned, $slot, $editBuild, $false)))
                $expected = if ($hasOwned -and $editBuild) { @($fittedItem) }
                    elseif ($hasOwned) { @($fittedItem, $pickerItem) } else { @($pickerItem) }
                if ($rows.Count -ne @($expected).Count) { throw "Wrong row count (Edit Build=$editBuild, owned=$hasOwned)" }
                for ($i = 0; $i -lt $rows.Count; $i++) {
                    if (![object]::ReferenceEquals($rows[$i], @($expected)[$i])) { throw 'A native picker item was substituted.' }
                }
            }
        }
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
        $null = $cache.Remove($pickerItem.StringTemplateId)
        $null = $previews.Remove($pickerItem)
        $null = $unlocks.Remove($pickerItem.StringTemplateId)
        $revision.SetValue($null, $originalRevision)
    }
    Write-Output 'PASS  An earned template labels both Edit Build item instances and leaves ordinary Modding rows alone.'
    Write-Output 'PASS  Both dropdown wrappers intercept only the exact live preview and pass the same-template owned item and Edit Build through.'
    Write-Output 'PASS  A conflicting attachment blocks preview hover and click with the stash error, and removing it permits a move or swap.'
    Write-Output 'PASS  Edit Build keeps the native item or appends one earned choice. Live Modding keeps owned and catalogue rows distinct.'

    # Exercise the actual trader hooks with managed EFT objects, without constructing Unity views.
    $privateInstance = [Reflection.BindingFlags]'Instance, NonPublic'
    $shopType = $client.GetType("$namespace.TraderPurchases", $true)
    $activeShop = $shopType.GetField('_current', $flags)
    $shop = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($shopType)
    $stockView = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.UI.DragAndDrop.TradingItemView])
    $screen = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.UI.TraderDealScreen])
    $stockGrid = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.UI.DragAndDrop.TradingGridView])
    $screen._traderGridView = $stockGrid
    $container = [EFT.UI.DragAndDrop.ItemView].GetField('Container')
    $container.SetValue($stockView, $stockGrid)
    $viewItem = [EFT.UI.DragAndDrop.ItemView].GetField('_item', $privateInstance)
    $trader = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.Trading.Trader])
    $assort = $trader._currentAssortment = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.Trading.Assortment])
    $read = [Threading.Tasks.TaskCompletionSource[bool]]::new()
    $shopType.GetField('_screen', $privateInstance).SetValue($shop, $screen)
    $shopType.GetField('_trader', $privateInstance).SetValue($shop, $trader)
    $shopType.GetField('_firstRead', $privateInstance).SetValue($shop, $read.Task)
    $marked = $shopType.GetMethod('Marked', $flags)
    $select = $client.GetType("$namespace.TraderPurchases+Select").GetMethod('Prefix', $flags)
    $validate = $client.GetType("$namespace.TraderPurchases+Validate").GetMethod('Prefix', $flags)
    $canDeal = { $validate.Invoke($null, [object[]]@($screen, $false, $null, $false)) }
    $stillSelected = $shopType.GetMethod('StillSelected', $privateInstance)
    $medkitTemplate = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.ItemTemplate])
    $medkitTemplate._id = [EFT.MongoID]'000000000000000000000031'
    $medkit = [EFT.InventoryLogic.Item]::new('000000000000000000000032', $medkitTemplate)
    $previousShop = $activeShop.GetValue($null)
    try {
        $activeShop.SetValue($null, $shop)
        $assort._selectedItem = $medkit
        $revision.SetValue($null, -1)
        if (& $canDeal) { throw 'Deal accepted a purchase during the first catalogue read.' }
        $read.SetResult($false)
        if (-not (& $canDeal)) { throw 'A failed first read disabled a fresh priced purchase.' }
        $revision.SetValue($null, 1)
        $null = $unlocks.Add($pickerItem.StringTemplateId)
        $assort._schemes = [Collections.Generic.Dictionary[string,EFT.BarterScheme]]::new()
        # Two offers for the same earned template, then the reverse barter.
        foreach ($offer in @(@($pickerItem, '5449016a4bdc2d6f028b456f', $true),
                             @($fittedItem, $medkit.StringTemplateId, $true),
                             @($medkit, $pickerItem.StringTemplateId, $false))) {
            $outputItem, $paymentTemplate, $blocked = $offer
            $scheme = [EFT.BarterScheme]::new()
            $variant = [EFT.BarterVariant]::new()
            $variant.Add([EFT.BarterTemplate]@{ _tpl = $paymentTemplate; count = 1 })
            $scheme.Add($variant)
            $assort._schemes.Add($outputItem.Id, $scheme)
            $assort._selectedItem = $outputItem
            $viewItem.SetValue($stockView, $outputItem)
            $selection = [object[]]@($assort, $outputItem)
            $select.Invoke($null, $selection)
            if ($marked.Invoke($null, @($stockView, $assort)) -ne $blocked -or
                ($null -eq $selection[1]) -ne $blocked -or (& $canDeal) -eq $blocked) {
                throw "Trader price, selection or Deal disagrees for output $($outputItem.Id), payment $paymentTemplate."
            }
        }
        $assort._currentQuantity = 2
        $quote = [object[]]@($assort, $medkit, 2)
        if (!$stillSelected.Invoke($shop, $quote)) { throw 'A current exchange quote was rejected.' }
        $assort._currentQuantity = 3
        if ($stillSelected.Invoke($shop, $quote)) { throw 'A quote survived a change to the purchase quantity.' }
        $assort._currentQuantity = 2
        $assort._selectedItem = $pickerItem
        if ($stillSelected.Invoke($shop, $quote)) { throw 'A quote survived a change to the selected offer.' }
        $assort._selectedItem = $medkit
        $null = $unlocks.Add($medkit.StringTemplateId)
        if ($stillSelected.Invoke($shop, $quote)) { throw 'A pending exchange could spend after the output unlocked.' }
        $otherAssort = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.Trading.Assortment])
        $otherSelection = [object[]]@($otherAssort, $medkit)
        $select.Invoke($null, $otherSelection)
        if ($marked.Invoke($null, @($stockView, $otherAssort)) -or $null -eq $otherSelection[1]) {
            throw 'Another assortment was affected by the active trader.'
        }
        $container.SetValue($stockView, $null)
        if ($marked.Invoke($null, @($stockView, $assort))) { throw 'A player-owned view received a trader price label.' }
        $container.SetValue($stockView, $stockGrid)
        $screen.TradeMode = [EFT.UI.ETradeMode]::Sale
        if ($marked.Invoke($null, @($stockView, $assort)) -or -not (& $canDeal)) { throw 'The Sell tab was restricted.' }
        $activeShop.SetValue($null, $null)
        [EFT.Trading.Trader].GetField('Id').SetValue($trader, '579dc571d53a0658a154fbec')
        $shopType.GetMethod('Postfix', $flags).Invoke($null, [object[]]@($screen, $trader, $null, $null, $null))
        if ($null -ne $activeShop.GetValue($null)) { throw 'Fence activated the shop restriction.' }
    } finally {
        $activeShop.SetValue($null, $previousShop)
        $null = $unlocks.Remove($pickerItem.StringTemplateId)
        $null = $unlocks.Remove($medkit.StringTemplateId)
        $revision.SetValue($null, $originalRevision)
    }
    Write-Output 'PASS  Trader costs, selection and Deal follow the offered template for cash and both barter directions.'
    Write-Output 'PASS  Catalogue loading, a stale exchange quote, the stash, Sell and Fence use the intended purchase rules.'

    function New-SaleTemplate([string]$id, [EFT.InventoryLogic.ItemTemplate]$parent) {
        $tpl = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.ItemTemplate])
        $tpl._id = [EFT.MongoID]$id
        $tpl._type = [EFT.InventoryLogic.NodeType]::Item
        $tpl.Width = $tpl.Height = 1
        $tpl.Parent = $parent
        return $tpl
    }
    $family = New-SaleTemplate '5448fe124bdc2da5018b4567' $null
    $partTemplate = New-SaleTemplate '000000000000000000000061' $family
    $part = [EFT.InventoryLogic.Item]::new('000000000000000000000062', $partTemplate)
    $questTemplate = New-SaleTemplate '000000000000000000000063' $family
    $questTemplate.QuestItem = $true
    $questPart = [EFT.InventoryLogic.Item]::new('000000000000000000000064', $questTemplate)
    $ammoFamily = New-SaleTemplate '5485a8684bdc2da71d8b4567' $family
    $round = [EFT.InventoryLogic.Item]::new('000000000000000000000065', (New-SaleTemplate '000000000000000000000066' $ammoFamily))
    $box = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.CompoundItem])
    [EFT.InventoryLogic.Item].GetField('<Template>k__BackingField').SetValue($box, $medkitTemplate)
    $boxSlot = [EFT.InventoryLogic.Slot]::new('test', [EFT.InventoryLogic.ItemFilter[]]@(), $false, 0)
    $box.Slots = [EFT.InventoryLogic.Slot[]]@($boxSlot)
    $box.Grids = [EFT.InventoryLogic.Grid[]]@()
    $salesType = $client.GetType("$namespace.GearSales")
    $activeBuyer = $salesType.GetField('CurrentTrader', $flags)
    $quoteCheck = $client.GetType("$namespace.GearSales+SellQuote").GetMethod('Prefix', $flags)
    $confirmSale = $client.GetType("$namespace.GearSales+Sell").GetMethod('Prefix', $flags)
    $listingCheck = $client.GetType("$namespace.GearSales+SelectOffer").GetMethod('Prefix', $flags)
    $confirmListing = $client.GetType("$namespace.GearSales+ConfirmOffer").GetMethod('Prefix', $flags)
    $context = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.UI.RagfairNewOfferContext])
    $context._selectedItems = [Collections.Generic.HashSet[EFT.InventoryLogic.Item]]::new()
    $null = $context._selectedItems.Add($box)
    $assort._clones = [Collections.Generic.Dictionary[EFT.InventoryLogic.Item,EFT.InventoryLogic.Item]]::new()
    $assort._clones.Add($medkit, $box)
    try {
        # Fence enters the same sale scope as an ordinary trader, before the native screen builds its item views.
        $shopType.GetMethod('Prefix', $flags).Invoke($null, [object[]]@($trader))
        foreach ($item in $questPart, $round, $box, $part) {
            $covered = $item -eq $part
            $selection = [object[]]@($item, $null, $true)
            if ($quoteCheck.Invoke($null, [object[]]@($trader, $item, $null)) -eq $covered -or
                $listingCheck.Invoke($null, $selection) -eq $covered -or
                ($covered -and ($selection[1] -ne 'Gear sales are disabled.' -or $selection[2]))) {
                throw "Wrong PMC sale decision for template $($item.TemplateId)."
            }
        }
        $boxSlot.ContainedItem = $part
        $confirmation = [object[]]@($context, $null)
        if ($confirmSale.Invoke($null, @($assort)) -or $quoteCheck.Invoke($null, [object[]]@($trader, $box, $null)) -or
            $confirmListing.Invoke($null, $confirmation) -or !$confirmation[1].Failed) {
            throw 'A newly packed attachment could be quoted, sold or listed with its container.'
        }
        $boxSlot.ContainedItem = $null
        if (!$confirmSale.Invoke($null, @($assort)) -or !$confirmListing.Invoke($null, [object[]]@($context, $null))) {
            throw 'The empty container stayed blocked.'
        }
        $salesType.GetMethod('Prefix', $flags).Invoke($null, @())
        if (!$quoteCheck.Invoke($null, [object[]]@($trader, $part, $null))) { throw 'Fence kept the PMC sale filter for the Scav transfer.' }
    } finally {
        $activeBuyer.SetValue($null, $null)
    }
    Write-Output 'PASS  PMC quotes and flea offers check live contents and template exclusions. Scav Fence quotes pass through.'

    $inspect = $client.GetType("$namespace.InspectDescription").GetMethod('Postfix', $flags)
    $updated = $gear.GetField('Updated', $flags)
    $panel = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.UI.ItemSpecificationPanel])
    $labels = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.UI.ItemInfoWindowLabels])
    $description = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([CustomTextMeshProUGUI])
    [EFT.UI.ItemInfoWindowLabels].GetField('_description').SetValue($labels, $description)
    [EFT.UI.ItemSpecificationPanel].GetField('_itemLabels').SetValue($panel, $labels)
    $ui = [EFT.UI.UIParent]::new()
    $original = "Total: 1 ₽`n<color=green>Valeur</color>`n`n説明"
    try {
        $description.text = $original
        $inspect.Invoke($null, @($panel, $round, $ui))
        if ($null -ne $updated.GetValue($null)) { throw 'Inspecting ammunition requested catalogue updates.' }
        $inspect.Invoke($null, @($panel, $part, $ui))
        if ($description.text -cne $original) { throw 'A locked item received an inspect marker.' }
        $null = $unlocks.Add($part.StringTemplateId)
        $updated.GetValue($null).Invoke()
        $updated.GetValue($null).Invoke()
        if ($description.text -cne "UNLOCKED`n`n$original") { throw 'An unlock update duplicated the marker or changed the description.' }
        $ui.Dispose()
        if ($null -ne $updated.GetValue($null)) { throw 'A closed inspect window still receives updates.' }

        [Comfort.Common.Singleton[EFT.AbstractGame]]::Instance = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.LocalGame])
        $copy = [EFT.InventoryLogic.Item]::new('000000000000000000000067', $partTemplate)
        $description.text = $original
        $inspect.Invoke($null, @($panel, $copy, $ui))
        if ($description.text -cne "UNLOCKED`n`n$original") { throw 'A different copy of unlocked loot lost its marker in raid.' }
    } finally {
        $ui.Dispose()
        $null = $unlocks.Remove($part.StringTemplateId)
        [Comfort.Common.Singleton[EFT.AbstractGame]]::Instance = $null
    }
    Write-Output 'PASS  Inspect descriptions preserve localized text across unlock updates, window reuse, and in-raid inspection.'

    $spares = $client.GetType("$namespace.SpareMagazines", $true)
    $binding = $spares.GetCustomAttributes([HarmonyLib.HarmonyPatch], $false)[0].info
    if ($binding.methodType -ne [HarmonyLib.MethodType]::Constructor -or
        $binding.declaringType -ne [EFT.UI.InventoryItemContextInteractions] -or
        $null -eq $binding.declaringType.GetConstructor($binding.argumentTypes)) {
        throw 'The spare magazine action must bind to the native inventory context menu.'
    }
    $request = $spares.GetMethod('RequestedCopies', $flags)
    $inventoryProperty = $gear.GetProperty('Inventory', $flags)
    $previousInventory = $inventoryProperty.GetValue($null)
    $inventory = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.BackEndInventoryController])
    $weapon = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.Weapon])
    $magazine = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.Magazine])
    $magTemplate = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.MagazineTemplate])
    $magId = '000000000000000000000021'
    $magTemplate._id = [EFT.MongoID]$magId
    [EFT.InventoryLogic.Item].GetField('<Template>k__BackingField').SetValue($magazine, $magTemplate)
    $magSlot = [EFT.InventoryLogic.Slot]::new('mod_magazine', [EFT.InventoryLogic.ItemFilter[]]@(), $false, 0)
    $weapon.Slots = [EFT.InventoryLogic.Slot[]]@($magSlot)
    $weapon.CurrentAddress = $inventory.CreateItemAddress()
    $magSlot.ContainedItem = $magazine
    try {
        $revision.SetValue($null, 1)
        $inventoryProperty.SetValue($null, $inventory)
        $requestSpares = { $request.Invoke($null, @($weapon, $magId)) }
        if ($null -ne (& $requestSpares)) { throw 'A locked magazine could be copied.' }
        $null = $unlocks.Add($magId)
        foreach ($mode in 'ExternalMagazine', 'ExternalMagazineWithInternalReloadSupport') {
            $magTemplate.ReloadMagType = $mode
            foreach ($size in @(@(1, 1, 3), @(1, 2, 2), @(2, 1, 2), @(1, 3, 2), @(3, 1, 2), @(2, 2, 1), @(1, 4, 1))) {
                $magTemplate.Width, $magTemplate.Height, $expected = $size
                $line = & $requestSpares
                if ($line.TemplateId -cne $magId -or $line.Quantity -ne $expected) {
                    throw "Wrong spare magazine request for footprint $($magTemplate.Width)x$($magTemplate.Height)."
                }
            }
        }
        if ($null -ne $request.Invoke($null, @($weapon, '000000000000000000000022'))) {
            throw 'A different model could be issued after the weapon menu opened.'
        }
        $magTemplate.ReloadMagType = 'InternalMagazine'
        if ($null -ne (& $requestSpares)) { throw 'An internal magazine could be copied.' }
        $magTemplate.ReloadMagType = 'ExternalMagazine'
        $magSlot.ContainedItem = $null
        if ($null -ne (& $requestSpares)) { throw 'A weapon with an empty slot could issue magazines.' }
        $magSlot.ContainedItem = $magazine
        $weapon.CurrentAddress = $null
        if ($null -ne (& $requestSpares)) { throw 'A weapon outside the PMC inventory could issue magazines.' }
    } finally {
        $null = $unlocks.Remove($magId)
        $inventoryProperty.SetValue($null, $previousInventory)
        $revision.SetValue($null, $originalRevision)
    }
    Write-Output 'PASS  The weapon action requests empty stash copies of the fitted, unlocked magazine according to its footprint.'

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
