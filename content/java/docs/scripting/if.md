# If
You can use `if`, `else if` and `else` to run code conditionally.

`if (condition1) {
    doAFlip();
} else if (condition2) {
    harvest();
} else {
    doAFlip();
    harvest();
}`

## Syntax
`if`s allow you to run code only if some condition is `true`. They are like a `while` loop that doesn't loop.
The `if` takes a condition in parentheses just like the `while` loop and executes the code block if the condition evaluates to `true`:

`// do a flip if condition is true
if (condition) {
    doAFlip();
}`

You can also add an `else` after the if that defines code to be executed if the condition evaluates to `false`.

Do a flip if `condition` is true, otherwise harvest.
`if (condition) {
    doAFlip();
} else {
    harvest();
}`

You can put another `if` inside the `else` block:

`if (condition1) {
    // a
} else {
    if (condition2) {
        // b
    } else {
        // c
    }
}`

This can be shortened by writing `else if`:

`if (condition1) {
    // a
} else if (condition2) {
    // b
} else {
    // c
}`

You can add as many `else if` blocks as you want. Only the first block whose condition is `true` runs.
