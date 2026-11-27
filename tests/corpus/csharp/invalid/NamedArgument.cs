// Named arguments are not available.
// ERROR: named arguments are not supported in JavaFarmer
// AT: steps: 2
void Walk(Direction d, int steps = 1)
{
	Move(d);
}
Walk(North, steps: 2);
