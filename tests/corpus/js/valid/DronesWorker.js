// spawnDrone takes a function: a declared function, a function expression or an arrow function.
let started = 0;

function worker() {
	for (let i = 0; i < 10; i++) {
		if (canHarvest()) {
			harvest();
		}
		move(North);
	}
}

if (spawnDrone(worker) !== null) {
	started++;
}
const helper = spawnDrone(function () {
	while (!canHarvest()) {
		doAFlip();
	}
	harvest();
});
if (helper !== null) {
	waitFor(helper);
}
spawnDrone(() => {
	started += 1;
	petThePiggy();
});
quickPrint("drones started", started, "max", maxDrones());
