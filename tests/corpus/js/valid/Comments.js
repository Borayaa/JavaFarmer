/**
 * A program with every kind of comment.
 */

// a line comment
const n = 3; // trailing comment

/* a block comment */
function twice(x) {
	// inside a function
	return x * 2; /* trailing block */
}

for (let i = 0; i < n; i++) {
	quickPrint(twice(i));
	// at the end of a block
}
// at the end of the file
