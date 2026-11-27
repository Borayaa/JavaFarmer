public static class Utils
{
	public static int moves = 0;
	private static int secret = 42;

	public static int Size()
	{
		return GetWorldSize();
	}

	public static int Twice(int x) => x * 2 + secret - 42;

	public static void GoTo(int x, int y)
	{
		while (GetPosX() != x)
		{
			Move(West);
			moves++;
		}
		while (GetPosY() != y)
		{
			Move(South);
			moves++;
		}
	}
}
