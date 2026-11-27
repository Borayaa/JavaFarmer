# Sort a row of cacti with bubble sort using measure() and swap().
sort_row <- function() {
  n <- get_world_size()
  swapped <- TRUE
  while (swapped) {
    swapped <- FALSE
    for (i in seq_len(n - 1)) {
      here <- measure()
      after <- measure(East)
      if (here > after) {
        swap(East)
        swapped <- TRUE
      }
      move(East)
    }
    for (i in seq_len(n - 1)) {
      move(West)
    }
  }
}

size <- get_world_size()
heights <- rep(0, size)
for (x in seq_len(size)) {
  if (get_ground_type() != Grounds$Soil) {
    till()
  }
  plant(Entities$Cactus)
  heights[x] <- measure()
  move(East)
}
tallest <- 0
for (h in heights) {
  tallest <- max(tallest, h)
}
quick_print("tallest", tallest)
sort_row()
harvest()
