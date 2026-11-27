// Measure every sunflower, then harvest the ones with the most petals first.
const byPetals = new Map();
const n = getWorldSize();
for (let x = 0; x < n; x++) {
	for (let y = 0; y < n; y++) {
		if (getGroundType() !== Grounds.Soil) till();
		plant(Entities.Sunflower);
		const petals = measure();
		const list = byPetals.get(petals) ?? [];
		list.push([getPosX(), getPosY()]);
		byPetals.set(petals, list);
		move(North);
	}
	move(East);
}
for (let petals = 15; petals >= 7; petals--) {
	if (!byPetals.has(petals)) {
		continue;
	}
	for (const [x, y] of byPetals.get(petals)) {
		quickPrint("petals", petals, "at", x, y);
	}
}
console.log("sizes: " + byPetals.size);
