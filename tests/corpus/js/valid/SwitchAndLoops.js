// switch with stacked cases, return inside a case, and continue inside a loop.
function classify(entity) {
	switch (entity) {
		case Entities.Grass:
		case Entities.Bush:
			return "plant";
		case Entities.Tree:
			return "tree";
		default:
			return "other";
	}
}

for (let i = 0; i < getWorldSize(); i++) {
	const kind = getEntityType();
	switch (classify(kind)) {
		case "plant":
			if (canHarvest()) {
				harvest();
			}
			break;
		case "tree":
			continue;
		default:
			plant(Entities.Grass);
	}
	move(North);
}
