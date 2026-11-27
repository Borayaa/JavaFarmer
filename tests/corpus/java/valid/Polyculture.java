import java.util.*;

// Plant each tile's preferred companion; remember wishes in a map keyed by position.
Map<Position, Entity> wishes = new HashMap<>();

Position here() {
    // Position is the game's (x, y) tuple; build one by measuring is not possible, so track ints instead
    return null;
}

void main() {
    int n = getWorldSize();
    Entity[] choices = {Entities.Grass, Entities.Bush, Entities.Tree, Entities.Carrot};
    for (int step = 0; step < n * n; step++) {
        Entity want = choices[step % choices.length];
        if (want == Entities.Carrot && getGroundType() != Grounds.Soil) {
            till();
        }
        plant(want);
        Companion c = getCompanion();
        if (c != null) {
            wishes.put(c.position(), c.entity());
        }
        if (getPosY() == n - 1) {
            move(East);
        }
        move(North);
    }
    for (Position p : wishes.keySet()) {
        Entity e = wishes.get(p);
        quickPrint(p.x(), p.y(), e);
    }
    quickPrint("wishes:", wishes.size(), "first choice:", String.valueOf(choices[0]));
}
