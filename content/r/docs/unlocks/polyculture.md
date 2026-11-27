# Polyculture
You may have already noticed that sometimes plants yield more when planted together.
Grass, bushes, trees, and carrots yield more when they have the right plant companion. Companion preference is different for each individual plant and cannot be predicted. Fortunately, the companion preference of the plant under the drone can be measured using `get_companion()`. It returns a tuple where the first element is the type of plant it wants as its companion and the second element is the position where it wants its companion. In R you read them with `[[1]]` and `[[2]]` (see [Tuples](docs/scripting/tuples.md)):

`comp <- get_companion()
if (!is.null(comp)) {
	plant_type <- comp[[1]]
	x <- comp[[2]][[1]]
	y <- comp[[2]][[2]]
}`

For example if you plant a bush and then call `get_companion()` it will return something like `list(Entities$Carrot, c(3, 5))`. This means that this bush would like to have carrots at the position x = `3`, y = `5`. So if you plant carrots there and then harvest the bush, it will yield more wood. The growth stage of the carrot doesn't matter.

A plant's companion preference can be either `Entities$Grass`, `Entities$Bush`, `Entities$Tree` or `Entities$Carrot`. Each plant chooses this randomly, but it will always choose a different plant than itself. The position can also be any position within 3 moves of the plant except the position of the plant itself.

If there is no plant under the drone that has a companion preference `get_companion()` will return `NULL`. Check it with `is.null()` before you read the elements, like the example above does.

Before polyculture is unlocked, the yield multiplier is `5`. It doubles everytime you upgrade it.