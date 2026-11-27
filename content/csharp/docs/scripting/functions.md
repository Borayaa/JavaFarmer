# Functions
In C#, functions are called methods. Define a new one by writing its return type, its name and its parameters:
`void F(int arg1, bool arg2 = false)
{
	// method code
}`

You can use the call operator `()` to call it:
`F(42);`

Also see [Scopes](docs/scripting/scopes.md) to learn about local and top-level variables in methods.

## Introduction
You've already seen built-in methods like `Harvest()`.
You can also define your own methods which allows structuring your code in a modular way. It basically allows you to give a name to a block of code so you can call it from anywhere you want.

A method written between the top-level statements of a window is called a local function in C#. If you use the class form `class Program { static void Main() { } }`, write your methods next to `Main` inside the class and put `static` in front of them: `static void MoveNDir(int n, Direction dir)`.

## Method Definitions
For example, you could define a method that moves the drone several times.

`void MoveNDir(int n, Direction dir)
{
	for (int i = 0; i < n; i++)
	{
		Move(dir);
	}
}`

`void` means that the method does not return a value (see below).
`MoveNDir` is the name of the method. This can be any valid name and will be used to call the method. C# names methods in PascalCase: every word starts with a capital letter.
`n` and `dir` are parameters. They are variables that hold the values that are passed into the method (these values are also called arguments). Every parameter needs a type, just like a variable. You can add as many parameters to a method definition as you want, separated by commas.
After the parentheses comes the code block in `{ }` that will run when the method is called.

With the above definition the following code then moves the drone `10` tiles `North` and `2` tiles `West`.

`MoveNDir(10, North);
MoveNDir(2, West);`

Unlike variables, it doesn't matter where in the window a method is written. You can call it in a line above its definition:

`Flip();

void Flip()
{
	DoAFlip();
}`

If you call a method with the wrong number or types of arguments, the editor shows an error right away: `MoveNDir(North);` gives `There is no argument given that corresponds to the required parameter 'dir'`.

## Return Values
Use the `return` keyword to make a method return a value. Instead of `void`, write the type of the returned value in front of the method name.
For example, the following method defines the exclusive or operation. The exclusive or returns `true` if one value is `true` and the other one is `false`:

`if (Xor(true, false))
{
	DoAFlip();
}

bool Xor(bool a, bool b)
{
	return a != b;
}`

A method with a return type must return a value on every path, otherwise you get the error `not all code paths return a value`.

`return` ends the method immediately. In a `void` method you can use `return;` without a value to leave the method early.

A method that only computes one expression can also be written with `=>`:

`int Twice(int x) => x * 2;`

[Tuples](docs/scripting/tuples.md) allow returning multiple values.

## Default Arguments
You can also assign default values that will be used if no arguments are passed. A default value must be a constant, like `false`, `3` or `"text"`.

`Flips();
Flips(3);

void Flips(int n = 1)
{
	for (int i = 0; i < n; i++)
	{
		DoAFlip();
	}
}`

A parameter that has a default value cannot be followed by a parameter that doesn't have a default value.

Two methods can't have the same name in JavaFarmer, even if their parameters differ (real C# calls this overloading). Give each method its own name.

## Advanced Method Usage
Methods are values just like any other value. You can store a method in a variable of a delegate type: `Action` for a method without parameters and without a return value, `Action<int>` for one with an `int` parameter, `Func<bool>` for one that returns a `bool`, `Func<int, bool>` for one that takes an `int` and returns a `bool`, and so on.

`Action flip = DoAFlip;
flip();`

Instead of naming an existing method you can also write a lambda: a method without a name, written as `() => ...`.
This allows doing things like this:

`MakeFlip()();

Action MakeFlip()
{
	Action d = () => DoAFlip();
	return d;
}`

Here `MakeFlip()` calls the method `MakeFlip` which creates and returns a new method `d`. The second `()` then executes the returned method and performs a flip.
(Doing these sort of things is usually not a good idea because it's hard to see what's going on)

Methods that take other methods as arguments let you get really creative:

`void Repeat(Action task, int times)
{
	for (int i = 0; i < times; i++)
	{
		task();
	}
}

Repeat(() => Move(North), 10);
Repeat(() => UseItem(Items.Fertilizer), 10);`

This code moves the drone `North` 10 times and then uses fertilizer 10 times.
A lambda can also contain several statements in braces: `() => { Harvest(); Move(North); }`. This is how you tell a new drone what to do (see the Megafarm unlock).
