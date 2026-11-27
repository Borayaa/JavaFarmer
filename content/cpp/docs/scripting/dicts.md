# std::map (Dictionaries)
A `std::map` is a datastructure that allows you to map keys to values in the same way that a real dictionary maps words to their definitions and you can look them up very quickly. (In Python this is called a dictionary.) It needs `#include <map>`.

A map can be created like this:
`std::map<Direction, Direction> right_of = { {North, East}, {East, South}, {South, West}, {West, North} };`

`std::map<Direction, Direction>` means that the keys are `Direction`s and the values are `Direction`s too. Each pair in braces is one entry: the first value is the key and the second value is the value to which the key maps.
The above map maps each direction to the direction to its right.

Here's another one that maps the drone's position to the entity it's over.
`int x = get_pos_x();
int y = get_pos_y();
std::map<Position, Entity> entity_map = { {Position{x, y}, get_entity_type()} };`

Accessing the value mapped to a key is similar to accessing an element in a vector:
`Direction orientation = right_of[South];`
This sets `orientation` to `West`. `right_of.at(South)` does the same.

You can add a new key-value pair to a map like this:
`right_of[North] = East;`

Example:
`entity_map[Position{get_pos_x(), get_pos_y()}] = get_entity_type();`
This updates the entity stored for the current position.

Keys are unique, so adding a key that already exists in the map will overwrite the previous value.

Use `m.erase(key)` to remove a key-value pair from the map `m`.

`m.contains(key)` evaluates to `true` if `key` is a key in `m` and `false` otherwise.
So you can use `if (m.contains(key))` to check if `m` contains the key. Only read keys that are in the map.

`m.size()` returns the number of key-value pairs and `m.empty()` tells you if there are none.

Putting a map in a range-for loop allows you to iterate through all key-value pairs:
`for (auto& [key, value] : right_of) {
	std::println("{} -> {}", key, value);
}`

Real C++ visits the keys of a `std::map` in sorted order, the game in the order they were added (a `std::unordered_map` has no order at all). So don't write code that depends on the order in which the keys are iterated.

`std::println("{}", right_of);` prints the whole map like `{North: East, East: South, ...}`.

See also [std::set](docs/scripting/sets.md)
