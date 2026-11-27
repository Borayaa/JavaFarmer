// Game constants are fields, not compile-time constants: use if / else if.
// ERROR: CS9135: A constant value of type 'Entity?' is expected
// AT: Entities.Carrot
Entity? e = GetEntityType();
switch (e)
{
	case Entities.Carrot:
		Harvest();
		break;
}
