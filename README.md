# ShopPreorder – a R.E.P.O. mod

Didn't see the item you wanted in the shop? **Preorder it.** Pay now, and it's guaranteed to be on a shelf, already paid for, in the next shop.

- **Preorder Catalog:** press **P** in the shop to browse every item that isn't on the shelves, by category, with prices. Click an item, confirm, done.
- **"PREORDERED - PAID" tag:** in the next shop the delivered item shows a green tag instead of a price.
- **Hover hint:** look at any shop item and a hint reminds you that you can preorder something else.
- **A shopkeeper with personality:** 1 in 3 chance of a 20–50% discount, and a 10% chance they get confused and stock the *wrong* item (still free).
- **Multiplayer:** the host decides whether only the host or everyone can preorder. The team gets one preorder per round, paid from the team's money.
- Chat commands (`/preorder`, `/po`) work too, so the mod still works without MenuLib.
- **Host settings from chat:** `/preorder settings`, `/preorder everyone on|off`, `/preorder markup 50`, `/preorder discount 33` or `20-50`, `/preorder confusion 10`.

See [package/README.md](package/README.md) for the full player guide and settings.

## Installing

Needs [BepInExPack](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/) and [MenuLib](https://thunderstore.io/c/repo/p/REPOModding/MenuLib/).

1. Download the zip from this repo's **Releases** page (or build it yourself, see below).
2. In Thunderstore Mod Manager or r2modman: **Settings → Import local mod** → choose the zip.
3. Install **MenuLib** from the Online tab if you don't have it.

Only the host strictly needs the mod. Other players need it (plus MenuLib) to preorder, or to see the hints and tags.

## Building

Requirements: the [.NET 8 SDK](https://dotnet.microsoft.com/download), R.E.P.O., and a Thunderstore Mod Manager profile with BepInEx and MenuLib installed.

The project references the game's DLLs and BepInEx from these default locations. Override them if yours differ:

| Property | Default |
|---|---|
| `GameDir` | `C:\Program Files (x86)\Steam\steamapps\common\REPO` |
| `BepInExCore` | `%APPDATA%\Thunderstore Mod Manager\DataFolder\REPO\profiles\Default\BepInEx\core` |
| `PluginsDir` | `%APPDATA%\Thunderstore Mod Manager\DataFolder\REPO\profiles\Default\BepInEx\plugins` |

```bash
dotnet build -c Release
```

The DLL lands in `bin/Release/ShopPreorder.dll`. To make a Thunderstore package, copy it into `package/` and zip the **contents** of `package/` (`manifest.json`, `icon.png`, `README.md`, `ShopPreorder.dll`).

Example with custom paths:

```bash
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\REPO"
```

## How it works

| File | What it does |
|---|---|
| `Plugin.cs` | Settings, on-screen messages, the catalog key |
| `Preorders.cs` | Item list, pricing (same formula as the game's shop), ordering and cancelling |
| `Patches.cs` | Hooks into the game: chat commands, putting the preorder on a shelf, making it free, hint and tag |
| `CatalogMenu.cs` | The MenuLib catalog menu |
| `Net.cs` | Photon events between players and the host |
| `ShopState.cs` / `CatalogInfo.cs` | Data shared with other players |
| `PreorderStore.cs` | Saves the pending preorder per save file |
