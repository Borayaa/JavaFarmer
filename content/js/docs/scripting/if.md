# If
You can use `if`, `else if` and `else` to run code conditionally.

`if (canHarvest()) {
	harvest();
} else if (getEntityType() === null) {
	plant(Entities.Bush);
} else {
	doAFlip();
}`

## Syntax
`if`s allow you to run code only if some condition is `true`. They are like a `while` loop that doesn't loop.
The `if` takes a condition in parentheses just like the `while` loop and executes the if code block if the condition evaluates to `true`:

`// do a flip if the plant is ready
if (canHarvest()) {
	doAFlip();
}`

You can also add an `else` after the if that defines code to be executed if the condition evaluates to `false`.

Do a flip if the plant is ready, otherwise harvest.
`if (canHarvest()) {
	doAFlip();
} else {
	harvest();
}`

`else if` is just an `else` followed by another `if`.

`if (canHarvest()) {
	// a
} else {
	if (canMove(North)) {
		// b
	} else {
		// c
	}
}`

can be shortened to:

`if (canHarvest()) {
	// a
} else if (canMove(North)) {
	// b
} else {
	// c
}`

The braces `{ }` around a code block are optional if it is only one statement, but it's a good habit to always write them.