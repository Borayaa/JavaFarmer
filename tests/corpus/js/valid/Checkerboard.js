// Grass and bushes in a checkerboard pattern.
const size = getWorldSize();
for (let x = 0; x < size; x++) {
	for (let y = 0; y < size; y++) {
		if (canHarvest()) {
			harvest();
		}
		if ((x + y) % 2 === 0) {
			plant(Entities.Bush);
		} else {
			plant(Entities.Grass);
		}
		move(North);
	}
	move(East);
}
