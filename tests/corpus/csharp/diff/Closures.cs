// Lambdas, local functions, captured top-level variables and function values.
int counter = 0;
Action bump = () => counter++;
Func<int> current = () => counter;
Func<int, int> square = x => x * x;
Func<int, int, int> add = (a, b) => a + b;

bump();
bump();
Increase(5);
Console.WriteLine(counter);
Console.WriteLine(current());
Console.WriteLine(square(7));
Console.WriteLine(add(2, 3));
Console.WriteLine(Apply(square, 4));
Console.WriteLine(Apply(x => x + 100, 1));
Console.WriteLine(Twice(() => counter * 2));
RunTimes(3, bump);
Console.WriteLine(counter);
Func<int, int> addTen = MakeAdder(10);
Console.WriteLine(addTen(5));
Func<int> next = Counter();
next();
next();
Console.WriteLine(next());
List<int> seen = new List<int>();
for (int i = 0; i < 3; i++)
{
	int captured = i * i;
	Action record = () => seen.Add(captured);
	record();
}
Console.WriteLine(seen.Count + " " + seen[2]);
Func<string> greet = () =>
{
	string who = "farmer";
	if (counter > 5)
	{
		return "hello " + who;
	}
	return "hi";
};
Console.WriteLine(greet.Invoke());
Func<int, int> fact = n => 1;
fact = n => n <= 1 ? 1 : n * fact(n - 1);
Console.WriteLine(fact(6));

void Increase(int by)
{
	counter += by;
}

int Apply(Func<int, int> f, int value) => f(value);

int Twice(Func<int> f)
{
	return f() + f();
}

void RunTimes(int times, Action action)
{
	for (int i = 0; i < times; i++)
	{
		action();
	}
}

Func<int, int> MakeAdder(int amount)
{
	return x => x + amount;
}

Func<int> Counter()
{
	List<int> state = [0];
	return () =>
	{
		state[0] = state[0] + 1;
		return state[0];
	};
}
