# Speed Upgrade
The execution speed has been doubled. The problem is that the drone now harvests faster than the grass can grow resulting in no harvest at all. To deal with this [if](docs/scripting/if.md) branches and the [canHarvest](functions/can_harvest) function are now unlocked.

## Checking Before Harvesting
So far we only had `true` and `false` as conditions, which is of course not very useful with `if`. 

The new function `canHarvest()` provides a better condition. `canHarvest()` returns `true` if the plant under the drone can be harvested and `false` otherwise.

`if (canHarvest()) {
	// do something
}`

The reason you can use this function as a condition like this is because it returns a boolean value.

A return value essentially means that after the functionality is executed, the function call expression evaluates to the returned value.

What happens when the above code runs:
	-the if runs
	-`canHarvest()` is called
	-`canHarvest()` does its thing
	-`canHarvest()` returns `true` or `false`
	-the statement is now `if (true)` or `if (false)`
	-the code block is only executed if it can harvest

Now we can use `if` to prevent the drone from harvesting too early.