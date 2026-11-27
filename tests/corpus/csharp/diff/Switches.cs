// switch statements and switch expressions with int, string and char labels.
for (int i = 0; i < 5; i++)
{
	switch (i)
	{
		case 0:
			Console.WriteLine("zero");
			break;
		case 1:
		case 2:
			Console.WriteLine("small");
			break;
		case 3 or 4:
			Console.WriteLine("medium");
			break;
		default:
			Console.WriteLine("big");
			break;
	}
}
string[] words = { "hay", "wood", "carrot", "other" };
foreach (string w in words)
{
	Console.WriteLine(Price(w));
}
char[] marks = { 'a', 'b', 'z' };
foreach (char c in marks)
{
	string name = c switch
	{
		'a' => "first",
		'b' => "second",
		_ => "unknown",
	};
	Console.WriteLine(name);
}
const int Limit = 10;
int value = 10;
switch (value)
{
	case Limit:
		Console.WriteLine("at the limit");
		break;
	default:
		Console.WriteLine("not the limit");
		break;
}
for (int i = 0; i < 3; i++)
{
	switch (i)
	{
		case 1:
			continue;
		default:
			Console.WriteLine("i = " + i);
			break;
	}
}
Console.WriteLine(Grade(95));
Console.WriteLine(Grade(70));
Console.WriteLine(Grade(10));

int Price(string item)
{
	switch (item)
	{
		case "hay":
			return 1;
		case "wood":
			return 5;
		case "carrot":
			return 12;
	}
	return 0;
}

string Grade(int score) => (score / 10) switch
{
	10 or 9 => "A",
	8 or 7 => "B",
	_ => "C",
};
