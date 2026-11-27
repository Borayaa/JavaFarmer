# Auto Unlocks
To fully automate the game, you can use the `farm.unlock()` function to automatically unlock features.
For example, you can use `_ = farm.unlock(.Speed);` and `_ = farm.unlock(.Expand);` to unlock the speed and expansion features. `farm.unlock()` returns a `bool` (whether it worked), so `_ =` discards it if you don't need it.

To determine the cost of an unlock, simply use the `farm.getCost()` function as you would for a plant or item.
Example:
`farm.getCost(farm.Unlock.Loops)`
returns a map (`?std.AutoHashMap(farm.Item, f64)`) containing `.Hay` → `5`.
