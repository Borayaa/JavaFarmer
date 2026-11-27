# Break
`break` allows stopping a loop early. When the `break` statement is reached it will immediately exit the innermost loop and start running the code after the loop.

`for (i in 1:10) {
	break
}
print(i)`
This prints `1` because `i` is `1` in the first iteration of the loop and then the break statement ends the loop.

It also works on `while` and `repeat` loops.

`while (TRUE) {
	if (can_harvest()) {
		break
	}
}`

This code runs the `while` loop until `can_harvest()` is `TRUE`. 
It has the same effect as

`while (!can_harvest()) {
}`

A `repeat` loop runs forever, so `break` is the only way to leave it:

`repeat {
	if (can_harvest()) {
		break
	}
}`

In nested loops `break` always exits the innermost loop.

`for (i in 1:10) {
	for (j in 1:10) {
		break
		print("this is never printed")
	}
	print("this is printed 10 times")
}`
