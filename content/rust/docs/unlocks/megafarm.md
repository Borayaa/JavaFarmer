# Mega Farm
This incredibly powerful unlock gives you access to multiple drones. 

As before, you still start with just one drone. Additional drones must first be spawned and will disappear after the program terminates.
Each drone runs its own separate program. New drones can be spawned using the `spawn_drone(task)` function. The task can be the name of a function, like `drone_task`, or a closure, like `|| { harvest(); }`.

`use farm::*;

fn drone_task() {
	move_to(North);
	do_a_flip();
}

fn main() {
	spawn_drone(drone_task);
}`

This spawns a new drone in the same position as the drone that ran the `spawn_drone()` command. The new drone then begins executing the specified function. After it is done, it will disappear automatically.

Note that you write `drone_task` without `()`. This passes the function itself to `spawn_drone` without calling it.
To run another code window's code on the new drone, pass one of its functions: `spawn_drone(utils::work);`.

Drones do not collide with each other. 

Use `max_drones()` to get the maximum number of drones that can be spawned.
Use `num_drones()` to get the number of drones that are already on the farm.


## Example:
`use farm::*;

fn harvest_column() {
	for _ in 0..get_world_size() {
		harvest();
		move_to(North);
	}
}

fn main() {
	loop {
		if spawn_drone(harvest_column).is_some() {
			move_to(East);
		}
	}
}`

This will cause your first drone to move horizontally and spawn more drones. The spawned drones will then move vertically and harvest everything in their path.

`spawn_drone()` returns an `Option<Drone<T>>`. If all available drones have already been spawned, `spawn_drone()` will do nothing and return `None`.

## Closures
A closure is a small function without a name that you write right where you need it: `|| { ... }`. The `||` means it takes no arguments, and the code in the braces is what the drone will run.
Here's an example that passes a different direction to each drone:
`for dir in [North, East, South, West] {
	spawn_drone(move || {
		move_to(dir);
		do_a_flip();
	});
}`

A closure can use the local variables around it. The `move` keyword in front of it gives the closure its own copy of them, so each drone gets its own `dir`. A closure can read these variables, but it can't assign new values to them.

<spoiler=show hint> Check out this super useful parallel `for_all` function, which takes any function and runs it on every farm tile. It makes use of all available drones to do so.

`use farm::*;

fn for_all(f: fn() -> bool) {
	let row = move || {
		for _ in 0..get_world_size() - 1 {
			f();
			move_to(East);
		}
		f();
	};
	for _ in 0..get_world_size() {
		if spawn_drone(row).is_none() {
			row();
		}
		move_to(North);
	}
}

fn main() {
	for_all(harvest);
}`

`fn() -> bool` is the type of a function that takes no arguments and returns a `bool`, like `harvest`. `f()` calls it.

One particularly useful pattern is to spawn a drone if one is available and otherwise do it yourself.

`let task = || {
	harvest();
};
if spawn_drone(task).is_none() {
	task();
}`
</spoiler>

## Awaiting Another Drone
Use the `wait_for(drone)` function to wait for another drone to finish. You receive the `Drone` handle when you spawn the drone. It comes inside a `Some`, so take it out with `.unwrap()` (or `if let Some(drone) = ...`).
`wait_for(drone)` returns the return value of the function that the other drone was running. A `Drone<T>` belongs to a task that returns a `T`, and `wait_for()` gives you that `T`.

The task must not take any arguments. To run a function with arguments on a drone, wrap the call in a closure:

`use farm::*;

fn get_entity_type_in_direction(dir: Direction) -> Option<Entity> {
	move_to(dir);
	get_entity_type()
}

fn main() {
	let drone = spawn_drone(|| get_entity_type_in_direction(North)).unwrap();
	let entity = wait_for(drone);
	println!("{:?}", entity);
}`

Note that spawning drones takes time, so it's not a good idea to spawn a new drone for every little thing.

You can use `has_finished(drone)` to check if the drone has finished without waiting for it.

## No Shared Memory
Each drone has its own memory and cannot directly read or write another drone's globals.

`use farm::*;

static mut X: i32 = 0;

fn increment() {
	unsafe {
		X += 1;
	}
}

fn main() {
	wait_for(spawn_drone(increment).unwrap());
	let x = unsafe { X };
	println!("{}", x);
}`

This will print `0` because the new drone incremented its own copy of the global `X`, which does not affect the first drone's `X`.

## Race Conditions
Multiple drones can interact with the same farm tile at the same time. If two drones interact with the same tile during the same tick, both interactions will occur, but the results may differ based on the order of the interactions.

For example, imagine that drones `0` and `1` are both over the same tree that is almost fully grown.
Drone `0` calls
`use_item(Item::Fertilizer);`
Drone `1` calls
`harvest();`

If these actions occur at the same time, the tree will first be fertilized and then harvested. In that case, you will receive wood from it. However, if Drone `1` is slightly faster, the tree will be harvested before it is fertilized, and you will not receive the wood.
This is called a "race condition." It is a common issue in parallel programming, where the result depends on the order in which operations are performed.

Here's another problematic situation that can happen when multiple drones run the same code simultaneously at the same position.
`if get_water() < 0.5 {
	use_item(Item::Water);
}`

If multiple drones run this simultaneously, they will all run the first line, which puts them into the `if` block. Then, they will all use water, wasting a lot of it.
By the time a drone reaches the second line, `get_water()` might no longer be less than `0.5` because another drone has watered the tile in the meantime.
