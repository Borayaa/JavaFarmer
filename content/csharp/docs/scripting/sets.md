# Sets
Sets are like [dictionaries](docs/scripting/dicts.md), but without values. They're just an unordered set of keys. In C# you use a `HashSet`.

`HashSet<Direction> set = [North, East, West];`

You can also write `new HashSet<Direction> { North, East, West }`. Use `new HashSet<Direction>()` or `[]` to create an empty set.

Use `set.Add(elem);` to add a new element to the set.

Use `set.Remove(elem);` to remove an element from a set.

Use `if (set.Contains(elem))` to check if the set contains an element.

Use `set.Count` to get the number of elements.

Use `foreach (Direction elem in set)` to iterate all elements in the set.
For larger sets `Contains` performs much faster than it would on a list.

Just like dictionaries, sets are unordered, so there are no guarantees about the order in which the elements are iterated.

Also, elements in sets are unique, so adding an element that is already in the set will not change the set.
