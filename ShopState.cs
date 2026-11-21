using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace ShopPreorder
{
    /// <summary>
    /// What every player needs to know about preorders in the current shop, for hover hints and price tags.
    /// The host works it out and sends it to everyone else.
    /// </summary>
    internal static class ShopState
    {
        private static readonly AccessTools.FieldRef<ItemAttributes, PhotonView> View =
            AccessTools.FieldRefAccess<ItemAttributes, PhotonView>("photonView");

        /// <summary>Host's AllowEveryone setting, as last told by the host.</summary>
        internal static bool AllowEveryone;

        /// <summary>Network ID of the delivered preorder item in this shop (0 = none / unknown).</summary>
        internal static int PreorderedViewId;

        /// <summary>The delivered item itself, on the host (works in singleplayer, where there are no network IDs).</summary>
        internal static ItemAttributes LocalPreordered;

        /// <summary>Name of the item preordered this round for the next shop ("" = none yet).</summary>
        internal static string PendingItemName = "";

        internal static bool IsHost => !SemiFunc.IsMultiplayer() || SemiFunc.IsMasterClient();

        /// <summary>Can the local player preorder right now (as far as we know)?</summary>
        internal static bool LocalCanPreorder => IsHost || AllowEveryone;

        internal static void Reset()
        {
            PreorderedViewId = 0;
            LocalPreordered = null;
            PendingItemName = "";
        }

        internal static bool IsPreordered(ItemAttributes attr)
        {
            if (!attr) return false;
            if (LocalPreordered && attr == LocalPreordered) return true;
            if (PreorderedViewId == 0) return false;
            var view = View(attr);
            return view && view.ViewID == PreorderedViewId;
        }

        internal static int ViewIdOf(ItemAttributes attr)
        {
            var view = attr ? View(attr) : null;
            return view ? view.ViewID : 0;
        }

        /// <summary>Host: recompute the state from the saved preorder and tell everyone.</summary>
        internal static void HostRefreshAndBroadcast()
        {
            if (!IsHost) return;
            AllowEveryone = Plugin.AllowEveryone.Value;
            var pending = PreorderStore.Load();
            PendingItemName = pending != null ? Preorders.ItemNameForKey(pending.ItemKey) : "";
            Net.BroadcastShopState();
        }

        internal static object[] ToNetwork() => new object[] { AllowEveryone, PreorderedViewId, PendingItemName ?? "" };

        internal static void FromNetwork(object[] data)
        {
            if (data == null || data.Length < 3) return;
            AllowEveryone = data[0] is bool b && b;
            PreorderedViewId = data[1] is int id ? id : 0;
            PendingItemName = data[2] as string ?? "";
        }
    }
}
