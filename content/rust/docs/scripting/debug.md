# Debug
Sometimes your code just doesn't work and you need to find out why. There are a couple of tools to help you do that.

The first is to execute the program step by step. 
You can go into step-by-step mode with the button next to the Execute button or by setting a breakpoint.

Breakpoints can be added by clicking on the breakpoint panel to the left of the code.
![](Breakpoints227)
When execution reaches the line where the breakpoint is, it will automatically switch to step-by-step mode.

When you move your mouse over a variable, its current value is displayed.

`println!` can also be very useful. It will write any value passed to it directly into the air.
`println!` is a macro, not a function. That's what the `!` means. Its first argument is always a text in double quotes, the format string. Every `{}` in it is replaced by the next value.

Examples:

Print "0.24".
`println!("{}", 0.24);`

Print "true" or "false".
`println!("{}", can_harvest());`

Print the current position.
`println!("{}, {}", get_pos_x(), get_pos_y());`

You can also write a variable name inside the braces: `println!("x is {x}");` prints the value of the variable `x`.
A `Vec`, a `HashMap`, a tuple or an `Option` needs `{:?}` instead of `{}`, for example `println!("{:?}", list);`.

`println!` prints the value directly into the air and to the [Output](docs/output.md) page. `print!` does the same thing (in the game it also ends the line).

Writing into the air can sometimes be a bit slow if you want to print a lot of values.
In this case you can use the `quick_print!` macro which prints only to the output window.

`quick_print!("tile {}", get_pos_x());`

Values are printed like Rust prints them: `true`, `false`, `None`, lists as `[1, 2, 3]`, maps as `{North: East}`, tuples as `(1, 2)` and game values by their name, like `Carrot` or `North`. An `Option` that holds a value prints just the value (`Carrot`, not `Some(Carrot)`). Numbers are printed the game's way: `2.0` shows up as `2`. Format options like `{:.2}` are not supported, the game always prints numbers in full.

To turn a value into a `String`, use `value.to_string()` or `format!("{}", value)`.

The output window also logs warnings and errors, so if something doesn't work as expected it can be useful to check that.

When the execution stops, the output is also written to the output.txt file in the game folder. [output.txt](persistent_data_path/output.txt).
