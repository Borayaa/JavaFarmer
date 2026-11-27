// Java/array habit: a List has Count, not Length.
// ERROR: CS1061: 'List<int>' does not contain a definition for 'Length'
// AT: Length
List<int> xs = new List<int> { 1, 2 };
QuickPrint(xs.Length);
