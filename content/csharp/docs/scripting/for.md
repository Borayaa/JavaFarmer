# For Loop
C# has two kinds of loops for this: the `for` loop that counts, and the `foreach` loop that goes through every element of a collection.

Similar to the `while` loop, both repeatedly run a block of code.

## The for loop
`for (int i = 0; i < 5; i++)
{
	Harvest();
}`

The parentheses contain three parts, separated by semicolons:
`int i = 0` runs once before the loop starts. It declares the loop variable `i`.
`i < 5` is the condition. It is checked before each iteration, just like in a `while` loop.
`i++` runs after each iteration. It increases `i` by one.

So this loop executes the body a fixed number of times. It is essentially the same as writing

`int i = 0;
while (i < 5)
{
	Harvest();
	i++;
}`

which calls `Harvest()` 5 times, with `i` being `0, 1, 2, 3` and `4`.

`i` can be any name you choose. You can also count in other ways:

`// 2, 4, 6, 8
for (int n = 2; n < 10; n += 2)
{
	Console.WriteLine(n);
}

// 10, 9, 8, ..., 1
for (int n = 10; n > 0; n--)
{
	Console.WriteLine(n);
}`

## The foreach loop
The `foreach` loop executes the loop body once for each element in a collection.

`foreach (Type variableName in collection)
{
	// code block
}`

`variableName` can be any name you choose. It is a variable that stores the current element of the collection, and `Type` is the type of the elements (or simply `var`). `collection` needs to be something that can be iterated, like an array or a list. The code block is executed for every element with the loop variable assigned to that element.

## Collections
<unlock=lists>[Lists and Arrays](docs/scripting/lists.md)      </unlock><unlock=functions>[Tuples](docs/scripting/tuples.md)      </unlock><unlock=dicts>[Dictionaries](docs/scripting/dicts.md)      </unlock><unlock=sets>[HashSet (Sets)](docs/scripting/sets.md)</unlock>

<unlock=lists>
## Example
`int[] numbers = [4, 7, 2, 5];
int sum = 0;
foreach (int number in numbers)
{
	sum += number;
}`
`sum` is now `18`.
</unlock>
See also [Break](docs/scripting/break.md) and [Continue](docs/scripting/continue.md)
