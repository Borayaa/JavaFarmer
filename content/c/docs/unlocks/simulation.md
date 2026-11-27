# Simulation

Simulations allow you to quickly test code without changing the state of the real farm.
The starting state of the simulation can be chosen freely, and when the simulation ends, the real farm will be in the exact state it was in before the simulation started.

The `simulate()` function is used to start a simulation.

the file the execution should start in
`string filename = "f1";`

start with carrots and trees fully upgraded
`Map(Unlock, int) sim_unlocks = map_of(Unlock, int, {Unlocks.Carrots, -1}, {Unlocks.Trees, -1});`

start with 10000 carrots and 50 hay
`Map(Item, double) sim_items = map_of(Item, double, {Items.Carrot, 10000}, {Items.Hay, 50});`

start with a global variable "a" with a value of 13
`Map(string, int) sim_globals = map_of(string, int, {"a", 13});`

use a fixed random seed
`int seed = 0;`

speed up the simulation by a factor 64
`int speedup = 64;`

run the simulation
`double run_time = simulate(filename, sim_unlocks, sim_items, sim_globals, seed, speedup);`

The `simulate()` function returns the time, in seconds, that it took to simulate the given start file.

### File Name
The first argument of the simulate function is the filename. This is the name that is displayed at the top of the code window, without its extension (`"f1"` for the window `f1.c`). The simulation will run the specified file as if you had clicked the Execute button on it.

### Starting Unlocks
All programming features such as loops, if statements, lists, maps,... will always remain unlocked. 

The second argument allows you to specify which unlocks/upgrades the simulation should start with in addition to the programming features. It is a map of type `Map(Unlock, int)` that maps the unlocks to unlock levels. Negative values like `-1` correspond to the maximum unlock level.

`Map(Unlock, int) some_unlocks = map_new(Unlock, int);
map_put(some_unlocks, Unlocks.Expand, 3);
map_put(some_unlocks, Unlocks.Speed, -1);`

### Starting Items
The third argument allows you to pass a map of type `Map(Item, double)` that maps items to numbers. It specifies the items to start the simulation with.

### Starting Globals
Because the simulation starts a completely new program execution, you can't access variables from the program that starts the simulation.
However, it is possible to pass values to the simulation using the fourth argument. This is a `Map(string, int)` that maps variable names in the form of strings to numbers. These variables are added to the global scope of the execution inside the simulation, before the start file runs.
Note that in JavaFarmer a global variable declaration like `int a = 0;` in the start window sets its starting value when the window is loaded, which replaces the passed value. If you don't need this feature, pass an empty map: `map_new(string, int)`.

Note that this copies all values, so mutating them inside the simulation won't affect the original values outside the simulation. It's not possible to return values from the simulation other than the time it took to run.

### Random Seed
The fifth argument allows you to specify the random seed used in the simulation. This must be a positive integer. Negative values will cause a random seed to be used.

The random seed affects everything from plant growth times to maze layouts to water decay times. If you start the same simulation multiple times with the same random seed and the same starting conditions, the result should always be the same.

### Speedup
The sixth argument is the starting speedup of the simulation. This allows you to test things quickly. If the game is unable to keep up with the set speed it will slow down automatically.

The speedup does not affect the result of the simulation in any way. It exists only to reduce the waiting time.
