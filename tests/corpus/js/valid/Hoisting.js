// Function declarations are hoisted: they can be called before they are defined.
let rows = 0;
farmRow(getWorldSize());
console.log("rows done:", rows);

function farmRow(length) {
	for (let i = 0; i < length; i++) {
		tendTile();
		move(East);
	}
	rows += 1;
}

function tendTile() {
	if (canHarvest()) {
		harvest();
	}
}
