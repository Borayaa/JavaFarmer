// Trees on a checkerboard, carrots in between, water when dry (class form).
class TreeCarrots
{
	const double WaterLevel = 0.75;

	static void Main()
	{
		Clear();
		while (true)
		{
			for (int i = 0; i < GetWorldSize() * GetWorldSize(); i++)
			{
				Tend();
				if (GetPosY() == GetWorldSize() - 1)
				{
					Move(East);
				}
				Move(North);
			}
		}
	}

	static void Tend()
	{
		Entity? here = GetEntityType();
		if (here != null && CanHarvest())
		{
			Harvest();
		}
		if (GetWater() < WaterLevel && NumItems(Items.Water) > 0)
		{
			UseItem(Items.Water);
		}
		if ((GetPosX() + GetPosY()) % 2 == 0)
		{
			Plant(Entities.Tree);
		}
		else
		{
			if (GetGroundType() != Grounds.Soil)
			{
				Till();
			}
			Plant(Entities.Carrot);
		}
	}
}
