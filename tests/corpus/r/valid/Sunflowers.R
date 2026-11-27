# Measure every sunflower, then harvest the ones with the most petals first.
by_petals <- list()
n <- get_world_size()
for (x in 1:n) {
  for (y in 1:n) {
    if (get_ground_type() != Grounds$Soil) till()
    plant(Entities$Sunflower)
    petals <- as.character(measure())
    spots <- by_petals[[petals]]
    if (is.null(spots)) {
      spots <- list()
    }
    spots[[length(spots) + 1]] <- c(get_pos_x(), get_pos_y())
    by_petals[[petals]] <- spots
    move(North)
  }
  move(East)
}
for (petals in 15:7) {
  key <- as.character(petals)
  if (!(key %in% names(by_petals))) {
    next
  }
  for (p in by_petals[[key]]) {
    quick_print("petals", petals, "at", p[[1]], p[[2]])
  }
}
print(paste("sizes:", length(by_petals)))
