# Helpers shared by the other windows.
rows <- 3

move_to <- function(x, y) {
  while (get_pos_x() != x) move(East)
  while (get_pos_y() != y %% get_world_size()) move(North)
}

describe <- function(n) paste("ready:", n)
