using System;
using System.IO;
using BepInEx;
using HarmonyLib;

namespace ShopPreorder
{
    /// <summary>A preorder that has been paid for and is waiting for the next shop.</summary>
    internal class PendingPreorder
    {
        public string ItemKey;   // key in StatsManager.itemDictionary
        public int PricePaid;    // in the game's $K units
        public string OrderedBy; // player name
    }

    /// <summary>
    /// Saves the pending preorder to a small file next to the BepInEx config,
    /// one file per R.E.P.O. save, so it survives quitting and reloading.
    /// </summary>
    internal static class PreorderStore
    {
        private static readonly AccessTools.FieldRef<StatsManager, string> SaveFileCurrent =
            AccessTools.FieldRefAccess<StatsManager, string>("saveFileCurrent");

        private static string Folder => Path.Combine(Paths.ConfigPath, "ShopPreorder");

        private static string FilePath
        {
            get
            {
                string save = null;
                try { if (StatsManager.instance) save = SaveFileCurrent(StatsManager.instance); }
                catch (Exception e) { Plugin.Log.LogWarning("Could not read save name: " + e.Message); }
                if (string.IsNullOrEmpty(save)) save = "unsaved-run";
                foreach (var c in Path.GetInvalidFileNameChars()) save = save.Replace(c, '_');
                return Path.Combine(Folder, save + ".txt");
            }
        }

        public static PendingPreorder Load()
        {
            try
            {
                var path = FilePath;
                if (!File.Exists(path)) return null;
                var parts = File.ReadAllText(path).Trim().Split('|');
                if (parts.Length < 2 || string.IsNullOrEmpty(parts[0])) return null;
                int.TryParse(parts[1], out var paid);
                return new PendingPreorder
                {
                    ItemKey = parts[0],
                    PricePaid = paid,
                    OrderedBy = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : "The host",
                };
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not load preorder: " + e);
                return null;
            }
        }

        public static void Save(PendingPreorder order)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var who = (order.OrderedBy ?? "").Replace("|", "/");
                File.WriteAllText(FilePath, order.ItemKey + "|" + order.PricePaid + "|" + who);
            }
            catch (Exception e) { Plugin.Log.LogError("Could not save preorder: " + e); }
        }

        public static void Clear()
        {
            try { var path = FilePath; if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Plugin.Log.LogError("Could not clear preorder: " + e); }
        }
    }
}
