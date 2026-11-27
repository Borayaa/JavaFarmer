# Uses functions and variables of two other code windows.
source("Utils.R")
source("Farming.R")

clear()
for (i in seq_len(rows)) {
  farm_row(get_world_size())
  move_to(0, get_pos_y() + 1)
}
print(describe(count_ready()))
