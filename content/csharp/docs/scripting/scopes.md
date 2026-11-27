# Name Scopes
Scopes determine which variables can be accessed from where.

## Local variables
A variable declared inside a block `{ }` only exists inside that block, from the line of the declaration to the closing `}` of the block.

`int x = 1;
if (CanHarvest())
{
	int y = 2;
	Console.WriteLine(x + y);
}
Console.WriteLine(y);`

The last line is an error (`The name 'y' does not exist in the current context`) because `y` only exists inside the block of the `if`.
This is also true for loops. The loop variable of a `for` loop only exists inside the loop:

`for (int i = 0; i < 3; i++)
{
	DoAFlip();
}
Console.WriteLine(i);`
This is an error too. If you need the value after the loop, declare the variable before the loop.

You can't declare a variable twice in the same scope: `int a = 1; int a = 2;` gives `A local variable or function named 'a' is already defined in this scope`. To change the value, just assign it: `a = 2;`.

Every method has its own local variables. Parameters are local variables too.

## Top-level variables
A variable declared by a top-level statement (outside of all methods) can be used by the statements below it, and also by the methods of the window. A method can read and change it.

`int count = 0;
Increment();
Console.WriteLine(count);

void Increment()
{
	count = 1;
}`

This prints `1`, because `Increment` changes the variable `count`.

Python needs a `global` keyword to change a global variable from inside a function. C# doesn't have that keyword and doesn't need it: if a method assigns to a name that isn't one of its own local variables, it changes the top-level variable.
But if the method declares a local variable with the same name, the local variable hides the top-level one:

`int total = 0;
Reset();
Console.WriteLine(total);

void Reset()
{
	int total = 1;
}`

This code prints `0` because the local `total` inside `Reset` is not the same variable as the top-level `total`, so the top-level one remains unchanged.

If you write your program as a class (see [Import](docs/scripting/import.md)), the variables that all methods share are static fields of the class:

`class Program
{
	static int count = 0;

	static void Increment()
	{
		count++;
	}

	static void Main()
	{
		Increment();
		Console.WriteLine(count);
	}
}`

Note that changing shared variables from many methods is usually the first step towards spaghetti code, where every part of the program affects every other part of the program, so don't overuse it.
