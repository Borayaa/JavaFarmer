# Costs
Any cost can be represented as a `HashMap` that maps items to numbers.

The `get_cost()` function returns such a map. It returns the cost of a plant or an unlock.
Its type is `Option<HashMap<Item, f64>>`: `Some(map)` with the cost, or `None` if there is no cost to show.

`get_cost(Entity::Pumpkin)`
returns a map that maps `Item::Carrot` to `1.0`.

For unlocks, you can also choose the unlock level you want to get the cost for. Rust has no optional arguments, so this version has its own name: `get_cost_with(unlock, level)`. `get_cost()` uses the current unlock level.

`get_cost_with(Unlock::Loops, 0)`
returns a map that maps `Item::Hay` to `5.0`.

For unlocks that are already at the max level, `get_cost()` will return `None`.

It can be used like this:
`if let Some(cost) = get_cost(Unlock::Carrots) {
	for item in cost.keys() {
		let amount_of_this_item_needed = cost[item];
	}
}`
