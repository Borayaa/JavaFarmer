# Continue
`continue` allows stopping the current iteration of a loop and jumping to the next iteration of the innermost loop.

`while (true) {
	if (!can_harvest()) {
		continue;
	}
	harvest();
}`

This code only calls `harvest()` when `can_harvest()` is `true`. 
It has the same effect as

`while (true) {
	if (can_harvest()) {
		harvest();
	}
}`

<unlock=for>It also works on `for` loops.

`for (int i = 0; i < 10; i++) {
	continue;
	printf("this is never printed\n");
}`

This runs all `10` iterations of the loop, but the `printf` statement after the `continue` is always skipped.
In a `for` loop, `continue` still runs the update part (`i++`) before the next iteration.

In nested loops `continue` always affects the innermost loop.

`for (int i = 0; i < 10; i++) {
	for (int j = 0; j < 10; j++) {
		printf("this is printed 100 times\n");
		continue;
		printf("this is never printed\n");
	}
	printf("this is printed 10 times\n");
}`
</unlock>
