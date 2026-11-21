using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace ShopPreorder
{
    /// <summary>Catches "/preorder ..." typed in chat so it never gets said out loud.</summary>
    [HarmonyPatch(typeof(ChatManager), "MessageSend")]
    internal static class ChatCommandPatch
    {
        private static readonly AccessTools.FieldRef<ChatManager, string> ChatMessage =
            AccessTools.FieldRefAccess<ChatManager, string>("chatMessage");
        private static readonly AccessTools.FieldRef<ChatManager, bool> ChatActive =
            AccessTools.FieldRefAccess<ChatManager, bool>("chatActive");

        private static bool Prefix(ChatManager __instance)
        {
            var text = (ChatMessage(__instance) ?? "").Trim();
            string args;
            if (text.StartsWith("/preorder", StringComparison.OrdinalIgnoreCase)) args = text.Substring(9);
            else if (text.Equals("/po", StringComparison.OrdinalIgnoreCase) ||
                     text.StartsWith("/po ", StringComparison.OrdinalIgnoreCase)) args = text.Substring(3);
            else return true; // normal chat message, let the game handle it

            // Clear the chat box the same way the game does after sending.
            ChatMessage(__instance) = "";
            ChatActive(__instance) = false;
            try
            {
                var chatText = Traverse.Create(__instance).Field("chatText").GetValue<TMPro.TMP_Text>();
                if (chatText) chatText.text = "";
            }
            catch { /* cosmetic only */ }

            Preorders.OnLocalCommand(args);
            return false;
        }
    }

    /// <summary>The game's network manager exists once a level loads, so Photon is ready by now.</summary>
    [HarmonyPatch(typeof(PunManager), "Start")]
    internal static class PunManagerStartPatch
    {
        private static void Postfix()
        {
            Runner.Ensure();
            Net.EnsureHooked();
        }
    }

    /// <summary>A new shop visit starts (host): forget this visit's discount rolls and tell everyone the fresh state.</summary>
    [HarmonyPatch]
    internal static class ShopInitializePatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.GetDeclaredMethods(typeof(ShopManager))
                .First(m => m.Name == "ShopInitialize" && m.GetParameters().Length == 1);

        private static void Prefix()
        {
            if (!SemiFunc.RunIsShop() || !ShopState.IsHost) return;
            Preorders.DiscountRolls.Clear();
            ShopState.Reset();
            ShopState.HostRefreshAndBroadcast();
        }
    }

    /// <summary>Adds a line under the item name when you look at something in the shop.</summary>
    [HarmonyPatch(typeof(SemiFunc), nameof(SemiFunc.UIItemInfoText))]
    internal static class HoverHintPatch
    {
        private static readonly AccessTools.FieldRef<ItemAttributes, bool> ShopItem =
            AccessTools.FieldRefAccess<ItemAttributes, bool>("shopItem");

        private static void Prefix(ItemAttributes itemAttributes, ref string message)
        {
            try
            {
                if (!itemAttributes || !SemiFunc.RunIsShop()) return;

                if (ShopState.IsPreordered(itemAttributes))
                {
                    message += "\n<size=75%><color=#5CFF7A>PREORDERED - PAID</color></size>";
                    return;
                }
                if (!ShopItem(itemAttributes) || !ShopState.LocalCanPreorder) return;

                string hint;
                if (!string.IsNullOrEmpty(ShopState.PendingItemName))
                    hint = $"{ShopState.PendingItemName} is preordered for the next shop.";
                else if (Plugin.HasMenuLib)
                    hint = $"Not what you wanted? Press [{Plugin.CatalogKeyName}] to preorder something else.";
                else
                    hint = "Not what you wanted? Type /preorder list in chat.";

                message += $"\n<size=70%><color=#FFCC1A>{hint}</color></size>";
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Hover hint failed: " + e.Message);
            }
        }
    }

    /// <summary>Replaces "-$0K" with "PREORDERED - PAID" on the delivered item's price label.</summary>
    [HarmonyPatch(typeof(WorldSpaceUIValue), nameof(WorldSpaceUIValue.Show),
        new[] { typeof(PhysGrabObject), typeof(int), typeof(bool), typeof(Vector3) })]
    internal static class PriceTagPatch
    {
        private static readonly AccessTools.FieldRef<WorldSpaceUIValue, PhysGrabObject> Current =
            AccessTools.FieldRefAccess<WorldSpaceUIValue, PhysGrabObject>("currentPhysGrabObject");
        private static readonly AccessTools.FieldRef<WorldSpaceUIValue, TMPro.TextMeshProUGUI> Text =
            AccessTools.FieldRefAccess<WorldSpaceUIValue, TMPro.TextMeshProUGUI>("text");

        private static void Postfix(WorldSpaceUIValue __instance, PhysGrabObject _grabObject, bool _cost)
        {
            try
            {
                if (!_cost || !_grabObject || Current(__instance) != _grabObject) return;
                var attr = _grabObject.GetComponent<ItemAttributes>();
                if (!ShopState.IsPreordered(attr)) return;
                var text = Text(__instance);
                if (text) text.text = "<size=55%><color=#5CFF7A>PREORDERED - PAID</color></size>";
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Price tag failed: " + e.Message);
            }
        }
    }

    /// <summary>Tracks the one shop item that should be free because it was preordered.</summary>
    internal static class Delivery
    {
        internal static Item ExpectedItem;
        internal static Vector3 ExpectedPosition;
        internal static bool Waiting;
    }

    /// <summary>
    /// Before the game fills the shelves, put the preordered item on one of them.
    /// Runs on the host only (the game itself only fills shelves on the host).
    /// </summary>
    [HarmonyPatch]
    internal static class ShopPopulatePatch
    {
        private static readonly MethodInfo SpawnShopItem = AccessTools.Method(typeof(PunManager), "SpawnShopItem");

        private static MethodBase TargetMethod() =>
            AccessTools.GetDeclaredMethods(typeof(PunManager))
                .First(m => m.Name == "ShopPopulateItemVolumes" && m.GetParameters().Length == 1);

        private static void Prefix(PunManager __instance, object[] __args)
        {
            try { Deliver(__instance, __args[0]); }
            catch (Exception e) { Plugin.Log.LogError("Preorder delivery failed: " + e); }
        }

        private static void Deliver(PunManager pun, object spawns)
        {
            if (SemiFunc.IsNotMasterClient() || !SemiFunc.RunIsShop()) return;

            var pending = PreorderStore.Load();
            if (pending == null) return;

            var shop = ShopManager.instance;
            if (!shop || shop.itemVolumes == null) return;

            if (!StatsManager.instance.itemDictionary.TryGetValue(pending.ItemKey, out var ordered) || !ordered)
            {
                Plugin.Log.LogWarning($"Preordered item '{pending.ItemKey}' no longer exists. Refunding ${pending.PricePaid}K.");
                SemiFunc.StatSetRunCurrency(SemiFunc.StatGetRunCurrency() + pending.PricePaid);
                PreorderStore.Clear();
                Plugin.AnnounceToAllLater(8f, $"The preorder vanished from the catalog. ${pending.PricePaid}K refunded.");
                return;
            }

            var item = ordered;
            bool confused = false;
            if (UnityEngine.Random.Range(0, 100) < Plugin.ConfusionChancePercent.Value)
            {
                var wrong = Preorders.PickConfusedItem(ordered);
                if (wrong) { item = wrong; confused = true; }
            }

            // Prefer a shelf spot of the right size; otherwise resize any free spot.
            var volume = shop.itemVolumes.FirstOrDefault(v => v && v.itemVolume == item.itemVolume)
                         ?? shop.itemVolumes.FirstOrDefault(v => v);
            if (!volume)
            {
                Plugin.Log.LogWarning("No free shelf spot for the preorder - it will try again next shop.");
                return;
            }
            volume.itemVolume = item.itemVolume;

            Delivery.ExpectedItem = item;
            Delivery.ExpectedPosition = volume.transform.position;
            Delivery.Waiting = true;

            var args = new object[] { volume, new List<Item> { item }, 0, false, spawns };
            bool spawned = (bool)SpawnShopItem.Invoke(pun, args);
            if (!spawned)
            {
                Delivery.Waiting = false;
                Plugin.Log.LogWarning("Could not spawn the preordered item - it will try again next shop.");
                return;
            }

            // The shelf spot is used up; take it out so the game doesn't fill it too.
            shop.itemVolumes.Remove(volume);
            UnityEngine.Object.Destroy(volume.gameObject);
            PreorderStore.Clear();
            ShopState.HostRefreshAndBroadcast();

            var orderedName = Preorders.DisplayName(ordered);
            var gotName = Preorders.DisplayName(item);
            Plugin.Log.LogInfo(confused
                ? $"Delivered {item.name} instead of {ordered.name} (shopkeeper confused)."
                : $"Delivered preordered {item.name}.");

            if (confused)
                Plugin.AnnounceToAllLater(8f, "!THE SHOPKEEPER GOT CONFUSED",
                    $"{pending.OrderedBy} ordered {orderedName}... but got {gotName}. It's free, at least!");
            else
                Plugin.AnnounceToAllLater(8f, $"{pending.OrderedBy}'s preordered {gotName} is on the shelf - already paid for!");
        }
    }

    /// <summary>When the preordered item rolls its shop price, make it free.</summary>
    [HarmonyPatch(typeof(ItemAttributes), nameof(ItemAttributes.GetValue))]
    internal static class FreeItemPatch
    {
        private static readonly AccessTools.FieldRef<ItemAttributes, int> Value =
            AccessTools.FieldRefAccess<ItemAttributes, int>("value");
        private static readonly AccessTools.FieldRef<ItemAttributes, PhotonView> View =
            AccessTools.FieldRefAccess<ItemAttributes, PhotonView>("photonView");

        private static void Postfix(ItemAttributes __instance)
        {
            if (!Delivery.Waiting || !SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (__instance.item != Delivery.ExpectedItem) return;
            if (Vector3.Distance(__instance.transform.position, Delivery.ExpectedPosition) > 2f) return;

            Delivery.Waiting = false;
            Value(__instance) = 0;

            var view = View(__instance);
            if (SemiFunc.IsMultiplayer() && view && view.ViewID != 0)
                view.RPC("GetValueRPC", RpcTarget.Others, 0);

            SemiFunc.ShopUpdateCost();
            Plugin.Log.LogInfo($"Made preordered {__instance.item.name} free.");

            // Remember which item it is, so everyone sees the PREORDERED tag on it.
            ShopState.LocalPreordered = __instance;
            ShopState.PreorderedViewId = ShopState.ViewIdOf(__instance);
            ShopState.HostRefreshAndBroadcast();
        }
    }
}
