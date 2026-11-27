// Closures read and change top-level variables; function values are passed around.
let counter = 0;

function makeAdder(n) {
	return (x) => x + n;
}

function twice(f, x) {
	return f(f(x));
}

const add5 = makeAdder(5);
console.log(add5(1), twice(add5, 0), twice((x) => x * 3, 2));
const bump = () => {
	counter += 1;
};
bump();
bump();
console.log("counter", counter);
function outer() {
	const base = 100;
	function inner(v) {
		return base + v;
	}
	return inner(1) + inner(2);
}
console.log(outer());
const ops = [(a) => a + 1, (a) => a * 10];
let v = 1;
for (const op of ops) {
	v = op(v);
}
console.log(v);
