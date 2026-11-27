# Other Files (Import)
Putting all your code in a single file quickly becomes unmanageable. 
In JavaScript every file is a module. `import` statements allow you to use functions and global variables from another file, as long as that file `export`s them.
The module of a code window is named after the window: the window `Utils` is the file `Utils.js`.

For example, create a new code window, name it `Utils` and write this into it:

`// Utils.js
export function plantRow(plantType) {
	for (let i = 0; i < getWorldSize(); i++) {
		plant(plantType);
		move(East);
	}
}`

The word `export` in front of the function makes it available to other files. A function or variable without `export` is private to its file.

Now you can use it from any other file:

`// main.js
import * as Utils from "./Utils.js";

Utils.plantRow(Entities.Bush);`

Here `import * as Utils from "./Utils.js";` runs the file named `Utils` and gives you access to everything it exports through the name `Utils`.
You can then access variables and functions within the imported module using the `.` operator.
So in this example, `Utils.plantRow(Entities.Bush)` calls `plantRow()` in `Utils`.
Imports go at the top of the file.

This also works for code windows in other languages: `import * as helper from "./helper.py";` uses the Python window `helper.py` and `import * as Farm from "./Farm.java";` the Java window `Farm.java`. Write the names exactly as that file writes them, for example `helper.plant_row()`. Other languages don't need `export`: you can use every function and global variable of their files.

### No need to read further

You can also import single names. Then you can use them without the module name in front:

`// main.js
import { plantRow } from "./Utils.js";

plantRow(Entities.Bush);`
Imports only the specified names from `Utils`.

or

`// main.js
import * as Utils from "./Utils.js";

Utils.plantRow(Entities.Bush);`
Imports everything that `Utils` exports.

Variables work the same way: if `Utils` declares `export let count = 0;`, other files can read it as `Utils.count`. Imported names are read-only: only the file that declares a variable can change it. If another file should change it, export a function that does it:

`// Counter.js
export let count = 0;

export function increase() {
	count++;
}`

`// main.js
import * as Counter from "./Counter.js";

Counter.increase();
console.log(Counter.count);`

If you import a name that the other file does not export, you get the error `The requested module './Utils.js' does not provide an export named 'f'`.
Files that are not code windows (like npm packages) can't be imported. The only exception is `import "./farm.js";`, which is useful in an external editor (see [External Editor](docs/external_editor.md)) and does nothing in the game.

# How it really works

## TLDR
Imports can be quite unintuitive, but most problems can be avoided by keeping the files that other files import free of drone commands: put only `function` definitions and variables there, and call the functions from the file you run.

## Import Side Effects
The first time you import a file, it will execute the entire file and then give you access to all variables that have been defined during the execution.
If you import the same file again, it will just return the cached module from the first time again.

This means that import statements can have side effects. If you import a file that calls `harvest()` at the top level, it will actually harvest during the import. But when you import it again, it won't harvest again because the file is only run once.

A good file structure is to put the code that should be executed when the file is run into a `main()` function and only call it in the file that you run with the play button. This way you have a clear distinction between local variables (defined inside `main()`) and global variables that can be imported (defined outside `main()`).

`// Farm.js
export const aGlobalVariable = "global";

export function main() {
	const aLocalVariable = "local";
	// do things
}`

`// run.js
import * as Farm from "./Farm.js";

Farm.main();`

## Import Cycles
What happens if file `a` imports file `b` and file `b` imports file `a`?

file `a`:
`// a.js
import * as b from "./b.js";

export const x = 0;`

file `b`:
`// b.js
import * as a from "./a.js";

export function f() {
	console.log(a.x);
}`

This will work fine. Let's say neither of the two files are loaded yet, and someone else executes `import * as a from "./a.js";`.

-`a` runs until the `import * as b from "./b.js";` line.
-`b` runs until the `import * as a from "./a.js";` line.
-The module `a` already exists, but doesn't contain `x` because it has only reached the `import * as b from "./b.js";` line.
-`b` stores a reference to the half-loaded module `a` in a variable called `a`.
-`b` runs the `function` statement and stores the function `f()`.
-`a` continues to run and initializes `x`.

When someone calls `b.f()` it will correctly print `0` because the module `a` that `b` has a reference to is now fully loaded.

A named import like `import { x } from "./a.js";` works the same way in JavaFarmer: `x` always means `a.x`, so it sees the value as soon as `a` has set it.

Problems can only appear if code at the top level of `b` uses something from `a` while `a` is still loading, for example `export const y = a.x + 1;` in `b`. At that point `a.x` doesn't exist yet and you get an error that `x` has never been defined. Give the variable a simple starting value and set the real value in a function instead.