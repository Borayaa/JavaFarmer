# For Loop
JavaScript has two kinds of `for` loops that you will use all the time: the classic `for` loop that counts, and the `for...of` loop that goes through every element of an array or another collection (called a foreach loop in some languages).

Similar to the `while` loop, both kinds of `for` loop repeatedly run a block of code.

## The classic for loop
`for (let i = 0; i < 5; i++) {
	harvest();
}`

The parentheses contain three parts, separated by semicolons:
`let i = 0` runs once before the loop starts. It declares the loop variable `i`.
`i < 5` is the condition. It is checked before each iteration, just like in a `while` loop.
`i++` runs after each iteration. It increases `i` by one.

So this loop executes the body a fixed number of times. It is essentially the same as writing

`let i = 0;
while (i < 5) {
	harvest();
	i++;
}`

which calls `harvest()` 5 times, with `i` being `0, 1, 2, 3` and `4`.

`i` can be any name you choose. You can also count in other ways:

`// 2, 4, 6, 8
for (let i = 2; i < 10; i += 2) {
	console.log(i);
}

// 10, 9, 8, ..., 1
for (let i = 10; i > 0; i--) {
	console.log(i);
}`

## The for...of loop
The `for...of` loop executes the loop body once for each element in a sequence.

`const sequence = [1, 2, 3];
for (const variableName of sequence) {
	// code block
}`

`variableName` can be any name you choose. It is a variable that stores the current element of the sequence. It is declared with `const` because it gets a new value in every iteration, but you don't change it yourself. `sequence` needs to be something that can be iterated, like an array, a text, a `Set` or the keys of a `Map`. The code block is executed for every element with the loop variable assigned to that element.

## Sequences
[Counting](functions/range)      <unlock=lists>[Arrays](docs/scripting/lists.md)      </unlock><unlock=functions>[Positions](docs/scripting/tuples.md)      </unlock><unlock=dicts>[Map](docs/scripting/dicts.md)      </unlock><unlock=sets>[Set](docs/scripting/sets.md)</unlock>

<unlock=lists>
## Example
`const numbers = [4, 7, 2, 5];
let sum = 0;
for (const number of numbers) {
	sum += number;
}`
`sum` is now `18`.

Careful: there is also a `for...in` loop. It goes through the keys of an object or a `Map`, not through the elements of an array. For arrays, always use `for...of`.
</unlock>
See also [Break](docs/scripting/break.md) and [Continue](docs/scripting/continue.md)