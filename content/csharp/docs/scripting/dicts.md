# Dictionaries
A `Dictionary` is a datastructure that allows you to map keys to values in the same way that a real dictionary maps words to their definitions and you can look them up very quickly.

A dictionary can be created like this:
`Dictionary<Direction, Direction> rightOf = new Dictionary<Direction, Direction>
{
	[North] = East,
	[East] = South,
	[South] = West,
	[West] = North
};`

`Dictionary<Direction, Direction>` means that the keys are `Direction`s and the values are `Direction`s too. Inside the braces, `[key] = value` adds one key-value pair: the expression in the square brackets is the key and the expression after the `=` is the value to which the key maps.
The above dictionary maps each direction to the direction to its right.

Here's another one that maps the drone's position to the entity it's over. Its keys are tuples `(x, y)` (see [Tuples](docs/scripting/tuples.md)):
`Dictionary<(int, int), Entity?> entityDict = new Dictionary<(int, int), Entity?>();
entityDict[(GetPosX(), GetPosY())] = GetEntityType();`

If the type is already written on the left, you can shorten the right side to `new()`: `Dictionary<Item, int> counts = new();`

Accessing the value mapped to a key is similar to accessing an element in a list: `dict[key]`

Example:
`Direction orientation = rightOf[South];`
This sets `orientation` to `West`.

Reading a key that is not in the dictionary is an error. `dict.GetValueOrDefault(key, defaultValue)` gives you a default value instead, and `TryGetValue` checks and reads in one step:
`if (rightOf.TryGetValue(North, out Direction right))
{
	Move(right);
}`

You can add a new key-value pair to a dictionary, or change the value of an existing key, like this: `dict[key] = value;`

Example:
`entityDict[(GetPosX(), GetPosY())] = GetEntityType();`
This updates the entity stored for the current position.

Keys are unique, so adding a key that already exists in the dictionary will overwrite the previous value.

Use `dict.Remove(key);` to remove a key-value pair from `dict`.

`dict.ContainsKey(key)` evaluates to `true` if `key` is a key in `dict` and `false` otherwise.
So you can use `if (dict.ContainsKey(key))` to check if `dict` contains the key.

`dict.Count` is the number of key-value pairs.

Use `dict.Keys` in a `foreach` loop to iterate through all keys:
`foreach (Direction key in rightOf.Keys)
{
	Direction value = rightOf[key];
}`

You can also get each key together with its value:
`foreach (var (key, value) in rightOf)
{
	Console.WriteLine($"{key} is left of {value}");
}`

`dict.Values` gives you all the values.

There are no guarantees about the order in which the keys are iterated.

See also [HashSet (Sets)](docs/scripting/sets.md)
