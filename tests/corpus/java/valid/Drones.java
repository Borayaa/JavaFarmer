// Every drone farms one column. Lambdas capture the loop-local column number.
void farmColumn(int column) {
    for (int i = 0; i < getWorldSize(); i++) {
        if (canHarvest()) {
            harvest();
        }
        move(North);
    }
    quickPrint("column done", column);
}

void main() {
    clear();
    for (int col = 0; col < getWorldSize(); col++) {
        int myColumn = col;
        Drone d = spawnDrone(() -> farmColumn(myColumn));
        if (d == null) {
            farmColumn(myColumn);
        }
        move(East);
    }
    while (numDrones() > 1) {
        doAFlip();
    }
}
