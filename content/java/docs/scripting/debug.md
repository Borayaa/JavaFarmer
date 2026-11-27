# Debug
Sometimes your code just doesn't work and you need to find out why. There are a couple of tools to help you do that.

The first is to execute the program step by step. 
You can go into step-by-step mode with the button next to the Execute button or by setting a breakpoint.

Breakpoints can be added by clicking on the breakpoint panel to the left of the code. A red dot marks the line.
When execution reaches the line where the breakpoint is, it will automatically switch to step-by-step mode.

When you move your mouse over a variable, its current value is displayed.

`System.out.println()` can also be very useful. It will write any value passed to it directly into the air.

Examples:

Print "0.24".
`System.out.println(0.24);`

Print "true" or "false".
`System.out.println(canHarvest());`

Print the current position.
`System.out.println(getPosX() + ", " + getPosY());`

`System.out.println` takes exactly one value. To print several values at once, join them into one `String` with `+` like in the last example.

`System.out.println` prints the value directly into the air and to the [Output](docs/output.md) page. `System.out.print` does the same thing.

Writing into the air can sometimes be a bit slow if you want to print a lot of values.
In this case you can use the `quickPrint()` method which prints only to the output window.

`quickPrint("tile " + getPosX());`

Values are printed like Java prints them: `true`, `null`, lists and sets as `[1, 2, 3]`, maps as `{a=1, b=2}`, a `Position` as `Position[x=1, y=2]` and a `Companion` as `Companion[entity=Entities.Bush, position=Position[x=1, y=2]]`. Numbers are printed the game's way: a `double` like `2.0` shows up as `2` and `1.23456` as `1.23` (at most two decimals).

To turn a value into a `String`, use `String.valueOf(value)` or simply `"" + value`.

The output window also logs warnings and errors, so if something doesn't work as expected it can be useful to check that.

When the execution stops, the output is also written to the output.txt file in the game folder. [output.txt](persistent_data_path/output.txt).
