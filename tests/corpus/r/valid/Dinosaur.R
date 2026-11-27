# Wear the dinosaur hat and steer toward apples using measure().
path <- list()

step <- function(d) {
  if (move(d)) {
    path[[length(path) + 1]] <<- d
    return(TRUE)
  }
  FALSE
}

change_hat(Hats$Dinosaur_Hat)
repeat {
  apple <- measure()
  if (is.null(apple)) {
    break
  }
  moved <- if (get_pos_x() < apple[[1]]) {
    step(East)
  } else if (get_pos_x() > apple[[1]]) {
    step(West)
  } else if (get_pos_y() < apple[[2]]) {
    step(North)
  } else {
    step(South)
  }
  if (!moved) {
    break
  }
}
quick_print("steps", length(path))
change_hat(Hats$Straw_Hat)
