// Plant a checkerboard of bushes and grass using the drone position.
void main() {
    int size = getWorldSize();
    while (true) {
        for (int x = 0; x < size; x++) {
            for (int y = 0; y < size; y++) {
                if (canHarvest()) {
                    harvest();
                }
                if ((getPosX() + getPosY()) % 2 == 0) {
                    plant(Entities.Bush);
                } else {
                    plant(Entities.Grass);
                }
                move(North);
            }
            move(East);
        }
    }
}
