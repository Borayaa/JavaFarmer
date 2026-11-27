// Measure every sunflower, then harvest the ones with the most petals first.
Dictionary<int, List<(int, int)>> byPetals = new Dictionary<int, List<(int, int)>>();

int n = GetWorldSize();
for (int x = 0; x < n; x++)
{
	for (int y = 0; y < n; y++)
	{
		if (GetGroundType() != Grounds.Soil)
		{
			Till();
		}
		Plant(Entities.Sunflower);
		int petals = (int)Measure();
		if (!byPetals.ContainsKey(petals))
		{
			byPetals[petals] = new List<(int, int)>();
		}
		byPetals[petals].Add((GetPosX(), GetPosY()));
		Move(North);
	}
	Move(East);
}
for (int petals = 15; petals >= 7; petals--)
{
	if (byPetals.TryGetValue(petals, out var spots))
	{
		foreach (var spot in spots)
		{
			var (px, py) = spot;
			QuickPrint("petals", petals, "at", px, py);
		}
	}
}
Console.WriteLine("sizes: " + byPetals.Count);
