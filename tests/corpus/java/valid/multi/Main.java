// Uses helpers from two other windows.
void main() {
    Utils.goTo(0, 0);
    int n = Utils.size();
    for (int i = 0; i < n; i++) {
        Farming.tendTile();
        Utils.moves++;
        move(North);
    }
    quickPrint("moves:", Utils.moves, "planted:", Farming.planted);
    Utils.moves = 0;
    spawnDrone(Farming::tendTile);
}
