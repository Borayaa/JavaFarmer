# HashMap (Dictionaries)
A `HashMap` is a datastructure that allows you to map keys to values in the same way that a real dictionary maps words to their definitions and you can look them up very quickly. (In Python this is called a dictionary.)

A `HashMap` can be created like this:
`HashMap<Direction, Direction> rightOf = new HashMap<>();
rightOf.put(North, East);
rightOf.put(East, South);
rightOf.put(South, West);
rightOf.put(West, North);`

`HashMap<Direction, Direction>` means that the keys are `Direction`s and the values are `Direction`s too. The first argument of `put` is the key and the second one is the value to which the key maps.
The above map maps each direction to the direction to its right.

If the map never changes, `Map.of` is shorter. It takes the keys and values in turns:
`Map<Direction, Direction> rightOf = Map.of(North, East, East, South, South, West, West, North);`

Accessing the value mapped to a key works with `get`:
`Value value = map.get(key);`

Example:
`Direction orientation = rightOf.get(South);`
This sets `orientation` to `West`.

If the key is not in the map, `get` returns `null`. Use `map.getOrDefault(key, defaultValue)` to get a default value instead.

You can add a new key-value pair to a map like this:
`map.put(key, value);`

Here's a map that remembers which entity is on which tile. The tile is turned into a single number that is used as the key:
`HashMap<Integer, Entity> entities = new HashMap<>();
int tile = getPosX() * getWorldSize() + getPosY();
entities.put(tile, getEntityType());`
This updates the entity stored for the current tile.

Keys are unique, so putting a key that already exists in the map will overwrite the previous value.

Use `map.remove(key)` to remove a key-value pair from `map`.

`map.containsKey(key)` evaluates to `true` if `key` is a key in `map` and `false` otherwise.
So you can use `if (map.containsKey(key))` to check if `map` contains the key.

`map.size()` returns the number of key-value pairs.

Use `map.keySet()` in a for loop to iterate through all keys:
`for (Direction key : rightOf.keySet()) {
    Direction value = rightOf.get(key);
}`

`map.values()` gives you all the values.

There are no guarantees about the order in which the keys are iterated.

See also [HashSet](docs/scripting/sets.md)
