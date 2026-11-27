// Replant dead pumpkins until the whole field is one big pumpkin.
const deadX = [];
const deadY = [];

function goTo(x, y) {
	while (getPosX() !== x) {
		move(East);
	}
	while (getPosY() !== y) {
		move(North);
	}
}

function plantAll() {
	const n = getWorldSize();
	for (let x = 0; x < n; x++) {
		for (let y = 0; y < n; y++) {
			if (getGroundType() !== Grounds.Soil) {
				till();
			}
			plant(Entities.Pumpkin);
			deadX.push(getPosX());
			deadY.push(getPosY());
			move(North);
		}
		move(East);
	}
}

clear();
plantAll();
while (deadX.length > 0) {
	const x = deadX.shift();
	const y = deadY.shift();
	goTo(x, y);
	if (getEntityType() === Entities.Pumpkin && canHarvest()) {
		continue;
	}
	plant(Entities.Pumpkin);
	deadX.push(x);
	deadY.push(y);
}
harvest();
