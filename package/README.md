# ShopPreorder

Didn't see the item you wanted in the shop? Preorder it. Pay now, and it's guaranteed to be on the shelf, already paid for, in the next shop.

## How to use

1. In the shop, look at any item. A hint under its name says **"Not what you wanted? Press [P] to preorder something else."**
2. Press **P** to open the **Preorder Catalog**.
3. Use the **Category** arrows to browse: Upgrades, Weapons, Explosives, Health, Drones, Carts & Vehicles, Tools.
4. Click an item and confirm with **Yes**. The money comes out of the team's balance straight away.
5. In the next shop, your item is on a shelf with a green **PREORDERED - PAID** tag instead of a price.

Press P or Esc again to close the catalog. Items you can't afford are greyed out. If the team has already preordered something this round, the catalog shows it, with a button to cancel and get a refund.

### Chat commands (still work)

| Command | What it does |
|---|---|
| `/preorder <item>` | Preorder an item that isn't on the shelves, e.g. `/preorder grenade` |
| `/preorder list` | Show every item you can preorder, with prices |
| `/preorder status` | Show this round's preorder |
| `/preorder cancel` | Cancel this round's preorder and get the money back |

`/po` works as a shortcut for `/preorder`. Press **T** to open chat. Commands are never said out loud.

## Rules

- **One preorder per round** for the whole team, paid from the team's money.
- **Preorder fee:** the shop price plus 50%.
- **Shopkeeper discount:** 1 in 3 chance of 20–50% off when you preorder.
- **Confused shopkeeper:** a 10% chance the shopkeeper stocks the *wrong* item. It's still free.
- Secret, disabled and unfinished items can't be preordered.

## Who can preorder

By default, **only the host** can preorder. The host can turn on `AllowEveryone` to let any player preorder.

- **The host must have the mod.** It runs the shop, takes the money and places the item.
- **Other players need the mod (and MenuLib) only to preorder, or to see the hints and PREORDERED tag.** Players without it can still play normally and buy the free item.

## Settings

Change these in-game with REPOConfig, or in `BepInEx/config/shoppreorder.repo.cfg`. Only the host's pricing and permission settings count.

| Setting | Default |
|---|---|
| Permissions → AllowEveryone | off |
| Controls → CatalogKey | P |
| Pricing → MarkupPercent | 50 |
| Shopkeeper → DiscountChancePercent | 33 |
| Shopkeeper → DiscountMinPercent / DiscountMaxPercent | 20 / 50 |
| Shopkeeper → ConfusionChancePercent | 10 |

Preorders are saved per save file in `BepInEx/config/ShopPreorder/`, so quitting and reloading keeps them.

Needs **MenuLib** for the catalog. Without it, everything still works through chat commands.
