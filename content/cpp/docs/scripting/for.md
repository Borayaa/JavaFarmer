# For Loop
C++ has two kinds of `for` loops: the classic `for` loop that counts, and the range-for loop that goes through every element of a collection (called a foreach loop in some languages).

Similar to the `while` loop, both kinds of `for` loop repeatedly run a block of code.

## The classic for loop
`for (int i = 0; i < 5; i++) {
	harvest();
}`

The parentheses contain three parts, separated by semicolons:
`int i = 0` runs once before the loop starts. It declares the loop variable `i`.
`i < 5` is the condition. It is checked before each iteration, just like in a `while` loop.
`i++` runs after each iteration. It increases `i` by one.

So this loop executes the body a fixed number of times. It is essentially the same as writing

`int i = 0;
while (i < 5) {
	harvest();
	i++;
}`

which calls `harvest()` 5 times, with `i` being `0, 1, 2, 3` and `4`.

`i` can be any name you choose. You can also count in other ways:

`// 2, 4, 6, 8
for (int i = 2; i < 10; i += 2) {
	std::cout << i << std::endl;
}

// 10, 9, 8, ..., 1
for (int i = 10; i > 0; i--) {
	std::cout << i << std::endl;
}`

## The range-for loop
The range-for loop executes the loop body once for each element in a collection.

`std::vector<int> numbers = {4, 7, 2, 5};
for (int number : numbers) {
	// code block
}`

`number` can be any name you choose. It is a variable that stores the current element of the collection, and `int` is the type of the elements. You can also write `auto` and let C++ find the type: `for (auto number : numbers)`. The collection needs to be something that can be iterated, like a `std::vector`, a `std::map` or a `std::set`. The code block is executed for every element with the loop variable assigned to that element.

## Collections
<unlock=lists>[std::vector (Lists)](docs/scripting/lists.md)      </unlock><unlock=functions>[Position (Tuples)](docs/scripting/tuples.md)      </unlock><unlock=dicts>[std::map](docs/scripting/dicts.md)      </unlock><unlock=sets>[std::set](docs/scripting/sets.md)</unlock>

<unlock=lists>
## Example
`std::vector<int> values = {4, 7, 2, 5};
int sum = 0;
for (int value : values) {
	sum += value;
}`
`sum` is now `18`.
</unlock>
See also [Break](docs/scripting/break.md) and [Continue](docs/scripting/continue.md)
