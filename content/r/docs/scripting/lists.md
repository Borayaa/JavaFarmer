# Lists
Lists are an easy way to store multiple values in a single variable.
In R, a sequence of values is made with `c()` (short for combine) or with `list()`. In JavaFarmer both create the same kind of list.
You can create new lists like this:

`plants <- list(2, TRUE, Items$Hay)`

The list now contains the values `2`, `TRUE` and `Items$Hay`.
`c()` is the usual way to write a list of values of the same kind, like `c(1, 2, 3)` or `c(North, East)`.
A list can also be empty:

`empty_list <- c()`

You can access an element of a list by its index, written in double square brackets. In R the index is `1` for the first element, `2` for the second element, `3` for the third... The last element is at the index `length(v)`.

plants carrots
`v <- c(Entities$Tree, Entities$Carrot, Entities$Pumpkin)
plant(v[[2]])`

`v[2]` with single brackets works too. Index `0` doesn't exist in R.

You can iterate over a list using a for loop. The following example sums the all elements in the list.

`v <- c(4, 7, 2, 5)
total <- 0
for (number in v) {
	total <- total + number
}`
`total` is now `18`

The following statements allow you to add and remove elements:

`v[[length(v) + 1]] <- elem` adds an element to the end of the list:

`v <- c(2, 6, 12)
v[[length(v) + 1]] <- 7`
`v` is now `c(2, 6, 12, 7)`

`v <- c(v, elem)` does the same. `v <- c(elem, v)` adds an element at the front.

`v <- v[-match(elem, v)]` removes the first occurrence of an element from a list. (`match(elem, v)` is the index of the first `elem` in `v`, and `v[-i]` is the list without the element at the index `i`.)

`v <- c(1, 2, 4, 2)
v <- v[-match(2, v)]`
`v` is now `c(1, 4, 2)`

`v <- append(v, elem, after = i)` inserts an element after the index `i`, so that it gets the index `i + 1`:

`v <- c(Entities$Tree, Items$Hay)
v <- append(v, Items$Wood, after = 1)`
`v` is now `c(Entities$Tree, Items$Wood, Items$Hay)`

`v <- v[-i]` removes the element at the index `i`.
`v <- v[-length(v)]` removes the last element.

`v <- c(3, 5, 8, 25)
v <- v[-length(v)]`
`v` is now `c(3, 5, 8)`
`v <- c(3, 5, 8)
v <- v[-2]`
`v` is now `c(3, 8)`

The `length()` function returns the length of the list.
`v <- c(3, 2, 1)
x <- length(v)`
`x` is now `3`

`elem %in% v` is `TRUE` if `elem` is in the list.

A list can also hold other lists. Use `list()` for that, because `c()` would combine them into one list: `grid <- list(c(1, 2), c(3, 4))`, and `grid[[2]][[1]]` is `3`.

In R, lists are values. This means that assigning a list to a variable makes a copy of the list, so changing the copy doesn't change the original.

`a <- c(1, 2)
b <- a
b <- b[-length(b)]`
`a` is still `c(1, 2)` and `b` is `c(1)`

The same is true when you pass a list to a function: if the function changes its parameter, the caller's list stays the same. Return the changed list from the function if you need it.
(The copy only goes one level deep: in a list of lists, the inner lists are still shared.)
