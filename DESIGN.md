# Unlock Gear Progression

This design gives each PMC in SPT 4.1 a permanent equipment catalogue. Once a trader other than Fence offers a piece of gear to that PMC, or the player brings one home, the player can create fresh copies through EFT’s equipment picker, Modding, and Edit Build. Selling or losing the original preserves the unlock.

For example, extract with a customized rifle and the weapon-slot picker gains one **Unlocked** entry for its standard build. The parts fitted to that build unlock immediately, even if the recovered rifle used different parts. The recovered optic and mount also become individual choices in Modding and Edit Build. The player can sell the recovered rifle and still use all of those choices.

## What earns an unlock

Unlocks use EFT template IDs. The first version covers weapons, attachments, magazines, armor, removable plates, and wearable equipment such as rigs, backpacks, helmets, headsets, face covers, eyewear, and armbands. Use the loaded template hierarchy so modded gear in these families participates. Ammo, grenades, consumables, currency, loose barter goods, and quest-only objects follow ordinary acquisition rules.

| Source | When the catalogue gains the gear |
| --- | --- |
| Trader assort | Use the offers of accessible traders other than Fence, including modded traders. Cash and barter offers both count at the PMC’s loyalty level and quest state, including the gear supplied in a bundle. Observe eligible rotations during the player’s session. Prices, barter ingredients, stock counters, and personal limits govern purchases. Opening the trader or buying the item is optional. |
| Extraction | At a completed PMC or player-Scav extraction, credit eligible gear carried, worn, packed, or installed. A Run Through counts. A successful Scav earns the unlock before choosing what to transfer or sell. A map transit defers the check until the player returns home. |
| PMC ownership | Credit gear that reaches the PMC’s stash, equipment, or owned storage by any route, including a purchase, collected craft, reward, Scav transfer, or direct grant from another mod. Scan nested items. On death, only gear actually retained by the PMC qualifies through this rule. |
| Mail | Credit each item tree when its transfer into PMC ownership succeeds. Insurance, quest rewards, gifts, and mod mail use the same rule. With a partial claim, the remaining attachments qualify when collected. |
| Existing profile | On first use, seed from current holdings and currently eligible traders. This includes worn gear, items inside cases, and gear on owned racks. History starts with that snapshot. A profile wipe starts a new catalogue. |

A purchase from Fence or the flea qualifies when the gear reaches the PMC. The same template can also unlock through another trader’s assort, extraction, or a successful mail claim. For ownership checks, start at the PMC’s stash, equipment, sorting table, and player-owned displays such as the weapon rack. Follow the item tree through cases and secure containers to find eligible contents. Handbook entries, build previews, and pending mail live outside those roots.

## Using unlocked gear in EFT’s menus

| Where | Player interaction |
| --- | --- |
| Equipment slot | Keep the existing choices for owned items and mark compatible catalogue rows with an **Unlocked** tag at the top. A weapon has one standard-build row. Selecting it creates the real item and equips it through EFT’s normal slot action. The selectable row stays available for additional copies. |
| Modding | Select an inventory attachment or choose a compatible catalogue part marked **Unlocked**. Choosing the catalogue row creates one real part and applies the normal install action to the selected slot. EFT handles conflicts and displaced parts. |
| Edit Build | Editing, previewing, and saving a weapon preset only change the plan. Assemble uses the selected weapon and available parts first. It can issue the standard build of an unlocked weapon when a body is missing, then creates missing unlocked parts and runs the native assembler. Only remaining locked shortages reach its purchase window. |
| Weapon context menu | Right-click an owned weapon and choose **Spawn spare magazines** to add empty copies of its fitted magazine to the main stash. |
| Equipment presets | Equip uses EFT’s loadout simulation to match worn and stored items. Issue missing unlocked weapons first, then recheck the loadout before issuing other unlocked gear. Apply the preset when complete or show the remaining locked items in EFT’s purchase window. |

Modding and Edit Build are available out of raid at every Workbench level, including level zero. The client enables the weapon modification bonus for the item context menu and the Edit Build taskbar button. Building and upgrading the Workbench still governs its crafts.

Copies are free, start at factory condition, and are issued with the Found in Raid flag cleared. Trader sales and flea listings follow the usual SPT rules for that item and condition. The server prefers a usable SPT default preset, then a complete trader build for the same receiver, then a bare receiver if it has all vital parts. Each weapon has one standard build, and its eligible fitted parts unlock with the weapon. Magazines and chambers start empty. Packs and rigs start with empty grids. Armor gets its integral inserts, and removable plates unlock individually.

Every new copy first enters the main stash, so even a one-click equip needs free stash space. If there is too little room for the request, show **Not enough stash space** and create zero items. If the item is created but the subsequent equip or install fails, leave the copy in the stash and tell the player where it is. Retrying the equip uses that copy.

