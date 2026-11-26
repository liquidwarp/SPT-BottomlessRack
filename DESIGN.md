# Unlock Gear Progression

This design gives each PMC in SPT 4.1 a permanent equipment catalogue. Once a trader other than Fence offers a piece of gear to that PMC, or the player brings one home, the player can create fresh copies through EFT’s equipment picker, Modding, and Edit Build. Selling or losing the original preserves the unlock.

For example, extract with a customized rifle and the weapon-slot picker gains one **Unlocked** entry for its standard build. Its factory parts unlock immediately, even if the recovered rifle used different parts. The recovered optic and mount also become individual choices in Modding and Edit Build. The player can sell the recovered rifle and still use all of those choices.

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
| Modding | Select an inventory attachment or choose a compatible part marked **Unlocked** in the bottom price box. Choosing **Unlocked** creates one real part and applies the normal install action to the selected slot. EFT handles conflicts and displaced parts. |
| Edit Build | Editing, previewing, and saving a weapon preset only change the plan. Assemble uses the selected weapon and available parts first. It can issue an unlocked factory weapon when a body is missing, then creates missing unlocked parts and runs the native assembler. Only remaining locked shortages reach the purchase window. |
| Equipment presets | Equip uses EFT’s loadout simulation to match worn and stored items. Issue missing unlocked weapons first, then recheck the loadout before issuing other unlocked gear. Apply the preset when complete or show the remaining locked items in EFT’s purchase window. |

Modding and Edit Build are available out of raid at every Workbench level, including level zero. The client enables the weapon modification bonus for the item context menu and the Edit Build taskbar button. Building and upgrading the Workbench still governs its crafts.

Copies are free, start at factory condition, and are issued with the Found in Raid flag cleared. Trader sales and flea listings follow the usual SPT rules for that item and condition. The server resolves the single standard weapon build from the loaded SPT default preset. Its eligible factory parts unlock individually with the weapon. Magazines and chambers start empty. Packs and rigs start with empty grids. Armor gets its integral inserts, and removable plates unlock individually. The equipment picker offers weapons with all vital parts present. For a modded weapon with no usable factory preset, a functional bare weapon can serve as its standard build.

Every new copy first enters the main stash, so even a one-click equip needs free stash space. If there is too little room for the request, show **Not enough stash space** and create zero items. If the item is created but the subsequent equip or install fails, leave the copy in the stash and tell the player where it is. Retrying the equip uses that copy.

## Server and client design

