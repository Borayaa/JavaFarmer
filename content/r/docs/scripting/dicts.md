# Dictionaries
Dictionaries are a datastructure that allows you to map keys to values in the same way that a real dictionary maps words to their definitions and you can look them up very quickly.

In R, a dictionary is a named list: a `list()` whose elements have names. The names are the keys.
A dictionary can be created like this:
`hay_needed <- list(carrot = 5, tree = 20)`

The name before the `=` is the key and the expression after the `=` is the value to which the key maps.
The above dictionary maps the text `"carrot"` to `5` and the text `"tree"` to `20`.

Keys can be texts or game constants like directions, items or entities. To use a constant as a key, put it between double square brackets.
This dictionary maps each direction to the direction to it's right:
`right_of <- list()
right_of[[North]] <- East
right_of[[East]] <- South
right_of[[South]] <- West
right_of[[West]] <- North`

(`list(North = East)` would not work here: in front of the `=` the name is just the text `"North"`, not the direction `North`.)

Accessing the value mapped to a key is similar to accessing an element in a list:
`m <- list(a = 1)
key <- "a"
value <- m[[key]]`

Example:
`right_of <- list()
right_of[[South]] <- West
orientation <- right_of[[South]]`
This sets `orientation` to `West`.

When the key is a plain name you can also write `m$a` instead of `m[["a"]]`.
Reading a key that is not in the dictionary gives `NULL`, so `is.null(m[[key]])` tells you that a key is missing.

You can add a new key-value pair to a dictionary like this:
`m <- list()
m[["hay"]] <- 10`

Here's another one that maps the drone's position to the entity it's over. R dictionaries are keyed by text, so the position becomes a text like `"3 5"` with `paste()`:
`entity_dict <- list()
entity_dict[[paste(get_pos_x(), get_pos_y())]] <- get_entity_type()`
This updates the entity stored for the current position.

Keys are unique, so adding a key that already exists in the dictionary will overwrite the previous value.

Use `m[[key]] <- NULL` to remove a key-value pair from `m`.

`names(m)` gives you all the keys of `m`, and `key %in% names(m)` evaluates to `TRUE` if `key` is a key in `m` and `FALSE` otherwise.
So you can use `if (key %in% names(m))` to check if `m` contains the key.

`length(m)` is the number of key-value pairs.

Loop over `names(m)` to iterate through all keys:
`m <- list(hay = 1, wood = 2)
for (key in names(m)) {
	value <- m[[key]]
	cat(key, value, "\n")
}`

There are no guarantees about the order in which the keys are iterated.

Like lists, dictionaries are values in R: `b <- a` gives `b` its own copy, so changing `b` does not change `a`.

See also [Sets](docs/scripting/sets.md)
