# Variables
Variables can be thought of as named containers that can hold a value.
In C, every variable has a type that says what kind of value it can hold. You declare a variable by writing its type, its name, `=` and the value to store in it:

`int variable_name = 5;`

The left hand side is the type and the variable name. You can give it any name you want. In C, variable names are usually written in lowercase with underscores between the words, like `plant_count`.
The right hand side is an expression whose resulting value will be stored in the variable.

Declare a variable named `a` that holds a whole number and store the value `5` in it:
`int a = 5;`
Declare a variable named `b` and store the return value of `can_harvest()` in it:
`bool b = can_harvest();`

## Types
These are the types you will use most:

`int` is a whole number like `5`, `0` or `-3`.
`double` is a number with a decimal point like `0.25` or `2.0`.
`bool` is `true` or `false`.
`string` is a piece of text in double quotes like `"hello"`. (`string` comes from `farm.h`. In other C programs you will see `const char *` instead, which means the same thing and works here too.)

The game has its own types too:
`Entity` for things like `Entities.Bush`, `Item` for things like `Items.Hay`, `Ground` for things like `Grounds.Soil` and `Direction` for `North`, `East`, `South` and `West`. A game value can also be `NULL`, which means "nothing", for example `get_entity_type()` on an empty tile.

`int count = 0;
double water = 0.25;
bool ready = can_harvest();
string greeting = "hello";
Entity next_plant = Entities.Bush;
Direction dir = North;`

The type protects you from mistakes. If you try to put a value of the wrong type into a variable, the code editor shows an error before the program even runs. For example `int a = "hello";` gives the error `incompatible types when initializing type 'int'`.

An `int` can always be stored in a `double`. A `double` stored in an `int` loses its decimal places, so write a cast to show that you mean it:

`double d = 5;
int i = (int) 2.7;`
`d` is now `5` and `i` is now `2`. The cast `(int)` cuts off the decimal places.

## Assigning new values
Do not confuse the `=` operator with the `==` operator. 
The `==` operator checks whether two values are equal and returns `true` or `false`.
The `=` operator assigns the value on the right to the variable on the left.

You only write the type once, when you declare the variable. After that you can assign a new value without the type:

`int n = 5;
n = 7;`

After a variable has been assigned you can use it in the code to retrieve the value it contains.
Variables let you count with a while loop:

`int limit = 5;
int k = 0;
while (k < limit) {
	do_a_flip();
	k = k + 1;
}`

The above loop is executed 5 times because `limit` is set to `5`.
Note that to increment `k`, we set it to be its own value plus `1`. Changing the value of a variable based on its previous value is something very common. 
It can be abbreviated using these operators: `+=, -=, *=, /=, %=`

`k = k + 1;` is the same as `k += 1;`
`n = n / 3;` is the same as `n /= 3;`

Adding or subtracting `1` is so common that it has its own even shorter form:

`k++;` is the same as `k += 1;`
`k--;` is the same as `k -= 1;`

In JavaFarmer, `k++` and `k--` can only be used as statements on their own, not inside a bigger expression.

A variable can only be used inside the block (the `{ }`) where it was declared. See [Scopes](docs/scripting/scopes.md) for more.

## Constants
If you add `const` in front of a declaration, the variable can't be changed anymore:
`const int max_flips = 3;`
Assigning a new value to `max_flips` is now an error.
