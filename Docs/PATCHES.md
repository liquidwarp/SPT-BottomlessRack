# Harmony patch inventory

BottomlessRack patches 43 runtime targets, 40 in the EFT client and 3 in the SPT server. Each row names the patch class, the original method, when the hook runs, and the feature it supports. A target with both a prefix and a postfix counts once. The two assemblies register their patches at startup. [Client registration](../Client/Plugin.cs) · [Server registration](../Server/Lifecycle.cs)

## Server ownership scans and sale enforcement

Gear earns unlocks when a scan finds it in the PMC’s equipment, stash, sorting table, or supported hideout displays, including nested contents. Each new unlock also examines the template for the PMC and Scav. Scav loot qualifies after transfer to the PMC. Catalogue reads also trigger a scan. The client checks that it is out of raid before sending a spawn request, and the server handles the grant through an SPT item-event route. [Lifecycle patches](../Server/Lifecycle.cs) · [Discovery](../Server/Discovery.cs) · [Gear service](../Server/GearService.cs) · [Routes](../Server/Routers.cs)

| Patch | Target | Hook | Feature |
| --- | --- | --- | --- |
| `ProfilePatch` | `ProfileCallbacks.GetProfileData` | Postfix | Scans the PMC’s holdings under the profile lock when SPT serves the profile. This seeds existing profiles and picks up newly owned gear. |
| `InventoryBatchPatch` | `ItemEventCallbacks.HandleEvents` | Prefix + postfix | Acquires the profile lock before an inventory command batch and releases it when the asynchronous batch finishes. Catalogue reads and other batches wait until the inventory changes are complete. |
| `InventoryActionPatch` | `ItemEventRouter.HandleItemEvent` | Prefix + postfix | Scans ownership before each command and again after a warning-free result, catching purchases, mail claims, and items about to leave inventory. Rejects PMC trader sales and flea listings containing covered gear. Associates sale and spawn warnings with the command that caused them. |

## Client inspection, Workbench access, and spare magazines

The main menu loads the catalogue early enough to mark loot inspected during a raid. The Workbench and magazine actions apply out of raid. [Inspect descriptions](../Client/InspectDescription.cs) · [Workbench access](../Client/WorkbenchAccess.cs) · [Spare magazines](../Client/SpareMagazines.cs)

| Patch | Target | Hook | Feature |
| --- | --- | --- | --- |
| `MenuCataloguePatch` | `MainMenuShowOperation.InventoryController` setter | Postfix | Loads the PMC’s catalogue as soon as EFT supplies the menu inventory, including when the player goes straight into a raid. |
| `InspectDescription` | `ItemSpecificationPanel.method_1` | Postfix | Prepends **UNLOCKED** to the localized description when the panel opens or examination finishes. Uses cached unlocks in raid and updates an open panel when a catalogue read brings in the item. |
| `WeaponModificationBonusPatch` | `BonusController.HasBonus` | Postfix | Grants the weapon-modification bonus for the stash context menu, making Modding and Edit Build available before Workbench construction. |
| `EditBuildTaskBarPatch` | `MenuTaskBar.BonusChangedHandler` | Prefix | Supplies the same active bonus to the taskbar’s separate event handler so the bottom-menu Edit Build button is available. |
| `SpareMagazines` | `InventoryItemContextInteractions(ItemContext, ItemUiContext, CompoundItem[], Action)` constructor | Postfix | Adds **Spawn spare magazines** to an owned weapon with an unlocked detachable magazine fitted. Rechecks the fitted model on click and issues empty copies to the main stash. A 1×1 gives three, a 1×2 or 1×3 gives two in either orientation, and other shapes give one. |

## Client equipment-slot picker

The picker displays a catalogue entry as a preview. Selecting it requests a real item for the main stash, then equips that copy. [Selector patches](../Client/Selectors.cs)

| Patch | Target | Hook | Feature |
| --- | --- | --- | --- |
| `StashPatch` | `InventoryScreen.Show(InventoryScreenController)` | Postfix | Binds the inventory controller and refreshes the catalogue when an accessible out-of-raid inventory opens. |
| `EquipRowsPatch` | `EquipItemWindow.GetAvailableItems` | Postfix | Adds a compatible unlocked preview when no offered stash item matches its full tree. Weapons use their standard build. Refreshes rows when the catalogue changes. |
| `EquipPreviewTagPatch` | `EquipItemWindow.RefreshItemViews` | Postfix | Tags preview rows **Unlocked** after EFT binds their views and hides the empty-list message when previews are available. |
| `EquipAvailabilityPatch` | `EquipItemWindow` implementation of `SelectableItemContext.ISelectionContext.IsActive` | Prefix | Enables preview selection when the catalogue is ready and shows **Spawn to stash and equip** on hover. |
| `EquipSelectionPatch` | `EquipItemWindow.ItemSelectedHandler` | Prefix | Requests one copy, then passes it to EFT’s equip action if the window, slot, and previous item still match. An issued copy stays in the stash if equip fails. |

