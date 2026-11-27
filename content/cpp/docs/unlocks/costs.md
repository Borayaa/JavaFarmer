# Costs
Any cost can be represented as a map that maps items to numbers.

The `get_cost()` function returns such a map. Its type is `std::optional<std::map<Item, double>>`. It returns the cost of a plant or an unlock.

`get_cost(Entities::Pumpkin);`
returns a map containing `Items::Carrot` → `1`

For unlocks, an optional second argument can be passed for the unlock level you want to get the cost for. Without it, you get the cost of the next unlock level.

`get_cost(Unlocks::Loops, 0);`
returns a map containing `Items::Hay` → `5`

For unlocks that are already at the max level, `get_cost()` returns no map at all (`std::nullopt`). That's why the map is wrapped in a `std::optional`: check it with `if (cost)` and then use `*cost` (the map itself) or `cost->` (a function of the map).

It can be used like this:
`auto cost = get_cost(Unlocks::Carrots);
if (cost) {
	for (auto& [item, amount_of_this_item_needed] : *cost) {
		std::println("{} {}", amount_of_this_item_needed, item);
	}
}`
