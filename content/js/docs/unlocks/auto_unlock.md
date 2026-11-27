# Auto Unlocks
To fully automate the game, you can use the `unlock()` function to automatically unlock features.
For example, you can use `unlock(Unlocks.Speed);` and `unlock(Unlocks.Expand);` to unlock the speed and expansion features.

To determine the cost of an unlock, simply use the `getCost()` function as you would for a plant or item.
Example:
`getCost(Unlocks.Loops);`
returns `Map(1) { Items.Hay => 5 }`
