# Other Windows (Import)
Putting all your code in a single file quickly becomes unmanageable. 
In Python, `import` statements give you access to the functions and global variables of another file. C++ has no `import`: every code window is a separate source file, and a window uses the functions and globals of another window by declaring them, exactly like separate C++ files that are linked together into one program.

For example, create a new code window, name it `farming` and write this into it:

`#include "farm.hpp"

int rows_done = 0;

void plant_row(Entity plant_type) {
	for (int i = 0; i < get_world_size(); i++) {
		plant(plant_type);
		move(East);
	}
	rows_done++;
}`

This window has no `main` function. A window without `main` is a library: it can't be run on its own, it just provides functions and globals for other windows.

Now you can use it from any other window. Declare the function with a prototype (its first line, ending with `;`) and the global with `extern`:

`void plant_row(Entity plant_type);
extern int rows_done;

int main() {
	plant_row(Entities::Bush);
	std::cout << rows_done << std::endl;
}`

The prototype tells the compiler that `plant_row` exists and which parameters it takes, `extern int rows_done;` says that the global `rows_done` is defined in another window. The game finds the window that defines them by itself.
The declaration has to match the definition. If the types differ you get the error `conflicting types`, and if no window defines the name you get `undefined reference`.
Prototypes and `extern` declarations themselves don't cost anything, but using another window needs the Import unlock.

### No need to read further

## Private functions
A function or global marked `static` belongs to its own window only. Other windows can't declare and use it, so you can reuse the same name in several windows without clashes.

`static int counter = 0;

static void count_up() {
	counter++;
}`

## #include
In normal C++ programs you would put the prototypes into a header file and `#include` it. JavaFarmer only knows `#include "farm.hpp"` and the standard headers like `<vector>` or `<iostream>`. Including another code window, for example `#include "farming.cpp"`, is not allowed. Write the prototypes you need at the top of the window instead.

# How it really works

## Loading windows
The first time a window is used, the game loads it. This sets up all its globals with their starting values. A window is only loaded once, so all windows that use `rows_done` share the same variable (within one drone, see the Megafarm unlock).

When a window is used by another window, its `main` function does not run. `main` only runs when you press the play button of that window. So a window can contain a `main` for testing and still be used as a library.

## Using each other
Two windows can use each other's functions. That always works, because functions only run when they are called.

window `a`:
`int x = 0;

void print_y();

void print_x() {
	std::cout << x << std::endl;
}`

window `b`:
`int y = 1;

extern int x;

void print_y() {
	std::cout << x + y << std::endl;
}`

Give globals simple starting values like `0` or `1` and set the real values inside a function. Then it doesn't matter which window is loaded first.
