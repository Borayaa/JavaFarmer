// Follow the wall in a maze, then read the next treasure position with measure().
const dirs = [North, East, South, West];

function turnRight(d) {
	return (d + 1) % 4;
}

function turnLeft(d) {
	return (d + 3) % 4;
}

plant(Entities.Bush);
while (getEntityType() === Entities.Bush) {
	useItem(Items.Weird_Substance, getWorldSize());
}
let dir = 0;
while (getEntityType() !== Entities.Treasure) {
	const right = turnRight(dir);
	if (canMove(dirs[right])) {
		dir = right;
	} else if (!canMove(dirs[dir])) {
		dir = turnLeft(dir);
		continue;
	}
	move(dirs[dir]);
}
const hint = measure();
if (hint !== null) {
	const [nx, ny] = hint;
	quickPrint("next treasure", nx, ny);
}
harvest();
