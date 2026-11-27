# Speed Upgrade
The execution speed has been doubled. The problem is that the drone now harvests faster than the grass can grow resulting in no harvest at all. To deal with this [if](docs/scripting/if.md) branches and the [CanHarvest](functions/can_harvest) method are now unlocked.

## Checking Before Harvesting
So far we only had `true` and `false` as conditions, which is of course not very useful with `if`. 

The new method `CanHarvest()` provides a better condition. `CanHarvest()` returns `true` if the plant under the drone can be harvested and `false` otherwise.

`if (CanHarvest())
{
	// do something
}`

The reason you can use this method as a condition like this is because it returns a `bool` value.

A return value essentially means that after the functionality is executed, the method call expression evaluates to the returned value.

What happens when the above code runs:
	-the if runs
	-`CanHarvest()` is called
	-`CanHarvest()` does its thing
	-`CanHarvest()` returns `true` or `false`
	-the statement is now `if (true)` or `if (false)`
	-the code block is only executed if it can harvest

Now we can use `if` to prevent the drone from harvesting too early:

`while (true)
{
	if (CanHarvest())
	{
		Harvest();
	}
}`
