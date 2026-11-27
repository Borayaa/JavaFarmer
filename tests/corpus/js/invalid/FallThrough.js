// ERROR: falling through to the next case is not supported in JavaFarmer
// AT: case 2:
const n = 1;
switch (n) {
	case 1:
		harvest();
	case 2:
		doAFlip();
		break;
}
