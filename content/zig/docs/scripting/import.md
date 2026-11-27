# Import
Putting all your code in a single file quickly becomes unmanageable. 
`@import` gives you access to the functions and top-level variables of another file.
Every code window is a file, and the module name is the file name without its extension: the code window `module2.zig` is the module `module2`.
How it works in two code windows:

`// module2.zig
const std = @import("std");

pub var x: i32 = 5;

pub fn printX() void {
    std.debug.print("{d}\n", .{ x });
}`

`// main.zig
const module2 = @import("module2.zig");

pub fn main() void {
    module2.printX();
}`

Here `const module2 = @import("module2.zig");` loads the file named `module2` and stores it in the constant `module2`. Always write the file name with its extension: `@import("module2")` without `.zig` gives an error.
You can then access variables and functions within the imported module using the `.` operator.
So in this example, `module2.printX()` calls `printX()` in `module2`, and `module2.x` reads its variable `x`.

Only declarations marked `pub` (public) can be used from another file. Without `pub`, `module2.printX()` gives the error `'printX' is not marked 'pub'`. Declarations without `pub` are the private helpers of their file.

Importing a file needs the Imports unlock.

You can also import files written in other languages, for example `const bot = @import("bot.py");` or `const Utils = @import("Utils.java");`, and use their functions and variables with the names written in that file, like `bot.pick_plant()` or `Utils.plantRow()`.
Values from Java, C# and Rust files have a type that Zig understands (an `int` becomes an `i32`, a `double` an `f64` and so on). Values from Python, JavaScript and R files have no type, so you have to write the type at the declaration: `const count: i32 = bot.count_plants();`.

### No need to read further

Zig has no way to copy all the names of another file into your file, so you always write the module name in front. If a name is too long to write every time, you can give a function of another file a short name with a constant:

`const module2 = @import("module2.zig");
const printX = module2.printX;

pub fn main() void {
    printX();
}`

Do this only for functions. For variables, always write the module name (`module2.x`), so that you always get the current value of the variable.

# How it really works

## TLDR
Imports can be quite unintuitive in Python, but Zig avoids most problems: a Zig file only contains declarations, all the code that does things is inside functions, and `pub fn main` only runs in the file you start.

## Import Side Effects
The first time you import a file, the game loads it: all its top-level variables get their starting values and all its functions are defined.
If you import the same file again, it will just return the already loaded module from the first time again. So all files that import `module2` share the same `module2.x` (within one drone, see the Megafarm unlock).

In Python, an import can have side effects: if the imported file harvests outside of a function, it actually harvests during the import.
In Zig this can't happen, because code like `_ = farm.harvest();` can only be written inside a function. Importing a file only runs the starting values of its top-level variables.

Every file can have its own `pub fn main`. It only runs when you start that file with its play button, not when another file imports it. (JavaFarmer turns it into the `__name__` check that Python programs use for this.)
This lets you test a file on its own:

`// helpers.zig
const farm = @import("farm");

pub fn plantRow(plant_type: farm.Entity) void {
    for (0..farm.getWorldSize()) |_| {
        _ = farm.plant(plant_type);
        _ = farm.move(.East);
    }
}

pub fn main() void {
    plantRow(.Bush);
}`

Starting `helpers.zig` plants a row of bushes. A file that imports it can call `helpers.plantRow(.Carrot)` without planting any bushes, because the `main` of `helpers` doesn't run.

This also gives you a clear distinction between local variables (declared inside `main` or another function) and top-level variables that other files can use (declared outside of all functions, with `pub`).

## Import Cycles
What happens if file `a` imports file `b` and file `b` imports file `a`?

file `a`:
`// a.zig
const std = @import("std");
const b = @import("b.zig");

pub var x: i32 = 0;

pub fn printB() void {
    std.debug.print("{d}\n", .{ b.y });
}`

file `b`:
`// b.zig
const std = @import("std");
const a = @import("a.zig");

pub var y: i32 = 1;

pub fn printA() void {
    std.debug.print("{d}\n", .{ a.x });
}`

This will work fine. Let's say neither of the two files are loaded yet, and someone else imports `a`.

-`a` starts loading and reaches the import of `b`.
-`b` starts loading and reaches the import of `a`.
-The module `a` already exists, but doesn't contain `x` yet because it has only reached the import of `b`.
-`b` stores a reference to the half-loaded module `a` in its constant `a`.
-`b` sets up `y` and the function `printA()`.
-`a` continues to load and sets up `x` and `printB()`.

When someone calls `b.printA()` it will correctly print `0`, because functions only run when they are called, and by then the module `a` that `b` has a reference to is fully loaded.

Problems can only appear if the starting value of a top-level variable depends on a constant of another file that is still loading. For example `pub const z: i32 = a.limit + 1;` in `b` while `a` is loading `b`: at that moment `a.limit` doesn't exist yet. If you get strange errors that a variable has never been defined, give the variable a simple starting value and set the real value in a function instead. (A starting value can never read a `var` or call a `farm` function, not even of the same file: Zig needs starting values to be known when the program is compiled, so `const n = farm.getWorldSize();` belongs inside a function.)
