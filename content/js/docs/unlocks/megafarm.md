# Mega Farm
This incredibly powerful unlock gives you access to multiple drones. 

As before, you still start with just one drone. Additional drones must first be spawned and will disappear after the program terminates.
Each drone runs its own separate program. New drones can be spawned using the `spawnDrone(task)` function. The task is a function: the name of a function you defined, or an arrow function like `() => harvest()`.

`function droneFunction() {
	move(North);
	doAFlip();
}

spawnDrone(droneFunction);`

This spawns a new drone in the same position as the drone that ran the `spawnDrone()` command. The new drone then begins executing the specified function. After it is done, it will disappear automatically.

Note that you write `spawnDrone(droneFunction)` without `()` after the function name: you pass the function itself, the new drone calls it.

Drones do not collide with each other. 

Use `maxDrones()` to get the maximum number of drones that can be spawned.
Use `numDrones()` to get the number of drones that are already on the farm.


## Example:
`function harvestColumn() {
	for (let i = 0; i < getWorldSize(); i++) {
		harvest();
		move(North);
	}
}

while (true) {
	if (spawnDrone(harvestColumn) !== null) {
		move(East);
	}
}`

This will cause your first drone to move horizontally and spawn more drones. The spawned drones will then move vertically and harvest everything in their path.

If all available drones have already been spawned, `spawnDrone()` will do nothing and return `null`.

## Arrow Functions
An arrow function is a small function without a name that you write right where you need it: `() => { ... }`. The `()` means it takes no arguments, and the code block after the arrow `=>` is what the drone will run.
Here's an example that passes a different direction to each drone:
`for (const dir of [North, East, South, West]) {
	spawnDrone(() => {
		move(dir);
		doAFlip();
	});
}`

The arrow function can use the variables around it, like the loop variable `dir`. Each drone gets the direction of its own iteration.

<spoiler=show hint> Check out this super useful parallel `forAll` function, which takes any function and runs it on every farm tile. It makes use of all available drones to do so.

`function forAll(f) {
	function row() {
		for (let i = 0; i < getWorldSize() - 1; i++) {
			f();
			move(East);
		}
		f();
	}
	for (let i = 0; i < getWorldSize(); i++) {
		if (spawnDrone(row) === null) {
			row();
		}
		move(North);
	}
}

forAll(harvest);`

One particularly useful pattern is to spawn a drone if one is available and otherwise do it yourself.

`function task() {
	harvest();
}

if (spawnDrone(task) === null) {
	task();
}`
</spoiler>

## Awaiting Another Drone
Use the `waitFor(drone)` function to wait for another drone to finish. You receive the `drone` handle when you spawn the drone.
`waitFor(drone)` returns the return value of the function that the other drone was running.

`function getEntityTypeInDirection(dir) {
	move(dir);
	return getEntityType();
}

const drone = spawnDrone(() => getEntityTypeInDirection(North));
console.log(waitFor(drone));`

`spawnDrone()` only takes functions without parameters, so the arrow function `() => getEntityTypeInDirection(North)` wraps the call. An arrow function without a code block returns the value of its expression.

Note that spawning drones takes time, so it's not a good idea to spawn a new drone for every little thing.

You can use `hasFinished(drone)` to check if the drone has finished without waiting for it.

## No Shared Memory
Each drone has its own memory and cannot directly read or write another drone's globals.

`let x = 0;

function increment() {
	x++;
}

waitFor(spawnDrone(increment));
console.log(x);`

This will print `0` because the new drone incremented its own copy of the global `x`, which does not affect the first drone's `x`.

## Race Conditions
Multiple drones can interact with the same farm tile at the same time. If two drones interact with the same tile during the same tick, both interactions will occur, but the results may differ based on the order of the interactions.

For example, imagine that drones `0` and `1` are both over the same tree that is almost fully grown.
Drone `0` calls
`useItem(Items.Fertilizer);`
Drone `1` calls
`harvest();`

If these actions occur at the same time, the tree will first be fertilized and then harvested. In that case, you will receive wood from it. However, if Drone `1` is slightly faster, the tree will be harvested before it is fertilized, and you will not receive the wood.
This is called a "race condition." It is a common issue in parallel programming, where the result depends on the order in which operations are performed.

Here's another problematic situation that can happen when multiple drones run the same code simultaneously at the same position.
`if (getWater() < 0.5) {
	useItem(Items.Water);
}`

If multiple drones run this simultaneously, they will all run the first line, which puts them into the `if` block. Then, they will all use water, wasting a lot of it.
By the time a drone reaches the second line, `getWater()` might no longer be less than `0.5` because another drone has watered the tile in the meantime.