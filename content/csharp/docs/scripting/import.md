# Import
Putting all your code in a single file quickly becomes unmanageable. 
In C# you can put methods into a class in another code window and call them from your program by writing the class name, a `.` and the method name. There are no import statements for this: the name of the class is enough.

For example, create a new code window, name it `Utils` and write this into it:

`public static class Utils
{
	public static int Count = 0;

	public static void PlantRow(Entity plantType)
	{
		for (int i = 0; i < GetWorldSize(); i++)
		{
			Plant(plantType);
			Move(East);
		}
	}
}`

The class name after `class` must be exactly the window name (the name displayed at the top of the code window), otherwise you get an error.
The class and its members are marked `static`. That means they belong to the class itself, so you can call them without creating anything first. Only static fields and methods are allowed in such a class.
Members that other windows use must be marked `public`. Without it they are private to the class, and using them from another window gives the error `is inaccessible due to its protection level`.

Now you can use it from any other window:

`Utils.PlantRow(Entities.Bush);`

`Utils.PlantRow(...)` calls the method `PlantRow` in the window `Utils`.
Fields work the same way: other windows can read and change `Count` as `Utils.Count`.

A window that only contains a static class like `Utils` is a library. It can't be run on its own, it just provides methods for other windows.
A window with top-level statements is a program. It cannot be used by other windows, because it has no class name. So keep the methods you want to share in a library window.

### No need to read further

## Using directives
In normal C# programs you often see lines like `using System.Collections.Generic;` at the top of a file. They make classes from other namespaces available.
In JavaFarmer, `Console`, `Math` and the collections (`List`, `Dictionary`, `HashSet`) are always available, and so are the game's methods and constants. So you can write `using System;`, `using System.Collections.Generic;`, `using static System.Math;` or `using static Farm;` if you like, but you don't have to. Other `using` lines and namespaces are not supported. You never need a `using` line for the windows of your own farm, just use their class name.

## Programs with a class
A program can also be written as a class with a `Main` method. This is the form that most older C# programs use:

`class Program
{
	static int harvested = 0;

	static void Main()
	{
		while (true)
		{
			if (CanHarvest())
			{
				Harvest();
				harvested++;
			}
		}
	}
}`

The short form with top-level statements that you have been using so far does the same thing with less typing.

If another window uses a class that has a `Main` method, its `Main` method does not run. It only runs when you press the play button of that window.

# How it really works

## Loading windows
The first time a window is used, the game loads it. This sets up all its static fields with their starting values. A window is only loaded once, so all windows that use `Utils` share the same `Utils.Count` (within one drone, see the Megafarm unlock).

## Using each other
Two windows can call each other's methods. That always works, because methods only run when they are called.

window `A`:
`public static class A
{
	public static int X = 0;

	public static void PrintB()
	{
		Console.WriteLine(B.Y);
	}
}`

window `B`:
`public static class B
{
	public static int Y = 1;

	public static void PrintA()
	{
		Console.WriteLine(A.X);
	}
}`

Problems can only appear if the starting value of a field depends on a field of another window that is still being loaded. For example `public static int Z = A.X + 1;` in `B` while `A` is loading `B`. If you get strange errors that a field has never been defined, give the field a simple starting value and set the real value in a method instead.
