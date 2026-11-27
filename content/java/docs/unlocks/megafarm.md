# Mega Farm
This incredibly powerful unlock gives you access to multiple drones. 

As before, you still start with just one drone. Additional drones must first be spawned and will disappear after the program terminates.
Each drone runs its own separate program. New drones can be spawned using the `spawnDrone(task)` method. The task can be a method reference like `this::droneTask`, a lambda like `() -> harvest()` or the name of a file as a `String`.

`void main() {
    spawnDrone(this::droneTask);
}

void droneTask() {
    move(North);
    doAFlip();
}`

This spawns a new drone in the same position as the drone that ran the `spawnDrone()` command. The new drone then begins executing the specified method. After it is done, it will disappear automatically.

`this::droneTask` is a method reference. It passes the method `droneTask` itself to `spawnDrone` without calling it. (In a file written as a class, use the class name instead: `MyFile::droneTask`.)
To run another file's code on the new drone, pass one of its methods: `spawnDrone(OtherFile::work)`.

Drones do not collide with each other. 

Use `maxDrones()` to get the maximum number of drones that can be spawned.
Use `numDrones()` to get the number of drones that are already on the farm.


## Example:
`void main() {
    while (true) {
        if (spawnDrone(this::harvestColumn) != null) {
            move(East);
        }
    }
}

void harvestColumn() {
    for (int i = 0; i < getWorldSize(); i++) {
        harvest();
        move(North);
    }
}`

This will cause your first drone to move horizontally and spawn more drones. The spawned drones will then move vertically and harvest everything in their path.

If all available drones have already been spawned, `spawnDrone()` will do nothing and return `null`.

## Lambdas
A lambda is a small method without a name that you write right where you need it: `() -> { ... }`. The `()` means it takes no arguments, and the code after the arrow `->` is what the drone will run.
Here's an example that passes a different direction to each drone:
`for (Direction dir : List.of(North, East, South, West)) {
    spawnDrone(() -> {
        move(dir);
        doAFlip();
    });
}`

A lambda can use the local variables around it, as long as they are never changed after they got their value. The loop variable `dir` of an enhanced `for` loop is fine. To use the counter `i` of a classic `for` loop, copy it into a new variable first: `int row = i;`.

<spoiler=show hint> Check out this super useful parallel `forAll` method, which takes any task and runs it on every farm tile. It makes use of all available drones to do so.

`void forAll(Runnable task) {
    Runnable row = () -> {
        for (int i = 0; i < getWorldSize() - 1; i++) {
            task.run();
            move(East);
        }
        task.run();
    };
    for (int i = 0; i < getWorldSize(); i++) {
        if (spawnDrone(row) == null) {
            row.run();
        }
        move(North);
    }
}

void main() {
    forAll(() -> harvest());
}`

`Runnable` is the type of a task that takes no arguments and returns nothing. `task.run()` runs it.

One particularly useful pattern is to spawn a drone if one is available and otherwise do it yourself.

`if (spawnDrone(task) == null) {
    task.run();
}`
</spoiler>

## Awaiting Another Drone
Use the `waitFor(drone)` method to wait for another drone to finish. You receive the `Drone` handle when you spawn the drone.
`waitFor(drone)` returns the return value of the method that the other drone was running. Its type is `Object`, so you cast it to the type you expect.

If the drone should return a value, use a `Supplier` instead of a `Runnable`. `Supplier<Entity>` is a task that returns an `Entity`:

`Entity getEntityTypeInDirection(Direction dir) {
    move(dir);
    return getEntityType();
}

void main() {
    Supplier<Entity> task = () -> getEntityTypeInDirection(North);
    Drone drone = spawnDrone(task);
    Entity result = (Entity) waitFor(drone);
    System.out.println(result);
}`

Note that spawning drones takes time, so it's not a good idea to spawn a new drone for every little thing.

You can use `hasFinished(drone)` to check if the drone has finished without waiting for it.

## No Shared Memory
Each drone has its own memory and cannot directly read or write another drone's fields.

`int x = 0;

void increment() {
    x++;
}

void main() {
    waitFor(spawnDrone(this::increment));
    System.out.println(x);
}`

This will print `0` because the new drone incremented its own copy of the field `x`, which does not affect the first drone's `x`.

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
