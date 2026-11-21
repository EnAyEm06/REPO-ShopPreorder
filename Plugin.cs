using System;
using System.Collections;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShopPreorder
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MenuLibGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "shoppreorder.repo";
        public const string Name = "ShopPreorder";
        public const string Version = "1.1.0";
        private const string MenuLibGuid = "nickklmao.menulib";

        internal static ManualLogSource Log;
        internal static bool HasMenuLib;

        internal static ConfigEntry<bool> AllowEveryone;
        internal static ConfigEntry<Key> CatalogKey;
        internal static ConfigEntry<int> MarkupPercent;
        internal static ConfigEntry<int> DiscountChancePercent;
        internal static ConfigEntry<int> DiscountMinPercent;
        internal static ConfigEntry<int> DiscountMaxPercent;
        internal static ConfigEntry<int> ConfusionChancePercent;

        private const float SecondsPerMessage = 3.5f;
        private static Coroutine _messageRoutine;

        private void Awake()
        {
            Log = Logger;

            AllowEveryone = Config.Bind("Permissions", "AllowEveryone", false,
                "Host setting. Off: only the host can preorder. On: any player with the mod can preorder " +
                "(the team still gets one preorder per round, paid from the team's money).");
            CatalogKey = Config.Bind("Controls", "CatalogKey", Key.P,
                "Key that opens the Preorder Catalog while in the shop.");
            MarkupPercent = Config.Bind("Pricing", "MarkupPercent", 50,
                new ConfigDescription("Extra fee on top of the shop price for preordering.",
                    new AcceptableValueRange<int>(0, 300)));
            DiscountChancePercent = Config.Bind("Shopkeeper", "DiscountChancePercent", 33,
                new ConfigDescription("Chance the shopkeeper gives a discount when you preorder.",
                    new AcceptableValueRange<int>(0, 100)));
            DiscountMinPercent = Config.Bind("Shopkeeper", "DiscountMinPercent", 20,
                new ConfigDescription("Smallest possible discount.", new AcceptableValueRange<int>(0, 100)));
            DiscountMaxPercent = Config.Bind("Shopkeeper", "DiscountMaxPercent", 50,
                new ConfigDescription("Largest possible discount.", new AcceptableValueRange<int>(0, 100)));
            ConfusionChancePercent = Config.Bind("Shopkeeper", "ConfusionChancePercent", 10,
                new ConfigDescription("Chance the shopkeeper gets confused and delivers the wrong item.",
                    new AcceptableValueRange<int>(0, 100)));

            AllowEveryone.SettingChanged += (s, e) => ShopState.HostRefreshAndBroadcast();

            HasMenuLib = Chainloader.PluginInfos.ContainsKey(MenuLibGuid);
            if (!HasMenuLib) Log.LogWarning("MenuLib not found - the catalog menu is off. /preorder in chat still works.");

            Runner.Ensure();
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded. Press {CatalogKey.Value} in the shop, or type /preorder in chat.");
        }

        internal static string CatalogKeyName => CatalogKey.Value.ToString();

        // ---------- the catalog ----------

        /// <summary>Opens the catalog with this data (MenuLib if present, otherwise a text fallback).</summary>
        internal static void OpenCatalog(CatalogInfo info)
        {
            if (!HasMenuLib)
            {
                if (!info.Allowed) { ShowMessages(new[] { info.DeniedReason }); return; }
                ShowMessages(new[] { "Install MenuLib for the catalog. For now: type /preorder list in chat." });
                return;
            }
            try { OpenCatalogMenuLib(info); }
            catch (Exception e) { Log.LogError("Could not open the catalog: " + e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void OpenCatalogMenuLib(CatalogInfo info) => CatalogMenu.Open(info);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool CatalogMenuIsOpen() => CatalogMenu.IsOpen;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CatalogMenuClose() => CatalogMenu.Close();

        private static readonly AccessTools.FieldRef<MenuManager, MenuPage> CurrentMenuPage =
            AccessTools.FieldRefAccess<MenuManager, MenuPage>("currentMenuPage");

        /// <summary>True in the shop; false (never an error) on the main menu or while loading.</summary>
        private static bool InShop()
        {
            try { return RunManager.instance && SemiFunc.RunIsShop(); }
            catch { return false; }
        }

        /// <summary>Called every frame: opens/closes the catalog when the catalog key is pressed.</summary>
        internal static void CatalogKeyTick()
        {
            // Leaving the shop closes the catalog.
            if (HasMenuLib && CatalogMenuIsOpen() && !InShop()) CatalogMenuClose();

            var keyboard = Keyboard.current;
            var key = CatalogKey.Value;
            if (keyboard == null || key == Key.None) return;

            bool pressed;
            try { pressed = keyboard[key].wasPressedThisFrame; }
            catch { return; }
            if (!pressed) return;

            if (HasMenuLib && CatalogMenuIsOpen()) { CatalogMenuClose(); return; }

            if (!InShop()) return;
            if (ChatManager.instance && ChatManager.instance.StateIsActive()) return;   // typing in chat
            if (!SemiFunc.NoTextInputsActive()) return;                                  // typing in a menu
            if (MenuManager.instance && CurrentMenuPage(MenuManager.instance)) return;   // another menu is open

            if (ShopState.IsHost)
                OpenCatalog(Preorders.BuildCatalog(Net.LocalRequester()));
            else
                Net.RequestCatalog();
        }

        // ---------- showing messages on screen ----------

        private static readonly Color Gold = new Color(1f, 0.8f, 0.1f);

        private static void Say(string message, float seconds)
        {
            Log.LogInfo("[msg] " + message);
            try { SemiFunc.UIFocusText(message, Color.white, Gold, seconds); }
            catch (Exception e) { Log.LogWarning("Could not show message: " + e.Message); }
        }

        private static void Shout(string message)
        {
            Log.LogInfo("[big] " + message);
            try { SemiFunc.UIBigMessage(message, "{!}", 25f, Gold, Color.white); }
            catch (Exception e) { Log.LogWarning("Could not show big message: " + e.Message); }
        }

        /// <summary>
        /// Shows messages on this player's screen, one after another.
        /// A message starting with "!" is shown as a big banner instead.
        /// </summary>
        internal static void ShowMessages(string[] messages)
        {
            if (messages == null || messages.Length == 0) return;
            var runner = Runner.Ensure();
            if (_messageRoutine != null) runner.StopCoroutine(_messageRoutine);
            _messageRoutine = runner.StartCoroutine(MessageRoutine(messages));
        }

        private static IEnumerator MessageRoutine(string[] messages)
        {
            foreach (var m in messages)
            {
                if (m.StartsWith("!")) Shout(m.Substring(1));
                else Say(m, SecondsPerMessage);
                yield return new WaitForSeconds(SecondsPerMessage);
            }
        }

        /// <summary>Host: tells everyone except the player who did it (who already got a reply).</summary>
        internal static void AnnounceToOthers(Requester who, string[] messages)
        {
            if (!who.IsHost) ShowMessages(messages);                 // the host sees what others did
            Net.SendToOthers(messages, who.IsHost ? 0 : who.ActorNumber);
        }

        /// <summary>Host: tells everyone (host included) after a delay, e.g. once the shop has finished loading.</summary>
        internal static void AnnounceToAllLater(float delay, params string[] messages)
        {
            Runner.Ensure().StartCoroutine(AnnounceLaterRoutine(delay, messages));
        }

        private static IEnumerator AnnounceLaterRoutine(float delay, string[] messages)
        {
            yield return new WaitForSeconds(delay);
            ShowMessages(messages);
            Net.SendToOthers(messages, 0);
        }
    }

    /// <summary>
    /// A hidden object that survives scene changes. It runs the per-frame key check and the
    /// message timers, so they don't depend on BepInEx's own manager object staying alive.
    /// </summary>
    internal class Runner : MonoBehaviour
    {
        private static Runner _instance;

        internal static Runner Ensure()
        {
            if (_instance) return _instance;
            var go = new GameObject("ShopPreorder.Runner") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<Runner>();
            return _instance;
        }

        private void Update()
        {
            try { Plugin.CatalogKeyTick(); }
            catch (Exception e) { Plugin.Log.LogError("Catalog key check failed: " + e); }
        }
    }
}
