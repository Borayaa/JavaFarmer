# Trees on a checkerboard, carrots in between, water when dry.
WATER_LEVEL <- 0.75

tend <- function() {
  here <- get_entity_type()
  if (!is.null(here) && can_harvest()) {
    harvest()
  }
  if (get_water() < WATER_LEVEL && num_items(Items$Water) > 0) {
    use_item(Items$Water)
  }
  if ((get_pos_x() + get_pos_y()) %% 2 == 0) {
    plant(Entities$Tree)
  } else {
    if (get_ground_type() != Grounds$Soil) {
      till()
    }
    plant(Entities$Carrot)
  }
}

clear()
repeat {
  for (i in seq_len(get_world_size() * get_world_size())) {
    tend()
    if (get_pos_y() == get_world_size() - 1) {
      move(East)
    }
    move(North)
  }
}
