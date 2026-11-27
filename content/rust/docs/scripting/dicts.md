# HashMap (Dictionaries)
A `HashMap` is a datastructure that allows you to map keys to values in the same way that a real dictionary maps words to their definitions and you can look them up very quickly. (In Python this is called a dictionary.)

`HashMap` is part of Rust's standard library, so a file that uses it needs this line at the top, next to `use farm::*;`
`use std::collections::HashMap;`

A `HashMap` can be created like this:
`let mut right_of = HashMap::new();
right_of.insert(North, East);
right_of.insert(East, South);
right_of.insert(South, West);
right_of.insert(West, North);`

The first argument of `insert` is the key and the second one is the value to which the key maps. The map needs `let mut` because `insert` changes it.
The above map maps each direction to the direction to its right. Rust figures out its type from the values: `HashMap<Direction, Direction>`, the keys are `Direction`s and the values are `Direction`s too.

You can also create a map from a list of `(key, value)` tuples:
`let right_of = HashMap::from([(North, East), (East, South), (South, West), (West, North)]);`

Here's another one that maps the drone's position to the entity it's over.
`let mut entities = HashMap::new();
entities.insert((get_pos_x(), get_pos_y()), get_entity_type());`

Accessing the value mapped to a key is similar to accessing an element in a list, but with a `&` in front of the key: `map[&key]`

Example:
`let orientation = right_of[&South];`
This sets `orientation` to `West`.

If the key is not in the map, `map[&key]` stops the program with an error. `map.get(&key)` is safer: it returns an `Option`, `Some(value)` if the key is there and `None` otherwise.
`if let Some(dir) = right_of.get(&North) {
	move_to(*dir);
}`
`get` gives you a reference to the value, the `*` in `*dir` reads the value behind it.

You can add a new key-value pair to a map with `map.insert(key, value);`

Example:
`entities.insert((get_pos_x(), get_pos_y()), get_entity_type());`
This updates the entity stored for the current position.

Keys are unique, so inserting a key that already exists in the map will overwrite the previous value.

Use `map.remove(&key)` to remove a key-value pair from `map`.

`map.contains_key(&key)` evaluates to `true` if `key` is a key in `map` and `false` otherwise.
So you can use `if map.contains_key(&key) {` to check if `map` contains the key.

`map.len()` returns the number of key-value pairs.

To count things, `*counts.entry(key).or_insert(0) += 1;` adds `1` to the value of `key`, starting at `0` if the key is new.

Putting a map in a for loop allows you to iterate through all key-value pairs:
`for (key, value) in &right_of {
	println!("{} is right of {}", value, key);
}`

`map.keys()` gives you all the keys and `map.values()` all the values, for example `for key in right_of.keys() {`.

There are no guarantees about the order in which the keys are iterated.

See also [HashSet](docs/scripting/sets.md)
