# Sets
Sets are like [maps](docs/scripting/dicts.md), but without values. They're just an unordered set of keys. `farm.h` gives you the type `Set(T)`, where `T` is the type of the elements.

`Set(Direction) set = set_of(Direction, North, East, West);`

Use `set_new(T)` to create an empty set, for example `Set(Position) visited = set_new(Position);`.

Use `set_add(set, elem)` to add a new element to the set.

Use `set_remove(set, elem)` to remove an element from a set.

Use `if (set_contains(set, elem))` to check if the set contains an element.

Use `set_len(set)` to get the number of elements.

Use `for_each (Direction elem, set)` to iterate all elements in the set.
For larger sets `set_contains` performs much faster than `list_contains` would on a list.

Just like maps, sets are unordered, so there are no guarantees about the order in which the elements are iterated.

Also, elements in sets are unique, so adding an element that is already in the set will not change the set.
