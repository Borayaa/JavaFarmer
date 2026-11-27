// Check what we can afford before unlocking.
function canAfford(thing) {
	const cost = getCost(thing);
	if (cost === null) {
		return false;
	}
	for (const item of cost.keys()) {
		if (numItems(item) < cost.get(item)) {
			return false;
		}
	}
	return true;
}

const wanted = [Unlocks.Speed, Unlocks.Expand, Unlocks.Plant];
for (const u of wanted) {
	if (canAfford(u) && numUnlocked(u) < 5) {
		unlock(u);
	}
}
changeHat(Hats.Straw_Hat);
setExecutionSpeed(2);
const t = getTime();
quickPrint("ticks", getTickCount(), "time", t);
