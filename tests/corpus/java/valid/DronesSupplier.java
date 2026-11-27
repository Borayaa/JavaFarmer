import java.util.*;

// Drones return a value; the main drone waits for all of them.
public class DronesSupplier {
    static int total = 0;

    static int countCarrots() {
        int found = 0;
        for (int i = 0; i < getWorldSize(); i++) {
            if (getEntityType() == Entities.Carrot) {
                found++;
            }
            move(North);
        }
        return found;
    }

    public static void main(String[] args) {
        List<Drone> drones = new ArrayList<>();
        for (int i = 0; i < maxDrones() - 1; i++) {
            Drone d = spawnDrone(DronesSupplier::countCarrots);
            if (d != null) {
                drones.add(d);
            }
            move(East);
        }
        for (Drone d : drones) {
            total += (int) waitFor(d);
        }
        Supplier<Integer> doubled = () -> total * 2;
        Runnable report = () -> {
            System.out.println("carrots: " + total);
        };
        report.run();
        quickPrint(doubled.get());
    }
}
