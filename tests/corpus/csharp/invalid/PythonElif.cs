// Python habit: elif instead of else if.
// ERROR: CS0103: The name 'elif' does not exist in the current context (C# writes else if
// AT: elif
if (CanHarvest())
{
	Harvest();
}
elif (GetPosX() == 0)
{
	Move(East);
}
