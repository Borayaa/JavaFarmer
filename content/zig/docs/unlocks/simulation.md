# Simulation

Simulations allow you to quickly test code without changing the state of the real farm.
The starting state of the simulation can be chosen freely, and when the simulation ends, the real farm will be in the exact state it was in before the simulation started.

The `farm.simulate()` function is used to start a simulation.

`const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    // the file the execution should start in
    const filename = "f1";

    // start with carrots and trees fully upgraded
    const sim_unlocks = [_]farm.Unlock{ .Carrots, .Trees };

    // start with 10000 carrots and 50 hay
    var sim_items = std.AutoHashMap(farm.Item, f64).init(farm.allocator);
    defer sim_items.deinit();
    sim_items.put(.Carrot, 10000) catch unreachable;
    sim_items.put(.Hay, 50) catch unreachable;

    // start with a global variable "a" with a value of 13
    var sim_globals = std.StringHashMap(i32).init(farm.allocator);
    defer sim_globals.deinit();
    sim_globals.put("a", 13) catch unreachable;

    // use a fixed random seed
    const seed = 0;

    // speed up the simulation by a factor 64
    const speedup = 64;

    // run the simulation
    const run_time = farm.simulate(filename, sim_unlocks, sim_items, sim_globals, seed, speedup);
    std.debug.print("{d}\n", .{run_time});
}`

The `farm.simulate()` function returns the time, in seconds, that it took to simulate the given start file.

### File Name
The first argument of the simulate function is the filename. This is the name that is displayed at the top of the code window, without its extension (`"f1"` for the window `f1.zig`). The simulation will run the specified file as if you had clicked the Execute button on it.

### Starting Unlocks
All programming features such as loops, if statements, lists, maps,... will always remain unlocked. 

The second argument allows you to specify which unlocks/upgrades the simulation should start with in addition to the programming features. This should be an array (or an `std.ArrayList`) of unlocks, like `[_]farm.Unlock{ .Carrots, .Trees }`. The simulation will start with all unlocks in the array upgraded to their maximum level. To start with nothing extra, pass an empty array: `[_]farm.Unlock{}`.

If you want to specify an upgrade level other than the maximum, you can pass an `std.AutoHashMap(farm.Unlock, i32)` that maps the unlocks to unlock levels. In this case, negative values correspond to the maximum unlock level.

`var sim_unlock_levels = std.AutoHashMap(farm.Unlock, i32).init(farm.allocator);
defer sim_unlock_levels.deinit();
sim_unlock_levels.put(.Expand, 3) catch unreachable;
sim_unlock_levels.put(.Speed, -1) catch unreachable;`

### Starting Items
The third argument allows you to pass a map of type `std.AutoHashMap(farm.Item, f64)` that maps items to numbers. It specifies the items to start the simulation with.

### Starting Globals
Because the simulation starts a completely new program execution, you can't access variables from the program that starts the simulation.
However, it is possible to pass values to the simulation using the fourth argument. This is an `std.StringHashMap` that maps variable names in the form of strings to values, for example `std.StringHashMap(i32)`. These variables are added to the global scope of the execution inside the simulation, before the start file runs.
In a Zig start file, declare such a variable with `undefined` as its starting value: `var a: i32 = undefined;` keeps the value the simulation passed in (any other starting value replaces it when the file is loaded). Read it only if you know it was passed: if it wasn't, the game reports that `a` has never been defined. If you don't need this feature, pass an empty map (`.{}` works for an empty map here).

Note that this copies all values, so mutating them inside the simulation won't affect the original values outside the simulation. It's not possible to return values from the simulation other than the time it took to run.

### Random Seed
The fifth argument allows you to specify the random seed used in the simulation. This must be a positive integer. Negative values will cause a random seed to be used.

The random seed affects everything from plant growth times to maze layouts to water decay times. If you start the same simulation multiple times with the same random seed and the same starting conditions, the result should always be the same.

### Speedup
The sixth argument is the starting speedup of the simulation. This allows you to test things quickly. If the game is unable to keep up with the set speed it will slow down automatically.

The speedup does not affect the result of the simulation in any way. It exists only to reduce the waiting time.