# If
You can use `if`, `else if` and `else` to run code conditionally.

`if condition1 {
	do_a_flip();
} else if condition2 {
	harvest();
} else {
	do_a_flip();
	harvest();
}`

## Syntax
`if`s allow you to run code only if some condition is `true`. They are like a `while` loop that doesn't loop.
The `if` takes a condition just like the `while` loop and executes the code block if the condition evaluates to `true`. Rust needs no parentheses around the condition, but the braces `{ }` are always required:

`// do a flip if condition is true
if condition {
	do_a_flip();
}`

The condition must be a `bool`. Rust does not treat other values as true or false: `if 1 {` is an error, write a comparison like `if x != 0 {` instead.

You can also add an `else` after the if that defines code to be executed if the condition evaluates to `false`.

Do a flip if `condition` is true, otherwise harvest.
`if condition {
	do_a_flip();
} else {
	harvest();
}`

You can put another `if` inside the `else` block:

`if condition1 {
	// a
} else {
	if condition2 {
		// b
	} else {
		// c
	}
}`

This can be shortened by writing `else if`:

`if condition1 {
	// a
} else if condition2 {
	// b
} else {
	// c
}`

You can add as many `else if` blocks as you want. Only the first block whose condition is `true` runs.
