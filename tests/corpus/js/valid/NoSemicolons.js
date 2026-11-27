// Automatic semicolon insertion: no semicolons needed.
let count = 0
const limit = 5
while (count < limit) {
	count++
	if (canHarvest()) harvest()
	move(North)
}
function done() {
	return count === limit
}
const total = count
	+ limit
console.log(done(), total)
