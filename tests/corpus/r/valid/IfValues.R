# if / else as a value.
x <- get_pos_x()
side <- if (x < get_world_size() / 2) "west" else "east"
size <- if (x == 0) {
  "edge"
} else if (x == get_world_size() - 1) {
  "edge"
} else {
  "middle"
}
maybe <- if (can_harvest()) "ripe"
quick_print(side, size, is.null(maybe))
