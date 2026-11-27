# Comments
Comments are parts of the code that are ignored during execution.
Comments can be added using `#`. Anything on the same line after the `#` is a comment and will be ignored.

`harvest() # this is a comment`

A comment can also have a line of its own. R has no comments that span several lines, so start every comment line with its own `#`.

This can be useful to add notes to the code, and also to temporarily disable parts of the code without deleting them.

Any comment on the line before a function definition will be part of the popup information that appears when you mouse over the function name. R programmers often start such documentation lines with `#'` instead of `#`, for example a line `#' Does a flip.` right above `flip <- function() {`.
