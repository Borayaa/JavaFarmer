# Break
`break` allows stopping a loop early. When the `break` statement is reached it will immediately exit the innermost loop and start running the code after the loop.

`for (let i = 0; i < 10; i++) {
	console.log(i);
	break;
}`
This only prints `0`, because `i` is `0` in the first iteration of the loop and then the `break` statement ends the loop.

It also works on `while` loops.

`while (true) {
	if (canHarvest()) {
		break;
	}
}`

This code runs the `while` loop until `canHarvest()` is `true`. 
It has the same effect as

`while (!canHarvest()) {
}`

In nested loops `break` always exits the innermost loop.

`for (let i = 0; i < 10; i++) {
	for (let j = 0; j < 10; j++) {
		break;
		console.log("this is never printed");
	}
	console.log("this is printed 10 times");
}`

Don't forget the semicolon: `break;` is a statement of its own.