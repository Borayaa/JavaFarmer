// Tuples and deconstruction of Position and Companion values.
(int, int) start = (GetPosX(), GetPosY());
var (sx, sy) = start;

Plant(Entities.Bush);
Companion? wish = GetCompanion();
if (wish != null)
{
	var (plant, (cx, cy)) = wish;
	QuickPrint("plant", plant, "at", cx, cy);
	Position where = wish.Position;
	QuickPrint(where.X, where.Y, wish.Entity);
}
var (a, b) = (3, 4);
(a, b) = (b, a);
QuickPrint(a, b, start.Item1, start.Item2, sx + sy);
MoveTo(Next(sx, sy));

(int, int) Next(int x, int y)
{
	return ((x + 1) % GetWorldSize(), y);
}

void MoveTo((int, int) target)
{
	var (tx, ty) = target;
	while (GetPosX() != tx)
	{
		Move(East);
	}
	while (GetPosY() != ty)
	{
		Move(North);
	}
}
