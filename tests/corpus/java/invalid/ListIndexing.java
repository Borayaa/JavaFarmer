// ERROR: array required, but List<Integer> found (use .get(i))
// AT: list[0]
import java.util.*;

void main() {
    List<Integer> list = new ArrayList<>();
    int x = list[0];
}
