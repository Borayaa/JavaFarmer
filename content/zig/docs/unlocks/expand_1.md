# Expand 1
<unlock=for>Also see [Expand_2](docs/unlocks/expand_2.md)

</unlock>Your farm has grown! This space is not much use if you can't move the drone, so there is a new function `farm.move()` that moves the drone. `farm.move()` requires that you specify the direction in which you want the drone to move. There are four new constants for this, the members of `farm.Direction`: `.North, .East, .South, .West`

For example, `_ = farm.move(.North);` will move the drone one square to the north. `farm.move()` returns `true` if the drone moved, and `_ =` discards that result.

If you move over the edge of the farm the drone will be moved to the other side of the farm.
The following example code will keep moving the drone north and wrap back to the start when it reaches the edge of the farm:

`const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    while (true) {
        _ = farm.move(.North);
    }
}`
