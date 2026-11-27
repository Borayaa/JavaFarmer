# Costs
Any cost can be represented as a dictionary that maps items to numbers. In R, a dictionary is a named list whose keys are the items (see [Dictionaries](docs/scripting/dicts.md)).

The `get_cost()` function returns such a dictionary. It returns the cost of a plant or an unlock.

`get_cost(Entities$Pumpkin)`
returns `list(Items$Carrot = 1)`

For unlocks, an optional second argument can be passed for the unlock level you want to get the cost for. By default, it's the current unlock level.

`get_cost(Unlocks$Loops, 0)`
returns `list(Items$Hay = 5)`

For unlocks that are already at the max level, `get_cost()` will return `NULL`.

It can be used like this:
`cost <- get_cost(Entities$Carrot)
for (item in names(cost)) {
	amount_of_this_item_needed <- cost[[item]]
	if (num_items(item) < amount_of_this_item_needed) {
		print("not enough items")
	}
}`
