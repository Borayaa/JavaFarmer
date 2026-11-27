# ERROR: a default value that uses another parameter (w) is not supported in JavaFarmer
# AT: w
area <- function(w, h = w) w * h
