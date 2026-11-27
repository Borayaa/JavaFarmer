// C# does not allow falling through to the next case.
// ERROR: CS0163: Control cannot fall through from one case label ('case 3:') to another
// AT: case 3:
int n = GetWorldSize();
switch (n)
{
	case 3:
		Move(North);
	case 4:
		Move(East);
		break;
}
