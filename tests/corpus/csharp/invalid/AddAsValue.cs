// The bool result of HashSet.Add cannot be used.
// ERROR: using the result of HashSet.Add is not supported in JavaFarmer; call it as a separate statement
// AT: seen.Add(3)
HashSet<int> seen = new HashSet<int>();
if (seen.Add(3))
{
	Harvest();
}
