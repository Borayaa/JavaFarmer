# Debug
Sometimes your code just doesn't work and you need to find out why. There are a couple of tools to help you do that.

The first is to execute the program step by step. 
You can go into step-by-step mode with the button next to the Execute button or by setting a breakpoint.

Breakpoints can be added by clicking on the breakpoint panel to the left of the code.
![](Breakpoints227)
When execution reaches the line where the breakpoint is, it will automatically switch to step-by-step mode.

When you move your mouse over a variable, its current value is displayed.

The `std.debug.print()` function can also be very useful. It will write the values passed to it directly into the air.

Examples:

Print "0.24".
`std.debug.print("{d}\n", .{ 0.24 });`

Print "true" or "false".
`std.debug.print("{}\n", .{ farm.canHarvest() });`

Print the current position.
`std.debug.print("{d} {d}\n", .{ farm.getPosX(), farm.getPosY() });`

`std.debug.print` takes two things: a format string in double quotes and the values to print in `.{ }`, separated by commas.
Each placeholder in `{ }` in the format string is replaced by the next value:

`{d}` for numbers (`i32`, `usize`, `f64`).
`{s}` for text (`[]const u8`).
`{}` for `true` / `false` and for game values like `farm.Entity.Carrot` or `farm.Direction.North`.
`{any}` for any value, also optional values that might be `null` (like the result of `farm.getEntityType()`), arrays and lists.

`const name: []const u8 = "carrots";
std.debug.print("I have {d} {s}\n", .{ farm.numItems(.Carrot), name });
std.debug.print("here is {any}\n", .{ farm.getEntityType() });
std.debug.print("hello\n", .{});`

The format string should end with `\n` (a line break) and contain no other line breaks: every print is one line in the game.
If you only want to print text, leave the `.{}` empty, like in the last line above.
The number of placeholders and values must match (otherwise you get `too few arguments` or `unused argument`), and the placeholder must fit the value: `{s}` with a number gives `invalid format string 's' for type 'comptime_int'`.

The print function prints the value directly into the air and to the [Output](docs/output.md) page.

Writing into the air can sometimes be a bit slow if you want to print a lot of values.
In this case you can use the `farm.quickPrint()` function which prints only to the output window. It takes a format string and values just like `std.debug.print`:

`farm.quickPrint("tile {d} {d}\n", .{ farm.getPosX(), farm.getPosY() });`

Values are printed like Zig prints them with `{any}`: `true`, `false`, `null`, game values as `.Carrot` or `.North`, arrays and lists as `{ 1, 2, 3 }` and a `farm.Position` as `.{ .x = 1, .y = 2 }`. Numbers are printed like `{d}` prints them: an `f64` like `2.0` shows up as `2`.

The output window also logs warnings and errors, so if something doesn't work as expected it can be useful to check that.

When the execution stops, the output is also written to the output.txt file in the game folder. [output.txt](persistent_data_path/output.txt).
