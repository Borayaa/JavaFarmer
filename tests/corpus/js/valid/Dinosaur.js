// Wear the dinosaur hat and steer toward apples using measure().
const path = [];

function step(d) {
	if (move(d)) {
		path.push(d);
		return true;
	}
	return false;
}

changeHat(Hats.Dinosaur_Hat);
while (true) {
	const target = measure();
	if (target === null) {
		break;
	}
	const [ax, ay] = target;
	let moved = false;
	if (getPosX() < ax) {
		moved = step(East);
	} else if (getPosX() > ax) {
		moved = step(West);
	} else if (getPosY() < ay) {
		moved = step(North);
	} else {
		moved = step(South);
	}
	if (!moved) {
		break;
	}
}
quickPrint("steps", path.length);
changeHat(Hats.Straw_Hat);
