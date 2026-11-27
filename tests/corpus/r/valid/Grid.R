# A grid as a list of vectors (1-based rows and columns).
n <- get_world_size()
grid <- list()
for (x in seq_len(n)) {
  grid[[x]] <- rep(0, n)
}
for (x in seq_len(n)) {
  for (y in seq_len(n)) {
    grid[[x]][[y]] <- x * y
  }
}
copy <- grid[[1]]
copy[[1]] <- 100
quick_print(grid[[1]][[1]], copy[[1]], length(grid))
