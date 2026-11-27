// TryGetValue only as the condition of an if.
// ERROR: TryGetValue is only supported as the whole condition of an if statement in JavaFarmer (if (dict.TryGetValue(key, out var value)) ...)
// AT: stock.TryGetValue("hay", out int hay)
Dictionary<string, int> stock = new();
bool found = stock.TryGetValue("hay", out int hay);
