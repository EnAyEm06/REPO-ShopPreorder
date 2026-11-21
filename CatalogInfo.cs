using System.Collections.Generic;

namespace ShopPreorder
{
    /// <summary>Everything the catalog menu shows. Built by the host; sent to other players as plain arrays.</summary>
    internal class CatalogInfo
    {
        internal static readonly string[] Categories =
            { "All", "Upgrades", "Weapons", "Explosives", "Health", "Drones", "Carts & Vehicles", "Tools", "Other" };

        public bool Allowed = true;
        public string DeniedReason = "";
        public int Money;
        public string Status = "";
        public bool HasPending;
        public bool CanCancel;
        public string PendingName = "";
        public int Refund;
        public readonly List<string> Keys = new List<string>();
        public readonly List<string> Names = new List<string>();
        public readonly List<int> Prices = new List<int>();
        public readonly List<int> CategoryIds = new List<int>();

        /// <summary>Category index (into Categories) for an item type.</summary>
        internal static int CategoryOf(int itemType)
        {
            switch (itemType)
            {
                case 3: return 1;                     // item_upgrade
                case 7: case 9: case 15: return 2;    // melee, gun, launcher
                case 6: case 11: return 3;            // grenade, mine
                case 8: return 4;                     // health pack
                case 0: case 1: return 5;             // drone, orb
                case 2: case 12: case 14: return 6;   // cart, pocket cart, vehicle
                case 10: case 13: return 7;           // tracker, tool
                default: return 8;                    // crystals and anything new
            }
        }

        public object[] ToNetwork() => new object[]
        {
            Allowed, DeniedReason ?? "", Money, Status ?? "", HasPending, CanCancel, PendingName ?? "", Refund,
            Keys.ToArray(), Names.ToArray(), Prices.ToArray(), CategoryIds.ToArray(),
        };

        public static CatalogInfo FromNetwork(object[] d)
        {
            var info = new CatalogInfo();
            if (d == null || d.Length < 12) { info.Allowed = false; info.DeniedReason = "The host sent a broken catalog."; return info; }
            info.Allowed = d[0] is bool a && a;
            info.DeniedReason = d[1] as string ?? "";
            info.Money = d[2] is int m ? m : 0;
            info.Status = d[3] as string ?? "";
            info.HasPending = d[4] is bool hp && hp;
            info.CanCancel = d[5] is bool cc && cc;
            info.PendingName = d[6] as string ?? "";
            info.Refund = d[7] is int r ? r : 0;
            if (d[8] is string[] keys) info.Keys.AddRange(keys);
            if (d[9] is string[] names) info.Names.AddRange(names);
            if (d[10] is int[] prices) info.Prices.AddRange(prices);
            if (d[11] is int[] cats) info.CategoryIds.AddRange(cats);
            return info;
        }
    }
}
