// Only static methods: there are no objects.
// ERROR: instance methods are not supported in JavaFarmer: declare it static
// AT: Tend
class Program
{
	void Tend()
	{
		Harvest();
	}

	static void Main()
	{
		Harvest();
	}
}
