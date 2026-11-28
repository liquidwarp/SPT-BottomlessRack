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
$null = [Reflection.Assembly]::LoadFrom((Join-Path $installDir 'SPT_Runtime/Mono.Cecil.dll'))
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
    'EFT.Trading.Assortment' = @(
        'System.Void SelectItem(EFT.InventoryLogic.Item)'
        'System.Threading.Tasks.Task`1<Comfort.Common.IResult> Purchase()'
    )
    'AutoExchange' = @('System.Threading.Tasks.Task`1<System.Boolean> ExchangeCurrency(EFT.InventoryLogic.ECurrencyType,System.Int32)')
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
        'System.Void Show(EFT.IEftSession,EFT.InventoryLogic.BackEndInventoryController,EFT.HealthSystem.IHealthController,EFT.InventoryLogic.InventoryEquipment)'
        'System.Void SetCurrentBuild(EFT.UI.Builds.EquipmentBuild)'
        'System.Void UpdatePreview(System.Collections.Generic.IEnumerable`1<EFT.InventoryLogic.Item>)'
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
        'EFT.UI.EquipItemWindow' = @('_slot', '_viewList', '_placeHolder', '_inventoryController')
        'EFT.UI.DragAndDrop.GridItemView' = @('TagName', '_tagColor', 'Caption')
        'EFT.UI.DragAndDrop.TradingItemView' = @('_traderAssortment', '_price', '_currency', '_schemeIcon')
        'EFT.UI.TraderDealScreen' = @('_autoExchange', '_traderGridView', '_trader')
        'EFT.UI.ItemObserveScreen`2' = @('Item', 'RefreshingWeapon', '_manipulation')
        'EFT.UI.EditBuildScreen' = @('_itemBody', '_playerItems', '_acceptableWeaponBodies')
        'EFT.UI.EquipmentBuildsScreen' = @('_currentBuild', '_realInventoryController', '_isApplying', '_equipmentContext', '_equipmentTabs')
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

    $selection = $module.GetType('EFT.UI.EquipmentBuildsScreen').Methods | Where-Object Name -ceq 'SetCurrentBuild'
    $previewSteps = $selection.Body.Instructions.Operand | Where-Object {
        $_ -is [Mono.Cecil.MethodReference] -and $_.Name -cin 'TryApplyBuild', 'set_Interactable', 'set_sprite', 'UpdatePreview'
    }
    if (($previewSteps.Name -join ',') -cne 'TryApplyBuild,set_Interactable,set_sprite,UpdatePreview') {
        throw 'Preset selection no longer sets equip availability and the icon before drawing missing items.'
    }

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
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $installDir 'SPT_Runtime/0Harmony.dll'))
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $installDir 'BepInEx/plugins/spt/spt-common.dll'))
    $client = [Reflection.Assembly]::LoadFrom((Get-Item -LiteralPath $ClientAssembly).FullName)
    $null = [Reflection.Assembly]::LoadFrom($eftPath)
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $installDir 'EscapeFromTarkov_Data/Managed/Comfort.dll'))
    $flags = [Reflection.BindingFlags]'NonPublic, Static'
    $namespace = 'Liquidwarp.BottomlessRack.Client'
    $plugin = [Mono.Cecil.ModuleDefinition]::ReadModule($client.Location)
    try {
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
        $hover = $plugin.GetType("$namespace.TraderPurchases/Hover")
        $afterHover = $hover.Methods | Where-Object Name -ceq 'Postfix'
        if (!$afterHover -or $hover.Methods.Name -ccontains 'Prefix' -or $afterHover.Body.Instructions.Operand.Name -ccontains 'Show') {
            throw 'The unlocked price must preserve the trader tooltip that EFT and item-source mods just displayed.'
        }
    } finally {
        $plugin.Dispose()
    }

    function New-SaleTemplate([string]$id, [EFT.InventoryLogic.ItemTemplate]$parent) {
        $tpl = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.ItemTemplate])
        $tpl._id = [EFT.MongoID]$id
        $tpl._type = [EFT.InventoryLogic.NodeType]::Item
        $tpl.Width = $tpl.Height = 1
        $tpl.Parent = $parent
        return $tpl
    }

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
    $cache = $previewRows.GetField('ByTemplate', $flags).GetValue($null)
    $harmony = [HarmonyLib.Harmony]::new('bottomlessrack.tests.selectors')
    try {
        $harmony.PatchAll($client)
        foreach ($factory in @([EFT.UI.EquipItemWindow].GetMethod('RefreshItemViews'),
                               [EFT.UI.WeaponModding.DropDownMenu].GetMethod('CreateDropdownModdingItemView'),
                               [EFT.UI.WeaponModding.ModdingScreenSlotView].GetMethod('CreateModdingItemView'))) {
            $tags = [HarmonyLib.Harmony]::GetPatchInfo($factory).Postfixes | Where-Object owner -eq $harmony.Id
            if ($tags.priority -notcontains [HarmonyLib.Priority]::Last) {
                throw "$($factory.Name) must label the item after its location or price has been drawn."
            }
        }
        Add-Type @'
public static class CatalogueHttpFixture
{
    public static readonly System.Collections.Generic.List<object[]> Requests = new();
    public static string Reply;
    public static bool Respond(object[] __args, ref System.Threading.Tasks.Task<string> __result)
    {
        Requests.Add(__args);
        __result = System.Threading.Tasks.Task.FromResult(Reply);
        return false;
    }
}
'@
        $requests = [CatalogueHttpFixture]::Requests
        $requests.Clear()
        $respond = [HarmonyLib.HarmonyMethod]::new([CatalogueHttpFixture].GetMethod('Respond'))
        foreach ($method in 'GetJsonAsync', 'PostJsonAsync') {
            $null = $harmony.Patch([SPT.Common.Http.RequestHandler].GetMethod($method), $respond)
        }
        $app = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.TarkovApplication])
        $backend = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.EftClientBackend])
        $backend._session = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.EftClientBackendSession])
        [EFT.ClientApplication[EFT.IEftSession]].GetField('ClientBackEnd', [Reflection.BindingFlags]'NonPublic, Instance').SetValue($app, $backend)
        [Comfort.Common.Singleton[EFT.ClientApplication[EFT.IEftSession]]]::Instance = $app
        $menu = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.MainMenuShowOperation])
        $menuController = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.OfflineInventoryController])
        $weapon = '000000000000000000000055'
        $optic = $pickerItem.StringTemplateId
        $pmc = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.Profile])
        $scav = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.Profile])
        $pmc.Id = '000000000000000000000021'
        $scav.Id = '000000000000000000000022'
        [EFT.Profile].GetField('PetId').SetValue($pmc, [EFT.MongoID]$scav.Id)
        foreach ($character in $pmc, $scav) {
            [EFT.Profile].GetField('Encyclopedia').SetValue($character, [Collections.Generic.Dictionary[EFT.MongoID,bool]]::new())
        }
        $pmc.Encyclopedia.Add([EFT.MongoID]$weapon, $true)
        $instanceFields = [Reflection.BindingFlags]'Public, NonPublic, Instance'
        [EFT.ClientBackendSession].GetField('_profile', $instanceFields).SetValue($backend._session, $pmc)
        [EFT.ClientBackendSession].GetField('_allProfiles', $instanceFields).SetValue($backend._session, [EFT.Profile[]]@($pmc, $scav))
        $recipe = @(@{ templateId = $weapon; parentIndex = -1 }, @{ templateId = $optic; parentIndex = 0; slotId = 'mod_scope' })
        $full = @{ revision = 2; templates = @($weapon, $optic); examined = @($weapon, $optic); weaponBuilds = @{ $weapon = $recipe }; receipt = @{ sequence = 0 } }
        foreach ($data in $full, @{ revision = 2; receipt = @{ sequence = 1 } }) {
            [CatalogueHttpFixture]::Reply = @{ data = $data } | ConvertTo-Json -Depth 8 -Compress
            $menu.InventoryController = $menuController
            $null = $gear.GetProperty('CurrentRead', $flags).GetValue($null).GetAwaiter().GetResult()
            $parts = $gear.GetProperty('WeaponBuilds', $flags).GetValue($null)[$weapon]
            if (![object]::ReferenceEquals($gear.GetProperty('Inventory', $flags).GetValue($null), $menuController) -or
                !$unlocks.SetEquals([string[]]@($weapon, $optic)) -or $parts.Count -ne 2 -or $parts[1].TemplateId -ne $optic) {
                throw 'Both menu reads must leave the current inventory with its earned templates and weapon recipe.'
            }
        }
        if (!$pmc.Examined($pickerItem) -or !$scav.Examined($pickerItem) -or !$pmc.Encyclopedia[[EFT.MongoID]$weapon]) {
            throw 'Catalogue refresh must examine unlocked templates on both profiles and preserve read entries.'
        }
        if ($requests.Count -ne 2 -or $requests[0].Length -ne 1 -or $requests[0][0] -ne '/bottomlessrack/catalogue' -or
            $requests[1][0] -ne $requests[0][0] -or ($requests[1][1] | ConvertFrom-Json).knownRevision -ne 2) {
            throw 'Expected a bodyless catalogue read followed by a read with the cached revision.'
        }
        Add-Type -ReferencedAssemblies ([Comfort.Common.IResult].Assembly.Location) -TypeDefinition @'
