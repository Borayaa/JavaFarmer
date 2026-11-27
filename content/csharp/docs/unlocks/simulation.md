# Simulation

Simulations allow you to quickly test code without changing the state of the real farm.
The starting state of the simulation can be chosen freely, and when the simulation ends, the real farm will be in the exact state it was in before the simulation started.

The `Simulate()` method is used to start a simulation.

`// the file the execution should start in
string filename = "f1";

// start with carrots and trees fully upgraded
List<Unlock> simUnlocks = [Unlocks.Carrots, Unlocks.Trees];

// start with 10000 carrots and 50 hay
Dictionary<Item, double> simItems = new Dictionary<Item, double>
{
	[Items.Carrot] = 10000,
	[Items.Hay] = 50
};

// start with a global variable "a" with a value of 13
Dictionary<string, object> simGlobals = new Dictionary<string, object>
{
	["a"] = 13
};

// use a fixed random seed
int seed = 0;

// speed up the simulation by a factor 64
int speedup = 64;

// run the simulation
double runTime = Simulate(filename, simUnlocks, simItems, simGlobals, seed, speedup);`

The `Simulate()` method returns the time, in seconds, that it took to simulate the given start file.

### File Name
The first argument of the `Simulate` method is the filename. This is the name that is displayed at the top of the code window. The simulation will run the specified file as if you had clicked the Execute button on it.

### Starting Unlocks
All programming features such as loops, if statements, lists, dictionaries,... will always remain unlocked. 

The second argument allows you to specify which unlocks/upgrades the simulation should start with in addition to the programming features. This should be a list of unlocks. The simulation will start with all unlocks in the list upgraded to their maximum level.

If you want to specify an upgrade level other than the maximum, you can pass a dictionary that maps the unlocks to unlock levels. In this case, negative values correspond to the maximum unlock level.

`Dictionary<Unlock, int> levels = new Dictionary<Unlock, int>
{
	[Unlocks.Expand] = 3,
	[Unlocks.Speed] = -1
};`

### Starting Items
The third argument allows you to pass a dictionary of type `Dictionary<Item, double>` that maps items to numbers. It specifies the items to start the simulation with.

### Starting Globals
Because the simulation starts a completely new program execution, you can't access variables from the program that starts the simulation.
However, it is possible to pass values to the simulation using the fourth argument. This is a `Dictionary<string, object>` that maps variable names to values. These variables are added to the global scope of the execution inside the simulation, before the start file runs.
Note that in C# a variable must be declared before it is used, and a declaration like `int a = 0;` in the start file sets its value when it runs, which replaces the passed value. If you don't need this feature, pass an empty dictionary (`new Dictionary<string, object>()`).

Note that this copies all values, so mutating them inside the simulation won't affect the original values outside the simulation. It's not possible to return values from the simulation other than the time it took to run.

### Random Seed
The fifth argument allows you to specify the random seed used in the simulation. This must be a positive integer. Negative values will cause a random seed to be used.

The random seed affects everything from plant growth times to maze layouts to water decay times. If you start the same simulation multiple times with the same random seed and the same starting conditions, the result should always be the same.

### Speedup
The sixth argument is the starting speedup of the simulation. This allows you to test things quickly. If the game is unable to keep up with the set speed it will slow down automatically.

The speedup does not affect the result of the simulation in any way. It exists only to reduce the waiting time.
