// switch statements and expressions, do-while, for with continue, ternary.
int moves = 0;
do
{
	Move(East);
	moves++;
} while (GetPosX() != 0);

for (int i = 0; i < moves; i++)
{
	if (i % 2 == 1)
	{
		continue;
	}
	Entity? e = GetEntityType();
	string kind;
	switch (moves)
	{
		case 1:
			kind = "tiny";
			break;
		case 2:
		case 3:
			kind = "small";
			break;
		default:
			kind = "big";
			QuickPrint("big farm");
			break;
	}
	QuickPrint(kind, Score(e));
}
int count = 0;
for (int i = 10; i > 0; i -= 3)
{
	count += i > 5 ? 2 : 1;
}
string size = count switch
{
	0 => "none",
	1 or 2 => "few",
	_ => "many",
};
QuickPrint(count, size);

int Score(Entity? e)
{
	switch (GetGroundType() == Grounds.Soil ? 1 : 0)
	{
		case 1:
			return 2;
		default:
			return e == Entities.Grass ? 1 : 0;
	}
}
