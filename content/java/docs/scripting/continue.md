# Continue
`continue` allows stopping the current iteration of a loop and jumping to the next iteration of the innermost loop.

`while (true) {
    if (!canHarvest()) {
        continue;
    }
    harvest();
}`

This code only calls `harvest()` when `canHarvest()` is `true`. 
It has the same effect as

`while (true) {
    if (canHarvest()) {
        harvest();
    }
}`

<unlock=for>It also works on `for` loops.

`for (int i = 0; i < 10; i++) {
    if (i % 2 == 0) {
        continue;
    }
    System.out.println(i);
}`

This runs all `10` iterations of the loop, but the `System.out.println(i);` statement is skipped for every even `i`. So it prints `1, 3, 5, 7, 9`.
In a classic `for` loop, `continue` still runs the update part (`i++`) before the next iteration.

In nested loops `continue` always affects the innermost loop.

`for (int i = 0; i < 10; i++) {
    for (int j = 0; j < 10; j++) {
        System.out.println("this is printed 100 times");
        if (j >= 0) {
            continue;
        }
        System.out.println("this is never printed");
    }
    System.out.println("this is printed 10 times");
}`
</unlock>
