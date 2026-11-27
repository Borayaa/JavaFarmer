# HashSet (Sets)
Sets are like [maps](docs/scripting/dicts.md), but without values. They're just an unordered set of keys. In Java you use a `HashSet`.

`HashSet<Direction> set = new HashSet<>();
set.add(North);
set.add(East);
set.add(West);`

You can also create a set from some values. `Set.of(...)` on its own creates a set that can't be changed:
`HashSet<Direction> set = new HashSet<>(Set.of(North, East, West));`

Use `set.add(elem)` to add a new element to the set.

Use `set.remove(elem)` to remove an element from a set.

Use `if (set.contains(elem))` to check if the set contains an element.

Use `set.size()` to get the number of elements.

Use `for (Direction elem : set)` to iterate all elements in the set.
For larger sets `contains` performs much faster than it would on a list.

Just like maps, sets are unordered, so there are no guarantees about the order in which the elements are iterated.

Also, elements in sets are unique, so adding an element that is already in the set will not change the set.
