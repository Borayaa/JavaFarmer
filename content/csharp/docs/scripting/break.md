# Break
`break` allows stopping a loop early. When the `break;` statement is reached it will immediately exit the innermost loop and start running the code after the loop.

`while (true)
{
	if (CanHarvest())
	{
		break;
	}
}
Harvest();`

This code runs the `while` loop until `CanHarvest()` is `true` and then harvests. 
It has the same effect as

`while (!CanHarvest())
{
}
Harvest();`

<unlock=for>It also works on `for` loops.

`for (int i = 0; i < 10; i++)
{
	if (i == 3)
	{
		break;
	}
	Console.WriteLine(i);
}`
This prints `0`, `1` and `2`. When `i` reaches `3` the `break` ends the loop.

In nested loops `break` always exits the innermost loop.

`for (int i = 0; i < 10; i++)
{
	for (int j = 0; j < 10; j++)
	{
		if (j == 0)
		{
			break;
		}
		Console.WriteLine("this is never printed");
	}
	Console.WriteLine("this is printed 10 times");
}`

</unlock>Code directly after a `break;` in the same block could never run. C# warns about such `unreachable code`. That's why the examples put `break;` inside an `if`.
