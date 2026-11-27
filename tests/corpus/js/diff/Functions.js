// Recursion, default parameters, functions as values.
function fact(n) {
	if (n <= 1) {
		return 1;
	}
	return n * fact(n - 1);
}

function fib(n) {
	return n < 2 ? n : fib(n - 1) + fib(n - 2);
}

function greet(name, greeting = "hello") {
	return greeting + ", " + name;
}

function apply(f, value) {
	return f(value);
}

console.log(fact(10), fib(15));
console.log(greet("drone"), greet("farmer", "hi"));
console.log(apply(fact, 5), apply((v) => v * v, 7));
const square = (v) => v * v;
console.log(square(12));
