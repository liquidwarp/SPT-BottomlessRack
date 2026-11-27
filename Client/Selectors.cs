using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using EFT.UI.WeaponModding;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Liquidwarp.UnlockGearProgression.Client;

internal static class PreviewRows
{
    private static readonly Dictionary<string, Item> ByTemplate = new Dictionary<string, Item>();
    private static readonly HashSet<Item> Previews = new HashSet<Item>();
    internal static void Reset() { ByTemplate.Clear(); Previews.Clear(); }
    internal static bool IsPreview(Item item) => item != null && Previews.Contains(item);

    private static Item Get(string id)
    {
        if (ByTemplate.TryGetValue(id, out var item)) return item;
        var factory = Singleton<ItemFactory>.Instance;
        if (factory.ItemTemplates.ContainsKey(id))
        {
            try
            {
                item = GearClient.WeaponBuilds.TryGetValue(id, out var parts)
                    ? BuildWeapon(factory, id, parts)
                    : factory.CreateAndFillItem(id);
                var owner = new ItemController(item, factory.NextId, "Unlocked gear", false, EOwnerType.HideoutProduction);
                item.CurrentAddress = owner.CreateItemAddress();
                Previews.Add(item);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not preview gear {id}: {ex.Message}");
                item = null;
            }
        }
        ByTemplate.Add(id, item);
        return item;
    }

    private static Item BuildWeapon(ItemFactory factory, string template, WeaponBuildPart[] parts)
    {
        if (parts.Length == 0 || parts[0].TemplateId != template || parts[0].ParentIndex != -1)
            throw new InvalidOperationException("Missing standard weapon root.");
        var items = new Item[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var item = items[i] = factory.CreateItem(factory.NextId, part.TemplateId, null);
            if (i == 0) continue;
            if (part.ParentIndex < 0 || part.ParentIndex >= i || !(items[part.ParentIndex] is CompoundItem parent))
                throw new InvalidOperationException("Invalid standard weapon parent.");
            var slot = Array.Find(parent.Slots, s => s.ID == part.SlotId);
            if (slot == null || slot.ContainedItem != null || !slot.CanAccept(item))
                throw new InvalidOperationException($"Cannot mount {part.TemplateId} in {part.SlotId}.");
            slot.Add(item);
        }
        return items[0];
    }

    internal static IEnumerable<Item> Include(IEnumerable<Item> owned, Slot slot,
        bool editBuild = false, bool equipment = false)
    {
        var shown = editBuild ? new HashSet<string>() : null;
        foreach (var item in owned)
        {
            shown?.Add(item.TemplateId);
            yield return item;
        }
        foreach (var id in CandidateTemplates(slot))
        {
            if (shown != null && shown.Contains(id)) continue;
            var item = Get(id);
            if (item == null || !slot.CanAccept(item)) continue;
            if (!equipment && slot.ContainedItem?.TemplateId == item.TemplateId) continue;
            if (equipment && item is Weapon weapon)
            {
                using var missing = weapon.MissingVitalParts.GetEnumerator();
                if (missing.MoveNext()) continue;
            }
            yield return item;
        }
    }

    private static IEnumerable<string> CandidateTemplates(Slot slot)
    {
        // Attachment slots often list only concrete template IDs. Avoid creating every unlocked preview
        // just to discard it. Base-class filters still go through EFT's normal CanAccept check below.
        foreach (var filter in slot.Filters ?? Array.Empty<ItemFilter>())
        {
            if (filter.Filter == null) continue;
            var concrete = true;
            foreach (var id in filter.Filter)
                if (JsonTypes.TypeTable.ContainsKey(id)) { concrete = false; break; }
            if (!concrete) continue;
            var seen = new HashSet<string>();
            foreach (var id in filter.Filter)
                if (GearClient.Unlocked.Contains(id) && seen.Add(id)) yield return id;
            yield break;
        }
        foreach (var id in GearClient.Unlocked) yield return id;
    }
}

