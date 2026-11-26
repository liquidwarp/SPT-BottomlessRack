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
                item = factory.CreateAndFillItem(id);
                if (item is Weapon) item = factory.GetPresetItem(id);
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

    internal static IEnumerable<Item> Include(IEnumerable<Item> owned, Slot slot,
        EditBuildManipulation editor = null, bool equipment = false)
    {
        var shown = editor == null ? null : new HashSet<string>();
        HashSet<string> available = null;
        if (editor?._playerItems != null)
        {
            available = new HashSet<string>();
            foreach (var part in editor._playerItems) available.Add(part.TemplateId);
        }
        foreach (var item in owned)
        {
            if (editor != null)
            {
                var id = item.TemplateId.ToString();
                shown.Add(id);
                if (available != null && !available.Contains(id) && GearClient.Unlocked.Contains(id))
                {
                    yield return Get(id) ?? item;
                    continue;
                }
            }
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
    private static void Postfix(ViewList<Item, ItemView> ____viewList)
    {
        if (____viewList == null) return;
        foreach (var (item, row) in ____viewList)
        {
            if (!PreviewRows.IsPreview(item) || !(row is GridItemView view) || view == null) continue;
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
    private static void Postfix(EditBuildManipulation __instance, Slot slot, ref IEnumerable<Item> __result)
    {
        if (GearClient.Ready) __result = PreviewRows.Include(__result, slot, __instance);
    }
}

[HarmonyPatch(typeof(WeaponModdingManipulation), nameof(WeaponModdingManipulation.Select))]
internal static class InstallPatch
{
    private static bool Prefix(WeaponModdingManipulation __instance, Item item, Slot slot, bool simulate,
        ref Error error, ref bool __result)
    {
        if (!PreviewRows.IsPreview(item)) return true;
        error = null;
        __result = GearClient.Ready && slot.CanAccept(item);
        if (__result && !simulate) GearClient.Run(Install(__instance, slot, item.TemplateId));
        return false;
    }

    private static async Task Install(WeaponModdingManipulation selector, Slot slot, string template)
    {
        var screen = ModdingScreenPatch.Current;
        var previous = slot.ContainedItem;
        var target = screen != null ? ModdingScreenPatch.ShownItem(screen) : null;
        var outcome = await StashSelection.IssueAndUse(template, GearClient.One, item =>
        {
            if (screen == null || !screen.isActiveAndEnabled || ModdingScreenPatch.ShownItem(screen) != target ||
                slot.ContainedItem != previous || !selector.Select(item, slot, false, out _)) return false;
            screen.WeaponUpdate();
            return true;
        });
        if (outcome == StashUseResult.InStash) GearClient.Warn("The part could not be installed. It is in your stash.");
    }
}

[HarmonyPatch(typeof(DropDownMenu), nameof(DropDownMenu.CreateDropdownModdingItemView))]
internal static class ModdingPreviewLabelPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(DropDownMenu __instance, SelectableItemContext sourceContext, Item item, RectTransform container)
    {
        if (PreviewRows.IsPreview(item) || IsUnlockedBuildPart(sourceContext, item))
            Show(__instance, item, container);
    }

    // Edit Build uses catalogue entries in the picker and clones the chosen item onto the plan.
    internal static bool IsUnlockedBuildPart(SelectableItemContext context, Item item) =>
        context is ModdingSelectableItemContext { ModdingSelectionContext: EditBuildScreen } &&
        GearClient.Ready && GearClient.Unlocked.Contains(item.TemplateId);

    internal static void Show(UIElement owner, Item item, Transform container)
    {
        if (container.childCount == 0) return;
        if (!container.GetChild(container.childCount - 1).TryGetComponent(out ModdingSelectableItemView view) ||
            view.Item != item || view.Caption == null) return;

        // Trader Modding appends the background and caption to the mod-type icon.
        // Read the newest pair because Unity defers destruction when the dropdown reopens.
        var icon = view.ItemViewStats != null ? view.ItemViewStats._modTypeIcon : null;
        var parent = icon != null ? icon.transform : null;
        Image box;
        TextMeshProUGUI label;
        if (parent != null && parent.childCount >= 2 &&
            parent.GetChild(parent.childCount - 1).name == "ItemPriceTag" &&
            parent.GetChild(parent.childCount - 1).TryGetComponent(out label) &&
            parent.GetChild(parent.childCount - 2).name == "ItemPriceTagBG2" &&
            parent.GetChild(parent.childCount - 2).TryGetComponent(out box))
        {
            box.transform.SetParent(view.transform, false);
            label.transform.SetParent(box.transform, false);
        }
        else
        {
            // Earned parts can have no offer, and Trader Modding is optional.
            box = new GameObject("Unlocked tag", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            box.transform.SetParent(view.transform, false);
            label = UnityEngine.Object.Instantiate(view.Caption, box.transform, false);
            owner.AddDisposable(() =>
            {
                box.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(box.gameObject);
            });
        }

        var rect = box.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1, 0);
        rect.offsetMin = new Vector2(14, 1);
        rect.offsetMax = new Vector2(-1, Mathf.Ceil(view.Caption.fontSize) + 4);
        box.color = Color.black;
        box.raycastTarget = false;

        var textRect = label.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(1, 0);
        textRect.offsetMax = new Vector2(-1, 0);
        label.text = "Unlocked";
        label.margin = Vector4.zero;
        label.enableAutoSizing = true;
        label.fontSizeMax = view.Caption.fontSize;
        label.fontSizeMin = Mathf.Min(6f, label.fontSizeMax);
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Truncate;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.gameObject.SetActive(true);
    }
}

[HarmonyPatch(typeof(ModdingScreenSlotView), nameof(ModdingScreenSlotView.CreateModdingItemView))]
internal static class BuildSlotLabelPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ModdingScreenSlotView __instance, SelectableItemContext itemContext, Item item, Transform container)
    {
        if (ModdingPreviewLabelPatch.IsUnlockedBuildPart(itemContext, item))
            ModdingPreviewLabelPatch.Show(__instance, item, container);
    }
}

[HarmonyPatch(typeof(DropDownMenu), nameof(DropDownMenu.Show))]
internal static class DropdownRefreshPatch
{
    private static void Postfix(DropDownMenu __instance, ModdingSelectableItemContext itemContext,
        ModdingScreenSlotView slotView, ItemController itemController)
    {
        if (!GearClient.OutOfRaid) return;
        SelectorRefresh.Watch(__instance.gameObject, () =>
        {
            if (!__instance.Open || slotView == null || !slotView.isActiveAndEnabled) return;
            __instance.Close();
            __instance.Show(itemContext, slotView, itemController);
        });
    }
}

public sealed class SelectorRefresh : MonoBehaviour
{
    private Action _refresh;
    internal static void Watch(GameObject owner, Action refresh)
    {
        var watcher = owner.GetComponent<SelectorRefresh>();
        if (watcher == null) watcher = owner.AddComponent<SelectorRefresh>();
        watcher._refresh = refresh;
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
        _refresh = null;
    }
    private void OnChanged() => _refresh?.Invoke();
    private void Poll() => GearClient.Refresh(checkOnly: true);
}