The SPT server owns unlocks and item creation. The BepInEx plugin adds unlocked choices to EFT’s existing menus on each participating player’s client. Fika headless handles ordinary EFT items during raids. Use the `Client/` and `Server/` solution layout from `SPT-HideLockedFlea` and the `SPT-Infil` build conventions. Build with `./build.ps1` and package the player plugin under `BepInEx/plugins` and the server mod under `SPT_Runtime/user/mods`. [SPT 4.1 server mod guide](https://github.com/sp-tarkov/wiki/blob/main/SPT_41/Server_40_to_41.md)

### Why the catalogue lives in the profile

A PMC can claim an optic by mail and sell their only copy. If none of their traders offer it, the next stash and assort scan has no way to recover that unlock. Saving it in the PMC’s profile keeps it earned. Restoring a backup brings back the matching inventory and unlocks, a wipe starts a fresh catalogue, and each Fika player keeps their own progression.

Store a single `net.liquidwarp.unlockgearprogression` entry in `SptProfile.SptData` extension data with a schema version, the set of earned template IDs, and the most recent spawn receipt. Each template is recorded once. Saving the receipt alongside inventory prevents a lost reply from turning one request into two copies. Keep the selector cache and its revision in memory and fetch a fresh catalogue on reconnect. Keep missing mod templates dormant so reinstalling an item mod restores its entries.

A separate file keyed to the character would also work. It would need to be backed up, restored, and reset together with the profile, even when a wipe reuses the same account. Using SPT’s existing save path keeps those operations together.

### Discovering and issuing gear

Skip `Traders.FENCE` by trader ID before fetching or processing an assort, on both initial discovery and refresh. For each other accessible, enabled trader, use `TraderAssortHelper.GetAssort` with `showLockedAssorts: false` and follow each eligible offer’s output tree. Evaluate assorts on login, relevant trader or quest progression, and assortment refresh for active profiles. Resolve standard builds after item mods have initialized. For raids, wait for an accepted final result and SPT’s loss processing. For a successful player Scav use the returned character’s equipment tree before loadout regeneration. [SPT trader assort helper](https://github.com/SP-Tushonka/server-csharp/blob/4.1x-dev/Libraries/SPTushonka.Server.Core/Helpers/Traders/TraderAssortHelper.cs)

Observe completed inventory actions individually, including actions in the same request. Follow new or transferred roots into the PMC’s inventory. Mail claims can move an existing tree without emitting new item IDs. Reconcile owned roots on login and stash entry. To catch a direct grant from another mod that is immediately sold or discarded, also inspect the owned tree just before it leaves the profile. Cache the catalogue per profile and refresh an open selector when its revision changes. [SPT inventory controller](https://github.com/SP-Tushonka/server-csharp/blob/4.1x-dev/Libraries/SPTushonka.Server.Core/Controllers/InventoryController.cs)

**Create real inventory items.** Expose a session-scoped catalogue read and a custom spawn item event on EFT’s existing inventory command queue. The read returns unlocked templates, the catalogue revision, and the last processed spawn sequence so a reconnected client can resume. A spawn request sends template IDs, positive quantities, and a request sequence. The server derives the PMC from the session, checks that the player is out of raid, validates every unlock, and builds fresh item trees with unique IDs and remapped parent links. Preflight and stage the whole batch before publishing changes. `InventoryHelper.AddItemsToStash` checks fit up front but inserts each tree separately, so a later placement failure needs to discard the staged changes. [SPT inventory helper](https://github.com/SP-Tushonka/server-csharp/blob/4.1x-dev/Libraries/SPTushonka.Server.Core/Helpers/Profile/InventoryHelper.cs)

Serialize the grant with the PMC’s inventory changes. Save the receipt and inventory together, then return SPT’s `ProfileChanges` item delta and the issued root IDs. A repeat of the same sequence and payload resolves to the original IDs. An older sequence or a reused sequence with different contents triggers a refresh. After a lost reply, reconcile those IDs with the profile before applying any delta again. Only pass acknowledged items into EFT’s equip, install, or assemble action.

**Client integration points.** The 4.1.6 client exposes `EquipItemWindow.GetAvailableItems` and `ItemSelectedHandler` for equipment. `WeaponModdingManipulation` and `EditBuildManipulation` expose `GetItemCollections` and `Select` for attachment dropdowns. `EditBuildScreen.GetMissingBuildItems` and `TryToAssembleBuild` provide the assembly boundary. The client sends grants through `SendOperationRightNow` on EFT’s inventory queue. EFT applies `ProfileChanges` before invoking its immediate callback. The mod then reads the server receipt and looks up the issued IDs in the real stash. The ordinary queued callback can fire before a request is sent. Preview items have a detached client-side owner and use EFT’s compatibility filters. The picker preserves existing owned-item choices, including entries supplied by other mods. [Equip From Weapon Rack](https://forge.sp-tarkov.com/mod/1136/equip-from-weapon-rack)

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
| Equipment, Modding, and Edit Build | Catalogue equipment uses the top location tag in both picker layouts. Attachment dropdowns mark catalogue parts **Unlocked** in the bottom price box. Check vital-slot hover on equipped weapons and weapons in the stash. Check access to both editing menus with the Workbench bonus active and inactive, including raid and Hideout contexts. Preview, save, and cancel send zero spawn requests. A build needing two identical parts with one already owned requests exactly one. After acknowledgement, equip uses the returned ID. A failed install leaves the issued item in the stash. |

The client tests cover selection decisions, queue messages, and method signatures. Actual Unity rendering and mouse interaction are outside their coverage.
