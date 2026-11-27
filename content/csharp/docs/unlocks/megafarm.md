# Mega Farm
This incredibly powerful unlock gives you access to multiple drones. 

As before, you still start with just one drone. Additional drones must first be spawned and will disappear after the program terminates.
Each drone runs its own separate program. New drones can be spawned using the `SpawnDrone(task)` method. The task is the method the new drone runs: the name of a method like `DroneTask`, or a lambda like `() => Harvest()`.

`SpawnDrone(DroneTask);

void DroneTask()
{
	Move(North);
	DoAFlip();
}`

This spawns a new drone in the same position as the drone that ran the `SpawnDrone()` command. The new drone then begins executing the specified method. After it is done, it will disappear automatically.

Note that you write `SpawnDrone(DroneTask)` without `()` after `DroneTask`. This passes the method itself to `SpawnDrone` instead of calling it.
To run another window's code on the new drone, pass one of its public methods: `SpawnDrone(Utils.Work)`.

Drones do not collide with each other. 

Use `MaxDrones()` to get the maximum number of drones that can be spawned.
Use `NumDrones()` to get the number of drones that are already on the farm.


## Example:
`while (true)
{
	if (SpawnDrone(HarvestColumn) != null)
	{
		Move(East);
	}
}

void HarvestColumn()
{
	for (int i = 0; i < GetWorldSize(); i++)
	{
		Harvest();
		Move(North);
	}
}`

This will cause your first drone to move horizontally and spawn more drones. The spawned drones will then move vertically and harvest everything in their path.

If all available drones have already been spawned, `SpawnDrone()` will do nothing and return `null`. That's why its return type is `Drone?`.

## Lambdas
A lambda is a small method without a name that you write right where you need it: `() => { ... }`. The `()` means it takes no arguments, and the code after the arrow `=>` is what the drone will run.
Here's an example that passes a different direction to each drone:
`List<Direction> directions = [North, East, South, West];
foreach (Direction dir in directions)
{
	SpawnDrone(() =>
	{
		Move(dir);
		DoAFlip();
	});
}`

A lambda can use the variables around it, like `dir` here. In JavaFarmer a lambda can't assign a new value to a local variable of the method or block around it (top-level variables and static fields are fine).

<spoiler=show hint> Check out this super useful parallel `ForAll` method, which takes any task and runs it on every farm tile. It makes use of all available drones to do so.

`void ForAll(Action task)
{
	Action row = () =>
	{
		for (int i = 0; i < GetWorldSize() - 1; i++)
		{
			task();
			Move(East);
		}
		task();
	};
	for (int i = 0; i < GetWorldSize(); i++)
	{
		if (SpawnDrone(row) == null)
		{
			row();
		}
		Move(North);
	}
}

ForAll(() => Harvest());`

`Action` is the type of a task that takes no arguments and returns nothing. `task()` runs it.

One particularly useful pattern is to spawn a drone if one is available and otherwise do it yourself.

`Action task = () => Harvest();
if (SpawnDrone(task) == null)
{
	task();
}`
</spoiler>

## Awaiting Another Drone
Use the `WaitFor(drone)` method to wait for another drone to finish. You receive the `Drone` handle when you spawn the drone.
`WaitFor(drone)` returns the return value of the method that the other drone was running. Its type is `object`, so you cast it to the type you expect.

If the drone should return a value, give it a method that returns something, for example a `Func<Entity?>` (a task that returns an `Entity?`):

`Entity? GetEntityTypeInDirection(Direction dir)
{
	Move(dir);
	return GetEntityType();
}

Func<Entity?> lookNorth = () => GetEntityTypeInDirection(North);
Drone? drone = SpawnDrone(lookNorth);
if (drone != null)
{
	Entity? result = (Entity)WaitFor(drone);
	Console.WriteLine(result);
}`

Note that spawning drones takes time, so it's not a good idea to spawn a new drone for every little thing.

You can use `HasFinished(drone)` to check if the drone has finished without waiting for it.

## No Shared Memory
Each drone has its own memory and cannot directly read or write another drone's variables.

`int x = 0;

Drone? helper = SpawnDrone(Increment);
if (helper != null)
{
	WaitFor(helper);
}
Console.WriteLine(x);

void Increment()
{
	x++;
}`

This will print `0` because the new drone incremented its own copy of the variable `x`, which does not affect the first drone's `x`.

## Race Conditions
Multiple drones can interact with the same farm tile at the same time. If two drones interact with the same tile during the same tick, both interactions will occur, but the results may differ based on the order of the interactions.

For example, imagine that drones `0` and `1` are both over the same tree that is almost fully grown.
Drone `0` calls
`UseItem(Items.Fertilizer);`
Drone `1` calls
`Harvest();`

If these actions occur at the same time, the tree will first be fertilized and then harvested. In that case, you will receive wood from it. However, if Drone `1` is slightly faster, the tree will be harvested before it is fertilized, and you will not receive the wood.
This is called a "race condition." It is a common issue in parallel programming, where the result depends on the order in which operations are performed.

Here's another problematic situation that can happen when multiple drones run the same code simultaneously at the same position.
`if (GetWater() < 0.5)
{
	UseItem(Items.Water);
}`

If multiple drones run this simultaneously, they will all run the first line, which puts them into the `if` block. Then, they will all use water, wasting a lot of it.
By the time a drone reaches the second line, `GetWater()` might no longer be less than `0.5` because another drone has watered the tile in the meantime.
