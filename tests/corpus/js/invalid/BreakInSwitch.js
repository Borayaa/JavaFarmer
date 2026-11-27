// ERROR: break inside a switch case is only supported as the last statement of the case
// AT: break;
const n = 1;
switch (n) {
	case 1:
		if (canHarvest()) {
			break;
		}
		harvest();
		break;
}
