# While Loop
You have unlocked the `while` loop and the values `TRUE` and `FALSE`. The `while` loop keeps executing the loop body as long as the condition is `TRUE`.

`while (TRUE) {
	harvest()
}`

Don't worry about creating infinite loops. The delays in the execution will prevent the program from freezing.

## For Beginners
Perhaps you have already tried to put several `harvest()` calls in a row:

`harvest()
harvest()
harvest()`

This allows you to harvest several times in one program run. 
However, it would be nice to harvest more than three times, and writing the same code multiple times is bad practice. 
The solution is a loop. 
A loop allows you to run the same code multiple times.

The while loop takes a condition, which is a logical value that can only be in one of two states: `TRUE` or `FALSE`. 
Such a value is called a Boolean value.

The loop then executes the code inside the loop until the condition is `FALSE`.
The while loop looks like this: `while (condition) { loop body }`. You write it over several lines, with the statements of the loop body between the curly braces:

`while (TRUE) {
	harvest()
	do_a_flip()
}`
	
Where you have to replace the condition between the parentheses `(` `)` with a boolean value and the statements between `{` and `}` with whatever you want to do in the loop.

There are two constant boolean values available. Constants are values that never change during the program.

To create a constant boolean value that is always `TRUE`, you can simply write `TRUE`. Write `FALSE` as a constant boolean value that will always be `FALSE`. Write them in capital letters: R doesn't know `true`.
So you could either write


`while (FALSE) {
	do_a_flip()
}`

or

`while (TRUE) {
	do_a_flip()
}`

The first will never do a flip and the second will do flips forever (an infinite loop). 

Normally creating an infinite loop is a bad idea because it will freeze the program, but in this game there are delays between each iteration of the loop, so it will cause the drone to keep doing a flip until you manually stop it by pressing the execute button again.

R also has a loop that always repeats forever: `repeat { ... }` does the same thing as `while (TRUE) { ... }`.

`repeat {
	do_a_flip()
}`

Notice how the lines between the curly braces are indented. R doesn't need the indentation, the braces `{` and `}` separate the blocks of code, but indentation makes it much easier to see which statements belong to the loop.
Just press Tab to add indentation and Shift + Tab (or Backspace) to remove it.

The loop will repeat all statements between the `{` and the matching `}`.
Statements after the closing `}` will be executed after the loop has finished.
