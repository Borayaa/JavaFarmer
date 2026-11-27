# For Loop
C has a `for` loop that counts. With the lists, sets and maps of `farm.h` you also get `for_each`, which goes through every element of a collection (called a foreach loop in some languages).

Similar to the `while` loop, both kinds of loop repeatedly run a block of code.

## The for loop
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
	printf("%d\n", i);
}

// 10, 9, 8, ..., 1
for (int i = 10; i > 0; i--) {
	printf("%d\n", i);
}`

## The for_each loop
`for_each` executes the loop body once for each element of a list, a set or a map.

`List(int) numbers = list_of(int, 4, 7, 2, 5);
for_each (int number, numbers) {
	printf("%d\n", number);
}`

Inside the parentheses you write the type and the name of the loop variable, then the collection. The variable stores the current element and can have any name you choose. The code block is executed for every element with the loop variable assigned to that element. For a map, the loop variable gets each key.

## Collections
<unlock=lists>[Lists and Arrays](docs/scripting/lists.md)      </unlock><unlock=dicts>[Maps](docs/scripting/dicts.md)      </unlock><unlock=sets>[Sets](docs/scripting/sets.md)</unlock>

<unlock=lists>
## Example
`int numbers[] = {4, 7, 2, 5};
int sum = 0;
for (int i = 0; i < 4; i++) {
	sum += numbers[i];
}`
`sum` is now `18`.
</unlock>
See also [Break](docs/scripting/break.md) and [Continue](docs/scripting/continue.md)
