# Mega Farm
This incredibly powerful unlock gives you access to multiple drones. 

As before, you still start with just one drone. Additional drones must first be spawned and will disappear after the program terminates.
Each drone runs its own separate program. New drones can be spawned using the `spawn_drone(function)` function. You pass it the name of a function that takes no parameters and returns nothing, like `void drone_function(void)`. Write the name without `()`, because you pass the function itself instead of calling it.

`void drone_function(void) {
	move(North);
	do_a_flip();
}

int main(void) {
	spawn_drone(drone_function);
}`

This spawns a new drone in the same position as the drone that ran the `spawn_drone()` command. The new drone then begins executing the specified function. After it is done, it will disappear automatically.

Drones do not collide with each other. 

Use `max_drones()` to get the maximum number of drones that can be spawned.
Use `num_drones()` to get the number of drones that are already on the farm.


## Example:
`void harvest_column(void) {
	for (int i = 0; i < get_world_size(); i++) {
		harvest();
		move(North);
	}
}

int main(void) {
	while (true) {
		if (spawn_drone(harvest_column) != NULL) {
			move(East);
		}
	}
}`

This will cause your first drone to move horizontally and spawn more drones. The spawned drones will then move vertically and harvest everything in their path.

If all available drones have already been spawned, `spawn_drone()` will do nothing and return `NULL`.

## Telling a drone what to do
The function of a drone can't have parameters. To give each drone its own job, use a global variable: a new drone starts with a copy of the global variables of the drone that spawned it. So set the global variable right before you spawn the drone.
Here's an example that passes a different direction to each drone:
`Direction drone_dir = North;

void task(void) {
	move(drone_dir);
	do_a_flip();
}`

`Direction dirs[] = {North, East, South, West};
for (int i = 0; i < 4; i++) {
	drone_dir = dirs[i];
	spawn_drone(task);
}`

Changing `drone_dir` after the drone was spawned doesn't affect that drone anymore, because it has its own copy (see "No Shared Memory" below).

<spoiler=show hint> Check out this super useful parallel `for_all` function, which takes any function and runs it on every farm tile. It makes use of all available drones to do so.

`void (*tile_task)(void);

void row(void) {
	for (int i = 0; i < get_world_size() - 1; i++) {
		tile_task();
		move(East);
	}
	tile_task();
}

void for_all(void (*f)(void)) {
	tile_task = f;
	for (int i = 0; i < get_world_size(); i++) {
		if (spawn_drone(row) == NULL) {
			row();
		}
		move(North);
	}
}

void harvest_tile(void) {
	harvest();
}`

`for_all(harvest_tile);`

`void (*f)(void)` is a function pointer (see [Functions](docs/scripting/functions.md)). The global variable `tile_task` tells every `row` drone which function to run. `harvest_tile` is needed because `harvest()` returns a `bool`, but the function pointer expects a function that returns nothing.

One particularly useful pattern is to spawn a drone if one is available and otherwise do it yourself.

`if (spawn_drone(task) == NULL) {
	task();
}`
</spoiler>

## Awaiting Another Drone
Use the `wait_for(drone)` function to wait for another drone to finish. You receive the `Drone` handle when you spawn the drone.
In C the function of a drone returns nothing, so `wait_for(drone)` gives nothing back either. It just waits.

`void harvest_here(void) {
	for (int i = 0; i < get_world_size(); i++) {
		harvest();
		move(North);
	}
}

void harvest_west(void) {
	move(West);
	harvest_here();
}`

`Drone helper = spawn_drone(harvest_west);
harvest_here();
if (helper != NULL) {
	wait_for(helper);
}`

This harvests two columns at the same time and continues only when both are done.

Note that spawning drones takes time, so it's not a good idea to spawn a new drone for every little thing.

You can use `has_finished(drone)` to check if the drone has finished without waiting for it.

## No Shared Memory
Each drone has its own memory and cannot directly read or write another drone's globals.

`int x = 0;

void increment(void) {
	x += 1;
}

int main(void) {
	wait_for(spawn_drone(increment));
	printf("%d\n", x);
}`

This will print `0` because the new drone incremented its own copy of the global `x`, which does not affect the first drone's `x`.

## Race Conditions
Multiple drones can interact with the same farm tile at the same time. If two drones interact with the same tile during the same tick, both interactions will occur, but the results may differ based on the order of the interactions.

For example, imagine that drones `0` and `1` are both over the same tree that is almost fully grown.
Drone `0` calls
`use_item(Items.Fertilizer);`
Drone `1` calls
`harvest();`

If these actions occur at the same time, the tree will first be fertilized and then harvested. In that case, you will receive wood from it. However, if Drone `1` is slightly faster, the tree will be harvested before it is fertilized, and you will not receive the wood.
This is called a "race condition." It is a common issue in parallel programming, where the result depends on the order in which operations are performed.

Here's another problematic situation that can happen when multiple drones run the same code simultaneously at the same position.
`if (get_water() < 0.5) {
	use_item(Items.Water);
}`

If multiple drones run this simultaneously, they will all run the first line, which puts them into the `if` block. Then, they will all use water, wasting a lot of it.
By the time a drone reaches the second line, `get_water()` might no longer be less than `0.5` because another drone has watered the tile in the meantime.
