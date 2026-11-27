# While Loop
You have unlocked the `while` loop and the values `true` and `false`. The `while` loop keeps executing the loop body as long as the condition is `true`.

`while (condition)
{
	// loop body
}`

Don't worry about creating infinite loops. The delays in the execution will prevent the program from freezing.

## For Beginners
Perhaps you have already tried to put several `Harvest()` calls in a row:

`Harvest();
Harvest();
Harvest();`

This allows you to harvest several times in one program run. 
However, it would be nice to harvest more than three times, and writing the same code multiple times is bad practice. 
The solution is a loop. 
A loop allows you to run the same code multiple times.

The while loop takes a condition, which is a logical value that can only be in one of two states: `true` or `false`. 
Such a value is called a Boolean value (`bool` in C#).

The loop then executes the code inside the loop until the condition is `false`.
The while loop looks like this:

`while (condition)
{
	// loop body
	// loop body
	// ...
}`
	
The condition always goes inside the parentheses `( )`. Replace "condition" with a boolean value and the `// loop body` lines with whatever you want to do in the loop.

There are two constant boolean values available. Constants are values that never change during the program.

To create a constant boolean value that is always true, you can simply write `true`. Write `false` as a constant boolean value that will always be false. (In C# they are written in lowercase.)
So you could either write

`while (false)
{
	DoAFlip();
}`

or

`while (true)
{
	DoAFlip();
}`

The first will never do a flip (C# even warns that the code inside is unreachable) and the second will do flips forever (an infinite loop). 

Normally creating an infinite loop is a bad idea because it will freeze the program, but in this game there are delays between each iteration of the loop, so it will cause the drone to keep doing a flip until you manually stop it by pressing the execute button again.

## Code blocks
The curly braces `{` and `}` mark the start and end of a block of code. The loop repeats all statements inside its braces.
Statements after the closing `}` will be executed after the loop has finished.

Notice how the code inside each pair of braces is indented. C# itself ignores indentation, but it makes it much easier to see which statements belong to which block.
Just press Tab to add indentation and Shift + Tab (or Backspace) to remove it.

## Do-While
C# also has a `do`-`while` loop. It runs the loop body first and checks the condition afterwards, so the body always runs at least once:

`do
{
	DoAFlip();
} while (false);`

Note the semicolon after the condition.
