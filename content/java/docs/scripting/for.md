# For Loop
Java has two kinds of `for` loops: the classic `for` loop that counts, and the enhanced `for` loop that goes through every element of a collection (called a foreach loop in some languages).

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
    System.out.println(i);
}

// 10, 9, 8, ..., 1
for (int i = 10; i > 0; i--) {
    System.out.println(i);
}`

## The enhanced for loop
The enhanced `for` loop executes the loop body once for each element in a collection.

`for (Type variableName : collection) {
    // code block
}`

`variableName` can be any name you choose. It is a variable that stores the current element of the collection, and `Type` is the type of the elements. `collection` needs to be something that can be iterated, like an array or a list. The code block is executed for every element with the loop variable assigned to that element.

## Collections
<unlock=lists>[Arrays and Lists](docs/scripting/lists.md)      </unlock><unlock=dicts>[HashMap](docs/scripting/dicts.md)      </unlock><unlock=sets>[HashSet](docs/scripting/sets.md)</unlock>

<unlock=lists>
## Example
`int[] numbers = new int[]{4, 7, 2, 5};
int sum = 0;
for (int number : numbers) {
    sum += number;
}`
`sum` is now `18`.
</unlock>
See also [Break](docs/scripting/break.md) and [Continue](docs/scripting/continue.md)
