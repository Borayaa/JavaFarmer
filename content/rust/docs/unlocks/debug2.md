# Debug 2
When your drone gets too fast and the grid too big it can be hard to see what's going on anymore.

For this reason there are the `set_execution_speed()` and `set_world_size()` functions.
They allow you to reduce the execution speed and the farm size. 

`set_world_size(3.0);
set_execution_speed(1.0);`

The farm size and the execution speed will be reset to the default values at the end of the execution.
