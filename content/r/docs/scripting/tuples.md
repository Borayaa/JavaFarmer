# Tuples
In Python, tuples are a way to combine multiple values into a single value, like an `(x, y)` position. R has no tuples. Instead you use a vector made with `c()`:

`pos <- c(1, 2)`

It is indexed like a list, starting at `1`:

`pos <- c(1, 2)
print(pos[[2]])`
prints `2`

You can't unpack a vector into several variables in one line. Read the elements one by one instead:

`pos <- c(3, 4)
x <- pos[[1]]
y <- pos[[2]]`

Some game functions give you a tuple: `measure()` returns the position of the next treasure or apple, and `get_companion()` returns a plant type and a position. You read them the same way, with `[[1]]` and `[[2]]`.<unlock=polyculture>

`comp <- get_companion()
if (!is.null(comp)) {
	plant_type <- comp[[1]]
	x <- comp[[2]][[1]]
	y <- comp[[2]][[2]]
}`
</unlock>

Values that come from the game like these can't be changed: `comp[[1]] <- Entities$Bush` throws an error. A vector you made yourself with `c()` can be changed like any list.
<unlock=dicts>
Dictionary keys in R are texts, so a position can't be a key directly. Turn it into a text with `paste()` instead: `paste(1, 2)` is the text `"1 2"`.

`d <- list()
d[[paste(1, 2)]] <- c(4, 5)
print(d[[paste(1, 2)]])`
prints `c(4, 5)`</unlock>

Vectors are also useful for returning multiple values from a function.

`f <- function() {
	c(1, 2)
}

result <- f()
a <- result[[1]]
b <- result[[2]]`
