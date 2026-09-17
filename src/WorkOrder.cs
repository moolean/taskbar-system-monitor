using System.Collections.Generic;

namespace TaskbarSystemMonitor
{
    internal static class WorkOrder
    {
        // A drop slot is the gap BEFORE an item (Count is the final gap).
        internal static int TargetIndex(int source, int slot, int count)
        {
            if (source < 0 || source >= count || slot < 0 || slot > count) return -1;
            return slot > source ? slot - 1 : slot;
        }
        internal static bool Move(List<WorkItem> items, int source, int target)
        {
            if (source < 0 || target < 0 || source >= items.Count || target >= items.Count || source == target) return false;
            var item = items[source]; items.RemoveAt(source); items.Insert(target, item); return true;
        }
    }
}
