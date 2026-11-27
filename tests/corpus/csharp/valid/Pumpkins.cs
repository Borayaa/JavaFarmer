using System.Collections.Generic;

// Replant dead pumpkins until the whole field is one big pumpkin.
List<int> deadX = new List<int>();
List<int> deadY = new();

Clear();
PlantAll();
while (deadX.Count > 0)
{
	int x = deadX[0];
	int y = deadY[0];
	deadX.RemoveAt(0);
	deadY.RemoveAt(0);
	GoTo(x, y);
	if (GetEntityType() == Entities.Pumpkin && CanHarvest())
	{
		continue;
	}
	Plant(Entities.Pumpkin);
	deadX.Add(x);
	deadY.Add(y);
}
Harvest();

void GoTo(int x, int y)
{
	while (GetPosX() != x)
	{
		Move(East);
	}
	while (GetPosY() != y)
	{
		Move(North);
	}
}

void PlantAll()
{
	int n = GetWorldSize();
	for (int x = 0; x < n; x++)
	{
		for (int y = 0; y < n; y++)
		{
			if (GetGroundType() != Grounds.Soil)
			{
				Till();
			}
			Plant(Entities.Pumpkin);
			deadX.Add(GetPosX());
			deadY.Add(GetPosY());
			Move(North);
		}
		Move(East);
	}
}
