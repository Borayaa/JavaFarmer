# For Loop
The `for` loop in Zig goes through every element of a sequence, like the `for` loop in Python. (Called a foreach loop in some languages. Zig has no C-style for loop with three parts.)

`for (0..5) |i| {
    // do something with i
    _ = i;
}`

Similar to the `while` loop, the `for` loop also repeatedly runs a block of code. Instead of looping based on a condition, it executes the loop body once for each element in a sequence.

## Syntax
A for loop looks like this:

`const sequence = [_]i32{ 4, 7, 2 };
for (sequence) |variable_name| {
    // code block
    std.debug.print("{d}\n", .{variable_name});
}`

`variable_name` can be any name you choose. It is a variable that stores the current element in the sequence. Zig calls it a capture, and it is written between two `|`. `sequence` needs to be some value that can be iterated, like a range of numbers or an array. The code block is executed for every element with the loop variable assigned to that element.
The loop variable only exists inside the loop, and you can't change it. Zig doesn't allow unused variables, so if you don't need it, write `_` instead of a name: `for (0..5) |_| {`

## Ranges
A range is written with two dots:
`0..5` is `0, 1, 2, 3, 4` (the end is not included).
`2..5` is `2, 3, 4`.
The numbers of a range are `usize`s.

`for (0..farm.getWorldSize()) |i| {
    std.debug.print("{d}\n", .{i});
}`

A range always counts up by one. To count in other steps or backwards, use a [while loop](docs/scripting/while.md) with a continue expression:

`// 2, 4, 6, 8
var i: i32 = 2;
while (i < 10) : (i += 2) {
    std.debug.print("{d}\n", .{i});
}`

## Sequences
[Ranges](functions/range)      <unlock=lists>[Arrays and Lists](docs/scripting/lists.md)      </unlock><unlock=dicts>[AutoHashMap](docs/scripting/dicts.md)      </unlock><unlock=sets>[Sets](docs/scripting/sets.md)</unlock>

A `std.ArrayList` is iterated through its elements: `for (list.items) |x| {`. A map or a set is iterated with an iterator in a `while` loop, see their pages.

## Example
`for (0..5) |_| {
    _ = farm.harvest();
}`

This loop executes the body a fixed number of times. It is essentially the same as writing

`_ = farm.harvest();
_ = farm.harvest();
_ = farm.harvest();
_ = farm.harvest();
_ = farm.harvest();`

So it calls `farm.harvest()` 5 times.
<unlock=lists>
Going through an array looks like this:
`const numbers = [_]i32{ 4, 7, 2, 5 };
var sum: i32 = 0;
for (numbers) |number| {
    sum += number;
}`
`sum` is now `18`.

If you also need the index, add a range `0..` as a second sequence and a second capture. Both are gone through at the same time:
`const plants = [_]farm.Entity{ .Bush, .Tree, .Carrot };
for (plants, 0..) |p, index| {
    std.debug.print("{d}: {}\n", .{ index, p });
}`
</unlock>
See also [Break](docs/scripting/break.md) and [Continue](docs/scripting/continue.md)
