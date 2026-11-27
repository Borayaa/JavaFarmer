import java.util.*;

// Bubble sort, 2D grid flood counting, recursion, string building, nested ternaries in lambdas.
int[] sorted(int[] input) {
    int[] a = new int[input.length];
    for (int i = 0; i < input.length; i++) a[i] = input[i];
    boolean swapped;
    do {
        swapped = false;
        for (int i = 0; i + 1 < a.length; i++) {
            if (a[i] > a[i + 1]) {
                int t = a[i];
                a[i] = a[i + 1];
                a[i + 1] = t;
                swapped = true;
            }
        }
    } while (swapped);
    return a;
}

int count(int[][] grid, int x, int y) {
    if (x < 0 || y < 0 || x >= grid.length || y >= grid[0].length) return 0;
    if (grid[x][y] != 1) return 0;
    grid[x][y] = 2;
    return 1 + count(grid, x + 1, y) + count(grid, x - 1, y) + count(grid, x, y + 1) + count(grid, x, y - 1);
}

String join(List<Integer> xs) {
    String s = "";
    for (int i = 0; i < xs.size(); i++) {
        String sep = i == 0 ? "" : ",";
        s += sep + xs.get(i);
    }
    return s;
}

void main() {
    int[] data = {5, -3, 9, 0, -3, 12, 7};
    int[] out = sorted(data);
    List<Integer> list = new ArrayList<>();
    for (int v : out) list.add(v);
    System.out.println(join(list));
    int[][] grid = {{1, 1, 0}, {0, 1, 0}, {1, 0, 1}};
    System.out.println(count(grid, 0, 0) + " " + count(grid, 2, 2) + " " + count(grid, 2, 0));
    Supplier<String> classify = () -> data.length > 5 ? (data[0] > 0 ? "big+" : "big-") : "small";
    System.out.println(classify.get());
    int mid = (out[0] + out[out.length - 1]) / 2;
    System.out.println("mid " + mid + " " + (-7 + 2) / 2 + " " + (-7 + 2) % 2);
    Map<String, Integer> counts = new HashMap<>();
    String[] words = {"hay", "wood", "hay", "carrot", "hay"};
    for (String w : words) counts.put(w, counts.getOrDefault(w, 0) + 1);
    System.out.println(counts.get("hay") + " " + counts.get("wood") + " " + counts.get("pumpkin"));
}
