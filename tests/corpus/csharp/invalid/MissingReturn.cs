// Not every path returns a value.
// ERROR: CS0161: 'Size()': not all code paths return a value
// AT: Size
int Size()
{
	if (CanHarvest())
	{
		return 1;
	}
}
QuickPrint(Size());
