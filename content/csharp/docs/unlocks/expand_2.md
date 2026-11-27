# Expand 2
Your farm has expanded again! Now the tiles are no longer in a nice row, so you need to find a way to traverse a square grid.

With the `while` loop this is not possible until you unlock senses and operators.
It is time to introduce the `for` loop.

You can read all about the `for` loop on the [For Loop](docs/scripting/for.md) page, but for now you will only need it to repeat code a fixed number of times.

`// do 5 flips
for (int i = 0; i < 5; i++)
{
	DoAFlip();
}`

The loop variable `i` starts at `0`. Before every iteration the loop checks the condition `i < 5`, and after every iteration `i++` increases `i` by one. So the loop body runs for `i` = `0, 1, 2, 3, 4`: `DoAFlip()` will be called `5` times.
You can use this kind of loop even before you have unlocked variables and operators.

The method `GetWorldSize()` is also available now. It returns the side length of your farm. This way you can write code that won't break with the next expand upgrade.

`for (int i = 0; i < GetWorldSize(); i++)
{
	Harvest();
	Move(North);
}`

This example harvests one column of the farm for any farm size.

If you're stuck on trying to figure out how to move the drone around the farm see the hint below.
<spoiler=show hint>There are, of course, several ways to move around the farm.
What we're looking for is a way to traverse it in a systematic way that won't break when the farm grows again.
A systematic way to get to every place on the farm would be to repeat the following 2 steps forever:

1.Move `North` until it wraps back.
2.Move `East`

`for (int i = 0; i < GetWorldSize(); i++)` may be helpful to turn this idea into code.
</spoiler>
<spoiler=show possible solution> The basic traversal might look like this:

`for (int i = 0; i < GetWorldSize(); i++)
{
	for (int j = 0; j < GetWorldSize(); j++)
	{
		// do a flip on every tile
		DoAFlip();
		Move(North);
	}
	Move(East);
}`
</spoiler>