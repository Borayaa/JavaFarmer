# Name Scopes
Scopes determine which variables can be accessed from where.

## Local variables
A variable declared inside a method is a local variable. It only exists inside the block `{ }` where it is declared, from the line of the declaration to the closing `}` of that block.

`void main() {
    int x = 1;
    if (canHarvest()) {
        int y = 2;
        System.out.println(x + y);
    }
    System.out.println(y);
}`

The last line is an error (`cannot find symbol: variable y`) because `y` only exists inside the block of the `if`.
This is also true for loops. The loop variable of a `for` loop only exists inside the loop:

`for (int i = 0; i < 3; i++) {
    doAFlip();
}
System.out.println(i);`
This is an error too. If you need the value after the loop, declare the variable before the loop.

You can't declare a variable twice in the same method while it's still in scope:
`int x = 1;
int x = 2;`
gives `variable x is already defined in method main()`. To change the value, just assign it: `x = 2;`.

Every method has its own local variables, even if they have the same name. A local variable `x` in one method has nothing to do with a local variable `x` in another method. Parameters are local variables too.

## Fields
A variable that is declared outside of all methods is called a field. Every method in the file can read and change it.

`int x = 0;

void main() {
    f();
    System.out.println(x);
}

void f() {
    x = 1;
}`

This prints `1`, because `f` changes the field `x`.

Python needs a `global` keyword to change a global variable from inside a function. Java doesn't have that keyword and doesn't need it: if a method assigns to a name that isn't a local variable, it changes the field.
But if you declare a local variable with the same name, the local variable hides the field:

`int x = 0;

void main() {
    f();
    System.out.println(x);
}

void f() {
    int x = 1;
}`

This code prints `0` because the local `x` inside `f` is not the same variable as the field `x`, so the field remains unchanged.

If you write your file as a class (see [Import](docs/scripting/import.md)), fields and methods are marked `static`:

`public class Counter {
    static int count = 0;

    static void increment() {
        count++;
    }
}`

Note that changing fields from many methods is usually the first step towards spaghetti code, where every part of the program affects every other part of the program, so don't overuse it.
