# Comments
Comments are parts of the code that are ignored during execution.
Comments can be added using `//`. Anything on the same line after the `//` is a comment and will be ignored.

`// this is a comment
_ = farm.harvest(); // this is also a comment`

This can be useful to add notes to the code, and also to temporarily disable parts of the code without deleting them.
Zig only has line comments. There are no block comments like `/* */` in other languages: to disable several lines, put `//` in front of each of them.

Any comment on the line before a function definition will be part of the popup information that appears when you mouse over the function name.
In Zig such a comment is usually written with three slashes `///`. This is called a doc comment.
`/// This function does nothing.
fn f() void {}`
