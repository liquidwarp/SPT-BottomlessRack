# BottomlessRack

This design gives each PMC in SPT 4.1 a permanent equipment catalogue. Once a trader other than Fence offers a piece of gear to that PMC, or the player brings one home, the player can create fresh copies through EFT’s equipment picker, Modding, and Edit Build. The unlock survives the loss of the last copy.

For example, extract with a customized rifle and the weapon-slot picker gains one **Unlocked** entry for its standard build. The parts fitted to that build unlock immediately, even if the recovered rifle used different parts. The recovered optic and mount also become individual choices in Modding and Edit Build, even after the player loses the rifle in another raid.

## What earns an unlock

Unlocks use EFT template IDs. The first version covers weapons, attachments, magazines, armor, removable plates, and wearable equipment such as rigs, backpacks, helmets, headsets, face covers, eyewear, and armbands. Use the loaded template hierarchy so modded gear in these families participates. Ammo, grenades, consumables, currency, loose barter goods, and quest-only objects follow ordinary acquisition rules.

| Source | When the catalogue gains the gear |
| --- | --- |
| Trader assort | Use the offers of accessible traders other than Fence, including modded traders. Cash and barter offers both count at the PMC’s loyalty level and quest state, including the gear supplied in a bundle. Observe eligible rotations during the player’s session. The PMC earns the unlock without opening the trader or paying. Prices, barter ingredients, stock counters, and personal limits govern offers that can still be bought. |
| Extraction | At a completed PMC or player-Scav extraction, credit eligible gear carried, worn, packed, or installed. A Run Through counts. A successful Scav earns the unlock before choosing what to transfer or sell. A map transit defers the check until the player returns home. |
| PMC ownership | Credit gear that reaches the PMC’s stash, equipment, or owned storage by any route, including a purchase, collected craft, reward, Scav transfer, or direct grant from another mod. Scan nested items. On death, only gear actually retained by the PMC qualifies through this rule. |
| Mail | Credit each item tree when its transfer into PMC ownership succeeds. Insurance, quest rewards, gifts, and mod mail use the same rule. With a partial claim, the remaining attachments qualify when collected. |
| Existing profile | On first use, seed from current holdings and currently eligible traders. This includes worn gear, items inside cases, and gear on owned racks. History starts with that snapshot. A profile wipe starts a new catalogue. |

A purchase from Fence or the flea qualifies when the gear reaches the PMC. The same template can also unlock through another trader’s assort, extraction, or a successful mail claim. For ownership checks, start at the PMC’s stash, equipment, sorting table, and player-owned displays such as the weapon rack. Follow the item tree through cases and secure containers to find eligible contents. Handbook entries, build previews, and pending mail live outside those roots.

## Using unlocked gear in EFT’s menus

| Where | Player interaction |
| --- | --- |
| Inspect window | If the examined item’s template is in the PMC’s catalogue, its description begins with **UNLOCKED** followed by a blank line. This also applies to loot inspected during a PMC or Scav raid. |
| Equipment slot | Keep the existing choices for owned items and mark compatible catalogue rows with an **Unlocked** tag at the top. A weapon has one standard-build row. Selecting it creates the real item and equips it through EFT’s normal slot action. The selectable row stays available for additional copies. |
| Modding | Choose a compatible catalogue part marked **Unlocked** to create one real part and install it in the selected slot. A blocked row shows EFT’s conflict on hover. Clicking it creates nothing. |
| Edit Build | Editing, previewing, and saving a weapon preset only change the plan. Assemble uses the selected weapon and available parts first. It can issue the standard build of an unlocked weapon when a body is missing, then creates missing unlocked parts and runs the native assembler. Only remaining locked shortages reach its purchase window. |
| Weapon context menu | Right-click an owned weapon and choose **Spawn spare magazines** to add empty copies of its fitted magazine to the main stash. |
| Equipment presets | Standard and custom presets show unlocked gear as available. Equip reuses complete matching items, including their contents. It assembles missing builds from new unlocked bodies and available loose supplies. EFT offers to buy outstanding shortages. |

Modding and Edit Build are available out of raid at every Workbench level, including level zero. The client enables the weapon modification bonus for the item context menu and the Edit Build taskbar button. Building and upgrading the Workbench still governs its crafts.

