// ?: lowering: declarations, assignments, returns, arguments, nesting, compound assignment.
int sign(int x) {
    return x > 0 ? 1 : x < 0 ? -1 : 0;
}

int twice(int v) {
    return v * 2;
}

void main() {
    for (int i = -2; i <= 2; i++) {
        int s = sign(i);
        String word = s == 0 ? "zero" : "nonzero";
        System.out.println(i + " " + s + " " + word);
    }
    int a = 5;
    a = a > 3 ? a - 3 : a + 3;
    System.out.println(a);
    System.out.println(twice(a > 1 ? 10 : 20));
    System.out.println(a == 2 ? "two" : "not two");
    int acc = 0;
    for (int i = 0; i < 5; i++) {
        acc += i % 2 == 0 ? i : -i;
    }
    System.out.println(acc);
    int[] arr = new int[3];
    arr[1] = acc > 0 ? 7 : 8;
    System.out.println(arr[0] + " " + arr[1] + " " + arr[2]);
    double d = a > 0 ? 1 : 2.5;
    System.out.println(d);
}
