// A main function is just a function: call it at the end.
function harvestColumn(height) {
	for (let i = 0; i < height; i++) {
		if (canHarvest()) {
			harvest();
		}
		move(North);
	}
}

function main() {
	const size = getWorldSize();
	for (let x = 0; x < size; x++) {
		harvestColumn(size);
		move(East);
	}
}

main();
