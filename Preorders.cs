using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ShopPreorder
{
    /// <summary>Who typed a /preorder command.</summary>
    internal class Requester
    {
        public bool IsHost;
        public int ActorNumber;  // Photon player number (0 in singleplayer)
        public string Name;
        public Action<string[]> Reply;
    }

    /// <summary>Finding items, pricing them and handling the /preorder command. All of this runs on the host.</summary>
    internal static class Preorders
    {
        // itemType values used by the game's own pricing code
        private const int TypeUpgrade = 3;
        private const int TypeCrystal = 5;
        private const int TypeHealthPack = 8;

        private static readonly AccessTools.FieldRef<ShopManager, float> ItemValueMultiplier =
            AccessTools.FieldRefAccess<ShopManager, float>("itemValueMultiplier");
        private static readonly AccessTools.FieldRef<ItemAttributes, bool> ShopItemField =
            AccessTools.FieldRefAccess<ItemAttributes, bool>("shopItem");

        /// <summary>Discount rolled for each item during the current shop visit. Cleared when a shop loads.</summary>
        internal static readonly Dictionary<string, int> DiscountRolls = new Dictionary<string, int>();

        // ---------- catalog ----------

        /// <summary>Every item the shop could normally sell right now, by dictionary key.</summary>
        internal static List<KeyValuePair<string, Item>> AllSellableItems()
        {
            var result = new List<KeyValuePair<string, Item>>();
            var stats = StatsManager.instance;
            if (!stats || stats.itemDictionary == null) return result;

            int players = GameDirector.instance && GameDirector.instance.PlayerList != null
                ? GameDirector.instance.PlayerList.Count : 1;

            foreach (var kv in stats.itemDictionary)
            {
                var item = kv.Value;
                if (!item || item.disabled) continue;
                if ((int)item.status != 1) continue;                      // 1 = Finalized; skip unfinished (WIP) items
                if ((int)item.itemSecretShopType != 0) continue;          // secret shop items stay secret
                if (item.minPlayerCount > 1 && players < item.minPlayerCount) continue;
                if (SemiFunc.StatGetItemsPurchased(item.name) >= item.maxAmountInShop) continue;
                if (item.maxPurchase && stats.GetItemsUpgradesPurchasedTotal(item.name) >= item.maxPurchaseAmount) continue;
                result.Add(kv);
            }
            return result;
        }

        /// <summary>Items currently sitting on the shop shelves.</summary>
        internal static HashSet<Item> ItemsOnShelves()
        {
            var set = new HashSet<Item>();
            foreach (var attr in UnityEngine.Object.FindObjectsByType<ItemAttributes>(FindObjectsSortMode.None))
            {
                if (attr && attr.item && ShopItemField(attr)) set.Add(attr.item);
            }
            return set;
        }

        /// <summary>Sellable items that are NOT in the shop right now, cheapest first.</summary>
        internal static List<KeyValuePair<string, Item>> PreorderableItems()
        {
            var onShelves = ItemsOnShelves();
            return AllSellableItems()
                .Where(kv => !onShelves.Contains(kv.Value))
                .OrderBy(kv => ShopPrice(kv.Value))
                .ThenBy(kv => DisplayName(kv.Value))
                .ToList();
        }

        internal static string DisplayName(Item item) =>
            string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;

        // ---------- pricing ----------

        /// <summary>The normal shop price in $K, using the same maths as the game (midpoint of the price range).</summary>
        internal static int ShopPrice(Item item)
        {
            var shop = ShopManager.instance;
            float mult = shop ? ItemValueMultiplier(shop) : 1f;
            float value = (item.value.valueMin + item.value.valueMax) * 0.5f * mult;
            if (value < 1000f) value = 1000f;
            value = Mathf.Ceil(value / 1000f);

            if (shop)
            {
                switch ((int)item.itemType)
                {
                    case TypeUpgrade: value = shop.UpgradeValueGet(value, item); break;
                    case TypeHealthPack: value = shop.HealthPackValueGet(value); break;
                    case TypeCrystal: value = shop.CrystalValueGet(value); break;
                }
            }
            return Mathf.Max(1, Mathf.CeilToInt(value));
        }

        internal static int PreorderPrice(Item item)
        {
            float markup = 1f + Plugin.MarkupPercent.Value / 100f;
            return Mathf.Max(1, Mathf.CeilToInt(ShopPrice(item) * markup));
        }

        // ---------- matching what the player typed ----------

        private static string Normalize(string s) =>
            new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        internal static List<KeyValuePair<string, Item>> Match(string query, List<KeyValuePair<string, Item>> pool)
        {
            var q = Normalize(query);
            if (q.Length == 0) return new List<KeyValuePair<string, Item>>();

            var exact = pool.Where(kv => Normalize(DisplayName(kv.Value)) == q).ToList();
            if (exact.Count > 0) return exact;

            var words = query.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize).Where(w => w.Length > 0).ToArray();

            return pool.Where(kv =>
            {
                var name = Normalize(DisplayName(kv.Value));
                var key = Normalize(kv.Key);
                return words.All(w => name.Contains(w) || key.Contains(w));
            }).ToList();
        }

        // ---------- the command ----------

        /// <summary>Called when the local player types /preorder. Runs it here if we're the host, otherwise asks the host.</summary>
        internal static void OnLocalCommand(string args)
        {
            args = (args ?? "").Trim();
            if (ShopState.IsHost)
            {
                HandleOnHost(args, Net.LocalRequester());
            }
            else
            {
                Net.SendRequest(args);
            }
        }

        internal static void HandleOnHost(string args, Requester who)
        {
            try { HandleOnHostInner(args.Trim(), who); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Preorder command failed: " + e);
                who.Reply(new[] { "Preorder failed - see the host's BepInEx log." });
            }
        }

        private static void HandleOnHostInner(string args, Requester who)
        {
            // Host settings work anywhere, not just in the shop.
            var words = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var first = words.Length > 0 ? words[0].ToLowerInvariant() : "";
            if (SettingCommands.Contains(first)) { HandleSetting(first, words, who); return; }

            if (!who.IsHost && !Plugin.AllowEveryone.Value)
            {
                who.Reply(new[] { "The host has preorders set to host-only." });
                return;
            }
            if (!SemiFunc.RunIsShop())
            {
                who.Reply(new[] { "You can only preorder while in the shop." });
                return;
            }

            var lower = args.ToLowerInvariant();
            if (lower == "" || lower == "help")
            {
                var help = new List<string>
                {
                    "/preorder <item> - pay now, it's in the next shop",
                    "/preorder list - what you can preorder",
                    "/preorder status - this round's preorder",
                    "/preorder cancel - refund this round's preorder",
                };
                if (who.IsHost) help.Add("/preorder settings - see and change settings (host)");
                who.Reply(help.ToArray());
                return;
            }
            if (lower == "list") { ShowList(who); return; }
            if (lower == "status") { ShowStatus(who); return; }
            if (lower == "cancel") { Cancel(who); return; }
            if (args.StartsWith("#key ")) { PlaceOrderByKey(args.Substring(5), who); return; }

            PlaceOrder(args, who);
        }

        // ---------- host settings from chat ----------

        private static readonly HashSet<string> SettingCommands =
            new HashSet<string> { "settings", "everyone", "markup", "discount", "confusion" };

        private static string OnOff(bool b) => b ? "ON" : "OFF";

        private static void HandleSetting(string command, string[] words, Requester who)
        {
            if (!who.IsHost)
            {
                who.Reply(new[] { "Only the host can change preorder settings." });
                return;
            }

            string value = words.Length > 1 ? words[1].ToLowerInvariant() : "";

            switch (command)
            {
                case "settings":
                    who.Reply(new[]
                    {
                        $"Everyone can preorder: {OnOff(Plugin.AllowEveryone.Value)}  |  Markup: {Plugin.MarkupPercent.Value}%",
                        $"Discount: {Plugin.DiscountChancePercent.Value}% chance of " +
                        $"{Plugin.DiscountMinPercent.Value}-{Plugin.DiscountMaxPercent.Value}% off  |  " +
                        $"Wrong item: {Plugin.ConfusionChancePercent.Value}% chance",
                        "Change with: /preorder everyone on|off, markup 50, discount 33, discount 20-50, confusion 10",
                    });
                    return;

                case "everyone":
                    bool? on = value == "on" || value == "true" || value == "yes" ? true
                             : value == "off" || value == "false" || value == "no" ? false
                             : (bool?)null;
                    if (on == null)
                    {
                        who.Reply(new[] { $"Everyone can preorder: {OnOff(Plugin.AllowEveryone.Value)}. Use: /preorder everyone on (or off)" });
                        return;
                    }
                    Plugin.AllowEveryone.Value = on.Value;   // saves the config and tells other players
                    var msg = on.Value
                        ? "The host turned preorders ON for everyone. Press P in the shop!"
                        : "The host set preorders to host-only.";
                    who.Reply(new[] { msg });
                    Net.SendToOthers(new[] { msg }, 0);
                    return;

                case "markup":
                    if (TryPercent(value, 0, 300, out int markup, who, "/preorder markup 50"))
                    {
                        Plugin.MarkupPercent.Value = markup;
                        who.Reply(new[] { $"Preorder markup set to {markup}%." });
                    }
                    return;

                case "discount":
                    var dash = value.IndexOf('-');
                    if (dash > 0)
                    {
                        if (TryPercent(value.Substring(0, dash), 0, 100, out int lo, who, "/preorder discount 20-50") &&
                            TryPercent(value.Substring(dash + 1), 0, 100, out int hi, who, "/preorder discount 20-50"))
                        {
                            Plugin.DiscountMinPercent.Value = Mathf.Min(lo, hi);
                            Plugin.DiscountMaxPercent.Value = Mathf.Max(lo, hi);
                            DiscountRolls.Clear();
                            who.Reply(new[] { $"Shopkeeper discounts are now {Mathf.Min(lo, hi)}-{Mathf.Max(lo, hi)}% off." });
                        }
                    }
                    else if (TryPercent(value, 0, 100, out int chance, who, "/preorder discount 33  or  /preorder discount 20-50"))
                    {
                        Plugin.DiscountChancePercent.Value = chance;
                        DiscountRolls.Clear();
                        who.Reply(new[] { $"Shopkeeper discount chance set to {chance}%." });
                    }
                    return;

                case "confusion":
                    if (TryPercent(value, 0, 100, out int confusion, who, "/preorder confusion 10"))
                    {
                        Plugin.ConfusionChancePercent.Value = confusion;
                        who.Reply(new[] { $"Wrong-item chance set to {confusion}%." });
                    }
                    return;
            }
        }

        private static bool TryPercent(string text, int min, int max, out int result, Requester who, string example)
        {
            if (int.TryParse((text ?? "").Trim().TrimEnd('%'), out result) && result >= min && result <= max)
                return true;
            who.Reply(new[] { $"Use a number from {min} to {max}, e.g. {example}" });
            return false;
        }

        internal static string ItemNameForKey(string key) =>
            StatsManager.instance.itemDictionary.TryGetValue(key, out var item) && item ? DisplayName(item) : key;

        private static void ShowList(Requester who)
        {
            var items = PreorderableItems();
            if (items.Count == 0) { who.Reply(new[] { "Everything is already on the shelves!" }); return; }

            var lines = new List<string>();
            const int perLine = 3;
            for (int i = 0; i < items.Count; i += perLine)
            {
                lines.Add(string.Join("   ", items.Skip(i).Take(perLine)
                    .Select(kv => $"{DisplayName(kv.Value)} ${PreorderPrice(kv.Value)}K")));
            }
            Plugin.Log.LogInfo("Preorderable items:\n  " + string.Join("\n  ", lines));
            who.Reply(lines.ToArray());
        }

        private static void ShowStatus(Requester who)
        {
            var pending = PreorderStore.Load();
            if (pending == null) { who.Reply(new[] { "No preorder yet. The team gets one per round!" }); return; }
            who.Reply(new[]
            {
                $"{pending.OrderedBy} preordered {ItemNameForKey(pending.ItemKey)} (paid ${pending.PricePaid}K)."
            });
        }

        private static void Cancel(Requester who)
        {
            var pending = PreorderStore.Load();
            if (pending == null) { who.Reply(new[] { "Nothing to cancel." }); return; }
            if (!who.IsHost && pending.OrderedBy != who.Name)
            {
                who.Reply(new[] { $"Only {pending.OrderedBy} or the host can cancel that preorder." });
                return;
            }

            SemiFunc.StatSetRunCurrency(SemiFunc.StatGetRunCurrency() + pending.PricePaid);
            PreorderStore.Clear();
            var msg = $"{who.Name} cancelled the {ItemNameForKey(pending.ItemKey)} preorder. ${pending.PricePaid}K refunded.";
            who.Reply(new[] { msg });
            Plugin.AnnounceToOthers(who, new[] { msg });
            ShopState.HostRefreshAndBroadcast();
        }

        /// <summary>Order from the catalog: the exact item key, no name guessing.</summary>
        private static void PlaceOrderByKey(string key, Requester who)
        {
            var match = PreorderableItems().FirstOrDefault(kv => kv.Key == key);
            if (match.Value == null)
            {
                who.Reply(new[] { $"{ItemNameForKey(key)} can't be preordered right now." });
                return;
            }
            Purchase(match.Key, match.Value, who);
        }

        private static void PlaceOrder(string query, Requester who)
        {
            var existing = PreorderStore.Load();
            if (existing != null)
            {
                who.Reply(new[]
                {
                    $"{existing.OrderedBy} already preordered {ItemNameForKey(existing.ItemKey)} this round."
                });
                return;
            }

            var pool = PreorderableItems();
            var matches = Match(query, pool);
            if (matches.Count == 0)
            {
                bool onShelf = Match(query, AllSellableItems()).Count > 0;
                who.Reply(new[]
                {
                    onShelf
                        ? $"\"{query}\" is already on the shelves - just buy it!"
                        : $"The shopkeeper doesn't know \"{query}\". Try /preorder list"
                });
                return;
            }
            if (matches.Count > 1)
            {
                var names = string.Join(", ", matches.Take(4).Select(kv => DisplayName(kv.Value)));
                who.Reply(new[] { $"Which one? {names}{(matches.Count > 4 ? "..." : "")}" });
                return;
            }

            Purchase(matches[0].Key, matches[0].Value, who);
        }

        private static void Purchase(string key, Item item, Requester who)
        {
            var existing = PreorderStore.Load();
            if (existing != null)
            {
                who.Reply(new[]
                {
                    $"{existing.OrderedBy} already preordered {ItemNameForKey(existing.ItemKey)} this round."
                });
                return;
            }

            int price = PreorderPrice(item);

            // Roll the discount once per item per shop visit, so cancelling and
            // re-ordering can't be used to fish for a better deal.
            if (!DiscountRolls.TryGetValue(key, out int discountPct))
            {
                discountPct = 0;
                if (UnityEngine.Random.Range(0, 100) < Plugin.DiscountChancePercent.Value)
                {
                    int lo = Mathf.Min(Plugin.DiscountMinPercent.Value, Plugin.DiscountMaxPercent.Value);
                    int hi = Mathf.Max(Plugin.DiscountMinPercent.Value, Plugin.DiscountMaxPercent.Value);
                    discountPct = UnityEngine.Random.Range(lo, hi + 1);
                }
                DiscountRolls[key] = discountPct;
            }
            if (discountPct > 0)
                price = Mathf.Max(1, Mathf.CeilToInt(price * (1f - discountPct / 100f)));

            int money = SemiFunc.StatGetRunCurrency();
            if (money < price)
            {
                who.Reply(new[] { $"{DisplayName(item)} costs ${price}K to preorder. The team has ${money}K." });
                return;
            }

            SemiFunc.StatSetRunCurrency(money - price);
            PreorderStore.Save(new PendingPreorder { ItemKey = key, PricePaid = price, OrderedBy = who.Name });
            Plugin.Log.LogInfo($"{who.Name} preordered {key} for ${price}K (discount {discountPct}%).");

            var done = $"{who.Name} preordered {DisplayName(item)} for ${price}K. See you next shop!";
            var messages = discountPct > 0
                ? new[] { $"!SHOPKEEPER DISCOUNT -{discountPct}%", done }
                : new[] { done };
            who.Reply(messages);
            Plugin.AnnounceToOthers(who, messages);
            ShopState.HostRefreshAndBroadcast();
        }

        // ---------- the catalog ----------

        /// <summary>Host: everything the catalog menu needs, for one player.</summary>
        internal static CatalogInfo BuildCatalog(Requester who)
        {
            var info = new CatalogInfo();
            if (!who.IsHost && !Plugin.AllowEveryone.Value)
            {
                info.Allowed = false;
                info.DeniedReason = "The host has preorders set to host-only.";
                return info;
            }
            if (!SemiFunc.RunIsShop())
            {
                info.Allowed = false;
                info.DeniedReason = "You can only preorder while in the shop.";
                return info;
            }

            info.Money = SemiFunc.StatGetRunCurrency();
            var pending = PreorderStore.Load();
            if (pending != null)
            {
                info.HasPending = true;
                info.PendingName = ItemNameForKey(pending.ItemKey);
                info.Refund = pending.PricePaid;
                info.CanCancel = who.IsHost || pending.OrderedBy == who.Name;
                info.Status = $"{pending.OrderedBy} preordered {info.PendingName} for the next shop (${pending.PricePaid}K).";
                return info;
            }

            info.Status = "Pick one item for the next shop. The team gets one preorder per round.";
            foreach (var kv in PreorderableItems())
            {
                info.Keys.Add(kv.Key);
                info.Names.Add(DisplayName(kv.Value));
                info.Prices.Add(PreorderPrice(kv.Value));
                info.CategoryIds.Add(CatalogInfo.CategoryOf((int)kv.Value.itemType));
            }
            return info;
        }

        // ---------- the shopkeeper's confusion ----------

        /// <summary>A different item, preferably of the same kind, that the shopkeeper "accidentally" ordered.</summary>
        internal static Item PickConfusedItem(Item ordered)
        {
            var all = AllSellableItems().Select(kv => kv.Value).Where(i => i != ordered).ToList();
            if (all.Count == 0) return null;
            var sameKind = all.Where(i => i.itemType == ordered.itemType).ToList();
            var pool = sameKind.Count > 0 && UnityEngine.Random.value < 0.7f ? sameKind : all;
            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }
    }
}
