using System;

// A class-form program that uses a library window.
class Program
{
	static int rounds = 0;

	static void Main()
	{
		Field.Prepare(Entities.Carrot);
		while (rounds < 3)
		{
			int got = Field.HarvestColumn();
			Console.WriteLine($"round {rounds}: {got} harvested, {Field.Total} in total");
			rounds++;
		}
		if (Field.Total > Field.Goal)
		{
			Console.WriteLine("goal reached");
		}
	}
}
