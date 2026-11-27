# Arrays and Lists
Arrays and lists are an easy way to store multiple values in a single variable.

## Arrays
An array holds a fixed number of values of the same type. You can create a new array like this:

`const numbers = [_]i32{ 2, 6, 12 };`

The array now contains the values `2`, `6` and `12`. The type is `[3]i32`, "an array of 3 `i32`". The `_` lets Zig count the elements for you.
All elements of an array have the same type, so an array can't hold a number and an item at the same time.

You can also create an array of a given length where every element starts with the same value:

`var counts: [10]i32 = @splat(0);`

You can access an element of an array by its index. The index is `0` for the first element, `1` for the second element, `2` for the third...

plants carrots
`const plants = [_]farm.Entity{ .Tree, .Carrot, .Pumpkin };
_ = farm.plant(plants[1]);`

An index is a `usize` (a whole number that is never negative). Zig has no negative indices: `plants[-1]` is an error. The last element is `plants[plants.len - 1]`.

To change an element, the array must be declared with `var` instead of `const`:
`var scores = [_]i32{ 2, 6, 12 };
scores[0] = 5;`

`numbers.len` is the number of elements in the array. It is a `usize`. The length of an array can never change.

## Iterating
You can iterate over an array using a `for` loop. The following example sums all elements in the array.

`const values = [_]i32{ 4, 7, 2, 5 };
var sum: i32 = 0;
for (values) |number| {
    sum += number;
}`
`sum` is now `18`

If you also need the index, add `0..` as a second input. The index is a `usize`:

`var more = [_]i32{ 4, 7, 2, 5 };
for (more, 0..) |number, i| {
    more[i] = number + 1;
}`

## ArrayList
An array can't grow or shrink. If you want to add and remove elements, use a `std.ArrayList` instead:

`var list: std.ArrayList(i32) = .empty;
defer list.deinit(farm.allocator);`

`std.ArrayList(i32)` means "list of `i32`". All elements of a list have the same type too. Game types are written as they are, like `std.ArrayList(farm.Entity)`.
`.empty` creates an empty list. A list needs memory to grow, so the functions that add elements take an allocator: use `farm.allocator`. `defer list.deinit(farm.allocator);` gives the memory back at the end of the block. The game doesn't need it, but real Zig does, so it's good practice.

Adding an element can fail in real Zig when there is no memory left. That's why you write `try` in front of it. A function that uses `try` must have the return type `!void`, like `pub fn main() !void`. You can also write `catch unreachable` after the call instead, which says "this never fails".

The elements of a list are in `list.items`. That's a slice: like an array, but its length can change.
`list.items[index]` returns the element at the given index, like `array[index]` does for arrays.
`list.items[index] = elem;` replaces the element at the given index.
`list.items.len` is the number of elements in the list.

The following list functions allow you to add and remove elements:

`list.append(farm.allocator, elem)` adds an element to the end of the list:

`var list: std.ArrayList(i32) = .empty;
try list.append(farm.allocator, 2);
try list.append(farm.allocator, 6);
try list.append(farm.allocator, 12);
try list.append(farm.allocator, 7);`
`list.items` is now `{ 2, 6, 12, 7 }`

`list.insert(farm.allocator, index, elem)` inserts an element at the given index:

`var list: std.ArrayList(farm.Item) = .empty;
try list.append(farm.allocator, .Hay);
try list.append(farm.allocator, .Carrot);
try list.insert(farm.allocator, 1, .Wood);`
`list.items` is now `{ .Hay, .Wood, .Carrot }`

`list.pop()` removes the last element and returns it as an optional (`null` if the list is empty).
`list.orderedRemove(index)` removes the element at the given index and returns it.
If you don't need the returned element, throw it away with `_ =`.

`var list: std.ArrayList(i32) = .empty;
try list.append(farm.allocator, 3);
try list.append(farm.allocator, 5);
try list.append(farm.allocator, 8);
try list.append(farm.allocator, 25);
_ = list.pop();`
`list.items` is now `{ 3, 5, 8 }`
`_ = list.orderedRemove(1);`
`list.items` is now `{ 3, 8 }`

To remove the first occurrence of an element, find its index with a loop and stop right after removing it:

`var list: std.ArrayList(i32) = .empty;
try list.append(farm.allocator, 1);
try list.append(farm.allocator, 2);
try list.append(farm.allocator, 4);
try list.append(farm.allocator, 2);
for (list.items, 0..) |x, i| {
    if (x == 2) {
        _ = list.orderedRemove(i);
        break;
    }
}`
`list.items` is now `{ 1, 4, 2 }`

Lists can be used in a `for` loop just like arrays. Loop over `list.items`:

`var plants: std.ArrayList(farm.Entity) = .empty;
try plants.append(farm.allocator, .Bush);
try plants.append(farm.allocator, .Tree);
for (plants.items) |p| {
    _ = farm.plant(p);
    _ = farm.move(.North);
}`

## Copies
Arrays are values in Zig. Assigning an array to another variable copies all of its elements, so changing the copy doesn't change the original:

`const a = [_]i32{ 1, 2 };
var b = a;
b[1] = 5;`
`a` is still `{ 1, 2 }` and `b` is `{ 1, 5 }`.

A `std.ArrayList` is different: it only points to its elements. Don't copy a list with `=`, because then two variables share the same elements and get confused when one of them changes. To get a real copy, use `clone`: `var c = try list.clone(farm.allocator);` gives you a new list with the same elements. Changing `c` doesn't change `list`.