### Spawn spare magazines

Offer **Spawn spare magazines** out of raid on a PMC-owned weapon with an unlocked, detachable magazine fitted. A 1×1 magazine gives three empty copies, a 1×2 or 1×3 gives two, and any other shape, including 2×2, gives one. Rotation preserves the count. All copies go into the main stash whether the weapon is equipped or stored.

On click, the client rechecks the fitted model and requests the batch through the usual stash grant. Once it finds the issued items in the stash, it shows the confirmed count in an EFT message such as **Spawned 2 spare magazines in your stash.**

## Server and client design

The SPT server owns unlocks and item creation. The BepInEx plugin adds unlocked choices to EFT’s existing menus on each participating player’s client. Fika headless handles ordinary EFT items during raids. Use the `Client/` and `Server/` solution layout from `SPT-HideLockedFlea` and the `SPT-Infil` build conventions. Build with `./build.ps1` and package the player plugin under `BepInEx/plugins` and the server mod under `SPT_Runtime/user/mods`. [SPT 4.1 server mod guide](https://github.com/sp-tarkov/wiki/blob/main/SPT_41/Server_40_to_41.md)

### Why the catalogue lives in the profile

A PMC can claim an optic by mail and sell their only copy. If none of their traders offer it, the next stash and assort scan has no way to recover that unlock. Saving it in the PMC’s profile keeps it earned. Restoring a backup brings back the matching inventory and unlocks, a wipe starts a fresh catalogue, and each Fika player keeps their own progression.

Store a single `net.liquidwarp.unlockgearprogression` entry in `SptProfile.SptData` extension data with a schema version, the set of earned template IDs, and the most recent spawn receipt. Each template is recorded once. Saving the receipt alongside inventory prevents a lost reply from turning one request into two copies. Keep the selector cache and its revision in memory and fetch a fresh catalogue on reconnect. Keep missing mod templates dormant so reinstalling an item mod restores its entries.

A separate file keyed to the character would also work. It would need to be backed up, restored, and reset together with the profile, even when a wipe reuses the same account. Using SPT’s existing save path keeps those operations together.

### Discovering and issuing gear

Skip `Traders.FENCE` by trader ID before fetching or processing an assort, on both initial discovery and refresh. For each other accessible, enabled trader, use `TraderAssortHelper.GetAssort` with `showLockedAssorts: false` and follow each eligible offer’s output tree. Evaluate assorts on login, relevant trader or quest progression, and assortment refresh for active profiles. Resolve standard builds after item mods have initialized. For raids, wait for an accepted final result and SPT’s loss processing. For a successful player Scav use the returned character’s equipment tree before loadout regeneration. [SPT trader assort helper](https://github.com/SP-Tushonka/server-csharp/blob/4.1x-dev/Libraries/SPTushonka.Server.Core/Helpers/Traders/TraderAssortHelper.cs)

Observe completed inventory actions individually, including actions in the same request. Follow new or transferred roots into the PMC’s inventory. Mail claims can move an existing tree without emitting new item IDs. Reconcile owned roots on login and stash entry. To catch a direct grant from another mod that is immediately sold or discarded, also inspect the owned tree just before it leaves the profile. Update the catalogue cache when its revision changes. The equipment picker can refresh its rows in place. An open attachment dropdown picks up new choices the next time EFT builds its list. [SPT inventory controller](https://github.com/SP-Tushonka/server-csharp/blob/4.1x-dev/Libraries/SPTushonka.Server.Core/Controllers/InventoryController.cs)

**Create real inventory items.** Expose a session-scoped catalogue read and a custom spawn item event on EFT’s existing inventory command queue. The read returns unlocked templates, the catalogue revision, and the last processed spawn sequence so a reconnected client can resume. A spawn request sends template IDs, positive quantities, and a request sequence. The server derives the PMC from the session, checks that the player is out of raid, validates every unlock, and builds fresh item trees with unique IDs and remapped parent links. Preflight and stage the whole batch before publishing changes. `InventoryHelper.AddItemsToStash` checks fit up front but inserts each tree separately, so a later placement failure needs to discard the staged changes. [SPT inventory helper](https://github.com/SP-Tushonka/server-csharp/blob/4.1x-dev/Libraries/SPTushonka.Server.Core/Helpers/Profile/InventoryHelper.cs)

Serialize the grant with the PMC’s inventory changes. Save the receipt and inventory together, then return SPT’s `ProfileChanges` item delta and the issued root IDs. A repeat of the same sequence and payload resolves to the original IDs. An older sequence or a reused sequence with different contents triggers a refresh. After a lost reply, reconcile those IDs with the profile before applying any delta again. Only pass acknowledged items into EFT’s equip, install, or assemble action.

**Client integration points.** The 4.1.6 client exposes `EquipItemWindow.GetAvailableItems` and `ItemSelectedHandler` for equipment. `WeaponModdingManipulation` and `EditBuildManipulation` expose `GetItemCollections` and `Select` for attachment dropdowns. `EditBuildScreen.GetMissingBuildItems` and `TryToAssembleBuild` provide the assembly boundary. The client sends grants through `SendOperationRightNow` on EFT’s inventory queue. EFT applies `ProfileChanges` before invoking its immediate callback. The mod then reads the server receipt and looks up the issued IDs in the main stash. The ordinary queued callback can fire before a request is sent. Preview items have a detached client-side owner and use EFT’s compatibility filters. The picker preserves existing owned-item choices, including entries supplied by other mods. [Equip From Weapon Rack](https://forge.sp-tarkov.com/mod/1136/equip-from-weapon-rack)

## Installed mod compatibility

The compatibility changes target EFT and SPT. The server reads loaded items and trader offers. The player plugin adds choices to EFT's selectors and hands acknowledged stash items to EFT for equip, swap, or assembly. Each plugin owns its labels, controls, and purchases. The five versions below were reviewed against the SPT 4.1.6 Fika installation. The client changes run on the player machine.

### Make Shotguns Great Again 1.20.0

Jaeger sells the MP-700 with its required barrel and stock at LL2, and the Shorty with its required barrel at LL3. Both receivers lack a global default. Their Jaeger offers now supply their standard builds. [MP-700 offer](https://github.com/viniHNS/Make-Shotguns-Great-Again/blob/d66370bba72aeaa6a64398a69601d033688f0fc1/Server/db/weaponPresets/Assorts/Nitro.json) · [Shorty offer](https://github.com/viniHNS/Make-Shotguns-Great-Again/blob/d66370bba72aeaa6a64398a69601d033688f0fc1/Server/db/weaponPresets/Assorts/MP700_SHORTY.json)

For any weapon missing a usable default, use the simplest complete build a non-Fence trader sells. Consider cash and barter offers in the baseline trader assortments after content mods load. Match the offer's root to the exact receiver template and follow its fitted slots. Keep eligible mounted attachments, including an empty magazine if one is fitted. Omit ammunition, loose contents, barter ingredients, and merchant stock or condition data. Reject a candidate if the resulting tree is malformed, incompatible, or missing a vital part.

Rank the valid builds by **fewest fitted parts**, then **lowest listed trader loyalty level** (unspecified levels sort last). Break any remaining tie by comparing the sorted pairs of full slot path and template ID in ordinal order. This gives identical builds the same result even if a mod regenerates offer IDs or loads its offers in a different order. Cache the winner for the loaded content set so a restock or price change does not reroll it. If neither a complete offer nor a functional bare receiver exists, keep the receiver unlock and omit the standard-copy option.

Before the first restock, the server puts the selected weapon builds in one in-memory recipe cache shared by all PMCs. The full catalogue carries each earned, issuable weapon's template IDs, parent indices, and slots. The picker creates detached EFT items from that tree. The grant uses the same recipe with fresh inventory IDs. This also covers usable bare weapons and published defaults. Loyalty and quest checks govern unlocks *from seeing an offer*. A player who extracts with the receiver earns the standard build and its eligible fitted parts immediately. The full catalogue read credits missing standard parts for previously earned receivers, including receivers the player has sold.

The installed-data regression compares the catalogue tree with the actual stash grant for both MP-700s. Omitting the MP-12's global default puts Jaeger's two LL1 offers in competition. Both have three fitted parts. The wood build wins the slot-path comparison even with reversed rows and regenerated offer IDs. The stock-data regression compares fitted parts at every slot path in the 148 published weapon defaults.

### Choo² Trader Modding 2.1.2

Choo² builds its shopping list as it draws the slots in Edit Build. Its cost panel totals that list and **Quick Buy Items** uses it to submit real trader purchases. Our **Unlocked** label can therefore appear on a part that still contributes to the quote. [Slot classification](https://github.com/Soulztorm/ChooChoo-TraderModding/blob/ee0c5c2c930df9eb868e44c7fb6a18810730a14c/ChooChoo-TraderModding_Client/Client/Patches/DropDownPatch.cs#L184) · [Quick Buy](https://github.com/Soulztorm/ChooChoo-TraderModding/blob/ee0c5c2c930df9eb868e44c7fb6a18810730a14c/ChooChoo-TraderModding_Client/Client/TraderModding/TraderModdingScript.cs#L272)

An earned Edit Build row carries an **Unlocked** badge with the caption **Free with Assemble**. The badge belongs to the native item view and is removed when EFT disposes that row's item context. Existing choices keep their own item and price. The selector appends a compatible earned choice when the template is absent, including after its last trader offer expires.

Choo²'s total is the price of buying the parts on its list. Clicking **Quick Buy Items** can spend currency on a part that Assemble would supply for free. Each action follows its own rules using the player's real inventory.

**Check** the same all-earned plan from the same starting inventory twice. Assemble must issue just the missing quantities with zero trader purchases. Explicit Quick Buy must behave as it does with Choo² alone. In a mixed plan, only locked shortages reach Assemble's purchase window. Check both labels on pooled rows, with trader filters and price tags toggled and with Choo² absent.

### UI Fixes 6.0.4

UI Fixes attempts a swap in the four-argument `WeaponModdingManipulation.Select` when a slot is occupied. HarmonyX runs all prefixes on that method even when one skips its original. Handling a detached catalogue preview at that point would expose it to the swap code. [UI Fixes swap](https://github.com/tyfon7/UIFixes/blob/v6.0.4/src/Patches/WeaponModdingPatches.cs#L324) · [HarmonyX prefix behavior](https://github.com/BepInEx/HarmonyX/wiki/Prefix-changes)

In live Modding, `DropdownManipulation.SelectCheck` answers availability and the three-argument `Select` requests one part on click. Our prefixes recognize the exact catalogue preview. After acknowledgement the client verifies the screen's selector, displayed weapon, slot parent, and previous occupant. It calls the three-argument wrapper with the real stash item, which reaches the four-argument swap and plays the native selection sound. Owned rows of the same template and Edit Build's simulated plan follow their existing paths.

**Check** empty and occupied slots in both plugin orders and with UI Fixes absent. Hovering issues nothing. One successful click issues one part, retains the displaced part, and uses only real item IDs in transactions. A full stash rejects the grant. Closing the screen before acknowledgement leaves any issued part in the stash.

### Modding Stats Helper 1.2.0

The 2026-11-26 EFT log records two null-reference exceptions in `ShowMeTheStats.ShowTooltipPatch.Prefix` from `HoverTooltipArea.Show`. The helper remembers the last item hovered in a grid and reads its attributes in a general tooltip handler. A control can request a tooltip before an item supplies that context. The stacks identify the handler but leave the hovered control and the null value unknown. [Tagged tooltip source](https://github.com/Soulztorm/Wara-ModdingStatsHelper/blob/84ae5472cb397ae1f38d378987e39c447f6c68a4/Wara-ModdingStatsHelper/Client/ShowMeTheStatsPatches.cs#L25-L63)

Each catalogue preview has a detached EFT item controller and address. EFT binds that owner to the native item view. The badge's background and caption both pass pointer events through, and the row's disposal event clears the badge before reuse. The native view handles hover and Ctrl comparison. The generic tooltip exception still needs a comparison with Stats Helper alone.

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
| Acquisition | Feed PMC and Scav extraction, Run Through, transit, and death results. Check worn and nested gear, retained items, craft collection, partial mail claims, and a claim followed immediately by a sale in one request. Only the expected templates enter the set. |
| Persistence | Seed an older profile, sell the last copy of a previously earned item, serialize, and reload. Its unlock survives. Restore an earlier snapshot and start a fresh character. Verify inventory and unlocks follow each snapshot and two PMCs stay independent. |
| Spawn and ordinary sale | A customized weapon yields one factory build plus its earned parts. Check fresh IDs and parent links, empty magazines, and cleared Found in Raid flags. Selling the spawned item has the same outcome as selling an equivalent ordinary item under the same trader or flea settings. |
| Placement and retries | A locked part, a full stash, or a later failure in a multi-item batch leaves inventory intact. Replaying the same request after reloading the profile creates zero additional copies. A deliberate second request creates a distinct copy. |
| Spare magazines | Try equipped and stashed weapons with loaded, unlocked magazines. For each footprint, verify the empty copies in the main stash, the count in the success notification, and the original rounds in the fitted magazine. With room in worn gear but too little stash space, verify that only the stash-space warning appears. Check dismissal, a model swap before clicking, and eligibility with a fixed, missing, or locked magazine. |
| Equipment, Modding, and Edit Build | Catalogue equipment uses the top location tag in both picker layouts. Attachment dropdowns mark catalogue parts **Unlocked**. Check vital-slot hover on equipped weapons and weapons in the stash. Check access to both editing menus with the Workbench bonus active and inactive, including raid and Hideout contexts. Preview, save, and cancel send zero spawn requests. A build needing two identical parts with one already owned requests exactly one. After acknowledgement, equip uses the returned ID. A failed install leaves the issued item in the stash. |

The client tests cover selection decisions, queue messages, and method signatures. Actual Unity rendering and mouse interaction are outside their coverage.
