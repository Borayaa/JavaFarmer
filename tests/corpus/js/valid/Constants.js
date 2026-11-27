// Game constants keep their game names: Entities, Items, Grounds, Unlocks, Hats, Leaderboards.
const wanted = [Items.Hay, Items.Wood, Items.Carrot];
for (const item of wanted) {
	quickPrint(item, numItems(item));
}
if (numUnlocked(Unlocks.Megafarm) > 0) {
	quickPrint("drones", maxDrones());
}
changeHat(Hats.Brown_Hat);
const ground = getGroundType();
if (ground === Grounds.Grassland) {
	till();
}
const run = false;
if (run) {
	leaderboardRun(Leaderboards.Hay, "Hay", 64);
}
const directions = [North, East, South, West];
quickPrint(directions.length, Entities.Sunflower);
