# Polyculture
You may have already noticed that sometimes plants yield more when planted together.
Grass, bushes, trees, and carrots yield more when they have the right plant companion. Companion preference is different for each individual plant and cannot be predicted. Fortunately, the companion preference of the plant under the drone can be measured using `farm.getCompanion()`. It returns a `?farm.Companion`: `.entity` is the type of plant it wants as its companion and `.x` and `.y` are the position where it wants its companion.

`fn showCompanion() void {
    const companion = farm.getCompanion() orelse return;
    const plant_type = companion.entity;
    const x = companion.x;
    const y = companion.y;
    std.debug.print("{} {d} {d}\n", .{ plant_type, x, y });
}`

For example if you plant a bush and then call `farm.getCompanion()` it might return `.{ .entity = .Carrot, .x = 3, .y = 5 }`. This means that this bush would like to have carrots at the position `(3, 5)`. So if you plant carrots at `(3, 5)` and then harvest the bush, it will yield more wood. The growth stage of the carrot doesn't matter.

A plant's companion preference can be either `farm.Entity.Grass`, `farm.Entity.Bush`, `farm.Entity.Tree` or `farm.Entity.Carrot`. Each plant chooses this randomly, but it will always choose a different plant than itself. The position can also be any position within 3 moves of the plant except the position of the plant itself.

If there is no plant under the drone that has a companion preference `farm.getCompanion()` will return `null`. That is why its type starts with `?`, and why the function above uses `orelse return` (it leaves the function when there is no companion). You can also check it with `if`:

`if (farm.getCompanion()) |companion| {
    std.debug.print("{} at {d}, {d}\n", .{ companion.entity, companion.x, companion.y });
}`

See also [Position and Companion](docs/scripting/tuples.md).

Before polyculture is unlocked, the yield multiplier is `5`. It doubles everytime you upgrade it.