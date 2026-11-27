// Uses the other code windows of the program.
import * as Utils from "./Utils.js";
import * as Farming from "./Farming.js";

const n = Math.min(getWorldSize(), Utils.SIZE_LIMIT);
for (let x = 0; x < n; x++) {
	for (let y = 0; y < n; y++) {
		Farming.plantTile(x, y);
	}
}
Utils.goTo(0, 0);
quickPrint("moves", Utils.moves, Utils.isEven(n));
