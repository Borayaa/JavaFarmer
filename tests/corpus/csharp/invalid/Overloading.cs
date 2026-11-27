// Two methods with the same name.
// ERROR: method overloading is not supported in JavaFarmer: there is already a method named Walk
// AT: Walk
class Program
{
	static void Walk(Direction d)
	{
		Move(d);
	}

	static void Walk(Direction d, int steps)
	{
		for (int i = 0; i < steps; i++)
		{
			Move(d);
		}
	}

	static void Main()
	{
		Walk(North, 2);
	}
}
