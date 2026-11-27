# Name Scopes
Scopes determine which variables can be accessed from where.

## Local variables
A variable declared inside a function is a local variable. It only exists inside the block `{ }` where it is declared, from the line of the declaration to the closing `}` of that block.

`int x = 1;
if (can_harvest()) {
	int y = 2;
	printf("%d\n", x + y);
}`

Writing `printf("%d\n", y);` after the closing `}` of the `if` is an error (`'y' undeclared`) because `y` only exists inside the block of the `if`.
This is also true for loops. The loop variable of a `for` loop only exists inside the loop. If you need the value after the loop, declare the variable before the loop:

`int i;
for (i = 0; i < 3; i++) {
	do_a_flip();
}
printf("%d\n", i);`

This prints `3`, because the loop stops as soon as `i < 3` is `false`.

You can't declare a variable twice in the same block. `int x = 2;` right after `int x = 1;` gives `redeclaration of 'x'`. To change the value, just assign it: `x = 2;`.

Every function has its own local variables, even if they have the same name. A local variable `x` in one function has nothing to do with a local variable `x` in another function. Parameters are local variables too.

## Global variables
A variable that is declared outside of all functions is a global variable. Every function in the window can read and change it.

`int counter = 0;

void f(void) {
	counter = 1;
}

int main(void) {
	f();
	printf("%d\n", counter);
}`

This prints `1`, because `f` changes the global variable `counter`.

Python needs a `global` keyword to change a global variable from inside a function. C doesn't have that keyword and doesn't need it: if a function assigns to a name that isn't a local variable, it changes the global variable.
But if you declare a local variable with the same name, the local variable hides the global one:

`int count = 0;

void g(void) {
	int count = 1;
}

int main(void) {
	g();
	printf("%d\n", count);
}`

This code prints `0` because the local `count` inside `g` is not the same variable as the global `count`, so the global variable remains unchanged. This is important because otherwise a function call could accidentally override a global variable that just happens to have the same name as a local variable of that function.

The starting value of a global variable must be a constant, like `0`, `true` or `"hello"`. To start it with the result of a function like `get_world_size()`, give it a simple value and set the real one in `main`.

Other windows can use your global variables too (see [Import](docs/scripting/import.md)). A global variable marked `static` stays private to its window.

Note that changing global variables from many functions is usually the first step towards spaghetti code, where every part of the program affects every other part of the program, so don't overuse it.
