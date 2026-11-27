# Every constant bag and a few game queries.
if (num_unlocked(Unlocks$Grass) > 0) {
  plant(Entities$Grass)
}
change_hat(Hats$Brown_Hat)
quick_print(get_entity_type() == Entities$Grass, get_ground_type() == Grounds$Grassland)
quick_print(num_items(Items$Hay), Leaderboards$Hay, max_drones(), get_tick_count())
if (has_finished(spawn_drone(harvest))) pet_the_piggy()
leaderboard_run(Leaderboards$Hay, "Hay", 64)
