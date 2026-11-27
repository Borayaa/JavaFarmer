// Composite format strings are not available: use $"...".
// ERROR: composite format strings (Console.WriteLine(format, args)) are not supported in JavaFarmer; use string interpolation $"..."
// AT: Console.WriteLine("x = {0}", x)
int x = 3;
Console.WriteLine("x = {0}", x);
