// Compact source file using this., static members, var and final.
static int harvested = 0;
final String label = "farm";

void harvestIfReady() {
    if (canHarvest()) {
        harvest();
        this.harvested += 1;
    }
}

void main() {
    var size = getWorldSize();
    for (var i = 0; i < size; i++) {
        this.harvestIfReady();
        move(North);
    }
    Runnable report = () -> quickPrint(label, harvested);
    report.run();
    double avg = harvested / (double) size;
    quickPrint("avg", avg, "rounded", Math.round(avg), "floor", Math.floor(avg));
    spawnDrone(this::harvestIfReady);
}
