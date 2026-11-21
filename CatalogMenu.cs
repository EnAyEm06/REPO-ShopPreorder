using System;
using System.Collections.Generic;
using MenuLib;
using MenuLib.MonoBehaviors;
using UnityEngine;

namespace ShopPreorder
{
    /// <summary>
    /// The pop-up Preorder Catalog, built with MenuLib.
    /// Only ever touched when MenuLib is installed (see Plugin.HasMenuLib).
    /// </summary>
    internal static class CatalogMenu
    {
        private const string Gold = "#FFCC1A";
        private const string Grey = "#7F7F7F";

        private static REPOPopupPage _page;
        private static readonly List<KeyValuePair<REPOButton, int>> Rows = new List<KeyValuePair<REPOButton, int>>();

        internal static bool IsOpen => _page;

        internal static void Close()
        {
            try { if (_page) _page.ClosePage(true); }
            catch (Exception e) { Plugin.Log.LogWarning("Closing catalog: " + e.Message); }
            _page = null;
            Rows.Clear();
        }

        internal static void Open(CatalogInfo info)
        {
            Close();
            if (!info.Allowed)
            {
                Plugin.ShowMessages(new[] { info.DeniedReason });
                return;
            }

            var page = MenuAPI.CreateREPOPopupPage("Preorder Catalog", REPOPopupPage.PresetSide.Left, false, true, 0f);
            page.onEscapePressed = () => true; // Esc closes the catalog
            _page = page;

            page.AddElement(parent => MenuAPI.CreateREPOButton("Close", Close, parent, new Vector2(66f, 18f)));

            // --- header: money and this round's preorder ---
            AddLabel(page, $"Team money: <color={Gold}>${info.Money}K</color>");
            AddLabel(page, $"<size=70%>{info.Status}</size>");

            if (info.CanCancel)
            {
                page.AddElementToScrollView(scroll => MenuAPI.CreateREPOButton(
                    $"Cancel {info.PendingName} preorder <color={Gold}>(+${info.Refund}K)</color>",
                    () => ConfirmCancel(info), scroll).rectTransform);
            }

            if (info.HasPending)
            {
                if (info.CanCancel) AddLabel(page, "<size=70%>Cancel it to pick something else.</size>");
                page.OpenPage(false);
                return;
            }

            if (info.Keys.Count == 0)
            {
                AddLabel(page, "<size=70%>Everything is already on the shelves!</size>");
                page.OpenPage(false);
                return;
            }

            // --- category picker (left/right arrows, like other in-game option sliders) ---
            Action<string> onCategory = ShowCategory;
            page.AddElementToScrollView(scroll => MenuAPI.CreateREPOSlider("Category", "", onCategory, scroll,
                CatalogInfo.Categories, CatalogInfo.Categories[0]).rectTransform);
            page.AddElementToScrollView(scroll => MenuAPI.CreateREPOSpacer(scroll, default, new Vector2(0f, 10f)).rectTransform);

            // --- one button per item ---
            for (int i = 0; i < info.Keys.Count; i++)
            {
                var key = info.Keys[i];
                var name = info.Names[i];
                var price = info.Prices[i];
                var category = info.CategoryIds[i];
                bool affordable = price <= info.Money;

                var label = affordable
                    ? $"{name}  <color={Gold}>${price}K</color>"
                    : $"<color={Grey}>{name}  ${price}K</color>";

                page.AddElementToScrollView(scroll =>
                {
                    var button = MenuAPI.CreateREPOButton(label, () => OnItemClicked(key, name, price, affordable, info.Money), scroll);
                    Rows.Add(new KeyValuePair<REPOButton, int>(button, category));
                    return button.rectTransform;
                });
            }

            page.OpenPage(false);
        }

        private static void AddLabel(REPOPopupPage page, string text)
        {
            page.AddElementToScrollView(scroll => MenuAPI.CreateREPOLabel(text, scroll).rectTransform);
        }

        private static void ShowCategory(string category)
        {
            int index = Array.IndexOf(CatalogInfo.Categories, category);
            foreach (var row in Rows)
            {
                if (!row.Key) continue;
                row.Key.repoScrollViewElement.visibility = index <= 0 || row.Value == index;
            }
            try { if (_page && _page.scrollView) _page.scrollView.SetScrollPosition(0f); }
            catch { /* cosmetic only */ }
        }

        private static void OnItemClicked(string key, string name, int price, bool affordable, int money)
        {
            if (!affordable)
            {
                Plugin.ShowMessages(new[] { $"{name} costs ${price}K to preorder. The team has ${money}K." });
                return;
            }

            MenuAPI.OpenPopup("Preorder?", new Color(1f, 0.8f, 0.1f),
                $"Preorder <b>{name}</b> for <b>${price}K</b>?\nIt will be waiting on a shelf in the next shop.\n" +
                "<size=80%>The shopkeeper might even give you a discount...</size>",
                () =>
                {
                    Close();
                    Preorders.OnLocalCommand("#key " + key);
                });
        }

        private static void ConfirmCancel(CatalogInfo info)
        {
            MenuAPI.OpenPopup("Cancel preorder?", Color.red,
                $"Cancel the <b>{info.PendingName}</b> preorder and get <b>${info.Refund}K</b> back?",
                () =>
                {
                    Close();
                    Preorders.OnLocalCommand("cancel");
                });
        }
    }
}