[HarmonyPatch(typeof(InventoryScreen), nameof(InventoryScreen.Show), typeof(InventoryScreen.InventoryScreenController))]
internal static class StashPatch
{
    private static void Postfix(InventoryScreen.InventoryScreenController controller)
    {
        if (!controller.InRaid && !controller.IsInventoryBlocked)
            GearClient.Enter(controller.InventoryController);
    }
}

[HarmonyPatch(typeof(EquipItemWindow), nameof(EquipItemWindow.GetAvailableItems))]
internal static class EquipRowsPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(EquipItemWindow __instance, Slot ____slot, ref IEnumerable<Item> __result)
    {
        if (!GearClient.OutOfRaid) return;
        SelectorRefresh.Watch(__instance.gameObject, __instance.RefreshItemViews);
        if (!GearClient.Ready) return;
        __result = PreviewRows.Include(__result, ____slot, equipment: true);
    }
}

[HarmonyPatch(typeof(EquipItemWindow), nameof(EquipItemWindow.RefreshItemViews))]
internal static class EquipPreviewTagPatch
{
    // Equip From Weapon Rack writes its location tags in a postfix on the same refresh.
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ViewList<Item, ItemView> ____viewList, GameObject ____placeHolder)
    {
        if (____viewList == null) return;
        foreach (var (item, row) in ____viewList)
        {
            if (!PreviewRows.IsPreview(item) || !(row is GridItemView view) || view == null) continue;
            if (____placeHolder != null && ____placeHolder.activeSelf) ____placeHolder.SetActive(false);
            var label = view.TagName;
            if (label == null) continue;
            // EFT's text-only prefab leaves the panel field empty. Binding it also lets EFT
            // clear the tag the next time it lends this view to an owned item.
            if (view._tagColor == null && !label.transform.parent.TryGetComponent(out view._tagColor)) continue;

            label.text = "Unlocked";
            label.gameObject.SetActive(true);
            var rect = view._tagColor.rectTransform;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(60f, label.preferredWidth + 12f));
            rect.gameObject.SetActive(true);
        }
    }
}

[HarmonyPatch(typeof(EquipItemWindow), "EFT.InventoryLogic.SelectableItemContext.ISelectionContext.IsActive")]
internal static class EquipAvailabilityPatch
{
    private static bool Prefix(ItemContext context, ref string tooltip, ref bool __result)
    {
        if (!PreviewRows.IsPreview(context.Item)) return true;
        tooltip = "Spawn to stash and equip";
        __result = GearClient.Ready;
        return false;
    }
}

[HarmonyPatch(typeof(EquipItemWindow), nameof(EquipItemWindow.ItemSelectedHandler))]
internal static class EquipSelectionPatch
{
    private static readonly AccessTools.FieldRef<EquipItemWindow, Slot> CurrentSlot =
        AccessTools.FieldRefAccess<EquipItemWindow, Slot>("_slot");

    private static bool Prefix(EquipItemWindow __instance, Item item)
    {
        if (!PreviewRows.IsPreview(item)) return true;
        GearClient.Run(Equip(__instance, CurrentSlot(__instance), item.TemplateId));
        return false;
    }

    private static async Task Equip(EquipItemWindow window, Slot slot, string template)
    {
        var before = slot.ContainedItem;
        var outcome = await StashSelection.IssueAndUse(template, GearClient.One, item =>
        {
            if (window == null || !window.isActiveAndEnabled || CurrentSlot(window) != slot || before != slot.ContainedItem) return false;
            window.ItemSelectedHandler(item);
            return !window.isActiveAndEnabled;
        });
        if (outcome == StashUseResult.InStash) GearClient.Warn("The new item is in your stash.");
    }
}

[HarmonyPatch(typeof(WeaponModdingScreen), nameof(WeaponModdingScreen.Show), typeof(Item), typeof(InventoryController), typeof(CompoundItem[]))]
internal static class ModdingScreenPatch
{
    internal static WeaponModdingScreen Current;
    internal static readonly AccessTools.FieldRef<WeaponModdingScreen, Item> ShownItem =
        AccessTools.FieldRefAccess<WeaponModdingScreen, Item>("Item");
    private static void Postfix(WeaponModdingScreen __instance, InventoryController inventoryController)
    {
        if (!GearClient.OutOfRaid) return;
        Current = __instance;
        SelectorRefresh.Watch(__instance.gameObject);
        GearClient.Enter(inventoryController);
    }
}

