// ERROR: throw is not supported in JavaFarmer
// AT: throw
function check(x) {
	if (x < 0) throw "negative";
	return x;
}
