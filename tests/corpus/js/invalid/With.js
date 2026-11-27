// ERROR: Strict mode code may not include a with statement
// AT: with
const o = {};
with (o) {
}
