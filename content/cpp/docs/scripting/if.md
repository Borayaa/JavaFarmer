# If
You can use `if`, `else if` and `else` to run code conditionally.

`if (can_harvest()) {
	harvest();
} else if (get_ground_type() == Grounds::Soil) {
	do_a_flip();
} else {
	do_a_flip();
	harvest();
}`

## Syntax
`if`s allow you to run code only if some condition is `true`. They are like a `while` loop that doesn't loop.
The `if` takes a condition in parentheses just like the `while` loop and executes the code block if the condition evaluates to `true`:

`// do a flip if the plant can be harvested
if (can_harvest()) {
	do_a_flip();
}`

You can also add an `else` after the if that defines code to be executed if the condition evaluates to `false`.

Do a flip if the plant can't be harvested yet, otherwise harvest.
`if (!can_harvest()) {
	do_a_flip();
} else {
	harvest();
}`

You can put another `if` inside the `else` block:

`if (can_harvest()) {
	// a
} else {
	if (get_entity_type() == Entities::Grass) {
		// b
	} else {
		// c
	}
}`

This can be shortened by writing `else if`:

`if (can_harvest()) {
	// a
} else if (get_entity_type() == Entities::Grass) {
	// b
} else {
	// c
}`

You can add as many `else if` blocks as you want. Only the first block whose condition is `true` runs.
