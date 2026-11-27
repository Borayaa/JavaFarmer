# Maps (Dictionaries)
A map is a datastructure that allows you to map keys to values in the same way that a real dictionary maps words to their definitions and you can look them up very quickly. (In Python this is called a dictionary.)
C itself has no maps, but `farm.h` gives you the type `Map(K, V)`, where `K` is the type of the keys and `V` the type of the values.

A map can be created like this:
`Map(Direction, Direction) right_of = map_of(Direction, Direction, {North, East}, {East, South}, {South, West}, {West, North});`

The first two arguments of `map_of` are the key type and the value type. Each pair in braces is a key followed by the value to which the key maps.
The above map maps each direction to the direction to its right.

An empty map is created with `map_new`:
`Map(Position, Entity) entities = map_new(Position, Entity);`

Accessing the value mapped to a key works with `map_get`:
`Direction orientation = map_get(right_of, South);`
This sets `orientation` to `West`.

If the key is not in the map, `map_get` stops the program with an error. Use `map_get_or(map, key, default_value)` to get a default value instead.

You can add a new key-value pair to a map with `map_put(map, key, value)`.

Here's a map that remembers which entity is on which tile. The key is the drone's position:
`map_put(entities, (Position){get_pos_x(), get_pos_y()}, get_entity_type());`
This updates the entity stored for the current position.

Keys are unique, so putting a key that already exists in the map will overwrite the previous value.

Use `map_remove(map, key)` to remove a key-value pair from `map`. It returns the removed value.

`map_has(map, key)` evaluates to `true` if `key` is a key in `map` and `false` otherwise.
So you can use `if (map_has(map, key))` to check if `map` contains the key.

`map_len(map)` returns the number of key-value pairs.

Putting a map in a `for_each` loop allows you to iterate through all keys:
`for_each (Direction key, right_of) {
	Direction value = map_get(right_of, key);
}`

`map_keys(map)` and `map_values(map)` give you all the keys or all the values as a list.

There are no guarantees about the order in which the keys are iterated.

A map prints like `{North: East, East: South}`.

See also [Sets](docs/scripting/sets.md)
