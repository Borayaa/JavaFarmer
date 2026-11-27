// Strings cannot be compared with <.
// ERROR: CS0019: Operator '<' cannot be applied to operands of type 'string' and 'string' (use string.Compare, which is not supported in JavaFarmer)
// AT: <
string a = "carrot";
if (a < "pumpkin")
{
	Harvest();
}
