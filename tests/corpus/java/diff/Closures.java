import java.util.*;

// Lambdas capture effectively-final locals; Supplier/Runnable; method references; fields with global.
int calls = 0;

int bump() {
    calls++;
    return calls;
}

int apply(Supplier<Integer> s) {
    return s.get() + s.get();
}

void runTwice(Runnable r) {
    r.run();
    r.run();
}

void main() {
    int base = 10;
    Supplier<Integer> plusOne = () -> base + 1;
    System.out.println(plusOne.get());
    System.out.println(apply(() -> base * 2));
    runTwice(() -> {
        calls += 5;
    });
    System.out.println(calls);
    Supplier<Integer> b = this::bump;
    System.out.println(b.get() + b.get());
    List<Integer> seen = new ArrayList<>();
    for (int i = 0; i < 3; i++) {
        int captured = i * i;
        Runnable r = () -> seen.add(captured);
        r.run();
    }
    System.out.println(seen);
    Supplier<String> greet = () -> {
        String who = "farmer";
        if (base > 5) {
            return "hello " + who;
        }
        return "hi";
    };
    System.out.println(greet.get());
}
