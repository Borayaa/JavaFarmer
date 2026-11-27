# Debug
Sometimes your code just doesn't work and you need to find out why. There are a couple of tools to help you do that.

The first is to execute the program step by step. 
You can go into step-by-step mode with the button next to the Execute button or by setting a breakpoint.

Breakpoints can be added by clicking on the breakpoint panel to the left of the code.
![](Breakpoints227)
When execution reaches the line where the breakpoint is, it will automatically switch to step-by-step mode.

When you move your mouse over a variable, its current value is displayed.

The `printf()` function can also be very useful. It will write any value passed to it directly into the air.

Examples:

Print "0.24".
`printf("%g\n", 0.24);`

Print "true" or "false".
`printf("%s\n", str_of(can_harvest()));`

Print the current position.
`printf("%d, %d\n", get_pos_x(), get_pos_y());`

The first argument of `printf` is the format text. Each `%` placeholder in it is replaced by the next argument: `%d` for an `int`, `%f` or `%g` for a `double`, `%s` for a string. `str_of(value)` turns any other value into a string, for example a bool, an entity or a list. `puts("text")` prints a string followed by a line break. In real C these functions come from `<stdio.h>`; `farm.h` already includes it for you.

`printf` prints the value directly into the air and to the [Output](docs/output.md) page. It writes one line every time it reaches a `\n`. Text without a `\n` at the end waits until a later `printf` finishes the line.

Writing into the air can sometimes be a bit slow if you want to print a lot of values.
In this case you can use the `quick_print()` function which prints only to the output window. It takes the same format text as `printf` and always prints one line, so it needs no `\n`.

`quick_print("tile %d", get_pos_x());`

Values are printed the game's way: `true`, `NULL`, lists and sets as `{1, 2, 3}`, maps as `{"a": 1}` and a `Position` as `{1, 2}`. Numbers never get extra zeros: `printf("%f", 2.0)` shows `2` and `1.23456` shows up as `1.23` (at most two decimals).

The output window also logs warnings and errors, so if something doesn't work as expected it can be useful to check that.

When the execution stops, the output is also written to the output.txt file in the game folder. [output.txt](persistent_data_path/output.txt).