public static class PurchaseFixture
{
    public static System.Threading.Tasks.Task<Comfort.Common.IResult> Reply;
    public static bool Respond(bool __runOriginal, ref System.Threading.Tasks.Task<Comfort.Common.IResult> __result)
    {
        if (__runOriginal) __result = Reply;
        return false;
    }
    public static bool Exchange(bool __runOriginal, ref System.Threading.Tasks.Task<bool> __result)
    {
        if (__runOriginal) __result = System.Threading.Tasks.Task.FromResult(true);
        return false;
    }
}
'@
        $stub = [HarmonyLib.HarmonyMethod]::new([PurchaseFixture].GetMethod('Respond'))
        $stub.priority = [HarmonyLib.Priority]::Last
        $null = $harmony.Patch([EFT.Trading.Assortment].GetMethod('Purchase'), $stub)
        $shopAssort = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.Trading.Assortment])
        $magazine = New-SaleTemplate '564ca99c4bdc2d16268b4589' (New-SaleTemplate '5448bc234bdc2d3c308b4569' $null)
        $purchase = [EFT.InventoryLogic.Item]::new('000000000000000000000056', $magazine)
        $shopAssort._selectedItem = $purchase
        $denied = [Comfort.Common.FailedResult]::new('Sold out', 0)
        [PurchaseFixture]::Reply = [Threading.Tasks.Task]::FromResult[Comfort.Common.IResult]($denied)
        if (![object]::ReferenceEquals($shopAssort.Purchase().GetAwaiter().GetResult(), $denied) -or $requests.Count -ne 2) {
            throw 'A rejected purchase changed its result or refreshed the catalogue.'
        }
        $full.revision = 3
        $full.templates += $purchase.StringTemplateId
        [CatalogueHttpFixture]::Reply = @{ data = $full } | ConvertTo-Json -Depth 8 -Compress
        $poll = [Threading.Tasks.TaskCompletionSource[bool]]::new()
        $gear.GetField('_loading', $flags).SetValue($null, $poll.Task)
        $payment = [Threading.Tasks.TaskCompletionSource[Comfort.Common.IResult]]::new()
        [PurchaseFixture]::Reply = $payment.Task
        $deal = $shopAssort.Purchase()
        $shopAssort._selectedItem = $null
        if ($deal.IsCompleted -or $requests.Count -ne 2) { throw 'An unconfirmed purchase refreshed the catalogue.' }
        $payment.SetResult([Comfort.Common.SuccessfulResult]::New)
        if (!$deal.GetAwaiter().GetResult().Succeed) { throw 'A successful purchase lost its result.' }
        $afterBuy = $gear.GetProperty('CurrentRead', $flags).GetValue($null)
        if ($afterBuy.IsCompleted -or $requests.Count -ne 2 -or $unlocks.Contains($purchase.StringTemplateId)) {
            throw 'A purchase used the catalogue from before delivery.'
        }
        $poll.SetResult($true)
        $null = $afterBuy.GetAwaiter().GetResult()
        if ($requests.Count -ne 3 -or ($requests[2][1] | ConvertFrom-Json).knownRevision -ne 2 -or !$unlocks.Contains($purchase.StringTemplateId)) {
            throw 'The first purchase did not refresh the catalogue after the earlier poll.'
        }
        Write-Output 'PASS  A gear purchase refreshes the catalogue after any earlier poll finishes.'
        $backend._session = $null
        $menu.InventoryController = $menuController
        $revision.SetValue($null, 1)
        $cache.Add($pickerItem.StringTemplateId, $pickerItem)
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
        $tableTemplate = [EFT.InventoryLogic.SortingTableTemplate]::new()
        $tableTemplate.Slots = @([EFT.InventoryLogic.Slot]::new('BuildSlot', $filters, $false, 'DontMerge'))
        $tableTemplate.Grids = @([EFT.InventoryLogic.Grid]::new('hideout', 0, 0, $true, $true, $filters, $null))
        $table = [EFT.InventoryLogic.SortingTable]::new('000000000000000000000021', $tableTemplate)
        $bindings = [Collections.Generic.Dictionary[EFT.InventoryLogic.EBoundItem,EFT.MongoID]]::new()
        $inventory = [EFT.InventoryLogic.Inventory]::new($equipment, $stash, $null, $null, $table, $null,
            $null, $bindings, $null, [Collections.Generic.List[EFT.MongoID]]::new(), $null, $false)
        $controller = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.BackEndInventoryController])
        [EFT.InventoryLogic.InventoryController].GetField('<Inventory>k__BackingField').SetValue($controller, $inventory)
        $controller._rootItem = $equipment
        $controller._searchController = $previewOwner.SearchController
        $controller._activeEvents = $previewOwner._activeEvents
        $controller._examined = $true
        $controller._restrictionsInLobby = [Collections.Generic.Dictionary[string,int]]::new()
        $table.CurrentAddress = $stash.CurrentAddress = $equipment.CurrentAddress = $controller.CreateItemAddress()
        foreach ($item in $body, $fittedItem) { $null = $stash.Grid.FindLocationForItem($item).Add($item, $false) }
        $live = [EFT.InventoryLogic.WeaponModdingManipulation]::new($controller, [EFT.InventoryLogic.CompoundItem[]]@($stash))
        $edit = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.EditBuildManipulation])
        $reason = $null
        $revision.SetValue($null, -1)
        # Native selection accepts the current item without needing Unity audio or a transaction.
        $slot.ContainedItem = $fittedItem
        if (!$live.Select($fittedItem, $slot, [ref]$reason)) { throw 'The owned copy was intercepted as a preview.' }
        $slot.ContainedItem = $pickerItem
        if (!$edit.Select($pickerItem, $slot, [ref]$reason)) { throw 'Edit Build tried to issue a preview.' }
        if ($live.Select($pickerItem, $slot, [ref]$reason) -or $null -eq $reason) {
            throw 'Live Modding accepted a stale catalogue click.'
        }
        $slot.ContainedItem = $null
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
            foreach ($operation in 'SelectCheck', 'Select') {
                $previewError = $null
                if ($live.$operation($pickerItem, $slot, [ref]$previewError) -or $previewError.GetType() -ne $reason.GetType() -or
                    $previewError.ConflictingItem -ne $blocker -or $slot.ContainedItem -ne $previous -or $pickerItem.Owner -ne $previewOwner) {
                    throw 'A blocked preview must reject hover and click with the same conflict as the stash copy and preserve both items.'
                }
            }
            $null = $nearbySlot.RemoveItemWithoutRestrictions()
            if (!$live.SelectCheck($fittedItem, $slot, [ref]$reason) -or !$live.SelectCheck($pickerItem, $slot, [ref]$reason) -or
                !$live.Select($pickerItem, $slot, [ref]$reason) -or $null -ne $reason -or
                $slot.ContainedItem -ne $previous -or $pickerItem.Owner -ne $previewOwner) {
                throw 'Removing the blocker must enable the preview, including when the part being replaced conflicts with it.'
            }
        }
        $null = $slot.RemoveItemWithoutRestrictions()
        # Execute the transactions locally so the real selector, swap and discard run without a backend session.
        Add-Type -ReferencedAssemblies @($eftPath, [Comfort.Common.IResult].Assembly.Location,
            "$PSHOME/ref/mscorlib.dll", "$PSHOME/ref/netstandard.dll", "$PSHOME/ref/System.Collections.dll") -TypeDefinition @'
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using System.Threading.Tasks;
public static class ModdingTransactionFixture
{
    public static readonly System.Collections.Generic.List<string> Operations = new();
    public static bool Execute(OperationResult operationResult, ref Task<IResult> __result)
    {
        Operations.Add(operationResult.Value.GetType().Name);
        var result = ((ISyncOperation)operationResult.Value).Execute();
        if (result.Failed) throw new System.Exception(result.Error.ToString());
        __result = Task.FromResult<IResult>(SuccessfulResult.New);
        return false;
    }
}
'@
        $runTransaction = [EFT.InventoryLogic.ItemController].GetMethod('TryRunNetworkTransaction')
        $fixture = [ModdingTransactionFixture].GetMethod('Execute')
        $null = $harmony.Patch($runTransaction, [HarmonyLib.HarmonyMethod]::new($fixture))
        try {
            $mountTemplate = [EFT.InventoryLogic.ModTemplate]::new()
            $mountTemplate._id = [EFT.MongoID]'000000000000000000000023'
            $mountTemplate.Width = $mountTemplate.Height = $mountTemplate.StackMaxSize = $mountTemplate.StackObjectsCount = 1
            $mountTemplate.ConflictingItems = [string[]]@()
            $mountTemplate.Slots = @([EFT.InventoryLogic.Slot]::new('mod_scope', $filters, $false, 'Merge'))
            $oldMount = [EFT.InventoryLogic.Mod]::new('000000000000000000000022', $mountTemplate)
            $oldMount.Slots[0].Add($oldSight)
            $slot.Add($oldMount)
            $nearbySlot.Add($blocker)
            if ($live.Select($fittedItem, $slot, $false, [ref]$reason) -or $slot.ContainedItem -ne $oldMount) {
                throw 'A blocked swap displaced the original mount.'
            }
            $null = $nearbySlot.RemoveItemWithoutRestrictions()
            if (!$live.SelectCheck($fittedItem, $slot, [ref]$reason) -or [ModdingTransactionFixture]::Operations.Count -ne 0) {
                throw 'Checking a swap submitted an inventory transaction.'
            }
            if (!$live.Select($fittedItem, $slot, $false, [ref]$reason) -or $slot.ContainedItem -ne $fittedItem -or
                $null -ne $oldMount.CurrentAddress -or $oldMount.Slots[0].ContainedItem -ne $oldSight -or
                ([ModdingTransactionFixture]::Operations -join ',') -cne 'SwapResult,DiscardResult') {
                throw 'A successful Modding swap must install the selected copy and discard the old tree.'
            }
            if (!$live.Select($null, $slot, $false, [ref]$reason) -or $null -ne $slot.ContainedItem -or
                $fittedItem.CurrentAddress.Container -ne $stash.Grid -or [ModdingTransactionFixture]::Operations[-1] -ne 'MoveResult') {
                throw 'Explicitly clearing a slot must return the fitted item to storage.'
            }
            Write-Output 'PASS  Live Modding discards the replaced tree after a successful swap and keeps explicitly removed parts.'

            # The assembler temporarily detaches a wanted optic while removing its old mount.
            $weaponTemplate = [EFT.InventoryLogic.WeaponTemplate]::new()
            $weaponTemplate._id = [EFT.MongoID]'000000000000000000000024'
            $weaponTemplate.Width = $weaponTemplate.Height = $weaponTemplate.StackMaxSize = $weaponTemplate.StackObjectsCount = 1
            $weaponTemplate.ConflictingItems = [string[]]@()
            $weaponTemplate.Slots = $bodyTemplate.Slots
            $weaponTemplate.Chambers = [EFT.InventoryLogic.Slot[]]@()
            $weaponTemplate.ammoCaliber = 'Caliber12g'
            [EFT.InventoryLogic.WeaponTemplate].GetField('weapFireType').SetValue($weaponTemplate,
                [EFT.InventoryLogic.Weapon+EFireMode[]]@('single'))
            $receiver = [EFT.InventoryLogic.Weapon]::new('000000000000000000000025', $weaponTemplate)
            $plan = [EFT.InventoryLogic.Weapon]::new('000000000000000000000026', $weaponTemplate)
            $null = $stash.Grid.FindLocationForItem($receiver).Add($receiver, $false)
            $null = $oldMount.Slots[0].RemoveItemWithoutRestrictions()
            $null = $stash.Grid.Remove($fittedItem, $false)
            $oldMount.Slots[0].Add($fittedItem)
            $receiver.Slots[0].Add($oldMount)
            $plan.Slots[1].Add([EFT.InventoryLogic.Mod]::new('000000000000000000000027', $template))
            $null = $table.Grid.FindLocationForItem($oldSight).Add($oldSight, $false)
            $donorTemplate = [EFT.InventoryLogic.ModTemplate]::new()
            $donorTemplate._id = [EFT.MongoID]'000000000000000000000028'
            $donorTemplate.Width = $donorTemplate.Height = $donorTemplate.StackMaxSize = $donorTemplate.StackObjectsCount = 1
            $donorTemplate.ConflictingItems = [string[]]@()
            $donorTemplate.Slots = $mountTemplate.Slots
            $donor = [EFT.InventoryLogic.Mod]::new('000000000000000000000029', $donorTemplate)
            $unused = [EFT.InventoryLogic.Mod]::new('000000000000000000000030', $mountTemplate)
            $donor.Slots[0].Add($unused)
            $null = $stash.Grid.FindLocationForItem($donor).Add($donor, $false)
            $plan.Slots[0].Add([EFT.InventoryLogic.Mod]::new('000000000000000000000031', $donorTemplate))
            $sources = [EFT.InventoryLogic.Item[]]@($donor)
            $native = [EFT.InventoryLogic.WeaponAssembler]::AssembleAsync($receiver, $plan, $controller, $sources, $true)
            if ($native.Failed -or !@($native.Value | Where-Object { $_.Item -eq $oldMount -and $_.To.Container -eq $stash.Grid }).Count) {
                throw 'Standalone assembly simulation should use normal storage.'
            }
            # Installing the donor frees just one cell, reserved for the unrelated item already on the table.
            $spares = [Collections.Generic.List[EFT.InventoryLogic.Item]]::new()
            while ($space = $stash.Grid.FindLocationForItem($oldMount)) {
                $spare = [EFT.InventoryLogic.Mod]::new([Guid]::NewGuid().ToString('N').Substring(8), $mountTemplate)
                $null = $space.Add($spare, $false)
                $spares.Add($spare)
            }
            $hook = $client.GetType("$namespace.AssemblyDiscards", $true)
            $arguments = [object[]]@($sources, $null)
            $hook.GetMethod('Prefix', $flags).Invoke($null, $arguments)
            $moves = [EFT.InventoryLogic.WeaponAssembler]::AssembleAsync($receiver, $plan, $controller, $arguments[0], $true)
            if ($moves.Failed -or $receiver.Slots[0].ContainedItem -ne $oldMount -or $oldMount.Slots[0].ContainedItem -ne $fittedItem) {
                throw "Assembly planning changed the original weapon or failed: $($moves.Error)"
            }
            foreach ($move in $moves.Value) {
                $applied = $move.Execute()
                if ($applied.Failed) { throw "Could not apply planned move: $($applied.Error)" }
            }
            [ModdingTransactionFixture]::Operations.Clear()
            foreach ($success in $false, $true) {
                $completion = [object[]]@($controller, $arguments[1], [Threading.Tasks.Task]::FromResult($success))
                $hook.GetMethod('Postfix', $flags).Invoke($null, $completion)
                if ($completion[2].GetAwaiter().GetResult() -ne $success) { throw 'Assembly result changed.' }
                if (!$success -and ($oldMount.CurrentAddress.Container -ne $table.Grid -or [ModdingTransactionFixture]::Operations.Count)) {
                    throw 'Failed assembly discarded a part or reserved stash space for it.'
                }
            }
            if ($null -ne $oldMount.CurrentAddress -or $null -ne $unused.CurrentAddress -or
                $receiver.Slots[0].ContainedItem -ne $donor -or $receiver.Slots[1].ContainedItem -ne $fittedItem -or
                $oldSight.CurrentAddress.Container -ne $stash.Grid -or ([ModdingTransactionFixture]::Operations -join ',') -cne 'DiscardResult,DiscardResult') {
                throw 'Assemble must discard the displaced mount and donor child, reuse the optic and preserve pre-existing table items.'
            }
            Write-Output 'PASS  Edit Build discards only successfully displaced parts, preserves reused attachments and stows unrelated table items.'
            # Restore the loose copy used by the following picker checks.
            foreach ($spare in $spares) {
                if ($spare.CurrentAddress.Container -ne $stash.Grid) { throw 'Assembly touched an unrelated loose copy.' }
                $null = $stash.Grid.Remove($spare, $false)
            }
            $null = $receiver.Slots[1].RemoveItemWithoutRestrictions()
            $null = $stash.Grid.FindLocationForItem($fittedItem).Add($fittedItem, $false)
            $null = $stash.Grid.Remove($receiver, $false)
            $null = $stash.Grid.Remove($oldSight, $false)

            $ammoTemplate = [EFT.InventoryLogic.AmmoTemplate]::new()
            [EFT.InventoryLogic.ItemTemplate].GetProperty('_id').SetValue($ammoTemplate, [EFT.MongoID]'000000000000000000000032')
            [EFT.InventoryLogic.ItemTemplate].GetField('StackObjectsCount').SetValue($ammoTemplate, 1)
            [EFT.InventoryLogic.ItemTemplate].GetField('StackMaxSize').SetValue($ammoTemplate, 50)
            [EFT.InventoryLogic.AmmoTemplate].GetField('casingSounds').SetValue($ammoTemplate, 'rifle')
            $magWeapon = [EFT.InventoryLogic.Weapon]::new('000000000000000000000033', $weaponTemplate)
            $barePlan = [EFT.InventoryLogic.Weapon]::new('000000000000000000000034', $weaponTemplate)
            $null = $stash.Grid.FindLocationForItem($magWeapon).Add($magWeapon, $false)
            foreach ($cylinder in $false, $true) {
                $magTemplate = if ($cylinder) { [EFT.InventoryLogic.CylinderMagazineTemplate]::new() }
                    else { [EFT.InventoryLogic.MagazineTemplate]::new() }
                $magTemplate._id = [EFT.MongoID]([Guid]::NewGuid().ToString('N').Substring(8))
                $magTemplate.Width = $magTemplate.Height = $magTemplate.StackMaxSize = $magTemplate.StackObjectsCount = 1
                $magTemplate.ConflictingItems = [string[]]@()
                $magTemplate.Cartridges = @([EFT.InventoryLogic.StackSlot]::new('cartridges', 5, $filters))
                if ($cylinder) { $magTemplate.Slots = @([EFT.InventoryLogic.Slot]::new('camora_000', $filters, $false, 'Merge')) }
                foreach ($assemble in $false, $true) {
                    foreach ($loaded in $false, $true) {
                        $id = [Guid]::NewGuid().ToString('N').Substring(8)
                        $mag = if ($cylinder) { [EFT.InventoryLogic.CylinderMagazine]::new($id, $magTemplate) }
                            else { [EFT.InventoryLogic.Magazine]::new($id, $magTemplate) }
                        if ($loaded) {
                            $round = [EFT.InventoryLogic.Ammo]::new([Guid]::NewGuid().ToString('N').Substring(8), $ammoTemplate)
                            if ($cylinder) { $mag.Camoras[0].Add($round) }
                            else { $null = $mag.Cartridges.Add($round, $false) }
                            if ($mag.Count -ne 1) { throw 'Magazine fixture must contain one round.' }
                        }
                        $magSlot = $magWeapon.Slots[0]
                        $magSlot.Add($mag)
                        $spares.Clear()
                        if ($loaded -and !$cylinder) {
                            while ($space = $stash.Grid.FindLocationForItem($oldMount)) {
                                $spare = [EFT.InventoryLogic.Mod]::new([Guid]::NewGuid().ToString('N').Substring(8), $mountTemplate)
                                if ($space.Add($spare, $false).Failed) { throw 'Could not fill the fixture stash.' }
                                $spares.Add($spare)
                            }
                            if ($assemble) {
                                $argsForPlan = [object[]]@([EFT.InventoryLogic.Item[]]@(), $null)
                                $hook.GetMethod('Prefix', $flags).Invoke($null, $argsForPlan)
                                $blocked = [EFT.InventoryLogic.WeaponAssembler]::AssembleAsync($magWeapon, $barePlan, $controller, $argsForPlan[0], $true)
                                if (!$blocked.Failed) { throw 'Assembly accepted a loaded magazine with no stash space.' }
                                foreach ($spare in $spares) { $null = $stash.Grid.Remove($spare, $false) }
                                $spares.Clear()
                            } elseif ($live.SelectCheck($pickerItem, $magSlot, [ref]$reason)) {
                                throw 'A preview swap accepted a loaded magazine with no stash space.'
                            }
                            if ($magSlot.ContainedItem -ne $mag -or $mag.Count -ne 1) { throw 'A failed placement disturbed the loaded magazine.' }
                        }
                        if ($assemble) {
                            $arguments = [object[]]@([EFT.InventoryLogic.Item[]]@(), $null)
                            $hook.GetMethod('Prefix', $flags).Invoke($null, $arguments)
                            $moves = [EFT.InventoryLogic.WeaponAssembler]::AssembleAsync($magWeapon, $barePlan, $controller, $arguments[0], $true)
                            if ($moves.Failed) { throw "Magazine removal plan failed: $($moves.Error)" }
                            foreach ($move in $moves.Value) {
                                $applied = $move.Execute()
                                if ($applied.Failed) { throw "Magazine removal move failed: $($applied.Error)" }
                            }
                            $completion = [object[]]@($controller, $arguments[1], [Threading.Tasks.Task]::FromResult($true))
                            $hook.GetMethod('Postfix', $flags).Invoke($null, $completion)
                            $null = $completion[2].GetAwaiter().GetResult()
                        } else {
                            if (!$live.Select($fittedItem, $magSlot, $false, [ref]$reason) -or $magSlot.ContainedItem -ne $fittedItem) {
                                throw "Magazine swap failed: $reason"
                            }
                            foreach ($spare in $spares) { $null = $stash.Grid.Remove($spare, $false) }
                            $null = $magSlot.RemoveItemWithoutRestrictions()
                            $null = $stash.Grid.FindLocationForItem($fittedItem).Add($fittedItem, $false)
                        }
                        if ($loaded) {
                            if ($mag.CurrentAddress.Container -ne $stash.Grid -or $mag.Count -ne 1 -or $round.Parent.Container.ParentItem -ne $mag) {
                                throw "Loaded magazine or its round lost (assemble=$assemble, cylinder=$cylinder)."
                            }
                            $null = $stash.Grid.Remove($mag, $false)
                        } elseif ($null -ne $mag.CurrentAddress) { throw 'An empty displaced magazine survived.' }
                    }
                }
            }
            $null = $stash.Grid.Remove($magWeapon, $false)
            Write-Output 'PASS  Modding and Assemble put partially loaded magazines and cylinders in the stash with their round, and discard empty ones.'
        } finally {
            $harmony.Unpatch($runTransaction, $fixture)
        }
        $id = $pickerItem.StringTemplateId
        $null = $unlocks.Add($id)
        $filter = [EFT.InventoryLogic.ItemFilter]::new()
        $filter.Filter = [EFT.MongoID[]]@($template._id)
        $pickerSlot = [EFT.InventoryLogic.Slot]::new('mod_scope', [EFT.InventoryLogic.ItemFilter[]]@($filter), $false, 'Merge')
        foreach ($editBuild in $false, $true) {
            foreach ($hasOwned in $false, $true) {
                $collections = [EFT.InventoryLogic.CompoundItem[]]@()
                if ($hasOwned) { $collections = [EFT.InventoryLogic.CompoundItem[]]@($stash) }
                $selector = if ($editBuild) { [EFT.InventoryLogic.EditBuildManipulation]::new($controller, $collections, $null, $null, $false) }
                    else { [EFT.InventoryLogic.WeaponModdingManipulation]::new($controller, $collections) }
                $rows = @($selector.GetItemCollections($pickerSlot))
                $expected = if ($hasOwned) { $fittedItem } else { $pickerItem }
                if ($rows.Count -ne 1 -or $rows[0] -ne $expected) {
                    throw "Wrong picker row (Edit Build=$editBuild, owned=$hasOwned)."
                }
            }
        }
        # Exercise both picker modes with the same owned rows, including a matching copy outside the stash.
        $emptyMount = [EFT.InventoryLogic.Mod]::new('000000000000000000000071', $mountTemplate)
        $mountId = $emptyMount.StringTemplateId
        $cache.Add($mountId, $emptyMount)
        $null = $unlocks.Add($mountId)
        $filter.Filter = [EFT.MongoID[]]@($mountTemplate._id)
        $otherMount = [EFT.InventoryLogic.Mod]::new('000000000000000000000072', $mountTemplate)
        $bareMount = [EFT.InventoryLogic.Mod]::new('000000000000000000000073', $mountTemplate)
        $otherMount.Slots[0].Add($oldSight)
        $null = $stash.Grid.FindLocationForItem($otherMount).Add($otherMount, $false)
        $null = $table.Grid.FindLocationForItem($bareMount).Add($bareMount, $false)
        $available = [EFT.InventoryLogic.Item[]]@($otherMount, $bareMount)
        $include = $previewRows.GetMethod('Include', $flags)
        foreach ($inStash in $false, $true) {
            if ($inStash) {
                $null = $table.Grid.Remove($bareMount, $false)
                $null = $stash.Grid.FindLocationForItem($bareMount).Add($bareMount, $false)
            }
            foreach ($equipmentPicker in $false, $true) {
                $rows = @($include.Invoke($null, [object[]]@($available, $pickerSlot, $stash, $false, $equipmentPicker)))
                if ($rows[0] -ne $otherMount -or $rows[1] -ne $bareMount -or
                    ($inStash -and $rows.Count -ne 2) -or
                    (!$inStash -and ($rows.Count -ne 3 -or $rows[2] -ne $emptyMount))) {
                    throw "Only a matching mount in the stash should hide the bare preview (equipment=$equipmentPicker, in stash=$inStash)."
                }
            }
        }
        $null = $cache.Remove($mountId)
        $null = $unlocks.Remove($mountId)
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
        $harmony.UnpatchSelf()
        [Comfort.Common.Singleton[EFT.ClientApplication[EFT.IEftSession]]]::Instance = $null
        $gear.GetProperty('Inventory', $flags).SetValue($null, $null)
        $null = $cache.Remove($pickerItem.StringTemplateId)
        $null = $unlocks.Remove($pickerItem.StringTemplateId)
        $revision.SetValue($null, $originalRevision)
    }
    Write-Output 'PASS  An earned template labels both Edit Build item instances and leaves ordinary Modding rows alone.'
    Write-Output 'PASS  The first menu entry loads the catalogue. Reentry sends its revision and preserves its templates and weapon recipe.'
    Write-Output 'PASS  Only the exact preview click in live Modding requires a ready catalogue.'
    Write-Output 'PASS  A conflicting attachment blocks preview hover and click with the stash error, and removing it permits a move or swap.'
    Write-Output 'PASS  Equipment and live Modding hide the preview for a matching stash build. Edit Build adds only missing templates.'

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
    $screenTrader = [EFT.UI.TraderDealScreen].GetField('_trader', $privateInstance)
    $screenTrader.SetValue($screen, $trader)
    $shopType.GetField('_catalogueRead', $privateInstance).SetValue($shop, $read.Task)
    $marked = $shopType.GetMethod('Marked', $flags)
    $select = $client.GetType("$namespace.TraderPurchases+Select").GetMethod('Prefix', $flags)
    $validate = $client.GetType("$namespace.TraderPurchases+Validate").GetMethod('Prefix', $flags)
    $canDeal = { $validate.Invoke($null, [object[]]@($screen, $false, $null, $false)) }
    $exchange = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([AutoExchange])
    [EFT.UI.TraderDealScreen].GetField('_autoExchange', $privateInstance).SetValue($screen, $exchange)
    function Assert-DealGuards($assortment, $converter, [bool]$allowed) {
        $converted = $converter.ExchangeCurrency([EFT.InventoryLogic.ECurrencyType]::USD, 1).GetAwaiter().GetResult()
        $result = $assortment.Purchase().GetAwaiter().GetResult()
        if ($converted -ne $allowed -or [object]::ReferenceEquals($result, $denied) -ne $allowed) {
            throw "Expected both calls to pass through = $allowed at line $($MyInvocation.ScriptLineNumber). Exchange returned $converted and purchase returned '$($result.Error)'."
        }
        if (!$allowed -and $result -isnot [Comfort.Common.FailedResult]) { throw 'Blocked purchase must return a failure.' }
    }
    $medkitTemplate = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.InventoryLogic.ItemTemplate])
    $medkitTemplate._id = [EFT.MongoID]'000000000000000000000031'
    $medkit = [EFT.InventoryLogic.Item]::new('000000000000000000000032', $medkitTemplate)
    $previousShop = $activeShop.GetValue($null)
    try {
        foreach ($patch in 'Exchange', 'Purchase') {
            $null = $harmony.CreateClassProcessor($client.GetType("$namespace.TraderPurchases+$patch")).Patch()
        }
        # Last-priority fixtures stand in for EFT spending or buying if the mod allows the call.
        $exchangeStub = [HarmonyLib.HarmonyMethod]::new([PurchaseFixture].GetMethod('Exchange'))
        $exchangeStub.priority = [HarmonyLib.Priority]::Last
        $null = $harmony.Patch([AutoExchange].GetMethod('ExchangeCurrency'), $exchangeStub)
        $null = $harmony.Patch([EFT.Trading.Assortment].GetMethod('Purchase'), $stub)
        [PurchaseFixture]::Reply = [Threading.Tasks.Task]::FromResult[Comfort.Common.IResult]($denied)
        $activeShop.SetValue($null, $shop)
        $assort._selectedItem = $medkit
        $revision.SetValue($null, -1)
        if (& $canDeal) { throw 'Deal accepted a purchase during the first catalogue read.' }
        Assert-DealGuards $assort $exchange $false
        $revision.SetValue($null, 1)
        if (& $canDeal) { throw 'Deal accepted a second purchase while the catalogue was refreshing after a purchase.' }
        Assert-DealGuards $assort $exchange $false
        $revision.SetValue($null, -1)
        $read.SetResult($false)
        if (-not (& $canDeal)) { throw 'A failed first read disabled a fresh priced purchase.' }
        Assert-DealGuards $assort $exchange $true
        $revision.SetValue($null, 1)
        $assort._selectedItem = $pickerItem
        $viewItem.SetValue($stockView, $pickerItem)
        if ($marked.Invoke($null, @($stockView, $assort)) -or !(& $canDeal)) { throw 'An unowned offer was blocked before its first purchase.' }
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
            Assert-DealGuards $assort $exchange (!$blocked)
            if ($marked.Invoke($null, @($stockView, $assort)) -ne $blocked -or
                ($null -eq $selection[1]) -ne $blocked -or (& $canDeal) -eq $blocked) {
                throw "Trader price, selection or Deal disagrees for output $($outputItem.Id), payment $paymentTemplate."
            }
        }
        $assort._selectedItem = $null
        Assert-DealGuards $assort $exchange $false
        $assort._selectedItem = $medkit
        $null = $unlocks.Add($medkit.StringTemplateId)
        Assert-DealGuards $assort $exchange $false
        $otherAssort = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.Trading.Assortment])
        $otherAssort._selectedItem = $medkit
        $otherExchange = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([AutoExchange])
        Assert-DealGuards $otherAssort $otherExchange $true
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
        Assert-DealGuards $assort $exchange $true
        $screen.TradeMode = [EFT.UI.ETradeMode]::Purchase
        $shopType.GetField('_catalogueRead', $privateInstance).SetValue($shop, $null) # Fence skips WatchRead.
        Assert-DealGuards $assort $exchange $true
        $shopType.GetField('_catalogueRead', $privateInstance).SetValue($shop, $read.Task)
        $activeShop.SetValue($null, $null)
        Assert-DealGuards $assort $exchange $true
    } finally {
        $harmony.UnpatchSelf()
        $activeShop.SetValue($null, $previousShop)
        $null = $unlocks.Remove($pickerItem.StringTemplateId)
        $null = $unlocks.Remove($medkit.StringTemplateId)
        $revision.SetValue($null, $originalRevision)
    }
    Write-Output 'PASS  Trader costs, selection and Deal follow the offered template for cash and both barter directions.'
    Write-Output 'PASS  Currency conversion and purchase recheck the current offer only on the active non-Fence Buy tab.'

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
    $quoteCheck = $client.GetType("$namespace.GearSales+SellQuote").GetMethod('Prefix', $flags)
    $confirmSale = $client.GetType("$namespace.GearSales+Sell").GetMethod('Prefix', $flags)
    $listingCheck = $client.GetType("$namespace.GearSales+SelectOffer").GetMethod('Prefix', $flags)
    $confirmListing = $client.GetType("$namespace.GearSales+ConfirmOffer").GetMethod('Prefix', $flags)
    $context = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.UI.RagfairNewOfferContext])
    $context._selectedItems = [Collections.Generic.HashSet[EFT.InventoryLogic.Item]]::new()
    $null = $context._selectedItems.Add($box)
    $assort._clones = [Collections.Generic.Dictionary[EFT.InventoryLogic.Item,EFT.InventoryLogic.Item]]::new()
    $assort._clones.Add($medkit, $box)
    $openShop = $shopType.GetMethod('Prefix', $flags)
    $oldLifetime = [EFT.UI.UIParent]::new()
    $lifetime = [EFT.UI.UIParent]::new()
    try {
        # Rebinding the same trader must survive cleanup from the previous screen lifetime.
        [EFT.Trading.Trader].GetField('Id').SetValue($trader, '579dc571d53a0658a154fbec')
        $screen.TradeMode = [EFT.UI.ETradeMode]::Purchase
        $openShop.Invoke($null, [object[]]@($screen, $null, $oldLifetime))
        $screenTrader.SetValue($screen, $null)
        $openShop.Invoke($null, [object[]]@($screen, $null, $lifetime))
        # EFT assigns the trader inside Show before binding the first quote.
        $screenTrader.SetValue($screen, $trader)
        $oldLifetime.Dispose()
        if ($quoteCheck.Invoke($null, [object[]]@($trader, $part, $null))) { throw 'Fence lost its sale scope before binding stock.' }
        $shopType.GetMethod('Postfix', $flags).Invoke($null, [object[]]@($null))
        if ($null -ne $shopType.GetProperty('BuyTab', $flags).GetValue($null)) { throw 'Fence activated the purchase restriction.' }
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
        $lifetime.Dispose()
        if (!$quoteCheck.Invoke($null, [object[]]@($trader, $part, $null))) { throw 'Fence kept the PMC sale filter for the Scav transfer.' }
    } finally {
        $oldLifetime.Dispose()
        $lifetime.Dispose()
    }
    Write-Output 'PASS  PMC quotes and flea offers check live contents and template exclusions. Fence permits purchases and restores Scav quotes on close.'

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

    $preview = $client.GetType("$namespace.EquipmentPresets+Preview").GetMethod('Prefix', $flags)
    $presetScreen = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.UI.EquipmentBuildsScreen])
    $presetScreen._equipButton = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([EFT.UI.DefaultUIButton])
    $presetScreen._equipBuildIcon = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([UnityEngine.UI.Image])
    $unavailable = [EFT.UI.InteractableElement].GetField('_unavailable', 'Instance, Public, NonPublic')
    $mosin = [EFT.InventoryLogic.WeaponTemplate]::new()
    $mosin._id = [EFT.MongoID]'5bfd297f0db834001a669119'
    [EFT.InventoryLogic.Item].GetField('<Template>k__BackingField').SetValue($weapon, $mosin)
    $round.StackObjectsCount = 3
    $mixed = [Collections.Generic.List[EFT.InventoryLogic.Item]]::new([EFT.InventoryLogic.Item[]]@($weapon, $round))
    $rifleOnly = [Collections.Generic.List[EFT.InventoryLogic.Item]]::new([EFT.InventoryLogic.Item[]]@($weapon))
    try {
        $inventoryProperty.SetValue($null, $inventory)
        $null = $unlocks.Add($weapon.StringTemplateId)
        foreach ($case in @(
            @('Standard', $mixed, 1, $inventory, $false, 1),
            @('Custom', $mixed, 1, $inventory, $false, 1),
            @('Standard', $rifleOnly, 1, $inventory, $false, 0),
            @('Storage', $mixed, 1, $inventory, $false, 2),
            @('Custom', $mixed, -1, $inventory, $false, 2),
            @('Custom', $mixed, 1, $controller, $false, 2),
            @('Custom', $mixed, 1, $inventory, $true, 2)
        )) {
            $kind, $shortages, $catalogueRevision, $owner, $disabled, $expected = $case
            $build = [EFT.UI.Builds.EquipmentBuild]::new([EFT.MongoID]'000000000000000000000070', 'Sharpshooter', $kind)
            $revision.SetValue($null, $catalogueRevision)
            $unavailable.SetValue($presetScreen._equipButton, $disabled)
            $values = [object[]]@($presetScreen, $build, $owner, $shortages)
            $preview.Invoke($null, $values)
            $visible = $values[3]
            if ($visible.Count -ne $expected -or ($expected -eq 1 -and
                (![object]::ReferenceEquals($visible[0], $round) -or $visible[0].StackObjectsCount -ne 3)) -or
                ($expected -eq 2 -and ![object]::ReferenceEquals($visible, $shortages))) {
                throw "Wrong missing items in the $kind preset preview (revision $catalogueRevision, disabled $disabled)."
            }
        }
        if ($mixed.Count -ne 2 -or $rifleOnly.Count -ne 1 -or $mixed[0] -ne $weapon) {
            throw 'Drawing a preset changed the inventory simulation used to supply gear.'
        }
    } finally {
        $null = $unlocks.Remove($weapon.StringTemplateId)
        $inventoryProperty.SetValue($null, $previousInventory)
        $revision.SetValue($null, $originalRevision)
    }
    Write-Output 'PASS  Standard and custom previews hide the unlocked Mosin and keep the exact missing ammunition stack. Storage, catalogue and simulation guards pass through.'

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
