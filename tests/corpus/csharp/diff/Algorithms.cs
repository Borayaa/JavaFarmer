// Recursion, gcd, primes and sorting with loops.
Console.WriteLine(Fib(15));
Console.WriteLine(Gcd(84, 36));
Console.WriteLine(Gcd(17, 5));
Console.WriteLine(Factorial(10));
Console.WriteLine(Power(3, 7));
List<int> primes = Primes(40);
Console.WriteLine(primes.Count);
Console.WriteLine(primes[^1]);
List<int> data = [5, 2, 9, 1, 7, 3];
BubbleSort(data);
string line = "";
foreach (int x in data)
{
	line += x + " ";
}
Console.WriteLine(line);
int[] arr = { 4, -2, 8, 0 };
InsertionSort(arr);
Console.WriteLine(arr[0] + " " + arr[1] + " " + arr[2] + " " + arr[3]);
Console.WriteLine(BinarySearch(data, 7));
Console.WriteLine(BinarySearch(data, 4));
Console.WriteLine(Hanoi(10));
Console.WriteLine(IsPalindrome("racecar"));
Console.WriteLine(IsPalindrome("farm"));
Console.WriteLine(DigitSum(98765));

int Fib(int n)
{
	if (n < 2)
	{
		return n;
	}
	return Fib(n - 1) + Fib(n - 2);
}

int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);

int Power(int b, int e)
{
	int result = 1;
	while (e > 0)
	{
		if (e % 2 == 1)
		{
			result *= b;
		}
		b *= b;
		e /= 2;
	}
	return result;
}

List<int> Primes(int limit)
{
	List<int> found = new List<int>();
	for (int n = 2; n <= limit; n++)
	{
		bool prime = true;
		foreach (int p in found)
		{
			if (p * p > n)
			{
				break;
			}
			if (n % p == 0)
			{
				prime = false;
				break;
			}
		}
		if (prime)
		{
			found.Add(n);
		}
	}
	return found;
}

void BubbleSort(List<int> xs)
{
	for (int i = 0; i < xs.Count; i++)
	{
		for (int j = 0; j < xs.Count - 1 - i; j++)
		{
			if (xs[j] > xs[j + 1])
			{
				(xs[j], xs[j + 1]) = (xs[j + 1], xs[j]);
			}
		}
	}
}

void InsertionSort(int[] xs)
{
	for (int i = 1; i < xs.Length; i++)
	{
		int key = xs[i];
		int j = i - 1;
		while (j >= 0 && xs[j] > key)
		{
			xs[j + 1] = xs[j];
			j--;
		}
		xs[j + 1] = key;
	}
}

int BinarySearch(List<int> xs, int target)
{
	int lo = 0;
	int hi = xs.Count - 1;
	while (lo <= hi)
	{
		int mid = (lo + hi) / 2;
		if (xs[mid] == target)
		{
			return mid;
		}
		if (xs[mid] < target)
		{
			lo = mid + 1;
		}
		else
		{
			hi = mid - 1;
		}
	}
	return -1;
}

int Hanoi(int disks) => disks == 0 ? 0 : 2 * Hanoi(disks - 1) + 1;

bool IsPalindrome(string text)
{
	for (int i = 0; i < text.Length / 2; i++)
	{
		if (text[i] != text[text.Length - 1 - i])
		{
			return false;
		}
	}
	return true;
}

int DigitSum(int n)
{
	int sum = 0;
	while (n > 0)
	{
		sum += n % 10;
		n /= 10;
	}
	return sum;
}
