# Follow the treasure hints in a maze using measure().
dirs <- c(North, East, South, West)

turn_right <- function(d) d %% 4 + 1
turn_left <- function(d) (d + 2) %% 4 + 1

plant(Entities$Bush)
while (get_entity_type() == Entities$Bush) {
  use_item(Items$Weird_Substance, get_world_size())
}
dir <- 1
while (get_entity_type() != Entities$Treasure) {
  right <- turn_right(dir)
  if (can_move(dirs[right])) {
    dir <- right
  } else if (!can_move(dirs[dir])) {
    dir <- turn_left(dir)
    next
  }
  move(dirs[dir])
}
hint <- measure()
if (!is.null(hint)) {
  quick_print("next treasure", hint[[1]], hint[[2]])
}
harvest()
