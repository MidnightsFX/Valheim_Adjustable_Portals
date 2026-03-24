using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdjustablePortals.common {
    internal static class Extensions {
        public static bool RemoveItemByPrefab(this Inventory inv, string prefab, int countToRemove) {
            List<ItemDrop.ItemData> user_inventory = inv.GetAllItems();
            int remaining = countToRemove;
            List<ItemDrop.ItemData> itemsToRemove = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData user_item in user_inventory) {
                //Logger.LogDebug($"Comparing {user_item.m_dropPrefab.name} to {prefab} match? {user_item.m_dropPrefab.name == prefab}");
                if (user_item.m_dropPrefab.name == prefab) {
                    //Logger.LogDebug($"stack {user_item.m_stack} > 0 = {user_item.m_stack > 0}");
                    if (user_item.m_stack > 0) {
                        if (remaining >= user_item.m_stack) {
                            if (user_item.m_stack <= remaining) {
                                itemsToRemove.Add(user_item);
                                remaining -= user_item.m_stack;
                            } else {
                                user_item.m_stack -= remaining;
                                remaining = 0;
                            }
                        } else {
                            user_item.m_stack -= remaining;
                            break;
                        }
                    } else {
                        // zero sized or less than zero size stacks are invalid and should be removed regardless
                        // but it doesn't count towards the tribute contribution you monster
                        itemsToRemove.Add(user_item);
                    }
                }
            }

            foreach (ItemDrop.ItemData item in itemsToRemove) {
                inv.RemoveItem(item);
            }
            Logger.LogDebug($"Remove summary: {prefab}x{countToRemove} successfully removed: {countToRemove - remaining}");
            return remaining == 0;
        }
    }
}
