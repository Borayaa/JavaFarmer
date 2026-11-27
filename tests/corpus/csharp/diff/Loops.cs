// for, while, do-while, break, continue and nested loops.
int sum = 0;
for (int i = 0; i < 10; i++)
{
	if (i == 7)
	{
		break;
	}
	if (i % 2 == 0)
	{
		continue;
	}
	sum += i;
}
Console.WriteLine(sum);
int n = 5;
while (n > 0)
{
	Console.Write(n);
	Console.Write(" ");
	n--;
}
Console.WriteLine("liftoff");
int k = 100;
do
{
	k /= 3;
} while (k > 5);
Console.WriteLine(k);
do
{
	k++;
} while (false);
Console.WriteLine(k);
int pairs = 0;
for (int x = 0; x < 4; x++)
{
	for (int y = x; y < 4; y++)
	{
		if (y == 3)
		{
			break;
		}
		pairs++;
	}
}
Console.WriteLine(pairs);
for (int i = 10; i > 0; i -= 3)
{
	Console.Write(i + ",");
}
Console.WriteLine();
for (int i = 0, j = 10; i < j; i += 2, j -= 2)
{
	Console.WriteLine(i * j);
}
int count = 0;
while (true)
{
	count++;
	if (count >= 4)
	{
		break;
	}
}
Console.WriteLine(count);
string word = "farm";
foreach (char c in word)
{
	Console.Write(c);
	Console.Write("-");
}
Console.WriteLine("!");
