# Break
`break` allows stopping a loop early. When the `break;` statement is reached it will immediately exit the innermost loop and start running the code after the loop.

`while (true) {
	if (can_harvest()) {
		break;
	}
}
harvest();`

This code runs the `while` loop until `can_harvest()` is `true` and then harvests. 
It has the same effect as

`while (!can_harvest()) {
}
harvest();`

<unlock=for>It also works on `for` loops.

`int i = 0;
for (i = 0; i < 10; i++) {
	break;
}
std::cout << i << std::endl;`
This prints `0` because `i` is `0` in the first iteration of the loop and then the `break` statement ends the loop.
(`i` is declared before the loop here. A variable declared inside `for (int i = 0; ...)` only exists inside the loop.)

In nested loops `break` always exits the innermost loop.

`for (int i = 0; i < 10; i++) {
	for (int j = 0; j < 10; j++) {
		break;
		std::cout << "this is never printed" << std::endl;
	}
	std::cout << "this is printed 10 times" << std::endl;
}`
</unlock>