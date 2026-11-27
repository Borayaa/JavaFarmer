// break only works inside a loop or a switch.
// ERROR: CS0139: No enclosing loop out of which to break or continue
// AT: break;
if (CanHarvest())
{
	break;
}
