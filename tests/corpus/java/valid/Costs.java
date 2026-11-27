import java.util.*;

// Check what we can afford before unlocking.
boolean canAfford(Unlock u) {
    Map<Item, Double> cost = getCost(u);
    if (cost == null) {
        return false;
    }
    for (Item item : cost.keySet()) {
        if (numItems(item) < cost.get(item)) {
            return false;
        }
    }
    return true;
}

void main() {
    Unlock[] wanted = {Unlocks.Speed, Unlocks.Expand, Unlocks.Plant};
    for (Unlock u : wanted) {
        if (canAfford(u) && numUnlocked(u) < 5) {
            unlock(u);
        }
    }
    Companion c = getCompanion();
    if (c != null) {
        quickPrint(c.entity(), c.x(), c.y());
    }
    changeHat(Hats.Straw_Hat);
    setExecutionSpeed(2);
    double t = getTime();
    quickPrint("ticks", getTickCount(), "time", t);
}
