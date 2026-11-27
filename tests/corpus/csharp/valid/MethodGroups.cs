// Method groups, default parameters and expression-bodied local functions.
Action tend = TendTile;
Func<int> count = CountTiles;

for (int i = 0; i < count(); i++)
{
	tend();
	Walk(North);
}
Walk(East, 2);
SpawnDrone(TendTile);
SpawnDrone(CountTiles);
QuickPrint(Area(), Area(3));

void TendTile()
{
	if (CanHarvest())
	{
		Harvest();
	}
	Plant(Entities.Bush);
}

int CountTiles() => GetWorldSize();

int Area(int side = 0) => side == 0 ? GetWorldSize() * GetWorldSize() : side * side;

void Walk(Direction d, int steps = 1)
{
	for (int i = 0; i < steps; i++)
	{
		Move(d);
	}
}
