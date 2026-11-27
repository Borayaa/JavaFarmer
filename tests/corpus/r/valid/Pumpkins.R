# Replant dead pumpkins until the whole field is one big pumpkin.
dead_x <- c()
dead_y <- c()

go_to <- function(x, y) {
  while (get_pos_x() != x) {
    move(East)
  }
  while (get_pos_y() != y) {
    move(North)
  }
}

plant_all <- function() {
  n <- get_world_size()
  for (x in seq_len(n)) {
    for (y in seq_len(n)) {
      if (get_ground_type() != Grounds$Soil) {
        till()
      }
      plant(Entities$Pumpkin)
      dead_x[[length(dead_x) + 1]] <<- get_pos_x()
      dead_y[[length(dead_y) + 1]] <<- get_pos_y()
      move(North)
    }
    move(East)
  }
}

clear()
plant_all()
while (length(dead_x) > 0) {
  x <- dead_x[[1]]
  y <- dead_y[[1]]
  dead_x <- dead_x[-1]
  dead_y <- dead_y[-1]
  go_to(x, y)
  if (get_entity_type() == Entities$Pumpkin && can_harvest()) {
    next
  }
  plant(Entities$Pumpkin)
  dead_x <- c(dead_x, x)
  dead_y <- c(dead_y, y)
}
harvest()
