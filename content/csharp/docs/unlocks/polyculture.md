# Polyculture
You may have already noticed that sometimes plants yield more when planted together.
Grass, bushes, trees, and carrots yield more when they have the right plant companion. Companion preference is different for each individual plant and cannot be predicted. Fortunately, the companion preference of the plant under the drone can be measured using `GetCompanion()`. It returns a `Companion`: `companion.Entity` is the type of plant it wants as its companion and `companion.Position` (or `companion.X` and `companion.Y`) is the position where it wants its companion.

If there is no plant under the drone that has a companion preference `GetCompanion()` will return `null`. That's why its return type is `Companion?`, so check for `null` first. Then you can deconstruct the companion like a tuple:

`Companion? companion = GetCompanion();
if (companion is not null)
{
	var (plantType, (x, y)) = companion;
	Console.WriteLine($"{plantType} at {x}, {y}");
}`

For example if you plant a bush and then call `GetCompanion()` it will return something that prints as `(Entities.Carrot, (3, 5))`. This means that this bush would like to have carrots at the position `(3, 5)`. So if you plant carrots at `(3, 5)` and then harvest the bush, it will yield more wood. The growth stage of the carrot doesn't matter.

A plant's companion preference can be either `Entities.Grass`, `Entities.Bush`, `Entities.Tree` or `Entities.Carrot`. Each plant chooses this randomly, but it will always choose a different plant than itself. The position can also be any position within 3 moves of the plant except the position of the plant itself.

See also [Tuples](docs/scripting/tuples.md).

Before polyculture is unlocked, the yield multiplier is `5`. It doubles everytime you upgrade it.
