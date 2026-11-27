// Plant each tile's preferred companion; remember wishes in a Map keyed by position.
const wishes = new Map();
const n = getWorldSize();
const choices = [Entities.Grass, Entities.Bush, Entities.Tree, Entities.Carrot];
for (let i = 0; i < n * n; i++) {
	const want = choices[i % choices.length];
	if (want === Entities.Carrot && getGroundType() !== Grounds.Soil) {
		till();
	}
	plant(want);
	const companion = getCompanion();
	if (companion !== null) {
		const [entity, position] = companion;
		wishes.set(position, entity);
	}
	if (getPosY() === n - 1) {
		move(East);
	}
	move(North);
}
for (const [position, entity] of wishes) {
	const [x, y] = position;
	quickPrint(x, y, entity);
}
quickPrint("wishes:", wishes.size, "first choice:", String(choices[0]));
