// Sort a row of cacti with bubble sort using measure() and swap().
function sortRow() {
	const n = getWorldSize();
	let swapped = true;
	while (swapped) {
		swapped = false;
		for (let i = 0; i < n - 1; i++) {
			const here = measure();
			const next = measure(East);
			if (here > next) {
				swap(East);
				swapped = true;
			}
			move(East);
		}
		for (let i = 0; i < n - 1; i++) {
			move(West);
		}
	}
}

const size = getWorldSize();
const heights = new Array(size).fill(0);
for (let x = 0; x < size; x++) {
	if (getGroundType() !== Grounds.Soil) {
		till();
	}
	plant(Entities.Cactus);
	heights[x] = measure();
	move(East);
}
let tallest = 0;
for (const h of heights) {
	tallest = Math.max(tallest, h);
}
quickPrint("tallest", tallest);
sortRow();
harvest();
