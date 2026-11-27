import java.util.*;

// Lists, maps, sets and arrays lowered to game lists/dicts/sets and helpers.
void main() {
    List<Integer> list = new ArrayList<>();
    for (int i = 0; i < 6; i++) list.add(i * 3);
    list.add(1, 100);
    list.set(0, -1);
    int removed = list.remove(2);
    list.remove(Integer.valueOf(9));
    System.out.println(list + " removed " + removed + " size " + list.size());
    System.out.println(list.contains(100) + " " + list.contains(42) + " " + list.indexOf(12) + " " + list.indexOf(42));
    System.out.println(list.get(list.size() - 1) + " " + list.isEmpty());
    int total = 0;
    for (int v : list) total += v;
    System.out.println("total " + total);
    List<Integer> copy = new ArrayList<>(list);
    copy.clear();
    System.out.println(copy.isEmpty() + " " + list.size());

    Map<String, Integer> stock = new HashMap<>();
    stock.put("hay", 5);
    stock.put("wood", 2);
    stock.put("hay", stock.get("hay") + 1);
    System.out.println(stock.get("hay") + " " + stock.get("nope") + " " + stock.getOrDefault("nope", 0));
    System.out.println(stock.containsKey("wood") + " " + stock.size());
    int sum = 0;
    for (String k : stock.keySet()) sum += stock.get(k);
    for (int v : stock.values()) sum += v;
    System.out.println("sum " + sum);
    System.out.println(stock.remove("wood") + " " + stock.remove("wood") + " " + stock.size());

    Set<Integer> set = new HashSet<>();
    set.add(3);
    set.add(3);
    set.add(4);
    set.remove(4);
    set.remove(99);
    System.out.println(set.size() + " " + set.contains(3) + " " + set.contains(4));

    int[] arr = new int[4];
    arr[2] = 7;
    int[][] grid = new int[3][2];
    grid[1][1] = 5;
    boolean[] flags = new boolean[2];
    String[] names = new String[2];
    System.out.println(arr[2] + arr.length + " " + grid[1][1] + " " + grid.length + " " + grid[0].length + " " + flags[1] + " " + names[0]);
    int[] init = {4, 5, 6};
    int[][] tri = {{1}, {2, 3}};
    System.out.println(init[2] + tri[1][1]);
    List<String> words = List.of("a", "b");
    System.out.println(words.size() + words.get(1));
    String s = "farm";
    System.out.println(s.length() + " " + s.charAt(1) + " " + s.isEmpty() + " " + s.equals("farm") + " " + s.contains("ar"));
}
