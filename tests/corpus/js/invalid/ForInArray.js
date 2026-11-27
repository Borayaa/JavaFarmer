// ERROR: for...in loops over the keys of an object
// AT: list
const list = [1, 2, 3];
for (const i in list) {
	console.log(i);
}
