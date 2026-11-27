# Position and Companion (Tuples)
In Python, tuples are used to combine several values into a single value, like an `(x, y)` position. Java has no tuples.
Instead, the game gives you two small types that each hold a fixed set of values: `Position` and `Companion`.
You can't change their values after they have been created, but you can read them with these methods:

A `Position` holds an x and a y coordinate:
`p.x()` returns the x coordinate.
`p.y()` returns the y coordinate.

<unlock=mazes>For example, `measure()` in a maze returns the `Position` of the treasure. `measure()` can return different kinds of values, so its return type is `Object` and you have to tell Java that it's a `Position` with a cast:

`Position treasure = (Position) measure();
int treasureX = treasure.x();
int treasureY = treasure.y();`

</unlock>A `Companion` is what `getCompanion()` returns. It holds an entity and a position:
`c.entity()` returns the type of plant.
`c.position()` returns the `Position`.
`c.x()` and `c.y()` are shortcuts for `c.position().x()` and `c.position().y()`.

<unlock=polyculture>`Companion c = getCompanion();
if (c != null) {
    Entity wanted = c.entity();
    Position where = c.position();
    System.out.println(wanted + " at " + where.x() + ", " + where.y());
}`

</unlock><unlock=dicts>Positions can be used as keys in a `HashMap` or as elements of a `HashSet`. Two positions are equal if their coordinates are equal.

</unlock>
## Returning multiple values
A method can only return one value. If you want to return several numbers, you can return an array:

`int[] currentPosition() {
    return new int[]{getPosX(), getPosY()};
}

void main() {
    int[] pos = currentPosition();
    int x = pos[0];
    int y = pos[1];
}`
