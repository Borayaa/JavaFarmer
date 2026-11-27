# Map (Dictionaries)
A `Map` is a datastructure that allows you to map keys to values in the same way that a real dictionary maps words to their definitions and you can look them up very quickly. (In Python this is called a dictionary.)

A `Map` can be created like this:
`const rightOf = new Map([[North, East], [East, South], [South, West], [West, North]]);`

Each `[key, value]` pair in the array is one entry: the first element is the key and the second element is the value to which the key maps.
The above map maps each direction to the direction to its right.

`new Map()` creates an empty map.

Accessing the value mapped to a key works with `get`:
`const rightOf = new Map([[North, East], [East, South], [South, West], [West, North]]);
const orientation = rightOf.get(South);`
This sets `orientation` to `West`.

If the key is not in the map, `get` returns `null` (in real JavaScript it would be `undefined`, which means the same thing in JavaFarmer). Use `map.get(key) ?? defaultValue` to get a default value instead.

You can add a new key-value pair to a map like this:
`const counts = new Map();
counts.set(Items.Hay, 5);`

Here's a map that remembers which entity is on which tile. The position `[x, y]` of the drone is used as the key:
`const entities = new Map();
entities.set([getPosX(), getPosY()], getEntityType());`
This updates the entity stored for the current position.
Write the position array directly inside `set`, `get` and `has` like this. Then two equal positions find the same entry.

Keys are unique, so setting a key that already exists in the map will overwrite the previous value.

Use `map.delete(key)` to remove a key-value pair from `map`.

`map.has(key)` evaluates to `true` if `key` is a key in `map` and `false` otherwise.
So you can use `if (map.has(key))` to check if `map` contains the key.

`map.size` is the number of key-value pairs.

Use a `for...of` loop to go through all key-value pairs:
`const rightOf = new Map([[North, East], [East, South], [South, West], [West, North]]);
for (const [key, value] of rightOf) {
	console.log(key, value);
}`

`map.keys()` gives you all the keys and `map.values()` all the values: `for (const key of rightOf.keys())`.

There are no guarantees about the order in which the keys are iterated.

For keys that are always texts, you can also use a plain object: `const ages = { hay: 1, wood: 2 };`, read a value with `ages.hay` or `ages["hay"]` and check a key with `"hay" in ages`. Reading a key that is not there is an error, so the `Map` is usually the better choice.

See also [Set](docs/scripting/sets.md)