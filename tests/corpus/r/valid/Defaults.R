# Default and named arguments.
go <- function(dir = North, steps = 1, flip = FALSE) {
  for (i in seq_len(steps)) {
    move(dir)
  }
  if (flip) do_a_flip()
}

go()
go(East, 3)
go(steps = 2)
go(flip = TRUE)
go(West, flip = TRUE)
use_item(Items$Fertilizer, n = 2)
