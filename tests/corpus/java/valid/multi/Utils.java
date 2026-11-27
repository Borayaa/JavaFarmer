public class Utils {
    public static int moves = 0;
    private static int secret = 42;

    public static int size() {
        return getWorldSize();
    }

    public static void goTo(int x, int y) {
        while (getPosX() != x) {
            move(West);
            moves++;
        }
        while (getPosY() != y) {
            move(South);
            moves++;
        }
    }
}
