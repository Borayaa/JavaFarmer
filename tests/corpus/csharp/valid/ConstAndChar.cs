// Constants, chars and strings.
const int Rows = 3;
const string Label = "row";
const char Mark = '#';

char empty = '.';
string line = "";
for (int i = 0; i < Rows; i++)
{
	if (i % 2 == 0)
	{
		line += Mark;
	}
	else
	{
		line = line + empty;
	}
}
QuickPrint(Label, line, line.Length, line[0]);
if (line[1] == '.')
{
	QuickPrint("dot in the middle");
}
for (int i = 0; i < line.Length; i++)
{
	if (line[i] == '#')
	{
		Plant(Entities.Bush);
	}
	Move(North);
}
foreach (char c in line)
{
	QuickPrint(c);
}
