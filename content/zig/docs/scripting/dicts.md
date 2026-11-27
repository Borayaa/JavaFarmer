# Dictionaries (AutoHashMap)
Dictionaries are a datastructure that allows you to map keys to values in the same way that a real dictionary maps words to their definitions and you can look them up very quickly. In Zig you use a `std.AutoHashMap`.

A map can be created like this:
`var right_of = std.AutoHashMap(farm.Direction, farm.Direction).init(farm.allocator);
defer right_of.deinit();
try right_of.put(.North, .East);
try right_of.put(.East, .South);
try right_of.put(.South, .West);
try right_of.put(.West, .North);`

`std.AutoHashMap(farm.Direction, farm.Direction)` means that the keys are `farm.Direction`s and the values are `farm.Direction`s too. All keys have the same type, and all values have the same type.
`.init(farm.allocator)` creates an empty map that gets its memory from `farm.allocator`, and `defer right_of.deinit();` gives the memory back at the end of the block.
The first argument of `put` is the key and the second one is the value to which the key maps. Like `append` on a list, `put` can fail in real Zig, so it needs `try` (or `catch unreachable`).
The above map maps each direction to the direction to its right.

Accessing the value mapped to a key works with `get`: `map.get(key)` returns an optional, which is `null` if the key is not in the map.
If you know that the key is there, unwrap the value with `.?`:

`const orientation = right_of.get(.South).?;`
This sets `orientation` to `.West`.

Use `orelse` to get a default value when the key is missing: `const d = right_of.get(.North) orelse farm.Direction.North;`

You can add a new key-value pair to a map with `try map.put(key, value);`

Here's a map that remembers which entity is on which tile. The tile position is the key:
`var entities = std.AutoHashMap(farm.Position, ?farm.Entity).init(farm.allocator);
defer entities.deinit();
try entities.put(.{ .x = farm.getPosX(), .y = farm.getPosY() }, farm.getEntityType());`
This updates the entity stored for the current position. The value type is `?farm.Entity` because there might be nothing on the tile.

Keys are unique, so putting a key that already exists in the map will overwrite the previous value.

Use `_ = map.remove(key);` to remove a key-value pair from `map`. `remove` returns `true` if the key was there.

`map.contains(key)` evaluates to `true` if `key` is a key in `map` and `false` otherwise.
So you can use `if (map.contains(key))` to check if `map` contains the key.

`map.count()` returns the number of key-value pairs.

To iterate through all keys, get a key iterator with `map.keyIterator()` and call `next()` in a `while` loop until it returns `null`. `key.*` is the key itself. This function prints every key of a map and its value:
`fn printMap(map: std.AutoHashMap(farm.Direction, farm.Direction)) void {
    var keys = map.keyIterator();
    while (keys.next()) |key| {
        const value = map.get(key.*).?;
        std.debug.print("{} {}\n", .{ key.*, value });
    }
}`

`map.valueIterator()` gives you all the values in the same way, and `map.iterator()` gives you both:
`fn printEntries(map: std.AutoHashMap(farm.Direction, farm.Direction)) void {
    var it = map.iterator();
    while (it.next()) |entry| {
        std.debug.print("{} {}\n", .{ entry.key_ptr.*, entry.value_ptr.* });
    }
}`

There are no guarantees about the order in which the keys are iterated.

See also [Sets](docs/scripting/sets.md)
