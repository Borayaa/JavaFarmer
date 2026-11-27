// ERROR: assigning a captured variable inside a closure is not supported in JavaFarmer
// AT: count
function farm() {
	let count = 0;
	spawnDrone(() => {
		count += 1;
	});
	return count;
}
