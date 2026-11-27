// Plant a checkerboard of bushes and grass using the drone position.
int size = GetWorldSize();
while (true)
{
	for (int x = 0; x < size; x++)
	{
		for (int y = 0; y < size; y++)
		{
			if (CanHarvest())
			{
				Harvest();
			}
			if ((GetPosX() + GetPosY()) % 2 == 0)
			{
				Plant(Entities.Bush);
			}
			else
			{
				Plant(Entities.Grass);
			}
			Move(North);
		}
		Move(East);
	}
}
