# Break
`break` allows stopping a loop early. When the `break;` statement is reached it will immediately exit the innermost loop and start running the code after the loop.

`while (true) {
    if (canHarvest()) {
        break;
    }
}
harvest();`

This code runs the `while` loop until `canHarvest()` is `true` and then harvests. 
It has the same effect as

`while (!canHarvest()) {
}
harvest();`

<unlock=for>It also works on `for` loops.

`for (int i = 0; i < 10; i++) {
    if (i == 3) {
        break;
    }
    System.out.println(i);
}`
This prints `0`, `1` and `2`. When `i` reaches `3` the `break` ends the loop.

In nested loops `break` always exits the innermost loop.

`for (int i = 0; i < 10; i++) {
    for (int j = 0; j < 10; j++) {
        if (j == 0) {
            break;
        }
        System.out.println("this is never printed");
    }
    System.out.println("this is printed 10 times");
}`

</unlock>Java does not allow code directly after a `break;` in the same block, because it could never run. The compiler reports this as an `unreachable statement`. That's why the examples put `break;` inside an `if`.
