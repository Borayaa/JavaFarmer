# Continue
R's word for continue is `next`. It stops the current iteration of a loop and jumps to the next iteration of the innermost loop.

`for (i in 1:10) {
	next
	print("this is never printed")
}`

This runs all `10` iterations of the loop, but the `print` statement after the `next` is always skipped.

It also works on `while` and `repeat` loops.

`while (TRUE) {
	if (!can_harvest()) {
		next
	}
	harvest()
}`

This code only calls `harvest()` when `can_harvest()` is `TRUE`. 
It has the same effect as

`while (TRUE) {
	if (can_harvest()) {
		harvest()
	}
}`

In nested loops `next` always affects the innermost loop.

`for (i in 1:10) {
	for (j in 1:10) {
		print("this is printed 100 times")
		next
		print("this is never printed")
	}
	print("this is printed 10 times")
}`
