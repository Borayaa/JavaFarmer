// Bubble sort, primes and binary search on arrays.
function sort(a) {
	const n = a.length;
	for (let i = 0; i < n; i++) {
		for (let j = 0; j < n - 1 - i; j++) {
			if (a[j] > a[j + 1]) {
				const t = a[j];
				a[j] = a[j + 1];
				a[j + 1] = t;
			}
		}
	}
	return a;
}

function primes(limit) {
	const found = [];
	for (let n = 2; found.length < limit; n++) {
		let prime = true;
		for (const p of found) {
			if (n % p === 0) {
				prime = false;
				break;
			}
		}
		if (prime) found.push(n);
	}
	return found;
}

function search(a, x) {
	let i = 0;
	while (i < a.length) {
		if (a[i] === x) return i;
		if (a[i] > x) break;
		i++;
	}
	return -1;
}

const sorted = sort([5, 2, 9, 1, 7]);
console.log(sorted);
console.log(primes(6));
console.log(search(sorted, 7), search(sorted, 4));
let [a, b] = [1, 1];
for (let i = 0; i < 10; i++) {
	[a, b] = [b, a + b];
}
console.log(a, b);
