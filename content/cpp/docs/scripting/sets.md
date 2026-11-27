# std::set (Sets)
Sets are like [maps](docs/scripting/dicts.md), but without values. They're just a set of keys. In C++ you use a `std::set` (from `#include <set>`).

They are created like vectors, with the elements in braces.
`std::set<Direction> directions = {North, East, West};`

`std::set<Direction> empty_set;` creates an empty set.

Use `s.insert(elem)` to add a new element to the set `s`.

Use `s.erase(elem)` to remove an element from a set.

Use `if (s.contains(elem))` to check if the set contains an element.

Use `s.size()` to get the number of elements.

Use `for (auto elem : s)` to iterate all elements in the set.
For larger sets `contains` performs much faster than `std::ranges::contains` would on a vector.

Real C++ visits the elements of a `std::set` in sorted order, the game in the order they were added (a `std::unordered_set` has no order at all). So don't write code that depends on the order in which the elements are iterated.

Also, elements in sets are unique, so adding an element that is already in the set will not change the set.
