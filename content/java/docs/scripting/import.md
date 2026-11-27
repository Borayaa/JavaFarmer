# Import
Putting all your code in a single file quickly becomes unmanageable. 
In Java every file is a class, and the name of the class is the file name without its extension (the code window `Utils.java` is the class `Utils`). You can call the methods of another file by writing the file name, a `.` and the method name.
This also works for files in other languages: `helpers.py` or `farm.rs` are used from Java as `helpers.someMethod()` or `farm.some_function()`, with the names written in that file. Values from Python, JavaScript and R files have no Java type: they are `Object`, so cast them, for example `(int) helpers.count()`.

For example, create a new code window, name it `Utils` and write this into it:

`public class Utils {
    static void plantRow(Entity plantType) {
        for (int i = 0; i < getWorldSize(); i++) {
            plant(plantType);
            move(East);
        }
    }
}`

The class name after `class` must be exactly the file name without `.java`, otherwise you get an error.
The methods are marked `static`. That means they belong to the class itself, so you can call them without creating anything first.

Now you can use it from any other file:

`void main() {
    Utils.plantRow(Entities.Bush);
}`

`Utils.plantRow(...)` calls the method `plantRow` in the file `Utils`.
Fields work the same way: if `Utils` declares `static int count = 0;`, other files can read it as `Utils.count`.

A file without a `main` method like `Utils` is a library. It can't be run on its own, it just provides methods for other files.

### No need to read further

## Import statements
In normal Java programs you often see lines like `import java.util.ArrayList;` at the top of a file. They make classes from other packages available.
In JavaFarmer the collections (`ArrayList`, `HashMap`, `HashSet`, `List`, `Map`, `Set`) are always available, so you can write `import java.util.*;` if you like, but you don't have to. You never need to import the files of your own farm, just use their name.

## Files with main
A file can also be written as a class with a `main` method. This is the form that most Java programs use:

`public class Farm {
    static int harvested = 0;

    public static void main(String[] args) {
        while (true) {
            if (canHarvest()) {
                harvest();
                harvested++;
            }
        }
    }
}`

The short form with only `void main() { ... }` that you have been using so far is called a "compact source file". It is the same thing with less typing.

When a file is used by another file, its `main` method does not run. It only runs when you press the play button of that file.

# How it really works

## Loading files
The first time a file is used, the game loads it. This sets up all its fields with their starting values. The file is only loaded once, so all files that use `Utils` share the same `Utils.count` (within one drone, see the Megafarm unlock).

## Using each other
Two files can call each other's methods. That always works, because methods only run when they are called.

file `A`:
`public class A {
    static int x = 0;

    static void printB() {
        System.out.println(B.y);
    }
}`

file `B`:
`public class B {
    static int y = 1;

    static void printA() {
        System.out.println(A.x);
    }
}`

Problems can only appear if the starting value of a field depends on a field of another file that is still being loaded. For example `static int z = A.x + 1;` in `B` while `A` is loading `B`. If you get strange errors that a field has never been defined, give the field a simple starting value and set the real value in a method instead.
