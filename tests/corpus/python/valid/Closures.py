# Every drone runs a closure over its row.
def for_all(f):
	def row():
		for _ in range(get_world_size() - 1):
			f()
			move(East)
		f()
	for _ in range(get_world_size()):
		if not spawn_drone(row):
			row()
		move(North)

def in_range(a, b, c):
	return a <= b < c

seen = set()
if 3 not in seen and in_range(0, get_pos_x(), 5):
	seen.add(3)
	quick_print("it's", 'say "hi"')
for_all(harvest)
