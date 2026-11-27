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
	std::cout << "this is never printed" << std::endl;
}`

This runs all `10` iterations of the loop, but the `std::cout` statement after the `continue` is always skipped.
In a classic `for` loop, `continue` still runs the update part (`i++`) before the next iteration.

In nested loops `continue` always affects the innermost loop.

`for (int i = 0; i < 10; i++) {
	for (int j = 0; j < 10; j++) {
		std::cout << "this is printed 100 times" << std::endl;
		continue;
		std::cout << "this is never printed" << std::endl;
	}
	std::cout << "this is printed 10 times" << std::endl;
}`
</unlock>