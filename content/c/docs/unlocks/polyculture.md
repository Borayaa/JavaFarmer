# Polyculture
You may have already noticed that sometimes plants yield more when planted together.
Grass, bushes, trees, and carrots yield more when they have the right plant companion. Companion preference is different for each individual plant and cannot be predicted. Fortunately, the companion preference of the plant under the drone can be measured using `get_companion()`. It returns a `Companion`: `->entity` is the type of plant it wants as its companion and `->position` is the position where it wants its companion.

`Companion companion = get_companion();
if (companion != NULL) {
	Entity plant_type = companion->entity;
	int x = companion->position.x;
	int y = companion->position.y;
}`

For example if you plant a bush and then call `get_companion()` it will return something like `{Entities.Carrot, {3, 5}}`. This means that this bush would like to have carrots at the position `{3, 5}`. So if you plant carrots at `{3, 5}` and then harvest the bush, it will yield more wood. The growth stage of the carrot doesn't matter.

A plant's companion preference can be either `Entities.Grass`, `Entities.Bush`, `Entities.Tree` or `Entities.Carrot`. Each plant chooses this randomly, but it will always choose a different plant than itself. The position can also be any position within 3 moves of the plant except the position of the plant itself.

If there is no plant under the drone that has a companion preference `get_companion()` will return `NULL`. That's why the example above checks `companion != NULL` before it reads the values.

Before polyculture is unlocked, the yield multiplier is `5`. It doubles everytime you upgrade it.