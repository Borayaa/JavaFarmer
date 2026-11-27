# Variables
Variables can be thought of as named containers that can hold a value.
In JavaScript you declare a variable with `let` or `const`, its name, `=` and the value to store in it:

`let variableName = 0;`

The left hand side of the `=` is the variable name. You can give it any name you want. In JavaScript, variable names are usually written in "camelCase": start with a lowercase letter and start every following word with an uppercase letter, like `plantCount`.
The right hand side is an expression whose resulting value will be stored in the variable.

Declare a variable named `a` and store the value `5` in it:
`let a = 5;`
Declare a variable named `b` and store the return value of `canHarvest()` in it:
`const b = canHarvest();`

## let and const
`let` declares a variable that you can change later. `const` declares a variable that keeps its value forever: assigning a new value to it is an error (`Assignment to constant variable.`).

`let count = 0;
count = 1;
const size = getWorldSize();`

Use `const` whenever the value never changes, and `let` otherwise. You only write `let` or `const` once, when you declare the variable. After that you assign a new value without it.
(There is also the old `var`, which works like `let` but ignores blocks, see [Scopes](docs/scripting/scopes.md). You don't need it.)

Variables don't have a fixed type in JavaScript: the same variable can hold a number, a text, `true` or `false`, or a game value like `Entities.Bush`.

`let next = Entities.Bush;
next = Entities.Tree;
let ready = false;`

`let x;` without a value declares a variable that holds `null` until you assign something to it. Reading a variable that was never declared is an error.

## Assigning new values
Do not confuse the `=` operator with the `===` operator. 
The `===` operator checks whether two values are equal and returns `true` or `false`.
The `=` operator assigns the value on the right to the name on the left.

After a variable has been assigned you can use it in the code to retrieve the value it contains

`const a = 5;
for (let i = 0; i < a; i++) {
	doAFlip();
}`

The above loop is executed 5 times because `a` is set to `5`.
The `i` in the `for` loop is also a variable. It starts at `0` and is increased by `i++` after every iteration of the loop. (It doesn't have to be called `i`, you can give it any valid variable name.)

Variables also let you do the same thing with a while loop:

`const a = 5;
let i = 0;
while (i < a) {
	doAFlip();
	i = i + 1;
}`

This does the same thing as the for loop above. We just have to increment `i` manually.
Note that to increment `i`, we set it to be its own value plus `1`. Changing the value of a variable based on its previous value is something very common. 
It can be abbreviated using these operators: `+=, -=, *=, /=, %=, **=`

`i = i + 1;` is the same as `i += 1;`
`a = a / 3;` is the same as `a /= 3;`

Adding or subtracting `1` is so common that it has its own even shorter form:

`i++;` is the same as `i += 1;`
`i--;` is the same as `i -= 1;`

In JavaFarmer, assignments, `i++` and `i--` can only be used as statements on their own, not inside a bigger expression.