## Client Modding and Edit Build

Live Modding fits a physical attachment. Edit Build lets the player plan a weapon and request missing unlocked parts when they click **Assemble**. [Selector patches](../Client/Selectors.cs) · [Assembly patches](../Client/BuildAssembly.cs)

| Patch | Target | Hook | Feature |
| --- | --- | --- | --- |
| `ModdingScreenPatch` | `WeaponModdingScreen.Show(Item, InventoryController, CompoundItem[])` | Postfix | Tracks the active weapon screen and starts catalogue refreshes, so later installs can verify their destination. |
| `EquippedModdingSlotPatch` | `ModdingScreenSlotView.Show` | Postfix | Restores hover and click on an equipped weapon’s vital slots when the slot and any containing equipment slot permit editing. |
| `ModdingRowsPatch` | `WeaponModdingManipulation.GetItemCollections` | Postfix | Adds compatible unlocked attachment previews when no offered stash part matches the preview and its contents. |
| `ReplaceModdingPartPatch` | `WeaponModdingManipulation.Select(Item, Slot, bool, out Error)` | Prefix | Simulates replacement of a fitted part on any gear. On click, swaps a loaded magazine directly into the stash. Other displaced parts are discarded after the swap succeeds. |
| `BuildRowsPatch` | `EditBuildManipulation.GetItemCollections` | Postfix | Adds compatible unlocked templates missing from the build editor’s choices, avoiding duplicates from EFT or other providers. |
| `InstallPatch` | `DropdownManipulation.Select(Item, Slot, out Error)` | Prefix | Checks a live preview click for fitting conflicts before issuing a part. On success it fits the real copy if the screen, weapon, and slot still match. |
| `ModdingPreviewLabelPatch` | `DropDownMenu.CreateDropdownModdingItemView` | Postfix | Labels catalogue previews and unlocked Edit Build choices **Unlocked** and hides Trader Modding price tags on those rows. |
| `ModdingPreviewLabelPatch.FittedPart` | `ModdingScreenSlotView.CreateModdingItemView` | Postfix | Applies the same badge and price-tag handling to the fitted-part view, so unlocks are visible on the build itself. |
| `BuildScreenPatch` | `EditBuildScreen.Show(Item, Item, InventoryController, IEftSession)` | Postfix | Loads the catalogue for Edit Build and reruns the assembly-availability check when unlocks change. |
| `UnlockedWeaponBodyPatch` | `EditBuildScreen.CheckForVitalParts` | Postfix | Enables Assemble when EFT found no suitable body and the catalogue can supply the planned weapon. |
| `MissingBuildItemsPatch` | `EditBuildScreen.GetMissingBuildItems` | Postfix | Corrects shortage quantities for unlocked parts, accounting for installed parts, loose copies, and children fitted to those copies. Each physical part counts once. |
| `BuildAssembly` | `EditBuildScreen.TryToAssembleBuild` | Prefix | Uses an owned body when available, can issue the weapon’s standard build, then requests only missing unlocked parts. Hands the result to EFT’s assembler or opens its purchase window for remaining locked shortages. |
| `AssemblyDiscards` | `WeaponAssembler.Assemble` | Prefix + postfix | Tracks one live assembly and discards eligible removed parts after all native moves succeed, rechecking for loaded magazines. [Source](../Client/AssemblyDiscards.cs) |
| `AssemblyDiscards.Plan` | `WeaponAssembler.AssembleAsync` | Transpiler | Keeps rounds in a removed cylinder and sends loaded magazines through EFT’s stash placement. Detached parts due for discard stay on the sorting table until assembly completes. [Source](../Client/AssemblyDiscards.cs) |

## Client equipment presets

These hooks serve stash-backed loadouts. Preview and Equip share the same search for matching physical gear. [Equipment preset patches](../Client/EquipmentPresets.cs) · [Preset item matching](../Client/PresetItems.cs)

