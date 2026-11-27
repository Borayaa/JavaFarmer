# Mega Farm
This incredibly powerful unlock gives you access to multiple drones. 

As before, you still start with just one drone. Additional drones must first be spawned and will disappear after the program terminates.
Each drone runs its own separate program. New drones can be spawned using the `farm.spawnDrone(function)` function. You pass it the name of a function that takes no parameters, like `fn droneTask() void`. Write the name without `()`, because you pass the function itself instead of calling it.

`const std = @import("std");
const farm = @import("farm");

fn droneTask() void {
    _ = farm.move(.North);
    farm.doAFlip();
}

pub fn main() void {
    _ = farm.spawnDrone(droneTask);
}`

This spawns a new drone in the same position as the drone that ran the `farm.spawnDrone()` command. The new drone then begins executing the specified function. After it is done, it will disappear automatically.

`farm.spawnDrone()` returns a `?farm.Drone`: the handle of the new drone, or `null`. `_ =` discards it when you don't need it.

Drones do not collide with each other. 

Use `farm.maxDrones()` to get the maximum number of drones that can be spawned.
Use `farm.numDrones()` to get the number of drones that are already on the farm.
Both return a `usize`.


## Example:
`const std = @import("std");
const farm = @import("farm");

fn harvestColumn() void {
    for (0..farm.getWorldSize()) |_| {
        _ = farm.harvest();
        _ = farm.move(.North);
    }
}

pub fn main() void {
    while (true) {
        if (farm.spawnDrone(harvestColumn) != null) {
            _ = farm.move(.East);
        }
    }
}`

This will cause your first drone to move horizontally and spawn more drones. The spawned drones will then move vertically and harvest everything in their path.

If all available drones have already been spawned, `farm.spawnDrone()` will do nothing and return `null`.

## Telling a drone what to do
The function of a drone can't have parameters, and Zig has no closures (functions that remember the local variables around them). To give each drone its own job, use a top-level `var`: a new drone starts with a copy of the top-level variables of the drone that spawned it. So set the variable right before you spawn the drone.
Here's an example that passes a different direction to each drone:
`var drone_dir: farm.Direction = .North;

fn task() void {
    _ = farm.move(drone_dir);
    farm.doAFlip();
}`

`const dirs = [_]farm.Direction{ .North, .East, .South, .West };
for (dirs) |dir| {
    drone_dir = dir;
    _ = farm.spawnDrone(task);
}`

Changing `drone_dir` after the drone was spawned doesn't affect that drone anymore, because it has its own copy (see "No Shared Memory" below).
For a few fixed jobs you can also write one function per job (`fn flipNorth() void`, `fn flipEast() void` ...) and spawn each of them.

<spoiler=show hint> Check out this super useful parallel `forAll` function, which runs `tileTask` on every farm tile. It makes use of all available drones to do so.

`fn tileTask() void {
    _ = farm.harvest();
}

fn row() void {
    for (0..farm.getWorldSize() - 1) |_| {
        tileTask();
        _ = farm.move(.East);
    }
    tileTask();
}

fn forAll() void {
    for (0..farm.getWorldSize()) |_| {
        if (farm.spawnDrone(row) == null) {
            row();
        }
        _ = farm.move(.North);
    }
}`

`forAll();`

In JavaFarmer a function can't be passed as a parameter, so `forAll` always runs `tileTask`. Change the body of `tileTask` to whatever should happen on each tile.

One particularly useful pattern is to spawn a drone if one is available and otherwise do it yourself.

`if (farm.spawnDrone(task) == null) {
    task();
}`
</spoiler>

## Awaiting Another Drone
Use the `farm.waitFor(T, drone)` function to wait for another drone to finish. You receive the `drone` handle when you spawn the drone.
`farm.waitFor(T, drone)` returns the return value of the function that the other drone was running. Like `farm.measure()`, its first argument is the type you expect back (`void` if the function returns nothing).

`fn getEntityTypeInDirection(dir: farm.Direction) ?farm.Entity {
    _ = farm.move(dir);
    return farm.getEntityType();
}

fn northEntity() ?farm.Entity {
    return getEntityTypeInDirection(.North);
}`

`if (farm.spawnDrone(northEntity)) |drone| {
    const result = farm.waitFor(?farm.Entity, drone);
    std.debug.print("{any}\n", .{result});
}`

`northEntity` is needed because the function of a drone can't have parameters.

Note that spawning drones takes time, so it's not a good idea to spawn a new drone for every little thing.

You can use `farm.hasFinished(drone)` to check if the drone has finished without waiting for it.

## No Shared Memory
Each drone has its own memory and cannot directly read or write another drone's top-level variables.

`const std = @import("std");
const farm = @import("farm");

var x: i32 = 0;

fn increment() void {
    x += 1;
}

pub fn main() void {
    farm.waitFor(void, farm.spawnDrone(increment).?);
    std.debug.print("{d}\n", .{x});
}`

This will print `0` because the new drone incremented its own copy of the top-level variable `x`, which does not affect the first drone's `x`. (`.?` unwraps the `?farm.Drone`; it is an error if no drone could be spawned.)

## Race Conditions
Multiple drones can interact with the same farm tile at the same time. If two drones interact with the same tile during the same tick, both interactions will occur, but the results may differ based on the order of the interactions.

For example, imagine that drones `0` and `1` are both over the same tree that is almost fully grown.
Drone `0` calls
`_ = farm.useItem(.Fertilizer);`
Drone `1` calls
`_ = farm.harvest();`

If these actions occur at the same time, the tree will first be fertilized and then harvested. In that case, you will receive wood from it. However, if Drone `1` is slightly faster, the tree will be harvested before it is fertilized, and you will not receive the wood.
This is called a "race condition." It is a common issue in parallel programming, where the result depends on the order in which operations are performed.

Here's another problematic situation that can happen when multiple drones run the same code simultaneously at the same position.
`if (farm.getWater() < 0.5) {
    _ = farm.useItem(.Water);
}`

If multiple drones run this simultaneously, they will all run the first line, which puts them into the `if` block. Then, they will all use water, wasting a lot of it.
By the time a drone reaches the second line, `farm.getWater()` might no longer be less than `0.5` because another drone has watered the tile in the meantime.