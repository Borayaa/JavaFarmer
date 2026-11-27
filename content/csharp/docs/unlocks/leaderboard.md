# Leaderboard
If you have made it this far, you have overcome many challenges. But have you solved them efficiently? 
You can compete with other players on various leaderboards for the most efficient farming methods.

JavaFarmer note: while the JavaFarmer mod is active, leaderboard scores are NOT uploaded. Leaderboard runs still work, so you can time your C# solutions, but your results stay on your computer. To compete on the real leaderboards, disable the mod and use the game's normal language.

You can start a leaderboard run by calling `LeaderboardRun(leaderboard, filename, speedup);`.
This starts a [simulation](docs/unlocks/simulation.md) similar to `Simulate()` except that the starting conditions are fixed. Each leaderboard category has different start and success conditions.

The leaderboard run succeeds if the success condition is `true` when the simulation ends. 

The simulation will NOT end automatically when the goal is reached. You must make sure that the program terminates.
If the run is successful, your time will be added to the leaderboard.

To reduce variance, all runs are required to run for at least 2 hours (You can speed it up, so it won't take that long). If a run is completed earlier, it will be repeated until a total time of 2 hours is reached. The average of all runs is then used as your score.

Here's an example setup for the hay leaderboard. It uses two windows. The window `HayRun` farms hay until the goal is reached and then ends:
`while (NumItems(Items.Hay) < 2000000000)
{
	if (CanHarvest())
	{
		Harvest();
	}
}`

A second window starts the leaderboard run with `HayRun` as its starting file:
`LeaderboardRun(Leaderboards.Hay, "HayRun", 64);`

In the examples below, `filename` is the name of your starting window and `speedup` is the speedup you want:
`string filename = "HayRun";
int speedup = 64;`

## Fastest Reset
The fastest reset is the most prestigious category. Completely automate the game from a single farm plot to unlocking the leaderboards again.

You do not have to unlock everything, just try to unlock `Unlocks.Leaderboard` as fast as possible.

Remember that you can use `NumUnlocked(unlock) > 0` to check if something is unlocked and you can use `GetCost()` on unlocks to see what they cost so you can automatically farm the right items.

Method Call:
`LeaderboardRun(Leaderboards.Fastest_Reset, filename, speedup);`

Equivalent Simulation:
`List<Unlock> unlocks = [];
Dictionary<Item, double> items = new Dictionary<Item, double>();
Dictionary<string, object> startGlobals = new Dictionary<string, object>();
// a negative seed value means a random seed
int seed = -1;
Simulate(filename, unlocks, items, startGlobals, seed, speedup);`

Success Condition: `NumUnlocked(Unlocks.Leaderboard) > 0`

## Maze
Start with everything unlocked and farm `9863168` gold as fast as you can. This is exactly the amount of gold you will earn by reusing one 32x32 maze `300` times.

Method Call:
`LeaderboardRun(Leaderboards.Maze, filename, speedup);`

Equivalent Simulation: every unlock at its maximum level, a random seed and these starting items:
`Dictionary<Item, double> mazeItems = new Dictionary<Item, double>
{
	[Items.Weird_Substance] = 1000000000,
	[Items.Power] = 1000000000
};`

Success Condition: `NumItems(Items.Gold) >= 9863168`

## Dinosaur
Start with everything unlocked and farm `33488928` bones as fast as you can. This is exactly the number of bones you will get if you fill a 32x32 area with the dinosaur tail.

Method Call:
`LeaderboardRun(Leaderboards.Dinosaur, filename, speedup);`

Equivalent Simulation: every unlock at its maximum level, a random seed and these starting items:
`Dictionary<Item, double> dinosaurItems = new Dictionary<Item, double>
{
	[Items.Cactus] = 1000000000,
	[Items.Power] = 1000000000
};`

Success Condition: `NumItems(Items.Bone) >= 33488928`

## Other Resource Leaderboards
Each plant has its own leaderboard for farming that particular plant as quickly as possible. You start with all the unlocks, the resources you need to grow the plant, and lots of power. The goal is to farm a set amount of the resource produced by the plant.

As always, you need to make sure that your program terminates when you reach the goal. A run is not finished until the program ends, even if the goal is reached.

### `Leaderboards.Cactus`
`LeaderboardRun(Leaderboards.Cactus, filename, speedup);`
Success Condition: `NumItems(Items.Cactus) >= 33554432`

### `Leaderboards.Sunflowers`
`LeaderboardRun(Leaderboards.Sunflowers, filename, speedup);`
Success Condition: `NumItems(Items.Power) >= 100000`

### `Leaderboards.Pumpkins`
`LeaderboardRun(Leaderboards.Pumpkins, filename, speedup);`
Success Condition: `NumItems(Items.Pumpkin) >= 200000000`

### `Leaderboards.Wood`
`LeaderboardRun(Leaderboards.Wood, filename, speedup);`
Success Condition: `NumItems(Items.Wood) >= 1e10` (that is 10000000000)

### `Leaderboards.Carrots`
`LeaderboardRun(Leaderboards.Carrots, filename, speedup);`
Success Condition: `NumItems(Items.Carrot) >= 2000000000`

### `Leaderboards.Hay`
`LeaderboardRun(Leaderboards.Hay, filename, speedup);`
Success Condition: `NumItems(Items.Hay) >= 2000000000`

## Single Drone Leaderboards
There are also Leaderboards for farming with a single drone. You only get one drone and an 8x8 farm and have to farm a certain amount of resources as quickly as possible.

### `Leaderboards.Maze_Single`
`LeaderboardRun(Leaderboards.Maze_Single, filename, speedup);`
Success Condition: `NumItems(Items.Gold) >= 616448`

### `Leaderboards.Cactus_Single`
`LeaderboardRun(Leaderboards.Cactus_Single, filename, speedup);`
Success Condition: `NumItems(Items.Cactus) >= 131072`

### `Leaderboards.Sunflowers_Single`
`LeaderboardRun(Leaderboards.Sunflowers_Single, filename, speedup);`
Success Condition: `NumItems(Items.Power) >= 10000`

### `Leaderboards.Pumpkins_Single`
`LeaderboardRun(Leaderboards.Pumpkins_Single, filename, speedup);`
Success Condition: `NumItems(Items.Pumpkin) >= 10000000`

### `Leaderboards.Wood_Single`
`LeaderboardRun(Leaderboards.Wood_Single, filename, speedup);`
Success Condition: `NumItems(Items.Wood) >= 500000000`

### `Leaderboards.Carrots_Single`
`LeaderboardRun(Leaderboards.Carrots_Single, filename, speedup);`
Success Condition: `NumItems(Items.Carrot) >= 100000000`

### `Leaderboards.Hay_Single`
`LeaderboardRun(Leaderboards.Hay_Single, filename, speedup);`
Success Condition: `NumItems(Items.Hay) >= 100000000`
