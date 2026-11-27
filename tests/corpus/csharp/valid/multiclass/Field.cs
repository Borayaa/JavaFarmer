// A library: the class is named like its window (Field).
public static class Field
{
	public const int Goal = 10;
	public static int Total = 0;

	public static void Prepare(Entity crop)
	{
		for (int i = 0; i < GetWorldSize(); i++)
		{
			if (GetGroundType() != Grounds.Soil)
			{
				Till();
			}
			Plant(crop);
			Move(North);
		}
	}

	public static int HarvestColumn()
	{
		int count = 0;
		for (int i = 0; i < GetWorldSize(); i++)
		{
			if (CanHarvest())
			{
				Harvest();
				count++;
				Total++;
			}
			Move(North);
		}
		return count;
	}
}
