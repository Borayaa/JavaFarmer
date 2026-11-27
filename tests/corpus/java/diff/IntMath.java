// Integer division and remainder with negatives, casts, compound assignment.
void main() {
    int[] as = {7, -7, 7, -7, 0, 9, -9, 1};
    int[] bs = {2, 2, -2, -2, 3, 4, 4, -5};
    for (int i = 0; i < as.length; i++) {
        int a = as[i];
        int b = bs[i];
        System.out.println(a + " / " + b + " = " + (a / b) + ", % = " + (a % b));
    }
    double d = -7.9;
    System.out.println((int) d);
    System.out.println((int) 7.9);
    System.out.println((int) (getSome() * 2.5));
    int x = 17;
    x /= 3;
    System.out.println(x);
    x = -17;
    x /= 3;
    System.out.println(x);
    x %= 4;
    System.out.println(x);
    x += 2.7;
    System.out.println(x);
    x *= 3;
    x -= 1;
    System.out.println(x);
    double y = 7;
    y /= 2;
    System.out.println(y);
    System.out.println(5.5 % 2);
    System.out.println(-5.5 % 2);
    System.out.println(1 + 2 * 3 - 4 / 2 + (1 + 2) * 3);
    System.out.println(-x * 2 + (-(x - 1)));
    System.out.println(10 - 2 - 3);
    System.out.println(10 - (2 - 3));
    System.out.println(2 * 3 % 4);
    System.out.println(100 / 10 / 5);
}

int getSome() {
    return 3;
}
