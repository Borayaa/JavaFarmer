# HashSet (Sets)
Sets are like [maps](docs/scripting/dicts.md), but without values. They're just an unordered set of keys. In Rust you use a `HashSet`, which needs `use std::collections::HashSet;` at the top of the file.

`let mut set = HashSet::new();
set.insert(North);
set.insert(East);
set.insert(West);`

You can also create a set from some values:
`let set = HashSet::from([North, East, West]);`

Use `HashSet::new()` to create an empty set.

Use `set.insert(elem);` to add a new element to the set.

Use `set.remove(&elem);` to remove an element from a set.

Use `if set.contains(&elem) {` to check if the set contains an element.

Use `set.len()` to get the number of elements.

Use `for elem in &set {` to iterate all elements in the set.
For larger sets `contains` performs much faster than it would on a list.

Just like maps, sets are unordered, so there are no guarantees about the order in which the elements are iterated.

Also, elements in sets are unique, so adding an element that is already in the set will not change the set.
