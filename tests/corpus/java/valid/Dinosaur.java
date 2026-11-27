import java.util.*;

// Wear the dinosaur hat and steer toward apples using measure().
public class Dinosaur {
    static List<Direction> path = new ArrayList<>();

    static boolean step(Direction d) {
        if (move(d)) {
            path.add(d);
            return true;
        }
        return false;
    }

    public static void main(String[] args) {
        changeHat(Hats.Dinosaur_Hat);
        while (true) {
            Object target = measure();
            if (target == null) {
                break;
            }
            Position apple = (Position) target;
            boolean moved = false;
            if (getPosX() < apple.x()) {
                moved = step(East);
            } else if (getPosX() > apple.x()) {
                moved = step(West);
            } else if (getPosY() < apple.y()) {
                moved = step(North);
            } else {
                moved = step(South);
            }
            if (!moved) {
                break;
            }
        }
        quickPrint("steps", path.size());
        changeHat(Hats.Straw_Hat);
    }
}
