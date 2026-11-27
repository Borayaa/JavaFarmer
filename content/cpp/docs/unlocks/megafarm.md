# Mega Farm
This incredibly powerful unlock gives you access to multiple drones. 

As before, you still start with just one drone. Additional drones must first be spawned and will disappear after the program terminates.
Each drone runs its own separate program. New drones can be spawned using the `spawn_drone(task)` function. The task is a function without parameters: the name of a function like `drone_task`, a lambda like `[&] { harvest(); }` or a `std::function<void()>` variable.

`void drone_task() {
	move(North);
	do_a_flip();
}

int main() {
	spawn_drone(drone_task);
}`

This spawns a new drone in the same position as the drone that ran the `spawn_drone()` command. The new drone then begins executing the specified function. After it is done, it will disappear automatically.

Writing `drone_task` without `()` passes the function itself to `spawn_drone` without calling it.
To run another window's code on the new drone, declare that window's function with a prototype (see [Import](docs/scripting/import.md)) and pass its name.

Drones do not collide with each other. 

Use `max_drones()` to get the maximum number of drones that can be spawned.
Use `num_drones()` to get the number of drones that are already on the farm.


## Example:
`void harvest_column() {
	for (int i = 0; i < get_world_size(); i++) {
		harvest();
		move(North);
	}
}

int main() {
	while (true) {
		if (spawn_drone(harvest_column)) {
			move(East);
		}
	}
}`

This will cause your first drone to move horizontally and spawn more drones. The spawned drones will then move vertically and harvest everything in their path.

If all available drones have already been spawned, `spawn_drone()` will do nothing and return `nullptr`, which counts as `false` in a condition.

## Lambdas
A lambda is a small function without a name that you write right where you need it: `[&] { ... }` or `[=] { ... }`. The code in the braces is what the drone will run. A lambda for `spawn_drone` takes no parameters.
Here's an example that passes a different direction to each drone:
`std::vector<Direction> directions = {North, East, South, West};
for (Direction dir : directions) {
	spawn_drone([=] {
		move(dir);
		do_a_flip();
	});
}`

A lambda can read the local variables around it, like `dir` here. It can't assign them: the game has no way to change a local variable of another function, so the editor reports `a closure cannot assign the outer variable`. Use a global variable instead.

<spoiler=show hint> Check out this super useful parallel `for_all` function, which takes any task and runs it on every farm tile. It makes use of all available drones to do so.

`void for_all(std::function<void()> task) {
	auto row = [&] {
		for (int i = 0; i < get_world_size() - 1; i++) {
			task();
			move(East);
		}
		task();
	};
	for (int i = 0; i < get_world_size(); i++) {
		if (!spawn_drone(row)) {
			row();
		}
		move(North);
	}
}

int main() {
	for_all(harvest);
}`

`std::function<void()>` is the type of a task that takes no arguments and returns nothing. `task()` runs it.

One particularly useful pattern is to spawn a drone if one is available and otherwise do it yourself.

`auto job = [&] { harvest(); };
if (!spawn_drone(job)) {
	job();
}`
</spoiler>

## Awaiting Another Drone
Use the `wait_for(drone)` function to wait for another drone to finish. You receive the `Drone` handle when you spawn the drone.
In Python, `wait_for(drone)` also returns the return value of the function that the other drone was running. In C++ the tasks of drones return nothing, so `wait_for` returns nothing too: it only waits.

`void look_north() {
	move(North);
	do_a_flip();
}

int main() {
	Drone drone = spawn_drone(look_north);
	wait_for(drone);
	std::println("the other drone has finished");
}`

Note that spawning drones takes time, so it's not a good idea to spawn a new drone for every little thing.

You can use `has_finished(drone)` to check if the drone has finished without waiting for it.

## No Shared Memory
Each drone has its own memory and cannot directly read or write another drone's globals.

`int x = 0;

void increment() {
	x++;
}

int main() {
	wait_for(spawn_drone(increment));
	std::cout << x << std::endl;
}`

This will print `0` because the new drone incremented its own copy of the global `x`, which does not affect the first drone's `x`.

## Race Conditions
Multiple drones can interact with the same farm tile at the same time. If two drones interact with the same tile during the same tick, both interactions will occur, but the results may differ based on the order of the interactions.

For example, imagine that drones `0` and `1` are both over the same tree that is almost fully grown.
Drone `0` calls
`use_item(Items::Fertilizer);`
Drone `1` calls
`harvest();`

If these actions occur at the same time, the tree will first be fertilized and then harvested. In that case, you will receive wood from it. However, if Drone `1` is slightly faster, the tree will be harvested before it is fertilized, and you will not receive the wood.
This is called a "race condition." It is a common issue in parallel programming, where the result depends on the order in which operations are performed.

Here's another problematic situation that can happen when multiple drones run the same code simultaneously at the same position.
`if (get_water() < 0.5) {
	use_item(Items::Water);
}`

If multiple drones run this simultaneously, they will all run the first line, which puts them into the `if` block. Then, they will all use water, wasting a lot of it.
By the time a drone reaches the second line, `get_water()` might no longer be less than `0.5` because another drone has watered the tile in the meantime.
