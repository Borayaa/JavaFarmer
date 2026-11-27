// Remember the state of every tile in a 2-D array.
int size = GetWorldSize();
bool[,] planted = new bool[size, size];
int[,] visits = new int[size, size];

while (true)
{
	for (int x = 0; x < planted.GetLength(0); x++)
	{
		for (int y = 0; y < planted.GetLength(1); y++)
		{
			visits[x, y]++;
			if (!planted[x, y])
			{
				Plant(Entities.Bush);
				planted[x, y] = true;
			}
			else if (CanHarvest())
			{
				Harvest();
				planted[x, y] = false;
			}
			Move(North);
		}
		Move(East);
	}
	QuickPrint("corner visits", visits[0, 0], visits[size - 1, size - 1]);
}
