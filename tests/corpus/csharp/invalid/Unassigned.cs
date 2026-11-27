// The variable is not assigned on every path.
// ERROR: CS0165: Use of unassigned local variable 'count'
// AT: count
int count;
if (CanHarvest())
{
	count = 1;
}
QuickPrint(count);
