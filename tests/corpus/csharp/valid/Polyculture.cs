// Plant each tile's preferred companion; remember wishes in a dictionary keyed by position.
Dictionary<Position, Entity> wishes = new();

int n = GetWorldSize();
Entity[] choices = [Entities.Grass, Entities.Bush, Entities.Tree, Entities.Carrot];
for (int step = 0; step < n * n; step++)
{
	Entity want = choices[step % choices.Length];
	if (want == Entities.Carrot && GetGroundType() != Grounds.Soil)
	{
		Till();
	}
	Plant(want);
	Companion? companion = GetCompanion();
	if (companion is not null)
	{
		var (plant, (cx, cy)) = companion;
		QuickPrint("wants", plant, "at", cx, cy);
		wishes[companion.Position] = companion.Entity;
	}
	if (GetPosY() == n - 1)
	{
		Move(East);
	}
	Move(North);
}
foreach (var (pos, entity) in wishes)
{
	QuickPrint(pos.X, pos.Y, entity);
}
QuickPrint("wishes:", wishes.Count, "first choice:", choices[0].ToString());
