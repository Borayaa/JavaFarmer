// ERROR: for...of over a Map gives [key, value] pairs
// AT: counts
const counts = new Map();
counts.set("a", 1);
for (const entry of counts) {
	console.log(entry);
}
