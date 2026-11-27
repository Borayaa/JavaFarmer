// A library window: static members only, no Main.
public static class Farming
{
	public static int planted = 0;
	public static readonly Entity Crop = Entities.Bush;

	public static void TendTile()
	{
		if (CanHarvest())
		{
			Harvest();
		}
		if (Plant(Crop))
		{
			planted++;
		}
	}
}
