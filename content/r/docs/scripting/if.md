# If
You can use `if`, `else if` and `else` to run code conditionally.

`if (can_harvest()) {
	harvest()
} else if (get_entity_type() == Entities$Bush) {
	do_a_flip()
} else {
	do_a_flip()
	harvest()
}`

## Syntax
`if`s allow you to run code only if some condition is `TRUE`. They are like a `while` loop that doesn't loop.
The `if` takes a condition in parentheses just like the `while` loop and executes the if code block (between `{` and `}`) if the condition evaluates to `TRUE`:

Do a flip if `can_harvest()` is `TRUE`:
`if (can_harvest()) {
	do_a_flip()
}`

You can also add an `else` after the if that defines code to be executed if the condition evaluates to `FALSE`.

Do a flip if `can_harvest()` is `TRUE`, otherwise harvest.
`if (can_harvest()) {
	do_a_flip()
} else {
	harvest()
}`

Write `else` on the same line as the closing `}` of the `if` block, like `} else {`. Otherwise R thinks the `if` is already finished.

`else if` lets you check another condition when the first one was `FALSE`.

`if (can_harvest()) {
	harvest()
} else {
	if (get_water() < 0.5) {
		use_item(Items$Water)
	} else {
		do_a_flip()
	}
}`

can be shortened to:

`if (can_harvest()) {
	harvest()
} else if (get_water() < 0.5) {
	use_item(Items$Water)
} else {
	do_a_flip()
}`

When a block has only one statement, you can leave out the curly braces: `if (can_harvest()) harvest()`.
