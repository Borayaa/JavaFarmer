// Uses helpers from two other windows (top-level statements).
Utils.GoTo(0, 0);
int n = Utils.Size();
for (int i = 0; i < n; i++)
{
	Farming.TendTile();
	Utils.moves++;
	Move(North);
}
QuickPrint("moves:", Utils.moves, "planted:", Farming.planted, "crop:", Farming.Crop);
Utils.moves = 0;
SpawnDrone(Farming.TendTile);
Console.WriteLine($"twice: {Utils.Twice(n)}");
