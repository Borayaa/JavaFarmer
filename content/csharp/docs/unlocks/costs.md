# Costs
Any cost can be represented as a dictionary that maps items to numbers.

The `GetCost()` method returns such a dictionary. Its type is `Dictionary<Item, double>?`. It returns the cost of a plant or an unlock.

`GetCost(Entities.Pumpkin);`
returns `{Items.Carrot: 1}`

For unlocks, an optional second argument can be passed for the unlock level you want to get the cost for. By default, it's the current unlock level.

`GetCost(Unlocks.Loops, 0);`
returns `{Items.Hay: 5}`

For unlocks that are already at the max level, `GetCost()` will return `null`. That's what the `?` at the end of the type means.

It can be used like this:
`Dictionary<Item, double>? cost = GetCost(Unlocks.Carrots);
if (cost is not null)
{
	foreach (Item item in cost.Keys)
	{
		double amountOfThisItemNeeded = cost[item];
	}
}`
