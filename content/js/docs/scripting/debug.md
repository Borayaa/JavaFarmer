# Debug
Sometimes your code just doesn't work and you need to find out why. There are a couple of tools to help you do that.

The first is to execute the program step by step. 
You can go into step-by-step mode with the button next to the Execute button or by setting a breakpoint.

Breakpoints can be added by clicking on the breakpoint panel to the left of the code.
![](Breakpoints227)
When execution reaches the line where the breakpoint is, it will automatically switch to step-by-step mode.

When you move your mouse over a variable, its current value is displayed.

The `console.log()` function can also be very useful. It will write any value passed to it directly into the air.

Examples:

Print "0.24".
`console.log(0.24);`

Print "true" or "false".
`console.log(canHarvest());`

Print the current position.
`console.log(getPosX(), getPosY());`

`console.log` takes any number of values and prints them separated by a space. `console.log()` with nothing in the parentheses prints an empty line.

`console.log` prints the value directly into the air and to the [Output](docs/output.md) page.

Writing into the air can sometimes be a bit slow if you want to print a lot of values.
In this case you can use the `quickPrint()` function which prints only to the output window.

`quickPrint("tile", getPosX(), getPosY());`

Values are printed like JavaScript prints them: `true`, `false`, `null`, arrays as `[ 1, 2, 3 ]`, a `Map` as `Map(1) { 'a' => 1 }` and a `Set` as `Set(2) { 1, 2 }`.

To build a text from several values, join the parts with `+`:
`const x = getPosX();
console.log("x is " + x);`
A template literal (text between backticks with `${x}` inside) works too.
Note that joining a value to a text uses the game's spelling: numbers and texts look the same as in JavaScript, but `"ready: " + true` gives "ready: True". `console.log("ready:", true)` prints "ready: true".

The output window also logs warnings and errors, so if something doesn't work as expected it can be useful to check that.

When the execution stops, the output is also written to the output.txt file in the game folder. [output.txt](persistent_data_path/output.txt).