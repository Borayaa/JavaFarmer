# ERROR: the vectorised & is not supported in JavaFarmer; use &&
# AT: &
if (can_harvest() & get_water() > 0.5) harvest()
