// A 2D grid built with Array.from and new Array(n).fill.
const n = getWorldSize();
const grid = Array.from({ length: n }, () => new Array(n).fill(0));
const marks = Array.from({ length: n }, () => false);
for (let x = 0; x < n; x++) {
	for (let y = 0; y < n; y++) {
		grid[x][y] = x * y;
	}
	marks[x] = x % 2 === 0;
}
const row = [...grid[0]];
const copy = grid[1].slice();
row.unshift(-1);
row.splice(1, 0, 99);
const removed = row.splice(2, 1)[0];
copy.splice(0, 1);
quickPrint(grid[n - 1][n - 1], marks[0], row.length, removed, copy.indexOf(0), copy.includes(2), Array(3).fill(7));
