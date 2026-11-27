// Booleans, ===, ternaries, && / || values and null.
const a = 5;
const b = 7;
console.log(a < b, a === 5, a !== b, !(a > b));
console.log(a > 3 && b > 3, a > 6 || b > 6);
console.log(a > b ? "a" : "b", a % 2 === 1 ? "odd" : "even");
const none = null;
console.log(none, none === null, none == undefined);
const pick = none || "fallback";
const keep = a && b;
console.log(pick, keep);
let label;
if (a > 0) {
	label = "positive";
}
console.log(label);
console.log(true, false, [true, null]);
