// switch with stacked cases, default in the middle and returns.
function name(n) {
	switch (n) {
		case 1:
			return "one";
		case 2:
		case 3:
			return "few";
		default:
			return "many";
	}
}

for (let i = 0; i < 6; i++) {
	let kind = "";
	switch (i % 4) {
		case 0:
			kind = "zero";
			break;
		default:
			kind = "other";
			break;
		case 1:
			kind = "one";
			break;
	}
	console.log(i, name(i), kind);
}
const day = "sat";
switch (day) {
	case "sat":
	case "sun":
		console.log("weekend");
		break;
	default:
		console.log("weekday");
}
