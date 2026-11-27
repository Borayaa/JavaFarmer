// List, Dictionary and HashSet operations.
List<int> numbers = new List<int> { 5, 3, 8 };
numbers.Add(1);
numbers.Insert(0, 9);
Console.WriteLine(numbers.Count);
Console.WriteLine(numbers[0] + " " + numbers[^1] + " " + numbers[^2]);
numbers.Remove(3);
numbers.Remove(42);
numbers.RemoveAt(0);
Console.WriteLine(numbers.Count + " " + numbers[0]);
Console.WriteLine(numbers.Contains(8));
Console.WriteLine(numbers.Contains(3));
Console.WriteLine(numbers.IndexOf(8));
Console.WriteLine(numbers.IndexOf(77));
numbers[1] = 80;
int total = 0;
foreach (int x in numbers)
{
	total += x;
}
Console.WriteLine(total);
List<string> names = ["hay", "wood"];
List<string> copy = new List<string>(names);
copy.Add("carrot");
Console.WriteLine(names.Count + " " + copy.Count);
copy.Clear();
Console.WriteLine(copy.Count);

Dictionary<string, int> stock = new Dictionary<string, int>();
stock["hay"] = 10;
stock["wood"] = 4;
stock.Add("carrot", 7);
stock["hay"] += 5;
Console.WriteLine(stock["hay"]);
Console.WriteLine(stock.Count);
Console.WriteLine(stock.ContainsKey("wood"));
Console.WriteLine(stock.GetValueOrDefault("pumpkin"));
Console.WriteLine(stock.GetValueOrDefault("pumpkin", -1));
if (stock.TryGetValue("carrot", out int carrots))
{
	Console.WriteLine("carrots: " + carrots);
}
if (!stock.TryGetValue("cactus", out var cactus))
{
	Console.WriteLine("no cactus");
}
stock.Remove("wood");
int sum = 0;
foreach (int amount in stock.Values)
{
	sum += amount;
}
Console.WriteLine(sum);
foreach (var (key, value) in stock)
{
	Console.WriteLine(key + "=" + value);
}
foreach (var kv in stock)
{
	Console.WriteLine($"{kv.Key}: {kv.Value}");
}
foreach (string key in stock.Keys)
{
	Console.Write(key + ";");
}
Console.WriteLine();
Dictionary<int, string> squares = new()
{
	[1] = "one",
	[4] = "four",
};
Console.WriteLine(squares[4]);

HashSet<int> seen = new HashSet<int>();
seen.Add(3);
seen.Add(3);
seen.Add(5);
Console.WriteLine(seen.Count);
Console.WriteLine(seen.Contains(5));
seen.Remove(5);
Console.WriteLine(seen.Contains(5));
seen.Clear();
Console.WriteLine(seen.Count);

int[] arr = new int[4];
arr[2] = 7;
int[] primes = { 2, 3, 5 };
Console.WriteLine(arr.Length + " " + arr[2] + " " + arr[0] + " " + primes[^1]);
int[,] grid = new int[3, 2];
grid[2, 1] = 9;
Console.WriteLine(grid.GetLength(0) + " " + grid.GetLength(1) + " " + grid[2, 1]);
(int, string) pair = (1, "one");
Console.WriteLine(pair.Item1 + " " + pair.Item2);