Copies are free, start at factory condition, and are issued with the Found in Raid flag cleared. The server prefers a usable SPT default preset, then a complete trader build for the same receiver, then a bare receiver if it has all vital parts. Each weapon has one standard build, and its eligible fitted parts unlock with the weapon. Magazines and chambers start empty. Packs and rigs start with empty grids. Armor gets its integral inserts, and removable plates unlock individually.

Every new copy first enters the main stash, so even a one-click equip needs free stash space. If there is too little room for the request, show **Not enough stash space** and create zero items. If the item is created but the subsequent equip or install fails, leave the copy in the stash and tell the player where it is. A later equipment preset can reuse a copy that matches its entry.

### Switching equipment presets

If the PMC has unlocked the Mosin Infantry, **Sharpshooter** shows the rifle as available even with no copy in the stash. Missing ammunition still shows its outstanding quantity. The item tooltips, Equipment and Containers tabs, and Equip icon all reflect the shortages the PMC must acquire. The preview updates when the catalogue arrives. Only clicking **Equip** can issue gear. Mannequin storage presets continue to show what the mannequin can transfer.

The Unheard USEC starting Mossberg 590A1 has a SpeedFeed short handguard and a polymer stock. **Shotgunner** calls for a Magpul MOE forestock and an SGA stock. EFT ordinarily refits the starting gun and leaves its original parts loose. With BottomlessRack, both factory parts stay on the starting gun. If the PMC owns a complete Shotgunner Mossberg, Equip moves it as one item. Otherwise, Equip requests an unlocked bare receiver and builds a separate shotgun.

The same rule applies to helmets, armored rigs, backpacks, magazines, and their contents. Compare template IDs in each fitted slot, grid positions and rotations, and the order and quantity of loaded rounds. Durability, remaining uses, and Found in Raid status stay with the physical copy. A matching item moves intact. Gear with a different configuration keeps its parts and contents. Loose items in the stash and storage cases can supply a new build, including ammunition from a larger stack.

Preview and Equip use the same item search. When supplies are missing, Equip groups the unlocked shortages into one stash grant. Each new body goes to its corresponding preset entry, including when two entries use the same template. Unavailable ammunition, consumables, and locked gear go through EFT’s missing-items window before issuance. If the player chooses to equip what is available, only builds with a complete set of supplies receive new items.

**Round-trip check.** Start with one factory Mossberg and enough loose shells for Shotgunner. Equip Shotgunner, switch to the factory loadout, and switch back. The two receiver IDs and their fitted parts should survive every switch, with zero loose factory parts or additional grants on the return trip. Repeat with two rigs of the same model carrying different plates or magazines. Each rig should keep its original contents.

### Inspect descriptions

The client loads the PMC’s catalogue when EFT initializes the main menu inventory, so a player can go straight into a raid. Inspecting loot uses the cached unlocks. Out of raid, inspecting eligible gear missing from the cache checks the catalogue revision. If the template becomes available during a refresh, the open window gains the marker.

On SPT 4.1.6, EFT’s `ItemSpecificationPanel.method_1` fills the description when the window opens and again when examination finishes. The client prepends `UNLOCKED\n\n` to the displayed text, preserving its language, formatting, and additions from mods such as Item Valuation. Closing the window releases its catalogue subscription.

### Spawn spare magazines

Offer **Spawn spare magazines** out of raid on a PMC-owned weapon with an unlocked, detachable magazine fitted. A 1×1 magazine gives three empty copies, a 1×2 or 1×3 gives two, and any other shape, including 2×2, gives one. Rotation preserves the count. All copies go into the main stash whether the weapon is equipped or stored.

On click, the client rechecks the fitted model and requests the batch through the usual stash grant. Once it finds the issued items in the stash, it shows the confirmed count in an EFT message such as **Spawned 2 spare magazines in your stash.**

## Trader purchase controls

On the **Buy** tab of any trader other than Fence, including modded traders, apply the same rule to every cash and barter offer. Once the PMC’s catalogue is ready, look up the template ID of the item **the trader gives the player** with `GearClient.Unlocked.Contains(item.TemplateId)`. A match replaces the cash price or barter requirement with **Unlocked** and disables selection for a deal. The player can still examine the item and use its context menu. Fence uses EFT’s prices and purchase controls for every offer, including gear the PMC has already unlocked.

