# Costs
Any cost can be represented as a `Map` that maps items to numbers (a dictionary in Python).

The `getCost()` function returns such a `Map`. It returns the cost of a plant or an unlock.

`getCost(Entities.Pumpkin);`
returns `Map(1) { Items.Carrot => 1 }`

For unlocks, an optional second argument can be passed for the unlock level you want to get the cost for. By default, it's the current unlock level.

`getCost(Unlocks.Loops, 0);`
returns `Map(1) { Items.Hay => 5 }`

For unlocks that are already at the max level, `getCost()` will return `null`.

It can be used like this:
`const cost = getCost(Entities.Pumpkin);
for (const item of cost.keys()) {
	const amountOfThisItemNeeded = cost.get(item);
}`

or, with both the key and the value at once:
`const cost = getCost(Entities.Pumpkin);
for (const [item, amount] of cost) {
	console.log(item, amount);
}`
