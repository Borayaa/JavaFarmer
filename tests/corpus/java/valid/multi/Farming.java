// A library window in compact form (no main).
int planted = 0;

void tendTile() {
    if (canHarvest()) {
        harvest();
    }
    if (plant(Entities.Bush)) {
        planted++;
    }
}