Once a scoped rifle offer meets the PMC’s loyalty and quest requirements, trader discovery credits the receiver and every eligible fitted part. A separate offer for just that receiver also shows **Unlocked** if the PMC can spawn the weapon’s standard build. When the chosen build is a functional bare weapon, the grant delivers the bare receiver. An earned receiver classed as an attachment can be issued as a loose part and gets the same label.

A weapon body that lacks a vital part when bare needs a usable default preset or a complete trader build to spawn. If neither exists, the profile still records the unlock. The server rejects the spawn request, so the trader offers that body on ordinary terms.

| Trader gives you | You give the trader | Buy tab |
| --- | --- | --- |
| An unlocked rig | Roubles | **Unlocked**. Deal disabled. |
| The same unlocked rig | Two medkits | **Unlocked**. Deal disabled. |
| A medkit | An unlocked rig | Shows the rig requirement. The player can hand it over to receive the medkit. |

An unlocked magazine sold with rounds also shows **Unlocked**. Its free copy comes empty. Loose ammunition remains purchasable.

The trader tooltip shows **Free in Modding** for an attachment and **Free from inventory** for other gear. The client replaces the tooltip’s price after EFT displays it, preserving the item name and the source tab added by HJ’s Mod Source. [HJ’s trader tooltip patch](https://github.com/hjal-dev/Mod-Source/blob/9e9f185d39735092d0735a80a98e19e450d39340/ModSource4.1/Client/ItemTooltipPatch.cs)

### Client integration on SPT 4.1.6

1. At `TraderDealScreen.Show`, skip Fence by trader ID. For other traders, call `GearClient.Enter` with the screen’s inventory controller and reuse `SelectorRefresh` for catalogue updates. Allow browsing during the first read and wait for it before accepting a deal. If it fails, allow a fresh purchase at the displayed price. A later refresh failure can use this PMC’s last successful catalogue.
2. Use the same lookup in `TradingItemView.SetPrice` and `TradingItemView.ShowTooltip` for trader stock in Buy mode. On `GearClient.Updated`, repaint the visible stock. Restore the native price and hover behavior whenever EFT rebinds a cell, including when switching to Fence or the Sell tab.
3. For the active non-Fence assortment, guard `Assortment.SelectItem` with the same lookup. If the item is unlocked, clear the selection with `SelectItem(null)`. This covers both EFT’s click and UI Fixes’ buy-tab shortcut. Also clear a staged item if a catalogue update marks it unlocked. Recheck at Deal and before submitting a purchase or approving a pending currency conversion so a stale selection cannot spend money.

### Trader acceptance checks

- Check assembled and stripped offers for the same spawnable weapon, a functional bare weapon and a receiver attachment. Each should show **Unlocked**. Verify EFT’s usual purchase flow for a weapon body the server rejects.
- Exercise both directions of the rig barter above. A blocked rig offer consumes zero medkits. Buying the medkit with a rig consumes the payment and delivers the medkit. Repeat the unlocked-offer check with armor and a loaded magazine.
- Stage a priced item, click an unlocked item and try **Deal**. Check the label and tooltip, verify that inspection works, and expect zero purchase, exchange or spawn commands. Repeat when the staged item unlocks during currency-exchange approval.
- Switch from a regular trader to Fence and buy an already-unlocked item at Fence’s price. Switch back, check the label after a restock, and sell loose ammunition to a trader who buys it at the quoted price.

## PMC sale restrictions

Block PMC sales of every template covered by `GearDefinitions.Eligible`. A looted rifle, a purchased rifle and a free copy all have the same sale restriction, including copies already in an existing stash. The rule applies before the first unlock and also covers a receiver with no issuable build. Classify by template regardless of condition or Found in Raid status, without tagging or tracking individual copies. [Gear eligibility](Server/GearDefinitions.cs)

This covers firearms, melee weapons, attachments, magazines, armor, removable plates and wearable equipment, including modded items in those families. The existing exclusions for ammunition, grenades, built-in inserts and quest-only items still apply. Loose rounds, medicine and barter goods use each market’s ordinary sale rules.

| Where the player sells | Sale behavior |
| --- | --- |
| Trader **Sell** tab | All traders, including Fence and modded traders, decline covered gear. A blocked item has no sell quote and cannot be staged through drag, quick-sell or multiselect. |
| Flea **Add Offer** | Prevent selecting covered gear for a player listing, whether it is offered singly, in a batch or as a pack, for currency or barter. Players can still browse and buy gear offered by traders and simulated sellers. |
| Post-raid Scav **Sell All** | Fence buys the remaining Scav loadout at the normal quote, including weapons and equipment. Gear transferred to the PMC stash follows the PMC sale rule if the player later tries to sell it. |

For a PMC sale, check each offered item and everything that would go with it, including fitted parts and nested cargo. A magazine inside an otherwise saleable case blocks the sale until the PMC removes it. Reject a mixed submission as one sale so the player can remove the restricted item and retry. On the **Buy** tab an unlocked rig can still pay for a medkit. Quest hand-ins can also accept covered gear.

**Quest consequence.** Ragman’s *Circulate* requires 250 backpack or tactical rig sales. All 95 templates accepted by its SPT 4.1.6 objective are covered, so the PMC cannot complete it through sales. [SPT quest definition](https://github.com/SP-Tushonka/server-csharp/blob/4.1.6/Libraries/SPTushonka.Server.Assets/SPT_Data/database/templates/quests.json#L116418-L116550)

### Enforcement on SPT 4.1.6

- **Classification and menus.** Put the family and exclusion IDs in `Shared/`. The server uses `GearDefinitions.Eligible`. The client checks the same concrete template, quest flag, dimensions and ancestry, then caches the result by template. While the PMC trader screen is open, return an empty `Trader.GetUserItemPrice` quote for a blocked item tree. EFT uses that quote in `Assortment.CanPrepareItemToSell`. On the flea, reject the seller’s selection in `RagFair.CanBeSelectedAtRagfair` with **Gear sales are disabled**. Recheck current contents at confirmation. Keep this check local to player offers because SPT also reads `CanSellOnRagfair` when generating stock for purchase. [SPT market generation filter](https://github.com/SP-Tushonka/server-csharp/blob/4.1.6/Libraries/SPTushonka.Server.Core/Helpers/Ragfair/RagfairServerHelper.cs#L37-L65)
- **Trader and flea submissions.** Reuse the existing per-action inventory hook under the session’s inventory lock. For `TradingConfirm` with type `sell_to_trader` and for `RagFairAddOffer`, resolve every offered instance ID in the current PMC inventory and check each tree with `Eligible`. Reject a missing root or a covered item with an SPT warning indexed to that command. Validate the whole sale before SPT advances quest counters, adds stock to Fence, removes items, credits money or trader turnover, charges a listing fee, or publishes an offer. Earlier successful actions in the queue stand. [Trader sale handler](https://github.com/SP-Tushonka/server-csharp/blob/4.1.6/Libraries/SPTushonka.Server.Core/Helpers/Commerce/TradeHelper.cs#L256-L304) · [Player flea offer handler](https://github.com/SP-Tushonka/server-csharp/blob/4.1.6/Libraries/SPTushonka.Server.Core/Controllers/RagfairController.cs#L431-L466)
- **Scav payout.** The transfer screen uses `Trader.GetItemPriceOnScavSell` and can also call `Assortment.PrepareToSell` while submitting the loadout. Allow this path for the whole loadout. SPT already routes `SellAllFromSavage` to its separate Fence payout handler. [Fence payout](https://github.com/SP-Tushonka/server-csharp/blob/4.1.6/Libraries/SPTushonka.Server.Core/Controllers/TradeController.cs#L232-L245)
- **Existing flea offers.** Apply the restriction when adding a new offer. SPT handles pending listings, including purchases, renewals, cancellation and expiry.

### Sale acceptance checks

- Try a free magazine and a looted Found in Raid magazine at a regular trader, Fence, a modded trader and the flea, including with the flea’s Found in Raid requirement disabled. Repeat with a locked modded attachment and an unissuable receiver. Check an allowed ammunition sale and a medkit listing at their normal price and fee.
- Submit a mixed trader batch and a flea pack with the covered item last. Try a covered descendant in an otherwise permitted container. A rejected command preserves every item, the cash balance, quest counters, trader turnover, Fence stock, offers and fees. Unpack the container and retry the allowed sale. Exercise the server directly as well as EFT’s shortcuts.
- After a successful Scav run, sell a rifle and its attachments with **Sell All** and verify Fence mails the full normal payout. Repeat with a partial transfer. Gear kept by the PMC must be refused at Fence’s regular Sell tab and on the flea. Both runs must still credit extracted gear to that player’s catalogue.

## Server and client design

The SPT server owns unlocks and item creation. The BepInEx plugin adds unlocked choices to EFT’s existing menus on each participating player’s client. Fika headless handles ordinary EFT items during raids. Use the `Client/` and `Server/` solution layout from `SPT-HideLockedFlea` and the `SPT-Infil` build conventions. Build with `./build.ps1` and package the player plugin under `BepInEx/plugins` and the server mod under `SPT_Runtime/user/mods`. [SPT 4.1 server mod guide](https://github.com/sp-tarkov/wiki/blob/main/SPT_41/Server_40_to_41.md)

### Why the catalogue lives in the profile

A PMC can claim an optic by mail and lose their only copy in a raid. If none of their traders offer it, the next stash and assort scan has no way to recover that unlock. Saving it in the PMC’s profile keeps it earned. Restoring a backup brings back the matching inventory and unlocks, a wipe starts a fresh catalogue, and each Fika player keeps their own progression.

Store a single `net.liquidwarp.bottomlessrack` entry in `SptProfile.SptData` extension data with a schema version, the set of earned template IDs, and the most recent spawn receipt. Each template is recorded once. Saving the receipt alongside inventory prevents a lost reply from turning one request into two copies. Keep the selector cache and its revision in memory and fetch a fresh catalogue on reconnect. Keep missing mod templates dormant so reinstalling an item mod restores its entries.

A separate file keyed to the character would also work. It would need to be backed up, restored, and reset together with the profile, even when a wipe reuses the same account. Using SPT’s existing save path keeps those operations together.

### Discovering and issuing gear

Skip `Traders.FENCE` by trader ID before fetching or processing an assort, on both initial discovery and refresh. For each other accessible, enabled trader, use `TraderAssortHelper.GetAssort` with `showLockedAssorts: false` and follow each eligible offer’s output tree. Evaluate assorts on login, relevant trader or quest progression, and assortment refresh for active profiles. Resolve standard builds after item mods have initialized. For raids, wait for an accepted final result and SPT’s loss processing. For a successful player Scav use the returned character’s equipment tree before loadout regeneration. [SPT trader assort helper](https://github.com/SP-Tushonka/server-csharp/blob/4.1x-dev/Libraries/SPTushonka.Server.Core/Helpers/Traders/TraderAssortHelper.cs)

Observe completed inventory actions individually, including actions in the same request. Follow new or transferred roots into the PMC’s inventory. Mail claims can move an existing tree without emitting new item IDs. Reconcile owned roots on login and stash entry. To catch a direct grant from another mod that is immediately discarded, also inspect the owned tree just before it leaves the profile. Update the catalogue cache when its revision changes. The equipment picker can refresh its rows in place. An open attachment dropdown picks up new choices the next time EFT builds its list. [SPT inventory controller](https://github.com/SP-Tushonka/server-csharp/blob/4.1x-dev/Libraries/SPTushonka.Server.Core/Controllers/InventoryController.cs)

**Create real inventory items.** Expose a session-scoped catalogue read and a custom spawn item event on EFT’s existing inventory command queue. A reconnect loads the full catalogue and latest spawn receipt. On later screen entries the client sends its cached revision. The server checks owned gear and trader offers, then sends templates and weapon builds only if the revision changed. A spawn request sends template IDs, positive quantities, and a request sequence. The server derives the PMC from the session, checks that the player is out of raid, validates every unlock, and builds fresh item trees with unique IDs and remapped parent links. Preflight and stage the whole batch before publishing changes. `InventoryHelper.AddItemsToStash` checks fit up front but inserts each tree separately, so a later placement failure needs to discard the staged changes. [SPT inventory helper](https://github.com/SP-Tushonka/server-csharp/blob/4.1x-dev/Libraries/SPTushonka.Server.Core/Helpers/Profile/InventoryHelper.cs)

Serialize the grant with the PMC’s inventory changes. Save the receipt and inventory together, then return SPT’s `ProfileChanges` item delta and the issued root IDs. A repeat of the same sequence and payload resolves to the original IDs. For equipment presets, the weapon line carries `bareWeapon` so a receiver and a standard weapon have distinct receipts. Older receipts use the standard build. An older sequence or a reused sequence with different contents triggers a refresh. After a lost reply, reconcile those IDs with the profile before applying any delta again. Only pass acknowledged items into EFT’s equip, install, or assemble action.

**Client integration points.** The 4.1.6 client exposes `EquipItemWindow.GetAvailableItems` and `ItemSelectedHandler` for equipment. `WeaponModdingManipulation` and `EditBuildManipulation` expose `GetItemCollections` and `Select` for attachment dropdowns. `EditBuildScreen.GetMissingBuildItems` and `TryToAssembleBuild` provide the assembly boundary. The client sends grants through `SendOperationRightNow` on EFT’s inventory queue. EFT applies `ProfileChanges` before invoking its immediate callback. The mod then reads the server receipt and looks up the issued IDs in the main stash. The ordinary queued callback can fire before a request is sent. Preview items have a detached client-side owner and use EFT’s compatibility filters. The picker preserves existing owned-item choices, including entries supplied by other mods. [Equip From Weapon Rack](https://forge.sp-tarkov.com/mod/1136/equip-from-weapon-rack)

## Installed mod compatibility

The compatibility changes target EFT and SPT. The server reads loaded items and trader offers. The player plugin adds choices to EFT's selectors and hands acknowledged stash items to EFT for equip, swap, or assembly. Each plugin owns its labels, controls, and purchases. The five versions below were reviewed against the SPT 4.1.6 Fika installation. The client changes run on the player machine.

### Make Shotguns Great Again 1.20.0

Jaeger sells the MP-700 with its required barrel and stock at LL2, and the Shorty with its required barrel at LL3. Both receivers lack a global default. Their Jaeger offers now supply their standard builds. [MP-700 offer](https://github.com/viniHNS/Make-Shotguns-Great-Again/blob/d66370bba72aeaa6a64398a69601d033688f0fc1/Server/db/weaponPresets/Assorts/Nitro.json) · [Shorty offer](https://github.com/viniHNS/Make-Shotguns-Great-Again/blob/d66370bba72aeaa6a64398a69601d033688f0fc1/Server/db/weaponPresets/Assorts/MP700_SHORTY.json)

For any weapon missing a usable default, use the simplest complete build a non-Fence trader sells. Consider cash and barter offers in the baseline trader assortments after content mods load. Match the offer's root to the exact receiver template and follow its fitted slots. Keep eligible mounted attachments, including an empty magazine if one is fitted. Omit ammunition, loose contents, barter ingredients, and merchant stock or condition data. Reject a candidate if the resulting tree is malformed, incompatible, or missing a vital part.

Rank the valid builds by **fewest fitted parts**, then **lowest listed trader loyalty level** (unspecified levels sort last). Break any remaining tie by comparing the sorted pairs of full slot path and template ID in ordinal order. This gives identical builds the same result even if a mod regenerates offer IDs or loads its offers in a different order. Cache the winner for the loaded content set so a restock or price change does not reroll it. If neither a complete offer nor a functional bare receiver exists, keep the receiver unlock and omit the standard-copy option.

Before the first restock, the server puts the selected weapon builds in one in-memory recipe cache shared by all PMCs. The full catalogue carries each earned, issuable weapon's template IDs, parent indices, and slots. The picker creates detached EFT items from that tree. The grant uses the same recipe with fresh inventory IDs. This also covers usable bare weapons and published defaults. Loyalty and quest checks govern unlocks *from seeing an offer*. A player who extracts with the receiver earns the standard build and its eligible fitted parts immediately. The full catalogue read credits missing standard parts for previously earned receivers, including receivers the player no longer owns.

The installed-data regression compares the catalogue tree with the actual stash grant for both MP-700s. Omitting the MP-12's global default puts Jaeger's two LL1 offers in competition. Both have three fitted parts. The wood build wins the slot-path comparison even with reversed rows and regenerated offer IDs. The stock-data regression compares fitted parts at every slot path in the 148 published weapon defaults.

### Choo² Trader Modding 2.1.2

Choo² builds its shopping list as it draws the slots in Edit Build. Its cost panel totals that list and **Quick Buy Items** uses it to submit real trader purchases. Our **Unlocked** label can therefore appear on a part that still contributes to the quote. [Slot classification](https://github.com/Soulztorm/ChooChoo-TraderModding/blob/ee0c5c2c930df9eb868e44c7fb6a18810730a14c/ChooChoo-TraderModding_Client/Client/Patches/DropDownPatch.cs#L184) · [Quick Buy](https://github.com/Soulztorm/ChooChoo-TraderModding/blob/ee0c5c2c930df9eb868e44c7fb6a18810730a14c/ChooChoo-TraderModding_Client/Client/TraderModding/TraderModdingScript.cs#L272)

An earned Edit Build row shows **Unlocked** in the price position at the bottom of the icon. The selector appends a compatible earned choice when the template is absent, including after its last trader offer expires.

Choo²’s **Quick Buy Items** calls `ITradingSession.ConfirmPurchase` directly from Edit Build. That button can spend currency on an unlocked part. Assemble supplies the missing unlocked part for free. [Quick Buy submission](https://github.com/Soulztorm/ChooChoo-TraderModding/blob/ee0c5c2c930df9eb868e44c7fb6a18810730a14c/ChooChoo-TraderModding_Client/Client/TraderModding/TraderModdingScript.cs#L280-L296)

**Check** the same all-earned plan from the same starting inventory twice. Assemble must issue just the missing quantities with zero trader purchases. Explicit Quick Buy must behave as it does with Choo² alone. In a mixed plan, only locked shortages reach Assemble's purchase window. Check that an unlocked row shows a single bottom label and that a pooled row regains its price when rebound to a locked part. Repeat with price tags toggled and with Choo² absent.

### UI Fixes 6.0.4

UI Fixes attempts a swap in the four-argument `WeaponModdingManipulation.Select` when a slot is occupied. It honors EFT’s simulation flag and submits a transaction only for an actual selection. [UI Fixes swap](https://github.com/tyfon7/UIFixes/blob/v6.0.4/src/Patches/WeaponModdingPatches.cs#L324)

In live Modding, catalogue previews run EFT’s move or swap simulation for both hover and click. A failed simulation returns the same error as an owned attachment. Only a successful click requests a part. After acknowledgement the client verifies the screen’s selector, displayed weapon, slot parent, and previous occupant. It calls the three-argument `Select` with the issued stash item to perform the install and play the native selection sound.

**Check** empty and occupied slots in both plugin orders and with UI Fixes absent. Hovering issues nothing. One successful click issues one part, retains the displaced part, and uses only real item IDs in transactions. A full stash rejects the grant. Closing the screen before acknowledgement leaves any issued part in the stash.

### Modding Stats Helper 1.2.0

The 2026-11-26 EFT log records two null-reference exceptions in `ShowMeTheStats.ShowTooltipPatch.Prefix` from `HoverTooltipArea.Show`. The helper remembers the last item hovered in a grid and reads its attributes in a general tooltip handler. A control can request a tooltip before an item supplies that context. The stacks identify the handler but leave the hovered control and the null value unknown. [Tagged tooltip source](https://github.com/Soulztorm/Wara-ModdingStatsHelper/blob/84ae5472cb397ae1f38d378987e39c447f6c68a4/Wara-ModdingStatsHelper/Client/ShowMeTheStatsPatches.cs#L25-L63)

Each catalogue preview has a detached EFT item controller and address. EFT binds that owner to the native item view. The badge stays on the pooled row. Disposing the current item binding hides the badge, and its background and caption pass pointer events through. The native view handles hover and Ctrl comparison. The generic tooltip exception still needs a comparison with Stats Helper alone.

**Check** Stats Helper alone and then with our mod. Hover a generic control before the first item, then compare owned and unlocked parts over empty and occupied slots. Repeat after closing and reopening Modding and Edit Build. Check that stats follow the current item and that catalogue rows introduce no additional exceptions or spawn requests.

### WeaponBuilderSearch 2.0.0

WeaponBuilderSearch indexes item views when the attachment dropdown opens and clears its TextMeshPro search field. Reopening the dropdown on a catalogue update used to reset a query in progress. [Search field and row indexing](https://github.com/maschine34675/WeaponBuilderSearch/blob/103db3c63b6e924098e0d9a12a8346b12f1b8c67/UI/AttachmentSearchController.cs#L17)

Modding and Edit Build request the catalogue on entry and poll its revision while active. A refresh updates the cache. The current dropdown keeps its search text, focus, caret, and scroll position. EFT receives the updated rows when it next builds the list.

**Check** a zero-result query while a matching part unlocks. The active query and focus should survive the update. Reopen the slot, search again, and verify the new part appears once. Also check an idle poll, switching slots, a list below the six-row search threshold, and the plugin disabled or absent. Typing and filtering must issue zero items.

### Other installed overlaps

Equip From Weapon Rack contributes owned choices and writes their location tags. Catalogue choices are appended after its enumeration. When a catalogue row is the only choice, the final picker refresh hides EFT's empty-list message and labels the row **Unlocked**. Foldables 1.1.1 unfolds worn gear after EFT applies or swaps an equipment build. Completed equipment presets still reach the native apply action. Fika uses ordinary issued items during raids. The installation review found no further confirmed conflict. The visual checks above and a rack-only or catalogue-only picker still need a player-client run. [Weapon Rack enumeration](https://github.com/DrakiaXYZ/SPT-EquipFromWeaponRack/blob/master/Patches/EquipItemWindowListPatch.cs) · [Foldables preset callbacks](https://github.com/ozen-m/SPT-Foldables/blob/f560b24b9880c317621c6e7c46d6ac21c060d483/Foldables/Patches/Operations/EquipBuildPatch.cs#L9-L49)

## Automated test suite

Run `./build.ps1 -Test -SPTPath $sptPath` in PowerShell 7.6 or later with `$sptPath` pointing to an SPT 4.1.6 installation. To run the DLL compatibility checks alone, use `pwsh -NoProfile -File ./Tests/Test-Contracts.ps1 -SPTPath $sptPath`. The script round-trips a profile with SPT’s serializer and checks EFT’s selector signatures, private field bindings, and callback order using Mono.Cecil.

The NUnit suite under `Tests/` drives the stock inventory queue, placement, trader assort, sale, and save handlers with read-only SPT data and temporary profiles. Client tests record submitted commands and supply delayed, lost, or rejected acknowledgements. The coverage below describes the behavior each layer is responsible for.

| Coverage | Assertions |
| --- | --- |
| Trader eligibility | A Fence-only optic stays locked across login, restock, and standing changes. Buying it into the stash or qualifying for the same optic at another trader unlocks it. Check disabled traders, loyalty and quest gates, and output parts versus barter ingredients. |
| Acquisition | Feed PMC and Scav extraction, Run Through, transit, and death results. Check worn and nested gear, retained items, craft collection, partial mail claims, and a claim followed immediately by a discard in one request. Only the expected templates enter the set. |
| Persistence | Seed an older profile, discard the last copy of a previously earned item, serialize, and reload. Its unlock survives. Restore an earlier snapshot and start a fresh character. Verify inventory and unlocks follow each snapshot and two PMCs stay independent. |
| Spawn and sale policy | A customized weapon yields one factory build plus its earned parts. Check fresh IDs and parent links, empty magazines, and cleared Found in Raid flags. Routed trader sales reject both ordinary and issued magazines. An ordinary ammunition sale pays the quoted price. |
| Placement and retries | A locked part, a full stash, or a later failure in a multi-item batch leaves inventory intact. Replaying the same request after reloading the profile creates zero additional copies. A deliberate second request creates a distinct copy. |
| Equipment preset switches | Run EFT’s loadout planner with the client patches. Alternate the stock and Magpul Mossbergs three times and verify both receivers and their fitted part IDs. Check reversed plates, backpack grid positions and rotations, and mixed magazines, including equivalent runs split into separate stacks. An incomplete rig receives zero parts. Providing loose ammunition allows a second rig to be assembled and reused. |
| Inspect descriptions | Check locked and unlocked templates, including a second copy inspected in raid. A catalogue update adds the prefix once above localized rich text. Closing the window releases the update callback. |
| Spare magazines | Try equipped and stashed weapons with loaded, unlocked magazines. For each footprint, verify the empty copies in the main stash, the count in the success notification, and the original rounds in the fitted magazine. With room in worn gear but too little stash space, verify that only the stash-space warning appears. Check dismissal, a model swap before clicking, and eligibility with a fixed, missing, or locked magazine. |
| Equipment, Modding, and Edit Build | Catalogue equipment uses the top location tag in both picker layouts. Attachment dropdowns mark catalogue parts **Unlocked**. Check vital-slot hover on equipped weapons and weapons in the stash. Check access to both editing menus with the Workbench bonus active and inactive, including raid and Hideout contexts. Previewing, saving, cancelling, and clicking a conflicting attachment send zero spawn requests. A build needing two identical parts with one already owned requests exactly one. After acknowledgement, equip uses the returned ID. Closing the screen after a valid request leaves the issued item in the stash. |

The client tests cover selection decisions, queue messages, and method signatures. Actual Unity rendering and mouse interaction are outside their coverage.
