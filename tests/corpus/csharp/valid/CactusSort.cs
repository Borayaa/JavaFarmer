// Sort a row of cacti with bubble sort using Measure() and Swap().
const int Unknown = -1;
int size = GetWorldSize();
int[] heights = new int[size];
for (int x = 0; x < size; x++)
{
	if (GetGroundType() != Grounds.Soil)
	{
		Till();
	}
	Plant(Entities.Cactus);
	object? m = Measure();
	heights[x] = m == null ? Unknown : (int)m;
	Move(East);
}
var tallest = 0;
foreach (int h in heights)
{
	tallest = Math.Max(tallest, h);
}
QuickPrint("tallest", tallest, "count", heights.Length);
SortRow();
Harvest();

void SortRow()
{
	int n = GetWorldSize();
	bool swapped = true;
	while (swapped)
	{
		swapped = false;
		for (int i = 0; i < n - 1; i++)
		{
			int here = (int)Measure();
			int next = (int)Measure(East);
			if (here > next)
			{
				Swap(East);
				swapped = true;
			}
			Move(East);
		}
		for (int i = 0; i < n - 1; i++)
		{
			Move(West);
		}
	}
}
