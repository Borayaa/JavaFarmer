# Sets
In Python, sets are like [dictionaries](docs/scripting/dicts.md), but without values. They're just an unordered set of keys. 

R has no set type. Instead, you use a list (a vector made with `c()`) and make sure that every element is in it only once.

A set with three directions:
`s <- c(North, East, West)`

Use `c()` to create an empty set.

Add a new element only if it isn't in the set yet:
`s <- c(North, East)
x <- South
if (!(x %in% s)) {
	s[[length(s) + 1]] <- x
}`

Use `s <- s[-match(x, s)]` to remove the element `x` from `s` (only if it is in the set, otherwise `match()` fails).

Use `if (x %in% s)` to check if the set contains an element.

Use `for (x in s)` to iterate all elements in the set.

Because the set is a list, `%in%` has to compare `x` with the elements one after the other. For large sets it is much faster to use a [dictionary](docs/scripting/dicts.md) whose keys are the elements, with `TRUE` as the value:

`visited <- list()
visited[[paste(get_pos_x(), get_pos_y())]] <- TRUE
if (paste(get_pos_x(), get_pos_y()) %in% names(visited)) {
	do_a_flip()
}`

Elements in sets are unique: that's why the `if` above only adds `x` when it isn't in the set yet. 

A set made from a list keeps its elements in the order you added them. For a set made from a dictionary there are no guarantees about the order in which the keys are iterated.
