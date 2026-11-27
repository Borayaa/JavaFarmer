# Sets
Sets are like [dictionaries](docs/scripting/dicts.md), but without values. They're just an unordered set of keys.
Zig has no separate set type. You use a `std.AutoHashMap` whose value type is `void`, which means "no value".

`var set = std.AutoHashMap(farm.Direction, void).init(farm.allocator);
defer set.deinit();
try set.put(.North, {});
try set.put(.East, {});
try set.put(.West, {});`

`{}` is the only value of type `void`, so `put(elem, {})` stores the key and nothing else. `.init(farm.allocator)` creates an empty set.

Use `try set.put(elem, {});` to add a new element to the set.

Use `_ = set.remove(elem);` to remove an element from a set.

Use `if (set.contains(elem))` to check if the set contains an element.

Use `set.count()` to get the number of elements.

Use a key iterator to iterate all elements in the set. `elem.*` is the element itself:
`fn printSet(s: std.AutoHashMap(farm.Direction, void)) void {
    var it = s.keyIterator();
    while (it.next()) |elem| {
        std.debug.print("{}\n", .{elem.*});
    }
}`
For larger sets `contains` performs much faster than looking for an element in a list.

Just like maps, sets are unordered, so there are no guarantees about the order in which the elements are iterated.

Also, elements in sets are unique, so adding an element that is already in the set will not change the set.
