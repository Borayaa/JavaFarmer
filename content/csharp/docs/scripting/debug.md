# Debug
Sometimes your code just doesn't work and you need to find out why. There are a couple of tools to help you do that.

The first is to execute the program step by step. 
You can go into step-by-step mode with the button next to the Execute button or by setting a breakpoint.

Breakpoints can be added by clicking on the breakpoint panel to the left of the code.
![](Breakpoints227)
When execution reaches the line where the breakpoint is, it will automatically switch to step-by-step mode.

When you move your mouse over a variable, its current value is displayed.

`Console.WriteLine()` can also be very useful. It will write any value passed to it directly into the air.

Examples:

Print "0.24".
`Console.WriteLine(0.24);`

Print "True" or "False".
`Console.WriteLine(CanHarvest());`

Print the current position.
`Console.WriteLine($"{GetPosX()}, {GetPosY()}");`

`Console.WriteLine` takes exactly one value. To print several values at once, put them into one `string` with string interpolation `$"..."` like in the last example, or join them with `+`. `Console.Write` adds text without printing it yet: the text shows up together with the next `Console.WriteLine`.

`Console.WriteLine` prints the value directly into the air and to the [Output](docs/output.md) page.

Writing into the air can sometimes be a bit slow if you want to print a lot of values.
In this case you can use the `QuickPrint()` method which prints only to the output window.

`QuickPrint("tile " + GetPosX());`

Values are printed like C# prints them: a `bool` as "True" or "False", numbers like `3` or `2.5`, and strings as they are. Some values are shown in a more useful way than real C# does: `null` as `null`, lists and sets as `[1, 2, 3]`, dictionaries as `{"a": 1}`, tuples and positions as `(1, 2)` and game constants as `Entities.Carrot`.

To turn a value into a `string`, use `value.ToString()`, `$"{value}"` or simply `"" + value`.

The output window also logs warnings and errors, so if something doesn't work as expected it can be useful to check that.

When the execution stops, the output is also written to the output.txt file in the game folder. [output.txt](persistent_data_path/output.txt).
