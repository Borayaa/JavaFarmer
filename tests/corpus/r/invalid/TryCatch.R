# ERROR: tryCatch() is not supported in JavaFarmer
# AT: tryCatch
tryCatch(harvest(), error = function(e) print(e))
