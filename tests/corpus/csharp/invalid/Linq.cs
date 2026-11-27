// LINQ is not available.
// ERROR: LINQ (using System.Linq) is not supported in JavaFarmer
// AT: using System.Linq;
using System.Linq;

List<int> xs = [3, 1, 2];
QuickPrint(xs.Count);
