using static System.Math;

// Floor, Ceiling, Sqrt, Pow, Abs, Min, Max with exact values.
Console.WriteLine(Math.Floor(2.5));
Console.WriteLine(Math.Floor(-2.5));
Console.WriteLine(Math.Ceiling(2.25));
Console.WriteLine(Math.Ceiling(-2.25));
Console.WriteLine(Math.Sqrt(144));
Console.WriteLine(Math.Sqrt(2.25));
Console.WriteLine(Math.Pow(2, 10));
Console.WriteLine(Math.Pow(1.5, 2));
Console.WriteLine(Math.Pow(4, 0.5));
Console.WriteLine(Abs(-7));
Console.WriteLine(Min(2.5, 1.25));
Console.WriteLine(Max(-3, -8));
Console.WriteLine(Math.Round(2.25 * 4));
Console.WriteLine(Math.Round(7.75));
Console.WriteLine((int)Math.Sqrt(50));
Console.WriteLine((int)Math.Floor(-0.5));
double area = Math.Pow(3, 2) * 4;
Console.WriteLine(area);
Console.WriteLine(Hypot(3, 4));
Console.WriteLine(0.5 + 0.25);
Console.WriteLine(1.0 / 8);
Console.WriteLine(10.0 / 4);
Console.WriteLine(3.0 * 2);
Console.WriteLine(Math.Abs(-0.75) > 0.5);

double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);
