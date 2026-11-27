// continue in classic for (range and while forms), do-while with continue, break, nested loops.
void main() {
    int sum = 0;
    for (int i = 0; i < 10; i++) {
        if (i % 3 == 0) continue;
        sum += i;
    }
    System.out.println("range-for continue: " + sum);

    sum = 0;
    for (int i = 0; i < 20; i = i + 2) {
        if (i == 6) continue;
        if (i > 14) break;
        sum += i;
    }
    System.out.println("while-for continue: " + sum);

    int k = 0;
    int visits = 0;
    do {
        k++;
        if (k % 2 == 0) continue;
        visits++;
    } while (k < 9);
    System.out.println("do-while: " + k + " " + visits);

    int n = 0;
    do {
        n += 5;
    } while (false);
    System.out.println("do once: " + n);

    for (int i = 10; i >= 0; i -= 4) System.out.println("down " + i);
    for (int i = 3; i <= 5; i++) System.out.println("incl " + i);
    for (int i = 5; i > 2; i--) System.out.println("excl " + i);

    int count = 0;
    for (int i = 0; i < 4; i++) {
        for (int j = 0; j < 4; j++) {
            if (j > i) break;
            if (j == 1) continue;
            count++;
        }
    }
    System.out.println("nested: " + count);

    int w = 0;
    while (true) {
        w++;
        if (w >= 5) break;
    }
    System.out.println("while-true: " + w);

    int a = 0, b = 10;
    for (; a < b; a++, b--) {
        if (a == 2) continue;
    }
    System.out.println("two vars: " + a + " " + b);

    int limit = 3;
    for (int i = 0; i < limit; i++) {
        limit = 2;
        System.out.println("limit loop " + i);
    }
}
