// switch statements: colon form with grouped labels, arrow form, strings, default in the middle.
String name(int d) {
    switch (d) {
        case 0:
            return "zero";
        case 1:
        case 2:
            return "small";
        default:
            return "other";
        case -1:
            return "minus one";
    }
}

int code(String s) {
    int r = 0;
    switch (s) {
        case "hay" -> r = 1;
        case "wood", "tree" -> {
            r = 2;
            r = r * 10;
        }
        default -> r = -1;
    }
    return r;
}

void main() {
    for (int i = -1; i < 5; i++) {
        System.out.println(i + " " + name(i));
    }
    System.out.println(code("hay") + " " + code("tree") + " " + code("x"));
    int total = 0;
    for (int i = 0; i < 6; i++) {
        switch (i % 3) {
            case 0:
                total += 1;
                break;
            case 1:
                if (i > 3) {
                    total += 100;
                }
                total += 10;
                break;
        }
        if (i == 4) continue;
    }
    System.out.println("total " + total);
    int calls = 0;
    switch (next()) {
        case 1 -> System.out.println("one");
        default -> System.out.println("not one");
    }
}

int counter = 0;

int next() {
    counter++;
    return counter;
}
