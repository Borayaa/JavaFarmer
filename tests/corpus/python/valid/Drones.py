# Spawn drones that each harvest one column.
def column():
	for i in range(get_world_size()):
		if can_harvest():
			harvest()
		move(North)

drones = []
for i in range(max_drones() - 1):
	d = spawn_drone(column)
	if d != None:
		drones.append(d)
	move(East)
column()
for d in drones:
	wait_for(d)
