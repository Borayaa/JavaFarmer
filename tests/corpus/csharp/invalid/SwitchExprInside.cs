// A switch expression must be a whole value.
// ERROR: a switch expression is only supported as a whole value (variable initializer, assignment or return) in JavaFarmer
// AT: switch
int n = GetWorldSize();
QuickPrint(1 + n switch { 3 => 1, _ => 2 });
