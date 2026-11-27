# Set (Sets)
A `Set` is like a [Map](docs/scripting/dicts.md), but without values. It's just an unordered set of keys. 

You can create one from an array of elements:
`const directions = new Set([North, East, West]);`

Use `new Set()` to create an empty set.

Use `set.add(elem);` to add a new element to the set.

Use `set.delete(elem);` to remove an element from a set.

Use `if (set.has(elem))` to check if the set contains an element.

Use `for (const elem of set)` to iterate all elements in the set.
For larger sets `has` performs much faster than `includes` would on an array.

`set.size` is the number of elements.

`const seen = new Set();
seen.add([getPosX(), getPosY()]);
if (seen.has([0, 0])) {
	console.log("been here");
}
for (const elem of seen) {
	console.log(elem);
}`

Positions can be stored in a set like this: write the array `[x, y]` directly inside `add` and `has`, then equal positions count as the same element.

Just like maps, sets are unordered, so there are no guarantees about the order in which the elements are iterated (unlike in real JavaScript, a set in JavaFarmer doesn't remember the order in which the elements were added).

Also, elements in sets are unique, so adding an element that is already in the set will not change the set.