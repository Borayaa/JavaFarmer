# Every drone farms one column. The closure remembers its own column number.
farm_column <- function(column) {
  for (i in seq_len(get_world_size())) {
    if (can_harvest()) {
      harvest()
    }
    move(North)
  }
  quick_print("column done", column)
}

make_task <- function(column) {
  function() farm_column(column)
}

clear()
for (col in seq_len(get_world_size())) {
  drone <- spawn_drone(make_task(col))
  if (is.null(drone)) {
    farm_column(col)
  }
  move(East)
}
while (num_drones() > 1) {
  do_a_flip()
}
