# Variables
Variables can be thought of as named containers that can hold a value.
In Java, every variable has a type that says what kind of value it can hold. You declare a variable by writing its type, its name, `=` and the value to store in it:

`type variableName = value;`

The left hand side is the type and the variable name. You can give it any name you want. In Java, variable names are usually written in "camelCase": start with a lowercase letter and start every following word with an uppercase letter, like `plantCount`.
The right hand side is an expression whose resulting value will be stored in the variable.

Declare a variable named `a` that holds a whole number and store the value `5` in it:
`int a = 5;`
Declare a variable named `b` and store the return value of `canHarvest()` in it:
`boolean b = canHarvest();`

## Types
These are the types you will use most:

`int` is a whole number like `5`, `0` or `-3`.
`double` is a number with a decimal point like `0.25` or `2.0`.
`boolean` is `true` or `false`.
`String` is a piece of text in double quotes like `"hello"`.

The game has its own types too:
`Entity` for things like `Entities.Bush`, `Item` for things like `Items.Hay`, `Ground` for things like `Grounds.Soil` and `Direction` for `North`, `East`, `South` and `West`.

`int count = 0;
double water = 0.25;
boolean ready = canHarvest();
String greeting = "hello";
Entity nextPlant = Entities.Bush;
Direction dir = North;`

The type protects you from mistakes. If you try to put a value of the wrong type into a variable, the code editor shows an error before the program even runs:

`int a = "hello";`
gives the error `incompatible types: String cannot be converted to int`.

An `int` can always be stored in a `double`, but a `double` can't be stored in an `int` without a cast, because the decimal places would get lost:

`double d = 5;
int i = (int) 2.7;`
`d` is now `5.0` and `i` is now `2`. The cast `(int)` cuts off the decimal places.

If you don't want to write the type yourself, you can use `var` and Java will figure out the type from the value:
`var plantType = Entities.Carrot;`
`plantType` now has the type `Entity`.

## Assigning new values
Do not confuse the `=` operator with the `==` operator. 
The `==` operator checks whether two values are equal and returns `true` or `false`.
The `=` operator assigns the value on the right to the variable on the left.

You only write the type once, when you declare the variable. After that you can assign a new value without the type:

`int a = 5;
a = 7;`

After a variable has been assigned you can use it in the code to retrieve the value it contains.
Variables let you count with a while loop:

`int a = 5;
int i = 0;
while (i < a) {
    doAFlip();
    i = i + 1;
}`

The above loop is executed 5 times because `a` is set to `5`.
Note that to increment `i`, we set it to be its own value plus `1`. Changing the value of a variable based on its previous value is something very common. 
It can be abbreviated using these operators: `+=, -=, *=, /=, %=`

`i = i + 1;` is the same as `i += 1;`
`a = a / 3;` is the same as `a /= 3;`

Adding or subtracting `1` is so common that it has its own even shorter form:

`i++;` is the same as `i += 1;`
`i--;` is the same as `i -= 1;`

In JavaFarmer, `i++` and `i--` can only be used as statements on their own, not inside a bigger expression.

A variable can only be used inside the block (the `{ }`) where it was declared. See [Scopes](docs/scripting/scopes.md) for more.

## Constants
If you add `final` in front of a declaration, the variable can't be changed anymore:
`final int maxFlips = 3;`
Assigning a new value to `maxFlips` is now an error.
