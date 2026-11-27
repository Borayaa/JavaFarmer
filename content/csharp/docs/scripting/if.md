# If
You can use `if`, `else if` and `else` to run code conditionally.

In the examples on this page, `condition`, `condition1` and `condition2` stand for any condition, that is any value that is `true` or `false`. For example:

`bool condition = CanHarvest();
bool condition1 = CanHarvest();
bool condition2 = !CanHarvest();`

`if (condition1)
{
	DoAFlip();
}
else if (condition2)
{
	Harvest();
}
else
{
	DoAFlip();
	Harvest();
}`

## Syntax
`if`s allow you to run code only if some condition is `true`. They are like a `while` loop that doesn't loop.
The `if` takes a condition in parentheses just like the `while` loop and executes the code block if the condition evaluates to `true`:

`// do a flip if condition is true
if (condition)
{
	DoAFlip();
}`

You can also add an `else` after the if that defines code to be executed if the condition evaluates to `false`.

Do a flip if `condition` is true, otherwise harvest.
`if (condition)
{
	DoAFlip();
}
else
{
	Harvest();
}`

You can put another `if` inside the `else` block:

`if (condition1)
{
	// a
}
else
{
	if (condition2)
	{
		// b
	}
	else
	{
		// c
	}
}`

This can be shortened by writing `else if`:

`if (condition1)
{
	// a
}
else if (condition2)
{
	// b
}
else
{
	// c
}`

You can add as many `else if` blocks as you want. Only the first block whose condition is `true` runs.
