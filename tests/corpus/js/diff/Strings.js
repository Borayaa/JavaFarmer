// Strings, template literals and concatenation.
const name = "drone";
const count = 3;
console.log("hello " + name);
console.log(`${name} has ${count} hats`);
console.log(`${count}${count}`, count + count + "!", "!" + count + count);
let s = "";
for (let i = 0; i < 5; i++) {
	s += i;
}
console.log(s, s.length, s[2], s.charAt(4));
console.log("ab" === "a" + "b", "abc".includes("bc"));
console.log(String(1.5), String(true), String(null), (12).toString());
const words = ["one", "two"];
console.log(words, "x" + words.length);
console.log('it\'s', "say \"hi\"");
