# Comments and blank lines survive the round trip.

count = 0  # trailing


def bump(n = 1):
	# only functions that assign need 'global'
	global count
	count += n
	return count
	# end of bump

for key in [Items.Hay, Items.Wood]:
	if num_items(key) > 10:
		bump(2)
	elif num_items(key) == 0:
		# nothing yet
		pass
	else:
		bump()
x, y = get_pos_x(), get_pos_y()
quick_print(x, y, count)
# done
