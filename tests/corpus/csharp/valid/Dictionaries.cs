// Count what grows on the farm with a dictionary, then report it.
Dictionary<Entity, int> counts = new Dictionary<Entity, int>();
Dictionary<string, int> goals = new()
{
	["hay"] = 100,
	["wood"] = 50,
};
Dictionary<Item, string> names = new Dictionary<Item, string>
{
	{ Items.Hay, "hay" },
	{ Items.Wood, "wood" },
};

int n = GetWorldSize();
for (int i = 0; i < n; i++)
{
	Entity? here = GetEntityType();
	if (here != null)
	{
		if (counts.TryGetValue(here, out int seen))
		{
			counts[here] = seen + 1;
		}
		else
		{
			counts.Add(here, 1);
		}
	}
	Move(North);
}
foreach (var (entity, amount) in counts)
{
	QuickPrint(entity, amount);
}
foreach (var kv in names)
{
	int goal = goals.GetValueOrDefault(kv.Value, 0);
	if (NumItems(kv.Key) < goal)
	{
		QuickPrint("need more", kv.Value);
	}
}
foreach (string key in goals.Keys)
{
	QuickPrint(key);
}
int total = 0;
foreach (int goal in goals.Values)
{
	total += goal;
}
if (!goals.TryGetValue("carrot", out var carrotGoal))
{
	carrotGoal = 10;
}
goals.Remove("wood");
QuickPrint("total", total, "carrots", carrotGoal, "left", goals.Count, goals.ContainsKey("hay"));
