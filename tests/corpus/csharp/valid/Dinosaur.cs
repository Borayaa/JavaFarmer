using System.Collections.Generic;

// Wear the dinosaur hat and steer toward apples using Measure().
public class Dinosaur
{
	static List<Direction> path = new List<Direction>();

	static bool Step(Direction d)
	{
		if (Move(d))
		{
			path.Add(d);
			return true;
		}
		return false;
	}

	public static void Main()
	{
		ChangeHat(Hats.Dinosaur_Hat);
		while (true)
		{
			object? target = Measure();
			if (target == null)
			{
				break;
			}
			Position apple = (Position)target;
			bool moved;
			if (GetPosX() < apple.X)
			{
				moved = Step(East);
			}
			else if (GetPosX() > apple.X)
			{
				moved = Step(West);
			}
			else if (GetPosY() < apple.Y)
			{
				moved = Step(Direction.North);
			}
			else
			{
				moved = Step(South);
			}
			if (!moved)
			{
				break;
			}
		}
		QuickPrint("steps", path.Count);
		ChangeHat(Hats.Straw_Hat);
	}
}
