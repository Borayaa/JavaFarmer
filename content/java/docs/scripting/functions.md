# Functions
In Java, functions are called methods. Define a new method by writing its return type, its name and its parameters:
`void f(int arg1, boolean arg2) {
    // method code
}`

You can use the call operator `()` to call the method:
`f(42, false);`

Also see [Scopes](docs/scripting/scopes.md) to learn about local variables and fields.

## Introduction
You've already seen built-in methods like `harvest()`, and you have already written one method yourself: `main`.
You can also define your own methods which allows structuring your code in a modular way. It basically allows you to give a name to a block of code so you can call it from anywhere you want.

Methods are defined next to `main`, not inside it.

## Method Definitions
For example, you could define a method that moves the drone several times.

`void moveNDir(int n, Direction dir) {
    for (int i = 0; i < n; i++) {
        move(dir);
    }
}`

`void` means that the method does not return a value (see below).
`moveNDir` is the name of the method. This can be any valid name and will be used to call the method.
`n` and `dir` are parameters. They are variables that hold the values that are passed into the method (these values are also called arguments). Every parameter needs a type, just like a variable. You can add as many parameters to a method definition as you want, separated by commas.
After the parentheses comes the code block in `{ }` that will run when the method is called.

With the above definition the following program moves the drone `10` tiles `North` and `2` tiles `West`.

`void main() {
    moveNDir(10, North);
    moveNDir(2, West);
}

void moveNDir(int n, Direction dir) {
    for (int i = 0; i < n; i++) {
        move(dir);
    }
}`

Unlike variables, it doesn't matter in which order the methods are written in the file. `main` can call `moveNDir` even though `moveNDir` is defined below it.

If you call a method with the wrong number or types of arguments, the editor shows an error right away:
`moveNDir(North);`
gives `method moveNDir cannot be applied to given types`.

## Return Values
Use the `return` keyword to make a method return a value. Instead of `void`, write the type of the returned value in front of the method name.
For example, the following method defines the exclusive or operation. The exclusive or returns `true` if one value is `true` and the other one is `false`:

`boolean xor(boolean a, boolean b) {
    return a != b;
}`

`void main() {
    if (xor(true, false)) {
        doAFlip();
    }
}

boolean xor(boolean a, boolean b) {
    return a != b;
}`

A method with a return type must return a value on every path, otherwise you get the error `missing return statement`.

`return` ends the method immediately. In a `void` method you can use `return;` without a value to leave the method early.

A method can only return one value. If you need to return several values, see [Tuples](docs/scripting/tuples.md).

## Overloading
Java has no default arguments. A method that only needs a fixed value can call a more general method:

`void flips(int n) {
    for (int i = 0; i < n; i++) {
        doAFlip();
    }
}

void flipOnce() {
    flips(1);
}`

Now `flipOnce()` does one flip and `flips(3)` does three.
(Real Java also lets two methods share a name if their parameters differ. This is called overloading. JavaFarmer does not support it yet, so give each method its own name.)

## Advanced Method Usage
Sometimes you want to pass a piece of code to another method, for example to tell a new drone what to do (see the Megafarm unlock).
In Java this is done with a `Runnable`. You can create one with a lambda `() -> ...` or with a method reference `this::methodName` and run it with `.run()`:

`void main() {
    Runnable flip = () -> doAFlip();
    repeat(flip, 10);
    repeat(this::harvestNorth, 10);
}

void harvestNorth() {
    harvest();
    move(North);
}

void repeat(Runnable task, int times) {
    for (int i = 0; i < times; i++) {
        task.run();
    }
}`

This code does 10 flips and then harvests the next 10 tiles to the north.
If the code should also give back a value, use a `Supplier` instead: `Supplier<Entity> look = () -> getEntityType();` and call it with `look.get()`.
