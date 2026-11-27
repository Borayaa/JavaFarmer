// Properties are not available: use a static field.
// ERROR: properties
// AT: Size
class Program
{
	static int Size { get; set; }

	static void Main()
	{
		Harvest();
	}
}
