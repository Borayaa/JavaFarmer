# Plant each tile's preferred companion; remember wishes in a named list keyed by "x y".
n <- get_world_size()
choices <- c(Entities$Grass, Entities$Bush, Entities$Tree, Entities$Carrot)
wishes <- list()
for (step in seq_len(n * n)) {
  want <- choices[[(step - 1) %% length(choices) + 1]]
  if (want == Entities$Carrot && get_ground_type() != Grounds$Soil) {
    till()
  }
  plant(want)
  comp <- get_companion()
  if (!is.null(comp)) {
    spot <- comp[[2]]
    wishes[[paste(spot[[1]], spot[[2]])]] <- comp[[1]]
  }
  if (get_pos_y() == n - 1) {
    move(East)
  }
  move(North)
}
for (key in names(wishes)) {
  quick_print(key, wishes[[key]])
}
quick_print("wishes:", length(wishes), "first choice:", as.character(choices[1]))
