// String interpolation, Console.Write and Console.WriteLine.
int size = GetWorldSize();
string name = "farmer";
Console.WriteLine($"Hello, {name}! The farm is {size} x {size}.");
Console.Write("Hay: ");
Console.Write(NumItems(Items.Hay));
Console.WriteLine();
Console.Write("Position: ");
Console.WriteLine($"({GetPosX()}, {GetPosY()})");
double hay = NumItems(Items.Hay);
string message = "We have " + hay + " hay and " + size * size + " tiles";
Console.WriteLine(message);
Console.WriteLine(message.Length);
Console.WriteLine(size.ToString() + "!");
string wealth = hay > 100 ? "rich" : "poor";
Console.WriteLine(wealth);
Console.WriteLine(true);
QuickPrint($"{name} done", name.Contains("farm"));
