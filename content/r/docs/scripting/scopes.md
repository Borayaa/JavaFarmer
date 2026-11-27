# Name Scopes
Scopes determine which variables can be accessed from where. A scope is basically a mapping from names to values.

There is a global scope, and each function has a local scope.
When you assign a variable with `<-`, it gets added to the current scope.
Anything outside of a function definition is considered part of the global scope.

`x <- 1`
Assigns a value of `1` to the name `x` in the global scope.

This assignment stores a function in the name `f` in the global scope. Inside, `y <- 1` assigns a value of `1` to the name `y` in the local scope of `f`, and the function `g` is stored in the local scope of `f` too.
`f <- function() {
	y <- 1
	g <- function() {
		do_a_flip()
	}
	g()
}`

`f()`
Retrieves the function stored in `f` from the global scope and calls it.

Writing `print(y)` in the global scope after that throws an error (`object 'y' not found`) because `y` was never assigned in the global scope so we can't read it here.
It only existed in the local scope of `f`.

## Changing global variables
By default all variables that a function assigns with `<-` belong to its local scope, even if a variable of the same name exists in the global scope.

`x <- 0

f <- function() {
	x <- 1
}
f()
print(x)`

This code prints `0` because the local `x` inside `f` is not the same variable as the global `x`, so the global `x` remains unchanged. This is important because otherwise a function call could accidentally override a global variable that just happens to have the same name as a local variable of that function.

A function can read global variables without doing anything special. If you want to write to a global variable, you must do so explicitly using the `<<-` operator instead of `<-`.

`x <- 0

f <- function() {
	x <<- 1
}
f()
print(x)`

In this example, `x <<- 1` assigns the global variable `x` defined above it. This will now print `1`.
Be careful with `x <- x + 1` inside a function: it reads the global `x`, but stores the result in a new local `x`. Write `x <<- x + 1` to change the global one.
Note that changing global variables is usually the first step towards spaghetti code, where every part of the program affects every other part of the program, so don't overuse it.

## Loops and branches
Loops and branches do not create their own scopes, so anything assigned within them can still be used outside.

`for (i in 1:3) {
	do_a_flip()
}
print(i)`

This will print `3` because the last iteration of the `for` loop assigned `3` to `i`.
