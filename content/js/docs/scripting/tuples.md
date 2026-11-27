# Positions (Tuples)
In Python, tuples are used to combine several values into a single value, like an `(x, y)` position. JavaScript has no tuples. Instead, it uses arrays: a position is an array with two elements like `[x, y]`.

To create such an array, just write the values in square brackets, separated by commas:

`const pos = [getPosX(), getPosY()];`

You can also unpack them into several variables again. In the code below, the array `[1, 2]` is unpacked into two variables `a` and `b`. This is called destructuring.

`const [a, b] = [1, 2];`

Arrays can be indexed like any [array](docs/scripting/lists.md):

`const pos = [1, 2];
console.log(pos[1]);`
prints `2`

The game itself also gives you positions this way: <unlock=mazes>`measure()` in a maze returns the position of the treasure as an array `[x, y]`, </unlock>and <unlock=polyculture>`getCompanion()` returns an array `[plant, [x, y]]` that you can destructure in one go:

`const companion = getCompanion();
if (companion !== null) {
	const [plant, [x, y]] = companion;
	console.log(plant, x, y);
}`

</unlock>The arrays that come from the game are fixed: you can read them, but changing an element like `pos[0] = 3;` throws an error.
<unlock=dicts>
Positions can be used as keys in a `Map` and as elements of a `Set`. Write the array directly inside the call, or keep it in a `const` that you only read:

`const d = new Map();
d.set([1, 2], Entities.Bush);
const home = [0, 0];
d.set(home, Entities.Grass);

console.log(d.get([1, 2]));`
prints `Entities.Bush`

A key must be one of these fixed arrays: an array you change (with `push()` or `p[0] = 3;`), an array in a `let` variable or an array returned by your own function cannot be a key.
</unlock>
Arrays are also useful for returning multiple values from a function.

`function f() {
	return [1, 2];
}

const [a, b] = f();`