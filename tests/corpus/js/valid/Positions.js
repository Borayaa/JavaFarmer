// Positions are [x, y] arrays (the game's tuples): destructure them.
function here() {
	return [getPosX(), getPosY()];
}

const [x, y] = here();
let [a, b] = [1, 2];
[a, b] = [b, a];
const apple = measure();
if (apple !== null) {
	const [ax, ay] = apple;
	quickPrint("apple at", ax, ay, "from", x, y);
}
quickPrint(a, b, here()[0]);
