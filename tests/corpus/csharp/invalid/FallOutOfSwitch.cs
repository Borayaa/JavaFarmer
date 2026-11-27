// The last section of a switch also needs break.
// ERROR: CS8070: Control cannot fall out of switch from final case label ('default:')
// AT: default:
int n = GetWorldSize();
switch (n)
{
	case 3:
		Move(North);
		break;
	default:
		Move(East);
}
