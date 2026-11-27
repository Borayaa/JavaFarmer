# Auto Unlocks
To fully automate the game, you can use the `Unlock()` method to automatically unlock features.
For example, you can use `Unlock(Unlocks.Speed);` and `Unlock(Unlocks.Expand);` to unlock the speed and expansion features.

To determine the cost of an unlock, simply use the `GetCost()` method as you would for a plant or item.
Example:
`GetCost(Unlocks.Loops);`
returns `{Items.Hay: 5}`, a dictionary that maps `Items.Hay` to `5`.
