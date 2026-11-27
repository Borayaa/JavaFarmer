# Import
Putting all your code in a single file quickly becomes unmanageable. 
In R, `source()` lets you use the functions and global variables of another file.

For example, create a new code window, name it `utils.R` and write this into it:
file `utils.R`:
`plant_row <- function(plant_type) {
	for (i in seq_len(get_world_size())) {
		plant(plant_type)
		move(East)
	}
}

row_count <- 0`

Now you can use it from any other file:
`source("utils.R")
plant_row(Entities$Bush)
print(row_count)`

`source("utils.R")` gives you access to all functions and global variables of the code window `utils`. After it you use them by their names, just as if they were written in your own file: `plant_row()` calls the function `plant_row()` in `utils`.
Write the window name with `.R` at the end, between quotes, and put the `source()` lines at the top of your file. A file can source several files.

A file that only defines functions and variables, like `utils`, is a library. It's not meant to be run on its own, it just provides functions for other files.

### No need to read further

# How it really works

## TLDR
Most problems can be avoided by only putting function definitions and variables with simple starting values into files that other files `source()`.

## Source Side Effects
The first time a file is sourced, the game will execute the entire file and then give you access to all variables that have been defined during the execution.
If the same file is sourced again (for example by another file), the game will just reuse the file from the first time.

This means that `source()` can have side effects. If you source a file that calls `harvest()` at the top level, it will actually harvest during the `source()`. But when it's sourced again, it won't harvest again because the file is only run once.
(This is different from R outside of the game, where `source()` runs the file again every time.)

A common file structure is to put the code that should be executed when the file is run into a `main()` function and to call it from a file that is never sourced by others. This way you have a clear distinction between local variables (defined inside `main()`) and global variables that can be used by other files (defined outside `main()`).

file `farming.R`:
`a_global_variable <- "global"

main <- function() {
	a_local_variable <- "local"
	harvest()
}`

The file you run then only contains:
`source("farming.R")
main()`

## Files that source each other
What happens if file `a` sources file `b` and file `b` sources file `a`?

file `a.R`:
`source("b.R")
x <- 0`

file `b.R`:
`source("a.R")
f <- function() {
	print(x)
}`

This will work fine. Let's say neither of the two files are loaded yet, and someone else executes `source("a.R")`.

-`a` runs until the `source("b.R")` line.
-`b` runs until the `source("a.R")` line.
-The file `a` is already being loaded, but doesn't contain `x` yet because it has only reached the `source("b.R")` line.
-`b` remembers the half-loaded file `a`.
-`b` runs the function definition and stores the function `f()`.
-`a` continues to run and initializes `x`.

When someone calls `f()` it will correctly print `0`, because `f()` only reads `x` from `a` when it runs, and by then `a` is fully loaded.

Problems only appear if the top-level code of a file uses a variable of another file that is still being loaded. If `b` had a top-level line `y <- x + 1`, it would fail with an error that `x` has never been defined, because `a` hasn't reached the `x <- 0` line yet. If you get strange errors like that, give the variable a simple starting value and compute the real value in a function instead.
