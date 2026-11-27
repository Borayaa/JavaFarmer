import java.util.*;

// Measure every sunflower, then harvest the ones with the most petals first.
Map<Integer, List<Position>> byPetals = new HashMap<>();

void main() {
    int n = getWorldSize();
    for (int x = 0; x < n; x++) {
        for (int y = 0; y < n; y++) {
            if (getGroundType() != Grounds.Soil) till();
            plant(Entities.Sunflower);
            int petals = (int) measure();
            List<Position> list = byPetals.getOrDefault(petals, new ArrayList<>());
            list.add((Position) measure(North));
            byPetals.put(petals, list);
            move(North);
        }
        move(East);
    }
    for (int petals = 15; petals >= 7; petals--) {
        if (!byPetals.containsKey(petals)) {
            continue;
        }
        for (Position p : byPetals.get(petals)) {
            quickPrint("petals", petals, "at", p.x(), p.y());
        }
    }
    System.out.println("sizes: " + byPetals.size());
}
