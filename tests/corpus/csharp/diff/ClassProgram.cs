using System;
using System.Collections.Generic;

// A class-form program: static fields, constants and methods.
class Program
{
	const int Size = 4;
	static int calls = 0;
	static List<int> history = new List<int>();
	static Dictionary<string, int> scores = new Dictionary<string, int>();

	static int Next(int value)
	{
		calls++;
		history.Add(value);
		return value % 2 == 0 ? value / 2 : 3 * value + 1;
	}

	static int Steps(int start)
	{
		int steps = 0;
		int value = start;
		while (value != 1)
		{
			value = Next(value);
			steps++;
		}
		return steps;
	}

	static void Record(string name, int score = 1)
	{
		if (scores.ContainsKey(name))
		{
			scores[name] += score;
		}
		else
		{
			scores[name] = score;
		}
	}

	static void Main()
	{
		Console.WriteLine(Steps(27));
		Console.WriteLine(calls);
		Console.WriteLine(history.Count);
		Console.WriteLine(history[0] + " " + history[^1]);
		for (int i = 0; i < Size; i++)
		{
			Record(i % 2 == 0 ? "even" : "odd", i);
		}
		Record("bonus");
		foreach (var (name, score) in scores)
		{
			Console.WriteLine(name + ": " + score);
		}
		Action report = () => Console.WriteLine("calls = " + calls);
		report();
	}
}
