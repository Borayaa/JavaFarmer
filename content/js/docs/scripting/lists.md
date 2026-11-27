# Arrays (Lists)
Arrays are an easy way to store multiple values in a single variable. (In Python they are called lists.)
You can create new arrays like this:

`const list = [2, true, Items.Hay];`

The array now contains the values `2`, `true` and `Items.Hay`.
An array can also be empty:

`const emptyList = [];`

You can access an element of an array by its index. The index is `0` for the first element, `1` for the second element, `2` for the third...

plants carrots
`const list = [Entities.Tree, Entities.Carrot, Entities.Pumpkin];
plant(list[1]);`

You can change an element the same way: `list[0] = Entities.Bush;`.

You can iterate over an array using a `for...of` loop. The following example sums all elements in the array.

`const list = [4, 7, 2, 5];
let sum = 0;
for (const number of list) {
	sum += number;
}`
`sum` is now `18`

The following array functions (called methods, because you write them after the array and a `.`) allow you to add and remove elements:

`list.push(elem)` adds an element to the end of the array:

`const list = [2, 6, 12];
list.push(7);`
`list` is now `[2, 6, 12, 7]`

`list.indexOf(elem)` gives the index of the first occurrence of an element (or `-1` if it's not there). Together with `list.splice(index, 1)` this removes the first occurrence of an element from an array:

`const list = [1, 2, 4, 2];
list.splice(list.indexOf(2), 1);`
`list` is now `[1, 4, 2]`

`list.splice(index, 0, elem)` inserts an element at the given index:

`const list = [Entities.Tree, Items.Hay];
list.splice(1, 0, Items.Wood);`
`list` is now `[Entities.Tree, Items.Wood, Items.Hay]`

`list.pop()` removes the last element and `list.splice(index, 1)` removes the element at the specified index.

`const list = [3, 5, 8, 25];
list.pop();
list.splice(1, 1);`
After `list.pop();`, `list` is `[3, 5, 8]`.
After `list.splice(1, 1);`, `list` is `[3, 8]`.

`list.shift()` removes the first element and `list.unshift(elem)` adds one at the front.

`list.includes(elem)` is `true` if the array contains the element.

`list.length` is the length of the array.
`const list = [3, 2, 1];
const x = list.length;`
`x` is now `3`

`new Array(n).fill(value)` creates an array with `n` copies of `value`, for example `new Array(5).fill(0)` is `[0, 0, 0, 0, 0]`.

Arrays have reference semantics. This means that assigning an array to a variable assigns the same array object to that variable, rather than making a copy of the array.
If two variables reference the same array, changes to the array will be seen by both.

`const a = [1, 2];
const b = a;
b.pop();`
`a` and `b` are now both `[1]`

To make a copy, write `[...a]`.

Note: JavaFarmer supports the methods above, but not the ones that take a function, like `map`, `filter` or `sort`. Use a `for...of` loop instead.