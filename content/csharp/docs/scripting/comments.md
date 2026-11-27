# Comments
Comments are parts of the code that are ignored during execution.
In C# there are two kinds of comments.

A line comment starts with `//`. Anything on the same line after the `//` is a comment and will be ignored.

`// this is a comment
Harvest(); // this is also a comment`

A block comment starts with `/*` and ends with `*/`. It can span several lines.

`/* this comment
   spans several lines */`

Comments can be useful to add notes to the code, and also to temporarily disable parts of the code without deleting them.

Any comment on the line before a method definition will be part of the popup information that appears when you mouse over the method name.
`// This method does nothing.
void F()
{
}`
