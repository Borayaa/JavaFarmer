# Expand 1
<unlock=for>Also see [Expand_2](docs/unlocks/expand_2.md)

</unlock>Your farm has grown! This space is not much use if you can't move the drone, so there is a new method `Move()` that moves the drone. `Move()` requires that you specify the direction in which you want the drone to move. There are four new constants for this: `North, East, South, West` (short for `Direction.North` and so on)

For example, `Move(North);` will move the drone one square to the north.

If you move over the edge of the farm the drone will be moved to the other side of the farm.
The following example code will keep moving the drone north and wrap back to the start when it reaches the edge of the farm:

`while (true)
{
	Move(North);
}`
