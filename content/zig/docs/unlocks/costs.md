# Costs
Any cost can be represented as a map that maps items to numbers.

The `farm.getCost()` function returns such a map. Its type is `?std.AutoHashMap(farm.Item, f64)`. It returns the cost of a plant or an unlock.

`farm.getCost(farm.Entity.Pumpkin)`
returns a map containing `.Carrot` → `1`

For unlocks, you can also ask for the cost of a specific unlock level with `farm.getCostLevel()`. `farm.getCost()` always uses the current unlock level.

`farm.getCostLevel(farm.Unlock.Loops, 0)`
returns a map containing `.Hay` → `5`

For unlocks that are already at the max level, `farm.getCost()` will return `null`. That is what the `?` in front of the type means: the result is an optional, so you have to check it before you can use the map.

It can be used like this:
`if (farm.getCost(farm.Unlock.Carrots)) |cost| {
    var it = cost.iterator();
    while (it.next()) |entry| {
        const item = entry.key_ptr.*;
        const amount_of_this_item_needed = entry.value_ptr.*;
        std.debug.print("{} {d}\n", .{ item, amount_of_this_item_needed });
    }
}`

`if (...) |cost|` only runs its block when the result is not `null`, and `cost` is the map inside.