[HarmonyPatch(typeof(ModdingScreenSlotView), nameof(ModdingScreenSlotView.Show))]
internal static class EquippedModdingSlotPatch
{
    private static void Postfix(ModdingScreenSlotView __instance, IItemObserverScreen moddingScreen,
        CompoundItem item, Slot slot, bool ____locked)
    {
        // Show disables pointer input on every equipped vital slot, even when the
        // inventory context menu allowed this weapon to be opened for modding.
        if (____locked && moddingScreen is WeaponModdingScreen && item is Weapon && !slot.Locked &&
            !(item.CurrentAddress is SlotItemAddress address && address.Slot.Locked) && GearClient.OutOfRaid)
            __instance.SetLockedStatus(false);
    }
}

[HarmonyPatch(typeof(WeaponModdingManipulation), nameof(WeaponModdingManipulation.GetItemCollections))]
internal static class ModdingRowsPatch
{
    private static void Postfix(Slot slot, ref IEnumerable<Item> __result)
    {
        if (GearClient.Ready) __result = PreviewRows.Include(__result, slot);
    }
}

[HarmonyPatch(typeof(EditBuildManipulation), nameof(EditBuildManipulation.GetItemCollections))]
internal static class BuildRowsPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Slot slot, ref IEnumerable<Item> __result)
    {
        if (GearClient.Ready) __result = PreviewRows.Include(__result, slot, editBuild: true);
    }
}

[HarmonyPatch(typeof(DropdownManipulation), nameof(DropdownManipulation.SelectCheck))]
internal static class InstallCheckPatch
{
    private static bool Prefix(DropdownManipulation __instance, Item item, Slot slot, ref Error error, ref bool __result) =>
        InstallPatch.PreviewSelection(__instance, item, slot, false, ref error, ref __result);
}

