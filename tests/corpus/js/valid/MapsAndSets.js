// Map and Set: the game's dictionaries and sets.
const seen = new Set();
const counts = new Map([[Entities.Grass, 0], [Entities.Bush, 0]]);
for (let i = 0; i < 10; i++) {
	const kind = getEntityType();
	if (kind !== null && !seen.has(kind)) {
		seen.add(kind);
	}
	if (counts.has(kind)) {
		counts.set(kind, counts.get(kind) + 1);
	}
	move(North);
}
seen.delete(Entities.Grass);
const copy = new Set(seen);
const visited = new Set([[0, 0], [1, 0]]);
visited.add([getPosX(), getPosY()]);
quickPrint(seen.size, copy.size, visited.has([0, 0]), counts.get(Entities.Bush) ?? 0);
for (const [kind, count] of counts.entries()) {
	quickPrint(kind, count);
}
for (const v of counts.values()) {
	quickPrint(v);
}
counts.delete(Entities.Bush);
counts.clear();
seen.clear();
