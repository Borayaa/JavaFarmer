// Lambdas, Action and Func<T>, capturing top-level variables.
int harvested = 0;

Action harvestIfReady = () =>
{
	if (CanHarvest())
	{
		Harvest();
		harvested++;
	}
};
Func<bool> fieldIsFull = () => harvested >= 10;
Func<int, int> twice = x => x * 2;
Func<int, int, int> add = (a, b) => a + b;
Action<string> say = text => QuickPrint(text);

while (!fieldIsFull())
{
	harvestIfReady();
	Plant(Entities.Grass);
	Move(North);
}
RepeatTimes(3, () => Move(East));
say("harvested " + twice(harvested) + " " + add(1, 2));
SpawnDrone(() =>
{
	harvestIfReady.Invoke();
	QuickPrint("drone done");
});

void RepeatTimes(int times, Action action)
{
	for (int i = 0; i < times; i++)
	{
		action();
	}
}
