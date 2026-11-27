// Assigning a captured local of a method inside a lambda (the game has no nonlocal).
// ERROR: assigning a captured local variable inside a lambda or local function is not supported in JavaFarmer (use a static field, or a variable of the top-level statements)
// AT: count
void Count()
{
	int count = 0;
	Action bump = () => count++;
	bump();
}
Count();
