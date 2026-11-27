#' Harvests the tile under the drone when it is ready.
#' @return TRUE when something was harvested
harvest_ready <- function() {
  # only ripe plants
  if (can_harvest()) {
    harvest()  # trailing comment
    return(TRUE)
  }
  FALSE
  # end of function
}

# a counter

count <- 0
for (i in 1:3) {
  if (harvest_ready()) count <- count + 1
}
print(count)
# end of file
