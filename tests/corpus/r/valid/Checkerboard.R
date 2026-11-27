# Grass and bushes in a checkerboard pattern, harvesting as we go.
n <- get_world_size()
for (x in 0:(n - 1)) {
  for (y in seq_len(n) - 1) {
    if (can_harvest()) harvest()
    if ((x + y) %% 2 == 0) plant(Entities$Bush) else plant(Entities$Grass)
    move(North)
  }
  move(East)
}
