// Java habit: for-each with ':' instead of 'in'.
// ERROR: CS1515: 'in' expected
// AT: :
List<int> xs = [1, 2];
foreach (int x : xs)
{
	QuickPrint(x);
}
