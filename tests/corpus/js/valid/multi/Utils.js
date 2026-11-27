// A library file: exported functions and variables can be imported by other code windows.
export let moves = 0;
export const SIZE_LIMIT = 32;

export function goTo(x, y) {
	while (getPosX() !== x) {
		move(East);
		moves++;
	}
	while (getPosY() !== y) {
		move(North);
		moves++;
	}
}

export function isEven(n) {
	return n % 2 === 0;
}

function helper() {
	return 1;
}
