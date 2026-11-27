// Every drone farms one column. Lambdas capture the loop-local column number.
Clear();
for (int col = 0; col < GetWorldSize(); col++)
{
	int myColumn = col;
	Drone? d = SpawnDrone(() => FarmColumn(myColumn));
	if (d == null)
	{
		FarmColumn(myColumn);
	}
	Move(East);
}
while (NumDrones() > 1)
{
	DoAFlip();
}

void FarmColumn(int column)
{
	for (int i = 0; i < GetWorldSize(); i++)
	{
		if (CanHarvest())
		{
			Harvest();
		}
		Move(North);
	}
	QuickPrint("column done", column);
}
