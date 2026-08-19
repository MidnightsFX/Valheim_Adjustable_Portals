using System.Collections.Generic;
using UnityEngine;

namespace AdjustablePortals.common {
    internal static class Extensions {

        /// <summary>
        /// Total number of items across the inventory whose drop prefab matches <paramref name="prefab"/>.
        /// Inventory.CountItems matches on the shared display name, which is not what this mod keys on.
        /// </summary>
        public static int CountItemsByPrefab(this Inventory inv, string prefab) {
            int total = 0;
            foreach (ItemDrop.ItemData user_item in inv.GetAllItems()) {
                if (user_item.m_dropPrefab != null && user_item.m_dropPrefab.name == prefab && user_item.m_stack > 0) {
                    total += user_item.m_stack;
                }
            }
            return total;
        }

        /// <summary>
        /// Removes <paramref name="countToRemove"/> items matching <paramref name="prefab"/>, drawing
        /// across stacks. Returns false and removes nothing when the inventory cannot cover the cost.
        /// </summary>
        public static bool RemoveItemByPrefab(this Inventory inv, string prefab, int countToRemove) {
            if (countToRemove <= 0) {
                return true;
            }
            if (inv.CountItemsByPrefab(prefab) < countToRemove) {
                Logger.LogDebug($"Remove summary: {prefab}x{countToRemove} not available, removing nothing.");
                return false;
            }

            // Snapshot first: Inventory.RemoveItem mutates the list GetAllItems hands back.
            List<ItemDrop.ItemData> matching = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData user_item in inv.GetAllItems()) {
                if (user_item.m_dropPrefab != null && user_item.m_dropPrefab.name == prefab && user_item.m_stack > 0) {
                    matching.Add(user_item);
                }
            }

            int remaining = countToRemove;
            foreach (ItemDrop.ItemData user_item in matching) {
                if (remaining <= 0) {
                    break;
                }
                // Handles both the whole-stack and partial-stack cases, and marks the inventory
                // changed so the GUI and save state keep up.
                int taken = Mathf.Min(user_item.m_stack, remaining);
                inv.RemoveItem(user_item, taken);
                remaining -= taken;
            }

            Logger.LogDebug($"Remove summary: {prefab}x{countToRemove} successfully removed: {countToRemove - remaining}");
            return remaining == 0;
        }
    }
}
