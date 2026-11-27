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
        'EFT.UI.EquipItemWindow' = @('_slot', '_viewList', '_placeHolder')
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
        $recipe = @(@{ templateId = $weapon; parentIndex = -1 }, @{ templateId = $optic; parentIndex = 0; slotId = 'mod_scope' })
        $full = @{ revision = 2; templates = @($weapon, $optic); weaponBuilds = @{ $weapon = $recipe }; receipt = @{ sequence = 0 } }
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
        if ($requests.Count -ne 2 -or $requests[0].Length -ne 1 -or $requests[0][0] -ne '/bottomlessrack/catalogue' -or
            $requests[1][0] -ne $requests[0][0] -or ($requests[1][1] | ConvertFrom-Json).knownRevision -ne 2) {
            throw 'Expected a bodyless catalogue read followed by a read with the cached revision.'
        }
        Add-Type -ReferencedAssemblies ([Comfort.Common.IResult].Assembly.Location) -TypeDefinition @'
public static class PurchaseFixture
{
    public static System.Threading.Tasks.Task<Comfort.Common.IResult> Reply;
    public static bool Respond(ref System.Threading.Tasks.Task<Comfort.Common.IResult> __result)
    {
        __result = Reply;
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
    $screenTrader = [EFT.UI.TraderDealScreen].GetField('_trader', $privateInstance)
    $screenTrader.SetValue($screen, $trader)
    $shopType.GetField('_catalogueRead', $privateInstance).SetValue($shop, $read.Task)
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
        $revision.SetValue($null, 1)
        if (& $canDeal) { throw 'Deal accepted a second purchase while the catalogue was refreshing after a purchase.' }
        $revision.SetValue($null, -1)
        $read.SetResult($false)
        if (-not (& $canDeal)) { throw 'A failed first read disabled a fresh priced purchase.' }
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
    } finally {
        $activeShop.SetValue($null, $previousShop)
        $null = $unlocks.Remove($pickerItem.StringTemplateId)
        $null = $unlocks.Remove($medkit.StringTemplateId)
        $revision.SetValue($null, $originalRevision)
    }
    Write-Output 'PASS  Trader costs, selection and Deal follow the offered template for cash and both barter directions.'
    Write-Output 'PASS  Catalogue loading, a stale exchange quote, the stash and Sell use the intended purchase rules.'

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
