# Tuples
Tuples are a great way to combine multiple values into a single value.
To create a tuple, write the values in parentheses, separated by commas:

`(int, int) tuple = (1, 2);`

The type `(int, int)` means "a tuple of two `int`s".

You can also unpack them into several variables again. This is called deconstruction. In the code below, the tuple `(1, 2)` is unpacked into two variables `a` and `b`.

`var (a, b) = (1, 2);`

Deconstruction can also swap two variables in one line: `(a, b) = (b, a);`

You read the elements of a tuple with `Item1`, `Item2` and so on. In JavaFarmer you can't change the elements of a tuple after creation, create a new tuple instead.

`(int, int) pair = (1, 2);
Console.WriteLine(pair.Item2);`
prints `2`

## Position and Companion
The game also gives you two small types that hold a fixed set of values: `Position` and `Companion`.

A `Position` holds an x and a y coordinate: `p.X` and `p.Y`. You can deconstruct it like a tuple: `var (x, y) = p;`

<unlock=mazes>For example, `Measure()` in a maze returns the `Position` of the treasure. `Measure()` can return different kinds of values, so its return type is `object` and you have to tell C# that it's a `Position` with a cast:

`var (treasureX, treasureY) = (Position)Measure();`

</unlock>A `Companion` is what `GetCompanion()` returns. It holds an entity and a position: `c.Entity` is the type of plant, `c.Position` is the `Position`, and `c.X` and `c.Y` are shortcuts for `c.Position.X` and `c.Position.Y`.

<unlock=polyculture>`Companion? companion = GetCompanion();
if (companion is not null)
{
	var (plant, (x, y)) = companion;
	Console.WriteLine($"{plant} at {x}, {y}");
}`

</unlock><unlock=dicts>
Like lists, tuples can be used as elements of a `HashSet`, and unlike lists they can be used as keys in dictionaries. Two tuples are equal if their elements are equal.

`Dictionary<(int, int), (int, int)> d = new Dictionary<(int, int), (int, int)>
{
	[(1, 2)] = (4, 5)
};
Console.WriteLine(d[(1, 2)]);`
prints `(4, 5)`</unlock>

They can also be useful for returning multiple values from a method.

`var (posX, posY) = CurrentPosition();

(int, int) CurrentPosition()
{
	return (GetPosX(), GetPosY());
}`
