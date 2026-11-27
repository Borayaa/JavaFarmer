# Leaderboard
If you have made it this far, you have overcome many challenges. But have you solved them efficiently? 
You can compete with other players on various leaderboards for the most efficient farming methods.

JavaFarmer note: while the JavaFarmer mod is active, leaderboard scores are NOT uploaded. Leaderboard runs still work, so you can time your Rust solutions, but your results stay on your computer. To compete on the real leaderboards, disable the mod and use the game's normal language.

You can start a leaderboard run by calling `leaderboard_run(leaderboard, filename, speedup);`.
This starts a [simulation](docs/unlocks/simulation.md) similar to `simulate()` except that the starting conditions are fixed. Each leaderboard category has different start and success conditions.
`filename` is the name of the code window that the run starts with, without its `.rs` extension, as a string slice (`&str`) like `"hay_run"`. `speedup` is an `f64` that says how much faster than normal the run is executed. The examples on this page use the file `"main"` and a speedup of `64.0`.

The leaderboard run succeeds if the success condition is `true` when the simulation ends. 

The simulation will NOT end automatically when the goal is reached. You must make sure that the program terminates.
If the run is successful, your time will be added to the leaderboard.

To reduce variance, all runs are required to run for at least 2 hours (You can speed it up, so it won't take that long). If a run is completed earlier, it will be repeated until a total time of 2 hours is reached. The average of all runs is then used as your score.

Here's an example setup for the hay leaderboard. It uses two files. The file `hay_run.rs` farms hay until the goal is reached and then ends:
`// hay_run.rs
use farm::*;

fn main() {
	while num_items(Item::Hay) < 2000000000.0 {
		if can_harvest() {
			harvest();
		}
	}
}`

A second file starts the leaderboard run with `hay_run` as its starting file:
`leaderboard_run(Leaderboard::Hay, "hay_run", 64.0);`

## Fastest Reset
The fastest reset is the most prestigious category. Completely automate the game from a single farm plot to unlocking the leaderboards again.

You do not have to unlock everything, just try to unlock `Unlock::Leaderboard` as fast as possible.

Remember that you can use `num_unlocked(unlock) > 0` to check if something is unlocked and you can use `get_cost()` on unlocks to see what they cost so you can automatically farm the right items.

Function Call:
`leaderboard_run(Leaderboard::Fastest_Reset, "main", 64.0);`

Equivalent Simulation:
`let unlocks: HashMap<Unlock, f64> = HashMap::new();
let items: HashMap<Item, f64> = HashMap::new();
let globals: HashMap<&str, f64> = HashMap::new();
// a negative seed value means a random seed
let seed = -1.0;
simulate("main", unlocks, items, globals, seed, 64.0);`

Success Condition:
`num_unlocked(Unlock::Leaderboard) > 0`

## Maze
Start with everything unlocked and farm `9863168` gold as fast as you can. This is exactly the amount of gold you will earn by reusing one 32x32 maze `300` times.

Function Call:
`leaderboard_run(Leaderboard::Maze, "main", 64.0);`

Equivalent Simulation: every unlock at its maximum level, a random seed and these starting items:
`let mut items: HashMap<Item, f64> = HashMap::new();
items.insert(Item::Weird_Substance, 1000000000.0);
items.insert(Item::Power, 1000000000.0);`

Success Condition:
`num_items(Item::Gold) >= 9863168.0`

## Dinosaur
Start with everything unlocked and farm `33488928` bones as fast as you can. This is exactly the number of bones you will get if you fill a 32x32 area with the dinosaur tail.

Function Call:
`leaderboard_run(Leaderboard::Dinosaur, "main", 64.0);`

Equivalent Simulation: every unlock at its maximum level, a random seed and these starting items:
`let mut items: HashMap<Item, f64> = HashMap::new();
items.insert(Item::Cactus, 1000000000.0);
items.insert(Item::Power, 1000000000.0);`

Success Condition:
`num_items(Item::Bone) >= 33488928.0`

## Other Resource Leaderboards
Each plant has its own leaderboard for farming that particular plant as quickly as possible. You start with all the unlocks, the resources you need to grow the plant, and lots of power. The goal is to farm a set amount of the resource produced by the plant.

As always, you need to make sure that your program terminates when you reach the goal. A run is not finished until the program ends, even if the goal is reached.

### `Leaderboard::Cactus`
`leaderboard_run(Leaderboard::Cactus, "main", 64.0);`
Success Condition: `num_items(Item::Cactus) >= 33554432.0`

### `Leaderboard::Sunflowers`
`leaderboard_run(Leaderboard::Sunflowers, "main", 64.0);`
Success Condition: `num_items(Item::Power) >= 100000.0`

### `Leaderboard::Pumpkins`
`leaderboard_run(Leaderboard::Pumpkins, "main", 64.0);`
Success Condition: `num_items(Item::Pumpkin) >= 200000000.0`

### `Leaderboard::Wood`
`leaderboard_run(Leaderboard::Wood, "main", 64.0);`
Success Condition: `num_items(Item::Wood) >= 10000000000.0`

### `Leaderboard::Carrots`
`leaderboard_run(Leaderboard::Carrots, "main", 64.0);`
Success Condition: `num_items(Item::Carrot) >= 2000000000.0`

### `Leaderboard::Hay`
`leaderboard_run(Leaderboard::Hay, "main", 64.0);`
Success Condition: `num_items(Item::Hay) >= 2000000000.0`

## Single Drone Leaderboards
There are also Leaderboards for farming with a single drone. You only get one drone and an 8x8 farm and have to farm a certain amount of resources as quickly as possible.

### `Leaderboard::Maze_Single`
`leaderboard_run(Leaderboard::Maze_Single, "main", 64.0);`
Success Condition: `num_items(Item::Gold) >= 616448.0`

### `Leaderboard::Cactus_Single`
`leaderboard_run(Leaderboard::Cactus_Single, "main", 64.0);`
Success Condition: `num_items(Item::Cactus) >= 131072.0`

### `Leaderboard::Sunflowers_Single`
`leaderboard_run(Leaderboard::Sunflowers_Single, "main", 64.0);`
Success Condition: `num_items(Item::Power) >= 10000.0`

### `Leaderboard::Pumpkins_Single`
`leaderboard_run(Leaderboard::Pumpkins_Single, "main", 64.0);`
Success Condition: `num_items(Item::Pumpkin) >= 10000000.0`

### `Leaderboard::Wood_Single`
`leaderboard_run(Leaderboard::Wood_Single, "main", 64.0);`
Success Condition: `num_items(Item::Wood) >= 500000000.0`

### `Leaderboard::Carrots_Single`
`leaderboard_run(Leaderboard::Carrots_Single, "main", 64.0);`
Success Condition: `num_items(Item::Carrot) >= 100000000.0`

### `Leaderboard::Hay_Single`
`leaderboard_run(Leaderboard::Hay_Single, "main", 64.0);`
Success Condition: `num_items(Item::Hay) >= 100000000.0`
