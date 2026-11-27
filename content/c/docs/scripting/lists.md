# Lists and Arrays
Arrays and lists are an easy way to store multiple values in a single variable.

## Arrays
An array holds a fixed number of values of the same type. You can create a new array like this:

`int numbers[] = {2, 6, 12};`

The array now contains the values `2`, `6` and `12`. The brackets `[]` after the name mean "array of `int`".
You can also create an array of a given length. All its elements start at `0` (`false` for bools and `NULL` for game values):

`int counts[10] = {0};`

You can access an element of an array by its index. The index is `0` for the first element, `1` for the second element, `2` for the third...

plants carrots
`Entity plants[] = {Entities.Tree, Entities.Carrot, Entities.Pumpkin};
plant(plants[1]);`

You can also change an element:
`numbers[0] = 5;`

The length of an array can never change. `sizeof(numbers) / sizeof(numbers[0])` is the number of elements in the array.

Arrays can have more than one dimension. This is handy to store something for every tile of the farm:

`int size = get_world_size();
bool watered[size][size];
watered[get_pos_x()][get_pos_y()] = true;`

## Iterating
You can iterate over an array using a `for` loop. The following example sums all elements in the array.

`int values[] = {4, 7, 2, 5};
int sum = 0;
for (int i = 0; i < 4; i++) {
	sum += values[i];
}`
`sum` is now `18`

## Lists
An array can't grow or shrink. If you want to add and remove elements, use a list from `farm.h` instead. `List(T)` is a list of elements of type `T`:

`List(int) empty_list = list_new(int);`

A list holds values of one type only, like `List(int)`, `List(double)`, `List(string)` or `List(Entity)`.
You can also create a list that already contains some values:

`List(int) list = list_of(int, 2, 6, 12);`

The following functions allow you to read, add and remove elements:

`list_get(list, index)` returns the element at the given index. A negative index counts from the end: `list_get(list, -1)` is the last element.
`list_set(list, index, elem)` replaces the element at the given index.
`list_len(list)` returns the number of elements in the list.

`list_add(list, elem)` adds an element to the end of the list:

`list_add(list, 7);`
`list` is now `{2, 6, 12, 7}`

`list_remove(list, elem)` removes the first occurrence of an element from a list:

`List(int) twos = list_of(int, 1, 2, 4, 2);
list_remove(twos, 2);`
`twos` is now `{1, 4, 2}`

`list_insert(list, index, elem)` inserts an element at the given index:

`List(Item) items = list_of(Item, Items.Hay, Items.Carrot);
list_insert(items, 1, Items.Wood);`
`items` is now `{Items.Hay, Items.Wood, Items.Carrot}`

`list_pop(list)` removes the last element and returns it. `list_remove_at(list, index)` removes the element at the given index and returns it.

`List(int) nums = list_of(int, 3, 5, 8, 25);
list_pop(nums);`
`nums` is now `{3, 5, 8}`
`list_remove_at(nums, 1);`
`nums` is now `{3, 8}`

`list_contains(list, elem)` returns `true` if the element is in the list.
`list_index_of(list, elem)` returns the index of the first occurrence of the element, or `-1` if it isn't in the list.
`list_is_empty(list)` returns `true` if the list has no elements.

Lists can be used in a `for_each` loop:

`List(Entity) crops = list_of(Entity, Entities.Bush, Entities.Tree);
for_each (Entity p, crops) {
	plant(p);
	move(North);
}`

A list of lists is not possible. Use a two-dimensional array instead.

## References
Lists have reference semantics. This means that assigning a list to a variable assigns the same list to that variable, rather than making a copy of the list.
If two variables reference the same list, changes to the list will be seen by both.

`List(int) a = list_of(int, 1, 2);
List(int) b = a;
list_pop(b);`
`a` and `b` are now both `{1}`

To get a copy, use `list_copy`: `List(int) c = list_copy(a);`
