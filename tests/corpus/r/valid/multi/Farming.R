source("Utils.R")

ready <- 0

farm_row <- function(width) {
  for (i in seq_len(width)) {
    if (can_harvest()) {
      harvest()
      ready <<- ready + 1
    }
    plant(Entities$Grass)
    move(East)
  }
}

count_ready <- function() ready
