// Collection expressions, lists, sets and indexing from the end.
List<Entity> plants = [Entities.Grass, Entities.Bush, Entities.Carrot];
int[] steps = [1, 2, 3];
HashSet<int> visited = new HashSet<int>();
List<int> empty = [];

plants.Insert(0, Entities.Tree);
plants.Remove(Entities.Bush);
QuickPrint("last plant", plants[^1], "first", plants[0]);
if (plants.Contains(Entities.Carrot))
{
	QuickPrint("carrot is at", plants.IndexOf(Entities.Carrot));
}
foreach (int step in steps)
{
	for (int i = 0; i < step; i++)
	{
		Move(North);
	}
	visited.Add(GetPosY());
	empty.Add(step * 2);
}
if (!visited.Contains(0))
{
	visited.Remove(GetPosY());
}
List<int> copy = new List<int>(empty);
copy.Clear();
QuickPrint(visited.Count, empty.Count, copy.Count, steps[steps.Length - 1]);
for (int i = 0; i < plants.Count; i++)
{
	Plant(plants[i]);
	Move(East);
}
