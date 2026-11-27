# Polyculture
You may have already noticed that sometimes plants yield more when planted together.
Grass, bushes, trees, and carrots yield more when they have the right plant companion. Companion preference is different for each individual plant and cannot be predicted. Fortunately, the companion preference of the plant under the drone can be measured using `getCompanion()`. It returns a `Companion`: `c.entity()` is the type of plant it wants as its companion and `c.position()` (or `c.x()` and `c.y()`) is the position where it wants its companion.

`Companion c = getCompanion();
Entity plantType = c.entity();
int x = c.x();
int y = c.y();`

For example if you plant a bush and then call `getCompanion()` it might return `Entities.Carrot` at the position `(3, 5)`. This means that this bush would like to have carrots at the position `(3, 5)`. So if you plant carrots at `(3, 5)` and then harvest the bush, it will yield more wood. The growth stage of the carrot doesn't matter.

A plant's companion preference can be either `Entities.Grass`, `Entities.Bush`, `Entities.Tree` or `Entities.Carrot`. Each plant chooses this randomly, but it will always choose a different plant than itself. The position can also be any position within 3 moves of the plant except the position of the plant itself.

If there is no plant under the drone that has a companion preference `getCompanion()` will return `null`. So it's a good idea to check that first:

`Companion c = getCompanion();
if (c != null) {
    System.out.println(c.entity() + " at " + c.x() + ", " + c.y());
}`

See also [Position and Companion](docs/scripting/tuples.md).

Before polyculture is unlocked, the yield multiplier is `5`. It doubles everytime you upgrade it.
