// Follow the wall in a maze until the treasure is found.
Direction[] dirs = { North, East, South, West };

Plant(Entities.Bush);
while (GetEntityType() == Entities.Bush)
{
	UseItem(Items.Weird_Substance, GetWorldSize());
}
int dir = 0;
while (GetEntityType() != Entities.Treasure)
{
	int right = TurnRight(dir);
	if (CanMove(dirs[right]))
	{
		dir = right;
	}
	else if (!CanMove(dirs[dir]))
	{
		dir = TurnLeft(dir);
		continue;
	}
	Move(dirs[dir]);
}
object? hint = Measure();
if (hint != null)
{
	var (tx, ty) = (Position)hint;
	QuickPrint("next treasure", tx, ty);
}
Harvest();

int TurnRight(int d) => (d + 1) % 4;

int TurnLeft(int d)
{
	return (d + 3) % 4;
}
