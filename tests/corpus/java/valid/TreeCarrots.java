// Trees on a checkerboard, carrots in between, water when dry.
public class TreeCarrots {
    static final double WATER_LEVEL = 0.75;

    public static void main(String[] args) {
        clear();
        while (true) {
            for (int i = 0; i < getWorldSize() * getWorldSize(); i++) {
                tend();
                if (getPosY() == getWorldSize() - 1) {
                    move(East);
                }
                move(North);
            }
        }
    }

    static void tend() {
        Entity here = getEntityType();
        if (here != null && canHarvest()) {
            harvest();
        }
        if (getWater() < WATER_LEVEL && numItems(Items.Water) > 0) {
            useItem(Items.Water);
        }
        if ((getPosX() + getPosY()) % 2 == 0) {
            plant(Entities.Tree);
        } else {
            if (getGroundType() != Grounds.Soil) {
                till();
            }
            plant(Entities.Carrot);
        }
    }
}
