using System;
using static System.Math;

// A class-form program with static fields, constants and expression-bodied methods.
class Program
{
	const int MaxRounds = 5;
	static readonly Entity Crop = Entities.Carrot;
	static int rounds = 0;
	static double bestHay = 0;

	static int Size() => GetWorldSize();

	static bool IsEven(int x) => x % 2 == 0;

	static void PrepareTile()
	{
		if (GetGroundType() != Grounds.Soil)
		{
			Till();
		}
		Plant(IsEven(GetPosX()) ? Crop : Entities.Grass);
	}

	static void Main()
	{
		while (rounds < MaxRounds)
		{
			for (int i = 0; i < Size(); i++)
			{
				if (CanHarvest())
				{
					Harvest();
				}
				PrepareTile();
				Move(North);
			}
			Move(East);
			rounds++;
			bestHay = Max(bestHay, NumItems(Items.Hay));
		}
		Console.WriteLine($"best hay: {bestHay}, rounds: {rounds}");
	}
}
