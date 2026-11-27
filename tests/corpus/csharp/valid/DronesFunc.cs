using System;
using System.Collections.Generic;

// Drones return a value (Func<int>); the main drone waits for all of them.
class DronesFunc
{
	static int total = 0;

	static int CountCarrots()
	{
		int found = 0;
		for (int i = 0; i < GetWorldSize(); i++)
		{
			if (GetEntityType() == Entities.Carrot)
			{
				found++;
			}
			Move(North);
		}
		return found;
	}

	static void Main(string[] args)
	{
		List<Drone> drones = new List<Drone>();
		for (int i = 0; i < MaxDrones() - 1; i++)
		{
			Func<int> task = CountCarrots;
			Drone? d = SpawnDrone(task);
			if (d != null)
			{
				drones.Add(d);
			}
			Move(East);
		}
		foreach (Drone d in drones)
		{
			total += (int)WaitFor(d);
		}
		Func<int> doubled = () => total * 2;
		Action report = () =>
		{
			Console.WriteLine("carrots: " + total);
		};
		report();
		QuickPrint(doubled.Invoke());
	}
}
