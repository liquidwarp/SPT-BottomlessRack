using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Liquidwarp.BottomlessRack.Client;

// EFT passes playerItems from the live Assemble call to its background planner. Carry the
// planned discards with that call so standalone simulations and overlapping builds stay independent.
[HarmonyPatch(typeof(WeaponAssembler), nameof(WeaponAssembler.Assemble))]
internal sealed class AssemblyDiscards(IEnumerable<Item> playerItems) : IEnumerable<Item>
{
    private readonly List<MoveResult> _removed = new();
    public IEnumerator<Item> GetEnumerator() => playerItems.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static void Prefix(ref IEnumerable<Item> playerItems, out AssemblyDiscards __state)
    {
        __state = null;
        if (!GearClient.OutOfRaid) return;
        __state = new AssemblyDiscards(playerItems);
        playerItems = __state;
    }

    private static void Postfix(InventoryController inventoryController, AssemblyDiscards __state, ref Task<bool> __result)
    {
        if (__state != null) __result = __state.Finish(__result, inventoryController);
    }

    private async Task<bool> Finish(Task<bool> assembly, InventoryController controller)
    {
        if (!await assembly) return false;
        foreach (var move in _removed)
            await ReplaceModdingPartPatch.DiscardAt(controller, move.Item, move.To);
        return true;
    }

    [HarmonyPatch(typeof(WeaponAssembler), nameof(WeaponAssembler.AssembleAsync))]
    private static class Plan
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // Filter the final sorting-table-to-stash list before EFT checks storage space.
            var code = new List<CodeInstruction>(instructions);
            var getItems = AccessTools.PropertyGetter(typeof(Grid), nameof(Grid.Items));
            var index = code.FindIndex(i => i.Calls(getItems));
            if (index < 2 || code[index - 1].operand is not FieldInfo { Name: "sortingTableGrid" } table ||
                code[index + 1].operand is not MethodInfo { Name: "ToList" })
                throw new InvalidOperationException("Could not find the weapon assembler's sorting table cleanup.");
            code.InsertRange(index + 2, new[] {
                new CodeInstruction(OpCodes.Ldarg_3), code[index - 2].Clone(),
                new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(table.DeclaringType, "moveOperations")),
                CodeInstruction.Call(typeof(Plan), nameof(ItemsToStore))
            });

            // Revolver cylinders hold rounds in slots. EFT would strip them before the stash check.
            var getItem = AccessTools.PropertyGetter(typeof(Slot), nameof(Slot.ContainedItem));
            var child = code.FindIndex(i => i.Calls(getItem));
            if (child < 0 || child + 1 >= code.Count ||
                (code[child + 1].opcode != OpCodes.Brfalse_S && code[child + 1].opcode != OpCodes.Brfalse))
                throw new InvalidOperationException("Could not find the weapon assembler's child removal check.");
            code.InsertRange(child + 1, new[] {
                new CodeInstruction(OpCodes.Ldarg_3), CodeInstruction.Call(typeof(Plan), nameof(ShouldDetach))
            });
            return code;
        }

        private static bool ShouldDetach(Item child, IEnumerable<Item> playerItems) =>
            child != null && !(playerItems is AssemblyDiscards && child is Ammo &&
                child.Parent.Container.ParentItem is Magazine { Count: > 0 });

        private static List<Item> ItemsToStore(List<Item> leftovers, IEnumerable<Item> playerItems, List<MoveResult> moves)
        {
            if (playerItems is not AssemblyDiscards batch) return leftovers;
            foreach (var move in moves)
            {
                if (move.From is SlotItemAddress && move.To is GridItemAddress && move.To.Equals(move.Item.CurrentAddress)
                    && move.Item is not Magazine { Count: > 0 } && !ItemManipulator.HasLockedContent(move.Item, out _)
                    && leftovers.Remove(move.Item))
                    batch._removed.Add(move);
            }
            return leftovers;
        }
    }
}
