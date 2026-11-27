directions = [North, East, South, West]
index = 0

def turn(delta):
	global index
	index = (index + delta) % 4

plant(Entities.Bush)
substance = get_world_size() * 2 ** (num_unlocked(Unlocks.Mazes) - 1)
use_item(Items.Weird_Substance, substance)
while get_entity_type() != Entities.Treasure:
	turn(1)
	if not move(directions[index]):
		turn(-2)
harvest()
