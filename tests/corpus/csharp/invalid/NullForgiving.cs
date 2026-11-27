// The null-forgiving operator is not available.
// ERROR: the null-forgiving operator '!' is not supported in JavaFarmer
// AT: !
object? m = Measure();
int x = (int)m!;
