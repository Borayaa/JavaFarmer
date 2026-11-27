# Lists, dicts, sets and tuples.
heights = []
for i in range(5):
	heights.append(i * 2)
heights.insert(0, 7)
last = heights.pop()
seen = set()
seen.add(Entities.Bush)
costs = {}
costs[Items.Hay] = 10
if Items.Hay in costs and Entities.Bush in seen:
	quick_print(len(heights), costs[Items.Hay], last)
x, y = get_pos_x(), get_pos_y()
pos = (x, y)
quick_print(pos[0] + pos[1])