[HarmonyPatch(typeof(DropdownManipulation), nameof(DropdownManipulation.Select),
    new[] { typeof(Item), typeof(Slot), typeof(Error) }, new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
internal static class InstallPatch
{
    private static readonly AccessTools.FieldRef<WeaponModdingScreen, DropdownManipulation> Manipulation =
        AccessTools.FieldRefAccess<WeaponModdingScreen, DropdownManipulation>("_manipulation");

    private static bool Prefix(DropdownManipulation __instance, Item item, Slot slot, ref Error error, ref bool __result) =>
        PreviewSelection(__instance, item, slot, true, ref error, ref __result);

    internal static bool PreviewSelection(DropdownManipulation selector, Item item, Slot slot, bool clicked,
        ref Error error, ref bool result)
    {
        if (!(selector is WeaponModdingManipulation) || !PreviewRows.IsPreview(item)) return true;
        if (!GearClient.Ready)
        {
            error = new StringError("This unlocked part cannot be fitted here.");
            result = false;
            return false;
        }
        // Use EFT's stash-row move or swap simulation for both the tooltip and the click.
        result = selector.Select(item, slot, simulate: true, out error);
        if (result && clicked) GearClient.Run(Install(selector, slot, item.TemplateId));
        return false;
    }

    private static async Task Install(DropdownManipulation selector, Slot slot, string template)
    {
        var screen = ModdingScreenPatch.Current;
        if (screen == null || !screen.isActiveAndEnabled || !ReferenceEquals(Manipulation(screen), selector)) return;
        var previous = slot.ContainedItem;
        var target = ModdingScreenPatch.ShownItem(screen);
        var outcome = await StashSelection.IssueAndUse(template, GearClient.One, item =>
        {
            var parent = slot.ParentItem;
            if (!GearClient.Ready || screen == null || !screen.isActiveAndEnabled ||
                !ReferenceEquals(Manipulation(screen), selector) || ModdingScreenPatch.ShownItem(screen) != target ||
                slot.ContainedItem != previous || parent == null || (parent != target && !parent.IsChildOf(target)) ||
                !selector.Select(item, slot, out _)) return false;
            screen.WeaponUpdate();
            return true;
        });
        if (outcome == StashUseResult.InStash) GearClient.Warn("The part could not be installed. It is in your stash.");
    }
}

[HarmonyPatch(typeof(DropDownMenu), nameof(DropDownMenu.CreateDropdownModdingItemView))]
internal static class ModdingPreviewLabelPatch
{
    // Trader Modding adds price tags after EFT parents each dropdown or fitted-part view.
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(SelectableItemContext sourceContext, Item item, RectTransform container) =>
        Mark(sourceContext, item, container);

    [HarmonyPatch(typeof(ModdingScreenSlotView), nameof(ModdingScreenSlotView.CreateModdingItemView))]
    private static class FittedPart
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(SelectableItemContext itemContext, Item item, Transform container) =>
            Mark(itemContext, item, container);
    }

    private static void Mark(SelectableItemContext context, Item item, Transform container)
    {
        if ((!IsUnlockedBuildPart(context, item) && !PreviewRows.IsPreview(item)) || container.childCount == 0) return;
        if (!container.GetChild(container.childCount - 1).TryGetComponent(out ModdingSelectableItemView view) ||
            !ReferenceEquals(view.Item, item) || view.Caption == null) return;

        // Choo² owns these objects and their cleanup. Same-frame pool reuse can leave
        // both the previous price (pending destruction) and the current one under this icon.
        var stats = view.ItemViewStats;
        if (stats != null && stats._modTypeIcon != null)
        {
            var icon = stats._modTypeIcon.transform;
            for (var i = 0; i < icon.childCount; i++)
            {
                var child = icon.GetChild(i).gameObject;
                var name = child.name;
                if (name == "ItemPriceTag" || name == "ItemPriceTagBG2") child.SetActive(false);
            }
        }

        var caption = view.Caption;
        var box = new GameObject("Unlocked gear badge", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        box.transform.SetParent(view.transform, false);
        var rect = box.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.right;
        rect.offsetMin = new Vector2(1, 1);
        rect.offsetMax = new Vector2(-1, 1 + Mathf.Ceil(caption.fontSize * 1.3f));
        box.color = new Color(0, 0, 0, 0.85f);
        box.raycastTarget = false;

        var label = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(rect, false);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(1, 0);
        label.rectTransform.offsetMax = new Vector2(-1, 0);
        label.font = caption.font;
        label.text = "Unlocked";
        label.enableAutoSizing = true;
        label.fontSizeMax = caption.fontSize;
        label.fontSizeMin = Mathf.Min(6f, label.fontSizeMax);
        label.enableWordWrapping = false;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        // EFT disposes this binding before lending the pooled view to another item.
        var binding = view.ItemContext;
        void Clear()
        {
            binding.OnDisposed -= Clear;
            if (box == null) return;
            box.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(box.gameObject);
        }
        binding.OnDisposed += Clear;
    }

    internal static bool IsUnlockedBuildPart(SelectableItemContext context, Item item) =>
        item != null && context is ModdingSelectableItemContext { ModdingSelectionContext: EditBuildScreen } &&
        GearClient.Ready && GearClient.Unlocked.Contains(item.TemplateId);
}

public sealed class SelectorRefresh : MonoBehaviour
{
    private Action _refresh;
    internal static SelectorRefresh Watch(GameObject owner, Action refresh = null)
    {
        var watcher = owner.GetComponent<SelectorRefresh>();
        if (watcher == null) watcher = owner.AddComponent<SelectorRefresh>();
        watcher._refresh = refresh;
        watcher.enabled = true;
        return watcher;
    }
    internal void StopWatching()
    {
        enabled = false;
        _refresh = null;
    }
    private void OnEnable()
    {
        GearClient.Updated += OnChanged;
        InvokeRepeating(nameof(Poll), 3f, 5f);
    }
    private void OnDisable()
    {
        CancelInvoke(nameof(Poll));
        GearClient.Updated -= OnChanged;
    }
    private void OnChanged() => _refresh?.Invoke();
    private void Poll() => GearClient.Refresh(checkOnly: true);
}
