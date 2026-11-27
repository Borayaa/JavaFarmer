// Check what we can afford before unlocking.
Unlock[] wanted = { Unlocks.Speed, Unlocks.Expand, Unlocks.Plant };
foreach (Unlock u in wanted)
{
	if (CanAfford(u) && NumUnlocked(u) < 5)
	{
		Unlock(u);
	}
}
Companion? c = GetCompanion();
if (c != null)
{
	QuickPrint(c.Entity, c.X, c.Y);
}
ChangeHat(Hats.Straw_Hat);
SetExecutionSpeed(2);
double t = GetTime();
QuickPrint("ticks", GetTickCount(), "time", t);

bool CanAfford(Unlock u)
{
	Dictionary<Item, double>? cost = GetCost(u);
	if (cost == null)
	{
		return false;
	}
	foreach (Item item in cost.Keys)
	{
		if (NumItems(item) < cost[item])
		{
			return false;
		}
	}
	return true;
}
