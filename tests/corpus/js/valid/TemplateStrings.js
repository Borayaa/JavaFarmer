// Template literals build text from values.
const size = getWorldSize();
const label = `size ${size}`;
console.log(`The farm is ${size} x ${size} = ${size * size} tiles`);
console.log(label + "!");
let text = "";
for (let i = 0; i < 3; i++) {
	text += i;
	text = text + ",";
}
console.log(text, `${getPosX()}`, String(42), (7).toString());
