# Lists
Lists and arrays are an easy way to store multiple values in a single variable.

## Lists
You can create new lists like this:

`List<int> numbers = [2, 6, 12];`

The list now contains the values `2`, `6` and `12`. The type `List<int>` means "list of `int`": all elements of this list are `int`s. A list of plants is a `List<Entity>`, a list of items a `List<Item>` and so on.
`[2, 6, 12]` is a collection expression. You can also write `new List<int> { 2, 6, 12 }`.
A list can also be empty:

`List<int> emptyList = [];`

If a list should hold values of different types, use `List<object>`: `List<object> things = [2, true, Items.Hay];`

You can access an element of a list by its index. The index is `0` for the first element, `1` for the second element, `2` for the third...

plants carrots
`List<Entity> plants = [Entities.Tree, Entities.Carrot, Entities.Pumpkin];
Plant(plants[1]);`

You can also change an element:
`numbers[0] = 5;`

`list[^1]` is the last element, `list[^2]` the second to last and so on.

You can iterate over a list using a `foreach` loop. The following example sums all elements in the list.

`List<int> values = [4, 7, 2, 5];
int sum = 0;
foreach (int number in values)
{
	sum += number;
}`
`sum` is now `18`

The following list methods allow you to add and remove elements:

`list.Add(elem);` adds an element to the end of the list:

`List<int> list = [2, 6, 12];
list.Add(7);`
`list` is now `[2, 6, 12, 7]`

`list.Remove(elem);` removes the first occurrence of an element from a list:

`List<int> list = [1, 2, 4, 2];
list.Remove(2);`
`list` is now `[1, 4, 2]`

`list.Insert(index, elem);` inserts an element at the given index:

`List<Item> list = [Items.Hay, Items.Carrot];
list.Insert(1, Items.Wood);`
`list` is now `[Items.Hay, Items.Wood, Items.Carrot]`

`list.RemoveAt(index);` removes the element at the specified index. To remove the last element, use the index `list.Count - 1`.

`List<int> list = [3, 5, 8, 25];
list.RemoveAt(list.Count - 1); // list is now [3, 5, 8]
list.RemoveAt(1); // list is now [3, 8]`

`list.Count` is the number of elements in the list.
`List<int> list = [3, 2, 1];
int x = list.Count;`
`x` is now `3`

`list.Contains(elem)` returns `true` if the element is in the list.
`list.IndexOf(elem)` returns the index of the first occurrence of the element, or `-1` if it isn't in the list.
`list.Clear();` removes all elements.

## Arrays
An array holds a fixed number of values. You can create a new array like this:

`int[] counts = new int[10];`

All its elements start with a default value (`0` for numbers, `false` for `bool`s and `null` for everything else). The type `int[]` means "array of `int`". You can also create an array with values: `int[] primes = [2, 3, 5];`

Arrays work like lists, but they can't grow or shrink, and `array.Length` is the number of elements:

`int[] squares = [4, 7, 2, 5];
for (int i = 0; i < squares.Length; i++)
{
	squares[i] += 1;
}`

Arrays can have two dimensions. This is handy to store something for every tile of the farm:

`int size = GetWorldSize();
bool[,] watered = new bool[size, size];
watered[GetPosX(), GetPosY()] = true;`

`watered.GetLength(0)` is the size of the first dimension and `watered.GetLength(1)` the size of the second one.

## References
Lists and arrays have reference semantics. This means that assigning a list to a variable assigns the same list object to that variable, rather than making a copy of the list.
If two variables reference the same list, changes to the list will be seen by both.

`List<int> a = [1, 2];
List<int> b = a;
b.RemoveAt(b.Count - 1);`
`a` and `b` are now both `[1]`

To get a copy, create a new list from the old one: `List<int> c = new List<int>(a);`
