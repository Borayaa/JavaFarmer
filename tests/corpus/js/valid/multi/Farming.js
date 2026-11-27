import { goTo, isEven } from "./Utils.js";

export function plantTile(x, y) {
	goTo(x, y);
	if (canHarvest()) {
		harvest();
	}
	if (isEven(x + y)) {
		plant(Entities.Bush);
	} else {
		plant(Entities.Grass);
	}
}
