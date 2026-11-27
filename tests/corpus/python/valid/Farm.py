# Plant a checkerboard of bushes and grass, then harvest it.
size = get_world_size()
count = 0

def plant_tile(x, y):
	if (x + y) % 2 == 0:
		plant(Entities.Bush)
	else:
		plant(Entities.Grass)

def sweep():
	global count
	for x in range(size):
		for y in range(size):
			if can_harvest():
				harvest()
				count += 1
			plant_tile(x, y)
			move(North)
		move(East)

while True:
	sweep()
	if count > 100:
		print("done", count)
		break
