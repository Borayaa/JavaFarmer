# Leaderboard
If you have made it this far, you have overcome many challenges. But have you solved them efficiently? 
You can compete with other players on various leaderboards for the most efficient farming methods.

JavaFarmer note: while the JavaFarmer mod is active, leaderboard scores are NOT uploaded. Leaderboard runs still work, so you can time your Zig solutions, but your results stay on your computer. To compete on the real leaderboards, disable the mod and use the game's normal language.

You can start a leaderboard run by calling `farm.leaderboardRun(leaderboard, filename, speedup);`.
This starts a [simulation](docs/unlocks/simulation.md) similar to `farm.simulate()` except that the starting conditions are fixed. Each leaderboard category has different start and success conditions.

The leaderboard run succeeds if the success condition is `true` when the simulation ends. 

The simulation will NOT end automatically when the goal is reached. You must make sure that the program terminates.
If the run is successful, your time will be added to the leaderboard.

To reduce variance, all runs are required to run for at least 2 hours (You can speed it up, so it won't take that long). If a run is completed earlier, it will be repeated until a total time of 2 hours is reached. The average of all runs is then used as your score.

Here's an example setup for the hay leaderboard. It uses two files. The file `hay_run.zig` farms hay until the goal is reached and then ends:
`// hay_run.zig
const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    while (farm.numItems(.Hay) < 2000000000) {
        if (farm.canHarvest()) {
            _ = farm.harvest();
        }
    }
}`

A second file starts the leaderboard run with `hay_run` as its starting file (the file name without `.zig`):
`// main.zig
const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    farm.leaderboardRun(.Hay, "hay_run", 64);
}`

In the function calls below, `filename` is the name of your start file and `speedup` is the speedup, for example:
`const filename = "hay_run";
const speedup = 64;`

## Fastest Reset
The fastest reset is the most prestigious category. Completely automate the game from a single farm plot to unlocking the leaderboards again.

You do not have to unlock everything, just try to unlock `farm.Unlock.Leaderboard` as fast as possible.

Remember that you can use `farm.numUnlocked(unlock) > 0` to check if something is unlocked and you can use `farm.getCost()` on unlocks to see what they cost so you can automatically farm the right items.

Function Call:
`farm.leaderboardRun(.Fastest_Reset, filename, speedup);`

Equivalent Simulation:
`fn fastestResetSimulation() f64 {
    const unlocks = [_]farm.Unlock{};
    var items = std.AutoHashMap(farm.Item, f64).init(farm.allocator);
    defer items.deinit();
    var start_globals = std.StringHashMap(i32).init(farm.allocator);
    defer start_globals.deinit();
    // a negative seed value means a random seed
    const seed = -1;
    return farm.simulate(filename, unlocks, items, start_globals, seed, speedup);
}`

Success Condition:
`farm.numUnlocked(farm.Unlock.Leaderboard) > 0`

## Maze
Start with everything unlocked and farm `9863168` gold as fast as you can. This is exactly the amount of gold you will earn by reusing one 32x32 maze `300` times.

Function Call:
`farm.leaderboardRun(.Maze, filename, speedup);`

Equivalent Simulation: every unlock at its maximum level, a random seed and these starting items:
`var maze_items = std.AutoHashMap(farm.Item, f64).init(farm.allocator);
defer maze_items.deinit();
maze_items.put(.Weird_Substance, 1000000000) catch unreachable;
maze_items.put(.Power, 1000000000) catch unreachable;`

Success Condition:
`farm.numItems(.Gold) >= 9863168`

## Dinosaur
Start with everything unlocked and farm `33488928` bones as fast as you can. This is exactly the number of bones you will get if you fill a 32x32 area with the dinosaur tail.

Function Call:
`farm.leaderboardRun(.Dinosaur, filename, speedup);`

Equivalent Simulation: every unlock at its maximum level, a random seed and these starting items:
`var dinosaur_items = std.AutoHashMap(farm.Item, f64).init(farm.allocator);
defer dinosaur_items.deinit();
dinosaur_items.put(.Cactus, 1000000000) catch unreachable;
dinosaur_items.put(.Power, 1000000000) catch unreachable;`

Success Condition:
`farm.numItems(.Bone) >= 33488928`

## Other Resource Leaderboards
Each plant has its own leaderboard for farming that particular plant as quickly as possible. You start with all the unlocks, the resources you need to grow the plant, and lots of power. The goal is to farm a set amount of the resource produced by the plant.

As always, you need to make sure that your program terminates when you reach the goal. A run is not finished until the program ends, even if the goal is reached.

### `farm.Leaderboard.Cactus`
`farm.leaderboardRun(.Cactus, filename, speedup);`
Success Condition: `farm.numItems(.Cactus) >= 33554432`

### `farm.Leaderboard.Sunflowers`
`farm.leaderboardRun(.Sunflowers, filename, speedup);`
Success Condition: `farm.numItems(.Power) >= 100000`

### `farm.Leaderboard.Pumpkins`
`farm.leaderboardRun(.Pumpkins, filename, speedup);`
Success Condition: `farm.numItems(.Pumpkin) >= 200000000`

### `farm.Leaderboard.Wood`
`farm.leaderboardRun(.Wood, filename, speedup);`
Success Condition: `farm.numItems(.Wood) >= 10000000000`

### `farm.Leaderboard.Carrots`
`farm.leaderboardRun(.Carrots, filename, speedup);`
Success Condition: `farm.numItems(.Carrot) >= 2000000000`

### `farm.Leaderboard.Hay`
`farm.leaderboardRun(.Hay, filename, speedup);`
Success Condition: `farm.numItems(.Hay) >= 2000000000`

## Single Drone Leaderboards
There are also Leaderboards for farming with a single drone. You only get one drone and an 8x8 farm and have to farm a certain amount of resources as quickly as possible.

### `farm.Leaderboard.Maze_Single`
`farm.leaderboardRun(.Maze_Single, filename, speedup);`
Success Condition: `farm.numItems(.Gold) >= 616448`

### `farm.Leaderboard.Cactus_Single`
`farm.leaderboardRun(.Cactus_Single, filename, speedup);`
Success Condition: `farm.numItems(.Cactus) >= 131072`

### `farm.Leaderboard.Sunflowers_Single`
`farm.leaderboardRun(.Sunflowers_Single, filename, speedup);`
Success Condition: `farm.numItems(.Power) >= 10000`

### `farm.Leaderboard.Pumpkins_Single`
`farm.leaderboardRun(.Pumpkins_Single, filename, speedup);`
Success Condition: `farm.numItems(.Pumpkin) >= 10000000`

### `farm.Leaderboard.Wood_Single`
`farm.leaderboardRun(.Wood_Single, filename, speedup);`
Success Condition: `farm.numItems(.Wood) >= 500000000`

### `farm.Leaderboard.Carrots_Single`
`farm.leaderboardRun(.Carrots_Single, filename, speedup);`
Success Condition: `farm.numItems(.Carrot) >= 100000000`

### `farm.Leaderboard.Hay_Single`
`farm.leaderboardRun(.Hay_Single, filename, speedup);`
Success Condition: `farm.numItems(.Hay) >= 100000000`