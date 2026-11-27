// = instead of == in a condition.
// ERROR: CS0029: Cannot implicitly convert type 'int' to 'bool' (to compare, write ==)
// AT: x = 5
int x = GetPosX();
if (x = 5)
{
	Harvest();
}
