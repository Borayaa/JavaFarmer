import java.util.ArrayList;
import java.util.List;

// Replant dead pumpkins until the whole field is one big pumpkin.
List<Integer> deadX = new ArrayList<>();
List<Integer> deadY = new ArrayList<>();

void goTo(int x, int y) {
    while (getPosX() != x) {
        move(East);
    }
    while (getPosY() != y) {
        move(North);
    }
}

void plantAll() {
    int n = getWorldSize();
    for (int x = 0; x < n; x++) {
        for (int y = 0; y < n; y++) {
            if (getGroundType() != Grounds.Soil) {
                till();
            }
            plant(Entities.Pumpkin);
            deadX.add(getPosX());
            deadY.add(getPosY());
            move(North);
        }
        move(East);
    }
}

void main() {
    clear();
    plantAll();
    while (!deadX.isEmpty()) {
        int x = deadX.remove(0);
        int y = deadY.remove(0);
        goTo(x, y);
        if (getEntityType() == Entities.Pumpkin && canHarvest()) {
            continue;
        }
        plant(Entities.Pumpkin);
        deadX.add(x);
        deadY.add(y);
    }
    harvest();
}
