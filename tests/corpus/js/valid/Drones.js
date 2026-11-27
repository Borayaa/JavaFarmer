// Every drone farms one column. The arrow function captures the column number.
function farmColumn(column) {
	for (let i = 0; i < getWorldSize(); i++) {
		if (canHarvest()) {
			harvest();
		}
		move(North);
	}
	quickPrint("column done", column);
}

clear();
for (let col = 0; col < getWorldSize(); col++) {
	const myColumn = col;
	const drone = spawnDrone(() => farmColumn(myColumn));
	if (drone === null) {
		farmColumn(myColumn);
	}
	move(East);
}
while (numDrones() > 1) {
	doAFlip();
}
