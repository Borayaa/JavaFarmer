# While Loop
You have unlocked the `while` loop and the values `true` and `false`. The `while` loop keeps executing the loop body as long as the condition is `true`.

`while (condition) {
    // loop body
}`

Don't worry about creating infinite loops. The delays in the execution will prevent the program from freezing.

## For Beginners
Perhaps you have already tried to put several `farm.harvest()` calls in a row:

`_ = farm.harvest();
_ = farm.harvest();
_ = farm.harvest();`

This allows you to harvest several times in one program run. 
However, it would be nice to harvest more than three times, and writing the same code multiple times is bad practice. 
The solution is a loop. 
A loop allows you to run the same code multiple times.

The while loop takes a condition, which is a logical value that can only be in one of two states: `true` or `false`. 
Such a value is called a boolean value. Its type is `bool`.

The loop then executes the code inside the loop until the condition is `false`.
The while loop looks like this:

`while (condition) {
    // loop body
    // loop body
    // ...
}`
	
The condition always goes inside the parentheses `( )`. Replace "condition" with a boolean value and the `// loop body` lines with whatever you want to do in the loop.

There are two constant boolean values available. Constants are values that never change during the program.

To create a constant boolean value that is always true, you can simply write `true`. Write `false` as a constant boolean value that will always be false. (In Zig they are written in lowercase.)
So you could either write


`while (false) {
    farm.doAFlip();
}`

or

`while (true) {
    farm.doAFlip();
}`

The first will never do a flip and the second will do flips forever (an infinite loop). 

Normally creating an infinite loop is a bad idea because it will freeze the program, but in this game there are delays between each iteration of the loop, so it will cause the drone to keep doing a flip until you manually stop it by pressing the execute button again.

## Code blocks
The curly braces `{` and `}` mark the start and end of a block of code. The loop repeats all statements inside its braces.
Statements after the closing `}` will be executed after the loop has finished.

Here is a complete program that harvests forever:

`const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    while (true) {
        _ = farm.harvest();
    }
}`

Notice how the code inside each pair of braces is indented. Zig itself ignores indentation, but it makes it much easier to see which statements belong to which block.
Just press Tab to add indentation and Shift + Tab (or Backspace) to remove it.

## Counting
A `while` loop can have a continue expression after a colon. It runs at the end of every iteration, which is handy for counting:

`var i: i32 = 0;
while (i < 5) : (i += 1) {
    std.debug.print("{d}\n", .{i});
}`

This prints `0`, `1`, `2`, `3` and `4`. It is the same as putting `i += 1;` at the end of the loop body, except that it also runs when you skip the rest of the body with [continue](docs/scripting/continue.md).
`i` is declared with `var` because it changes, and it needs a type like `i32`.
