// Nested functions and closures. Top-level variables can be changed from any function.
let harvested = 0;

function makeCounter(start) {
	const step = 2;
	function next(x) {
		return x + step;
	}
	return (value) => next(value) + start;
}

const add = makeCounter(10);
const doubler = function (x) {
	return x * 2;
};
const record = () => {
	harvested++;
};
for (let i = 0; i < 3; i++) {
	if (canHarvest()) {
		harvest();
		record();
	}
}
quickPrint(add(1), doubler(4), harvested);
