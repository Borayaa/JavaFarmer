# Mega Farm
This incredibly powerful unlock gives you access to multiple drones. 

As before, you still start with just one drone. Additional drones must first be spawned and will disappear after the program terminates.
Each drone runs its own separate program. New drones can be spawned using the `spawn_drone(f)` function, where `f` is the function that the new drone runs.

`drone_function <- function() {
	move(North)
	do_a_flip()
}

spawn_drone(drone_function)`

This spawns a new drone in the same position as the drone that ran the `spawn_drone(f)` command. The new drone then begins executing the specified function. After it is done, it will disappear automatically.

Drones do not collide with each other. 

Use `max_drones()` to get the maximum number of drones that can be spawned.
Use `num_drones()` to get the number of drones that are already on the farm.


## Example:
`harvest_column <- function() {
	for (y in seq_len(get_world_size())) {
		harvest()
		move(North)
	}
}

while (TRUE) {
	if (!is.null(spawn_drone(harvest_column))) {
		move(East)
	}
}`

This will cause your first drone to move horizontally and spawn more drones. The spawned drones will then move vertically and harvest everything in their path.

If all available drones have already been spawned, `spawn_drone()` will do nothing and return `NULL`. That's why the example checks the result with `is.null()`.

Here's another example that passes a different direction to each drone.
`for (dir in c(North, East, South, West)) {
	task <- function() {
		move(dir)
		do_a_flip()
	}
	spawn_drone(task)
}`

<spoiler=show hint> Check out this super useful parallel `for_all` function, which takes any function and runs it on every farm tile. It makes use of all available drones to do so.

`for_all <- function(f) {
	row <- function() {
		for (x in seq_len(get_world_size() - 1)) {
			f()
			move(East)
		}
		f()
	}
	for (y in seq_len(get_world_size())) {
		if (is.null(spawn_drone(row))) {
			row()
		}
		move(North)
	}
}

for_all(harvest)`

One particularly useful pattern is to spawn a drone if one is available and otherwise do it yourself.

`task <- function() {
	harvest()
}
if (is.null(spawn_drone(task))) {
	task()
}`
</spoiler>

You can also give `spawn_drone()` a function without a name: `spawn_drone(function() move_n_dir(5, North))` lets the new drone call your function `move_n_dir()` with arguments.

## Awaiting Another Drone
Use the `wait_for(drone)` function to wait for another drone to finish. You receive the `drone` handle when you spawn the drone.
`wait_for(drone)` returns the return value of the function that the other drone was running.

`get_entity_type_in_direction <- function(dir) {
	move(dir)
	get_entity_type()
}

zero_arg_wrapper <- function() {
	get_entity_type_in_direction(North)
}
drone <- spawn_drone(zero_arg_wrapper)
print(wait_for(drone))`

Note that spawning drones takes time, so it's not a good idea to spawn a new drone for every little thing.

You can use `has_finished(drone)` to check if the drone has finished without waiting for it.

## No Shared Memory
Each drone has its own memory and cannot directly read or write another drone's globals.

`x <- 0

increment <- function() {
	x <<- x + 1
}

wait_for(spawn_drone(increment))
print(x)`

This will print `0` because the new drone incremented its own copy of the global `x`, which does not affect the first drone's `x`.

## Race Conditions
Multiple drones can interact with the same farm tile at the same time. If two drones interact with the same tile during the same tick, both interactions will occur, but the results may differ based on the order of the interactions.

For example, imagine that drones `0` and `1` are both over the same tree that is almost fully grown.
Drone `0` calls
`use_item(Items$Fertilizer)`
Drone `1` calls
`harvest()`

If these actions occur at the same time, the tree will first be fertilized and then harvested. In that case, you will receive wood from it. However, if Drone `1` is slightly faster, the tree will be harvested before it is fertilized, and you will not receive the wood.
This is called a "race condition." It is a common issue in parallel programming, where the result depends on the order in which operations are performed.

Here's another problematic situation that can happen when multiple drones run the same code simultaneously at the same position.
`if (get_water() < 0.5) {
	use_item(Items$Water)
}`

If multiple drones run this simultaneously, they will all run the first line, which puts them into the `if` block. Then, they will all use water, wasting a lot of it.
By the time a drone reaches the second line, `get_water()` might no longer be less than `0.5` because another drone has watered the tile in the meantime.