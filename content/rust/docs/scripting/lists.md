# Vec (Lists)
A `Vec` (short for vector) is an easy way to store multiple values in a single variable. (In Python this is called a list.)
You can create a new `Vec` like this:

`let list = Vec::from([2, 6, 12]);`

The list now contains the values `2`, `6` and `12`. All elements of a `Vec` have the same type. This one is a `Vec<i32>`, a list of whole numbers.
(Most Rust code uses the vec macro for this. It does the same thing as `Vec::from`.)

A list can also be empty. Then Rust can't see the type of the elements, so you write it yourself:

`let mut empty_list: Vec<i32> = Vec::new();`

To change a list later, declare it with `let mut`.

You can access an element of a list by its index. The index is `0` for the first element, `1` for the second element, `2` for the third...

plants carrots
`let plants = Vec::from([Entity::Tree, Entity::Carrot, Entity::Pumpkin]);
plant(plants[1]);`

An index must be a `usize`. If your index is an `i32`, like a position, convert it: `plants[x as usize]`.

You can also change an element of a mutable list: `list[0] = 5;`

`list.len()` returns the number of elements in the list. It is a `usize` too, write `list.len() as i32` to compare it with an `i32`.

Lists can contain lists. This is handy to store something for every tile of the farm:

`let size = get_world_size();
let mut watered = Vec::new();
for _ in 0..size {
	let mut column = Vec::new();
	for _ in 0..size {
		column.push(false);
	}
	watered.push(column);
}
watered[get_pos_x() as usize][get_pos_y() as usize] = true;`

## Iterating
You can iterate over a list using a `for` loop. The following example sums all elements in the list.

`let list = Vec::from([4, 7, 2, 5]);
let mut sum = 0;
for number in &list {
	sum += number;
}`
`sum` is now `18`

The `&` means that the loop only borrows the list: it looks at the elements without using up the list, so you can still use `list` after the loop.

If you also need the index, loop over the indices:

`let mut numbers = Vec::from([4, 7, 2, 5]);
for i in 0..numbers.len() {
	numbers[i] += 1;
}`

## Adding and removing
The following methods allow you to add and remove elements. They change the list, so it must be declared with `let mut`.

`list.push(elem)` adds an element to the end of the list:

`let mut list = Vec::from([2, 6, 12]);
list.push(7);`
`list` is now `[2, 6, 12, 7]`

`list.insert(index, elem)` inserts an element at the given index:

`let mut list = Vec::from([Item::Hay, Item::Carrot]);
list.insert(1, Item::Wood);`
`list` is now `[Hay, Wood, Carrot]`

`list.pop()` removes the last element and returns it as an `Option` (`None` if the list is empty).
`list.remove(index)` removes the element at the given index and returns it.

`let mut list = Vec::from([3, 5, 8, 25]);
list.pop();`
`list` is now `[3, 5, 8]`
`list.remove(1);`
`list` is now `[3, 8]`

`list.remove_value(&elem)` removes the first occurrence of an element from a list:

`let mut list = Vec::from([Entity::Bush, Entity::Tree, Entity::Bush]);
list.remove_value(&Entity::Bush);`
`list` is now `[Tree, Bush]`

`list.contains(&elem)` returns `true` if the element is in the list.
`list.index_of(&elem)` returns the index of the first occurrence of the element, or `-1` if it isn't in the list.
`list.is_empty()` returns `true` if the list has no elements.
(`remove_value` and `index_of` are helpers of JavaFarmer, they are not part of Rust's standard library.)

## Moving and borrowing
In Rust a list has exactly one owner. Assigning a list to another variable moves it there, it does not make a copy:

`let a = Vec::from([1, 2]);
let mut b = a;
b.pop();`
`b` is now `[1]` and `a` can't be used anymore (Rust reports `borrow of moved value`).

To get a copy, use `clone()`: `let c = a.clone();` gives you a new list with the same elements.

To let a function change your list, pass it with `&mut`. The function then works on the same list, not on a copy:

`fn add_seven(list: &mut Vec<i32>) {
	list.push(7);
}

let mut numbers = Vec::from([1, 2]);
add_seven(&mut numbers);`
`numbers` is now `[1, 2, 7]`
A function that only reads the list takes `&Vec<i32>` and is called with `&numbers`.
