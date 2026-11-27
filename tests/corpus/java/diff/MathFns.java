// Math.* lowering and number formatting.
public class MathFns {
    static final int LIMIT = 4;

    public static void main(String[] args) {
        System.out.println(Math.min(3, 8) + " " + Math.max(-2, -9) + " " + Math.abs(-6));
        System.out.println(Math.floor(2.7) + " " + Math.floor(-2.2) + " " + Math.ceil(2.2) + " " + Math.ceil(-2.7));
        System.out.println(Math.round(2.5) + " " + Math.round(-2.5) + " " + Math.round(2.4));
        System.out.println(Math.sqrt(16) + " " + Math.pow(2, 10) + " " + Math.pow(2.0, 0.5));
        System.out.println(Math.max(1.5, 2) + " " + Math.min(LIMIT, 10));
        double avg = (3 + 4) / 2.0;
        System.out.println(avg);
        System.out.println(7 / 2 + 7 / 2.0);
        System.out.println(String.valueOf(42) + 1);
        System.out.println(1 + 2 + "x" + 1 + 2);
        System.out.println(fib(15));
        System.out.println(Integer.MAX_VALUE);
        boolean b = !(3 > 2) || 2 == 2 && !false;
        System.out.println(b);
        System.out.println((1 < 2) == (2 < 3));
    }

    static int fib(int n) {
        if (n < 2) {
            return n;
        }
        return fib(n - 1) + fib(n - 2);
    }
}
