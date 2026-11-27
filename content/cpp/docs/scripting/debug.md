# Debug
Sometimes your code just doesn't work and you need to find out why. There are a couple of tools to help you do that.

The first is to execute the program step by step. 
You can go into step-by-step mode with the button next to the Execute button or by setting a breakpoint.

Breakpoints can be added by clicking on the breakpoint panel to the left of the code.
![](Breakpoints227)
When execution reaches the line where the breakpoint is, it will automatically switch to step-by-step mode.

When you move your mouse over a variable, its current value is displayed.

Printing can also be very useful. `std::cout` writes any value passed to it with `<<` directly into the air. `std::endl` (or `'\n'`) ends the line.

Examples:

Print "0.24".
`std::cout << 0.24 << std::endl;`

Print "true" or "false". A `bool` needs `std::boolalpha`, otherwise real C++ would print `1` or `0`.
`std::cout << std::boolalpha << can_harvest() << std::endl;`

Print the current position.
`std::cout << get_pos_x() << ", " << get_pos_y() << std::endl;`

`std::println` is often shorter. Its first argument is the text to print, and every `{}` in it is replaced by the next value:
`std::println("{}, {}", get_pos_x(), get_pos_y());`
`std::println("{}", can_harvest());` prints `true` or `false` without `std::boolalpha`.

These print the value directly into the air and to the [Output](docs/output.md) page. One line is printed for every finished line: text without `std::endl` (or with `std::print` instead of `std::println`) waits until a later output ends the line.

Writing into the air can sometimes be a bit slow if you want to print a lot of values.
In this case you can use the `quick_print()` function which prints only to the output window. It works like `std::println`:

`quick_print("tile {}", get_pos_x());`

Values are printed like the game prints them: `true`, `nullptr`, vectors as `[1, 2, 3]`, maps as `{k: v}`, a `Position` as `(1, 2)`. Numbers are printed the game's way: a `double` like `2.0` shows up as `2` and `1.23456` as `1.23` (at most two decimals).

To turn a number into a `std::string`, use `std::to_string(x)`. `std::format("{}", value)` works for any value.

The output window also logs warnings and errors, so if something doesn't work as expected it can be useful to check that.

When the execution stops, the output is also written to the output.txt file in the game folder. [output.txt](persistent_data_path/output.txt).
