# Costs
Any cost can be represented as a map that maps items to numbers.

The `getCost()` method returns such a map. Its type is `Map<Item, Double>`. It returns the cost of a plant or an unlock.

`getCost(Entities.Pumpkin)`
returns a map containing `Items.Carrot` → `1`

For unlocks it returns the cost of the next unlock level.
For unlocks that are already at the max level, `getCost()` will return `null`.

It can be used like this:
`Map<Item, Double> cost = getCost(Unlocks.Carrots);
if (cost != null) {
    for (Item item : cost.keySet()) {
        double amountOfThisItemNeeded = cost.get(item);
    }
}`
