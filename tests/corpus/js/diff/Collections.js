// Arrays, Maps and Sets.
const list = [3, 1, 4];
list.push(1, 5);
console.log(list.length, list[0], list[list.length - 1]);
const last = list.pop();
const first = list.shift();
list.unshift(9);
list.splice(1, 0, 2);
console.log(last, first, list);
console.log(list.indexOf(4), list.indexOf(42), list.includes(9));
const removed = list.splice(1, 1)[0];
console.log(removed, list);
const copy = [...list];
copy.push(0);
console.log(list.length, copy.length);
const m = new Map();
m.set("apple", 3);
m.set("pear", 5);
m.set("apple", m.get("apple") + 1);
console.log(m.get("apple"), m.get("kiwi") ?? 0, m.has("pear"), m.size);
m.delete("pear");
console.log(m);
for (const [k, v] of m) {
	console.log(k, v);
}
const s = new Set([1, 2, 3]);
s.add(2);
s.delete(1);
console.log(s.size, s.has(2), s.has(1));
const grid = Array.from({ length: 2 }, () => new Array(3).fill(0));
grid[1][2] = 7;
console.log(grid);
console.log([], new Map());
