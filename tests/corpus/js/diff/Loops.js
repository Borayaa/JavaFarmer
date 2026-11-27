// for, while, do...while, for...of, break and continue.
let sum = 0;
for (let i = 0; i < 10; i++) {
	if (i % 3 === 0) {
		continue;
	}
	sum += i;
}
console.log("sum", sum);
let n = 27;
let steps = 0;
while (n !== 1) {
	n = n % 2 === 0 ? n / 2 : 3 * n + 1;
	steps++;
}
console.log("collatz steps", steps);
let k = 0;
do {
	k += 5;
} while (k < 12);
console.log("k", k);
for (let i = 10; i > 0; i -= 3) {
	console.log("down", i);
}
for (const x of [4, 8, 15]) {
	if (x > 10) break;
	console.log("item", x);
}
for (const ch of "abc") {
	console.log(ch);
}
let total = 0;
outerLoop();
function outerLoop() {
	for (let a = 0; a < 4; a++) {
		for (let b = 0; b < 4; b++) {
			if (b > a) break;
			total += a * b;
		}
	}
}
console.log("total", total);
