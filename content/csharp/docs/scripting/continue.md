# Continue
`continue` allows stopping the current iteration of a loop and jumping to the next iteration of the innermost loop.

`while (true)
{
	if (!CanHarvest())
	{
		continue;
	}
	Harvest();
}`

This code only calls `Harvest()` when `CanHarvest()` is `true`. 
It has the same effect as

`while (true)
{
	if (CanHarvest())
	{
		Harvest();
	}
}`

<unlock=for>It also works on `for` loops.

`for (int i = 0; i < 10; i++)
{
	if (i % 2 == 0)
	{
		continue;
	}
	Console.WriteLine(i);
}`

This runs all `10` iterations of the loop, but the `Console.WriteLine(i);` statement is skipped for every even `i`. So it prints `1, 3, 5, 7, 9`.
In a `for` loop, `continue` still runs the update part (`i++`) before the next iteration.

In nested loops `continue` always affects the innermost loop.

`for (int i = 0; i < 10; i++)
{
	for (int j = 0; j < 10; j++)
	{
		Console.WriteLine("this is printed 100 times");
		if (j >= 0)
		{
			continue;
		}
		Console.WriteLine("this is never printed");
	}
	Console.WriteLine("this is printed 10 times");
}`
</unlock>