| Patch | Target | Hook | Feature |
| --- | --- | --- | --- |
| `EquipmentPresets.Open` | `EquipmentBuildsScreen.Show(IEftSession, BackEndInventoryController, IHealthController, InventoryEquipment)` | Prefix + postfix | Starts the catalogue read before the screen opens. Refreshes the displayed preset when missing gear becomes available and preserves the selected Containers tab. |
| `EquipmentPresets.Preview` | `EquipmentBuildsScreen.UpdatePreview` | Prefix | Removes freely issuable gear from the displayed shortages and shows the ready-to-equip icon if all shortages can be supplied. |
| `PresetItems.Find` | `ItemManipulator.FindUserItems` | Postfix | Filters candidates to loose supplies and complete matching builds. Checks fitted slots, packed positions, and loaded ammunition, and reserves each newly issued body for its intended preset entry. |
| `PresetItems.Transfer` | `ItemManipulator.TransferItemsFromPreset` | Prefix | Moves a complete matching item intact, preserving condition, fitted parts, and contents across preset switches. |
| `EquipmentPresets` | `EquipmentBuildsScreen.EquipBuild` | Prefix | Simulates the loadout, offers to buy locked shortages, and batches missing unlocked supplies, including bare weapon bodies. **Equip available** issues supplies only for entries it can complete. |

## Client trader purchases

The Buy tab uses the unlock status of the item the trader gives the player. Unlocked gear can still serve as barter payment. Fence uses ordinary purchase prices. [Trader purchase patches](../Client/TraderPurchases.cs)

| Patch | Target | Hook | Feature |
| --- | --- | --- | --- |
| `TraderPurchases` | `TraderDealScreen.Show` | Prefix + postfix | Establishes which open shop the sale checks apply to, including Fence. For other traders it also watches the catalogue, repaints offers, and clears a staged purchase that becomes free. |
| `TraderPurchases.Price` | `TradingItemView.SetPrice` | Postfix | Replaces the cash price or barter requirement on a freely issuable offer with **Unlocked**. |
| `TraderPurchases.Hover` | `TradingItemView.ShowTooltip` | Postfix | Shows **Free in Modding** for an attachment or **Free from inventory** for other gear in the price tooltip. |
| `TraderPurchases.Select` | `Assortment.SelectItem` | Prefix | Clears the selection when the player chooses an unlocked offer, including through a shortcut. This also clears any previously staged deal. |
| `TraderPurchases.Validate` | `TraderDealScreen.IsValidDeal` | Prefix | Disables Deal and automatic exchange while the relevant catalogue read is pending, the selection is empty, or the selected offer is unlocked. |
| `TraderPurchases.Exchange` | `AutoExchange.ExchangeCurrency` | Prefix | Refuses conversion on the active non-Fence Buy tab if the catalogue read is pending or the selected offer is empty or unlocked. |
| `TraderPurchases.Purchase` | `Assortment.Purchase` | Prefix + postfix | Applies the same check to the active Buy assortment before submission. Captures whether the selected offer contains covered gear, including inside a bundle, and refreshes the catalogue after a successful gear purchase. |

## Client PMC trader sales and flea listings

The restriction covers gear in the mod’s categories and containers holding it, regardless of unlock status. It applies at Fence during PMC trading. Post-raid Scav **Sell All** uses its normal payout. The server enforces the same PMC restriction in `InventoryActionPatch`. [Sale patches](../Client/GearSales.cs)

| Patch | Target | Hook | Feature |
| --- | --- | --- | --- |
| `GearSales.SellQuote` | `Trader.GetUserItemPrice` | Prefix | Makes the current trader decline to quote a price for a restricted item or container. |
| `GearSales.Sell` | `Assortment.Sell` | Prefix | Checks the originals behind the staged sale copies and cancels the whole sale with a warning if any contains covered gear. |
| `GearSales.SelectOffer` | `RagFair.CanBeSelectedAtRagfair` | Prefix | Blocks selecting a restricted item for a flea listing and supplies the reason to the UI. |
| `GearSales.ConfirmOffer` | `AddOfferWindow.CheckRestrictions` | Prefix | Checks every selected item at confirmation, catching restricted contents in the final offer. |

## Test-only hooks

- `ItemController.TryRunNetworkTransaction` executes transactions against an in-memory inventory for the live Modding and Edit Build discard checks. [Contract tests](../Tests/Test-Contracts.ps1)
- `RequestHandler.GetJsonAsync` and `RequestHandler.PostJsonAsync` record catalogue requests and return fixture replies. Prefixes on `Assortment.Purchase` and `AutoExchange.ExchangeCurrency` supply controlled outcomes when the trader guards allow the call. [Contract tests](../Tests/Test-Contracts.ps1)
- `EquipmentBuildsScreen.TryApplyBuild` counts simulations. `EquipmentBuildsScreen.ApplyBuild` counts applications and skips the real submission so the fixture can exercise transfers in memory. [Equipment preset tests](../Tests/Test-EquipmentPresets.ps1)
