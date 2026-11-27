// ERROR: add(...) can only be used as a separate statement in JavaFarmer
// AT: list.add(1)
import java.util.*;

void main() {
    List<Integer> list = new ArrayList<>();
    boolean ok = list.add(1);
}
