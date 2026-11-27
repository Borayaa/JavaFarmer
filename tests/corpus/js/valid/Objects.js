// Plain objects work as string-keyed maps.
const harvests = { hay: 0, wood: 0 };
const names = {};
names["first"] = "Hay";
harvests.hay += 1;
if ("wood" in harvests) {
	harvests["wood"] = harvests.wood + 2;
}
for (const key in harvests) {
	quickPrint(key, harvests[key]);
}
for (const [key, value] of Object.entries(harvests)) {
	quickPrint(key + "=" + value);
}
quickPrint(Object.keys(names).length, names.first);
for (const v of Object.values(harvests)) {
	quickPrint(v);
}
