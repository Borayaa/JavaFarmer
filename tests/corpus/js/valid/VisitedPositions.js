// Remember visited cells: a position kept in a const works as a Set item and a Map key.
const visited = new Set();
const harvestedAt = new Map();

for (let step = 0; step < getWorldSize() * getWorldSize(); step++) {
	const here = [getPosX(), getPosY()];
	if (!visited.has(here)) {
		visited.add(here);
		if (canHarvest()) {
			harvest();
			harvestedAt.set(here, step);
		}
	}
	const [x, y] = here;
	if (y === getWorldSize() - 1) {
		move(East);
	}
	move(North);
	if (x === 0 && y === 0) {
		console.log(here, visited.size);
	}
}
console.log(harvestedAt.size);
