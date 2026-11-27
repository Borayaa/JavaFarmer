# Comments
Comments are parts of the code that are ignored during execution.
Comments can be added using `//`. Anything on the same line after the `//` is a comment and will be ignored.

`// this is a comment`

A comment can also span several lines. It starts with `/*` and ends with `*/`:

`/* this comment
   is ignored too */`

This can be useful to add notes to the code, and also to temporarily disable parts of the code without deleting them.

Any comment on the line before a function definition will be part of the popup information that appears when you mouse over the function name.
`// This function does nothing.
void f(void) {
}`
