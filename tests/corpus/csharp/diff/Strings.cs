// Concatenation, interpolation, Length, chars and Console.Write buffering.
string name = "farmer";
int level = 3;
Console.WriteLine("Hello " + name);
Console.WriteLine("level " + level + 1);
Console.WriteLine("level " + (level + 1));
Console.WriteLine(level + 1 + " levels");
Console.WriteLine($"{name} has level {level} and {level * 2} points");
Console.WriteLine($"{name}{level}");
Console.WriteLine(name.Length);
Console.WriteLine(name[0]);
Console.WriteLine(name[^1]);
Console.WriteLine(name.Contains("arm"));
Console.WriteLine(name.Contains("x"));
Console.WriteLine(level.ToString() + level.ToString());
Console.WriteLine(true.ToString());
string s = "";
for (int i = 0; i < 3; i++)
{
	s += i;
	s += ',';
}
Console.WriteLine(s);
Console.Write("a");
Console.Write(1);
Console.Write(false);
Console.WriteLine("b");
Console.Write("only write");
Console.WriteLine();
Console.WriteLine("" + 2.5 + " " + 0.1);
string empty = "";
Console.WriteLine(empty.Length == 0);
Console.WriteLine("x" == "x");
Console.WriteLine(name != "farm");
char c = 'q';
Console.WriteLine(c);
Console.WriteLine("char: " + c);
Console.WriteLine(name[1] == 'a');
string quote = "say \"hi\"";
Console.WriteLine(quote);
Console.WriteLine(Repeat("ab", 3));

string Repeat(string text, int times)
{
	string result = "";
	for (int i = 0; i < times; i++)
	{
		result += text;
	}
	return result;
}
