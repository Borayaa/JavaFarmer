# Other Code Windows (Import)
Putting all your code in a single file quickly becomes unmanageable. 
In C, a program can be split into several files. Each code window is one file, and a window can use the functions and global variables of the other windows.
C has no `import` statement. Instead, you tell C which functions and variables of another window you want to use by declaring them at the top of your window, just like separate C files that are linked together into one program.

For example, create a new code window, name it `farming` and write this into it:

`#include "farm.h"

int rows_done = 0;

void plant_row(Entity plant_type) {
	for (int i = 0; i < get_world_size(); i++) {
		plant(plant_type);
		move(East);
	}
	rows_done++;
}`

This window has no `main` function. A window without `main` is a library: it can't be run on its own, it just provides functions and variables for other windows.

Now you can use it from any other window:

`#include "farm.h"

void plant_row(Entity plant_type);
extern int rows_done;

int main(void) {
	plant_row(Entities.Bush);
	printf("%d rows done\n", rows_done);
}`

The line `void plant_row(Entity plant_type);` is a prototype: the first line of the function, followed by a semicolon instead of the code block. It tells C that a function with this name, these parameters and this return type exists in another window.
`extern int rows_done;` does the same for a global variable. `extern` means "this variable is defined somewhere else". Both windows now share the same `rows_done`, so a change made in one window is seen by the other.

You don't have to say which window the function is in. The game looks through your code windows and finds the one that defines it. If no window defines it, you get the error `undefined reference to 'plant_row'`. If the prototype doesn't match the real function, for example because a parameter has a different type, you get `conflicting types for 'plant_row'`.

Prototypes and `extern` declarations don't cost anything. Using functions or variables of another window needs the Import unlock.

### No need to read further

## Private functions and variables
Sometimes a library has helper functions or variables that no other window should use. Mark them `static`:

`static int helper_count = 0;

static void count_helper(void) {
	helper_count++;
}`

A `static` function or global variable belongs to its own window only. Other windows can't declare and use it, and two windows can each have their own `static` function with the same name.
Without `static`, every name is shared by all windows, so two windows can't both define a function called `plant_row`.

## Header files
In bigger C programs the prototypes are usually collected in a header file and included with `#include`. In JavaFarmer the only header of your own farm is `farm.h`. You can't `#include` another code window: write the prototypes you need at the top of the window instead.

# How it really works

## TLDR
In C, nothing runs when a window is used by another window. Only the `main` of the window whose play button you pressed runs. So most of the import problems of Python can't happen in C.

## Loading windows
The first time a window is used, the game loads it. This sets up all its global variables with their starting values. A window is only loaded once, so all windows that use `rows_done` share the same variable (within one drone, see the Megafarm unlock).

The starting value of a global variable must be a constant like `0` or `"hello"`. A function call like `get_world_size()` is not allowed outside of a function. This is a rule of C, and it means that loading a window can never harvest, move or do anything else by itself.

If a window that is used by another window has a `main` function, that `main` does not run. It only runs when you press the play button of that window. So a window can be a program and a library at the same time.

## Using each other
Two windows can use each other's functions and variables. That always works, because functions only run when they are called, and global variables always start with a constant.

window `a`:
`int x = 0;

void print_y(void);

void print_x(void) {
	printf("%d\n", x);
}`

window `b`:
`int y = 1;

extern int x;

void print_y(void) {
	printf("%d %d\n", x, y);
}`

Here `b` uses `x` from `a`, and `a` declares `print_y` from `b` so that its functions can call it. When someone calls `print_y()`, it prints `0 1`. (The windows also start with `#include "farm.h"`, which is left out here.)
