# Arrays and Lists
Arrays and lists are an easy way to store multiple values in a single variable.

## Arrays
An array holds a fixed number of values of the same type. You can create a new array like this:

`int[] numbers = new int[]{2, 6, 12};`

The array now contains the values `2`, `6` and `12`. The type `int[]` means "array of `int`".
You can also create an array of a given length. All its elements start with a default value (`0` for numbers, `false` for booleans and `null` for everything else):

`int[] counts = new int[10];`

You can access an element of an array by its index. The index is `0` for the first element, `1` for the second element, `2` for the third...

plants carrots
`Entity[] plants = new Entity[]{Entities.Tree, Entities.Carrot, Entities.Pumpkin};
plant(plants[1]);`

You can also change an element:
`numbers[0] = 5;`

`numbers.length` is the number of elements in the array. The length of an array can never change.

Arrays can have more than one dimension. This is handy to store something for every tile of the farm:

`int size = getWorldSize();
boolean[][] watered = new boolean[size][size];
watered[getPosX()][getPosY()] = true;`

## Iterating
You can iterate over an array using a `for` loop. The following example sums all elements in the array.

`int[] numbers = new int[]{4, 7, 2, 5};
int sum = 0;
for (int number : numbers) {
    sum += number;
}`
`sum` is now `18`

If you also need the index, use a classic `for` loop:

`for (int i = 0; i < numbers.length; i++) {
    numbers[i] += 1;
}`

## ArrayList
An array can't grow or shrink. If you want to add and remove elements, use an `ArrayList` instead:

`ArrayList<Integer> list = new ArrayList<>();`

`ArrayList<Integer>` means "list of `Integer`". Lists can only hold objects, so you write `Integer` instead of `int`, `Double` instead of `double` and `Boolean` instead of `boolean`. Java converts between them automatically.
Game types are written as they are, like `ArrayList<Entity>`.

You can also create a list that already contains some values:

`ArrayList<Integer> list = new ArrayList<>(List.of(2, 6, 12));`

`List.of(2, 6, 12)` on its own creates a list that can't be changed, which is fine if you only want to read from it.

The following list methods allow you to read, add and remove elements:

`list.get(index)` returns the element at the given index, like `array[index]` does for arrays.
`list.set(index, elem);` replaces the element at the given index.
`list.size()` returns the number of elements in the list.

`list.add(elem)` adds an element to the end of the list:

`ArrayList<Integer> list = new ArrayList<>(List.of(2, 6, 12));
list.add(7);`
`list` is now `[2, 6, 12, 7]`

`list.add(index, elem)` inserts an element at the given index:

`ArrayList<Item> list = new ArrayList<>(List.of(Items.Hay, Items.Carrot));
list.add(1, Items.Wood);`
`list` is now `[Items.Hay, Items.Wood, Items.Carrot]`

`list.remove(index)` removes the element at the given index and returns it:

`ArrayList<Integer> list = new ArrayList<>(List.of(3, 5, 8, 25));
list.remove(list.size() - 1);`
`list` is now `[3, 5, 8]`
`list.remove(1);`
`list` is now `[3, 8]`

`list.remove(elem)` removes the first occurrence of an element from a list:

`ArrayList<Entity> list = new ArrayList<>(List.of(Entities.Bush, Entities.Tree, Entities.Bush));
list.remove(Entities.Bush);`
`list` is now `[Entities.Tree, Entities.Bush]`

Careful with lists of `Integer`: there `list.remove(2)` removes the element at index `2`, not the number `2`.

`list.contains(elem)` returns `true` if the element is in the list.
`list.indexOf(elem)` returns the index of the first occurrence of the element, or `-1` if it isn't in the list.
`list.isEmpty()` returns `true` if the list has no elements.

Lists can be used in a `for` loop just like arrays:

`ArrayList<Entity> plants = new ArrayList<>(List.of(Entities.Bush, Entities.Tree));
for (Entity p : plants) {
    plant(p);
    move(North);
}`

## References
Arrays and lists have reference semantics. This means that assigning a list to a variable assigns the same list object to that variable, rather than making a copy of the list.
If two variables reference the same list, changes to the list will be seen by both.

`ArrayList<Integer> a = new ArrayList<>(List.of(1, 2));
ArrayList<Integer> b = a;
b.remove(1);`
`a` and `b` are now both `[1]`

To get a copy, create a new list from the old one: `ArrayList<Integer> c = new ArrayList<>(a);`
